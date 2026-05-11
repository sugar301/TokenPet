using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TokenPet.Services;

public record ProxyTarget(string Name, string Prefix, string Host);

public class ProxyServer
{
    private static readonly string TargetsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "pet_data", "proxy_targets.json");

    private TcpListener? _listener;
    private readonly List<ProxyClient> _clients = new();
    private readonly object _lock = new();
    private bool _active;
    private int _port = 11435;
    private List<ProxyTarget> _targets = LoadTargets();

    private static readonly string LogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "pet_data", "debug.log");

    internal static void Log(string msg)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (dir != null) Directory.CreateDirectory(dir);
            var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            File.AppendAllText(LogPath, $"[{ts}] {msg}\n");
        }
        catch { }
    }

    public bool IsActive => _active;
    public int Port => _port;
    public List<ProxyTarget> Targets => _targets;

    public event Action<long, long, string>? TokenUsed;
    public event Action<string>? RequestReceived;
    public event Action<int, string>? ResponseFinished;

    public void Start(int port, List<ProxyTarget> targets)
    {
        _port = port;
        _targets = targets;
        _active = true;
        _listener = new TcpListener(IPAddress.Parse("127.0.0.1"), port);
        _listener.Start();
    }

    public void Stop()
    {
        _active = false;
        try { _listener?.Stop(); } catch { }
        lock (_lock)
        {
            foreach (var c in _clients) c.Dispose();
            _clients.Clear();
        }
        SaveTargets();
    }

    public void SaveTargets()
    {
        try
        {
            var dir = Path.GetDirectoryName(TargetsPath);
            if (dir != null) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_targets, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(TargetsPath, json);
        }
        catch { }
    }

    private static List<ProxyTarget> LoadTargets()
    {
        try
        {
            if (File.Exists(TargetsPath))
            {
                var json = File.ReadAllText(TargetsPath);
                var targets = JsonSerializer.Deserialize<List<ProxyTarget>>(json);
                if (targets != null && targets.Count > 0) return targets;
            }
        }
        catch { }
        return new List<ProxyTarget>
        {
            new("DeepSeek", "ds", "api.deepseek.com"),
            new("千问", "qw", "dashscope.aliyuncs.com"),
            new("OpenAI", "oai", "api.openai.com"),
        };
    }

    public void Poll()
    {
        if (!_active || _listener == null) return;

        while (_listener.Pending())
        {
            try
            {
                var client = _listener.AcceptTcpClient();
                lock (_lock) _clients.Add(new ProxyClient(client, _targets, OnToken, this));
            }
            catch { }
        }

        lock (_lock)
        {
            for (int i = _clients.Count - 1; i >= 0; i--)
            {
                if (!_clients[i].Poll() || _clients[i].Done)
                {
                    _clients[i].Dispose();
                    _clients.RemoveAt(i);
                }
            }
            if (_clients.Count > 100) _clients.RemoveRange(0, _clients.Count - 100);
        }
    }

    private void OnToken(long input, long output, string target)
    {
        TokenUsed?.Invoke(input, output, target);
    }

    private class ProxyClient : IDisposable
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _clientStream;
        private TcpClient? _target;
        private SslStream? _targetSsl;
        private readonly List<ProxyTarget> _targets;
        private readonly Action<long, long, string> _onToken;
        private readonly ProxyServer _server;
        private readonly MemoryStream _requestBuffer = new();
        private readonly MemoryStream _responseBuffer = new();
        private string _matchedTarget = "";
        private int _state; // 0=reading request, 1=connecting, 2=forwarding
        private long _inputTokens;
        private long _outputTokens;
        private Task? _connectTask;
        private volatile bool _done;
        private int _responseStatus;

        public bool Done => _done;

        public ProxyClient(TcpClient client, List<ProxyTarget> targets, Action<long, long, string> onToken, ProxyServer server)
        {
            _client = client;
            _clientStream = client.GetStream();
            _targets = targets;
            _onToken = onToken;
            _server = server;
        }

        public bool Poll()
        {
            if (_done) return false;
            try { return _state switch { 0 => ReadRequest(), 1 => ConnectAndForward(), _ => true }; }
            catch { _done = true; return false; }
        }

        private bool ReadRequest()
        {
            if (_client.Available == 0 && _requestBuffer.Length > 0) return true;
            if (_client.Available == 0) return true;

            var buf = new byte[4096];
            int read = _clientStream.Read(buf, 0, buf.Length);
            if (read == 0) return false;
            _requestBuffer.Write(buf, 0, read);

            var data = _requestBuffer.GetBuffer();
            int len = (int)_requestBuffer.Length;
            var headerEnd = FindBytes(data, len, "\r\n\r\n"u8);
            if (headerEnd < 0) return true;

            var headers = Encoding.ASCII.GetString(data, 0, headerEnd);
            var cl = -2;
            var clMatch = Regex.Match(headers, @"Content-Length:\s*(\d+)", RegexOptions.IgnoreCase);
            if (clMatch.Success) cl = int.Parse(clMatch.Groups[1].Value);

            int bodyStart = headerEnd + 4;
            if (cl >= 0 && _requestBuffer.Length < bodyStart + cl) return true;

            var reqLine = Encoding.ASCII.GetString(data, 0, bodyStart);
            var pathMatch = Regex.Match(reqLine, @"^[A-Z]+\s+/(\w+)");
            _matchedTarget = pathMatch.Success ? pathMatch.Groups[1].Value : "";

            // Detect streaming vs regular from request body
            var bodyText = cl >= 0 && _requestBuffer.Length >= bodyStart + cl
                ? Encoding.UTF8.GetString(data, bodyStart, Math.Min(cl, 500))
                : "";
            var isStream = bodyText.Contains("stream") && !bodyText.Contains("\"stream\":false")
                || bodyText.Contains("\"stream\":true");
            ProxyServer.Log($"[req] {(isStream ? "SSE" : "REST")} prefix={_matchedTarget}");

            _server.RequestReceived?.Invoke(_matchedTarget);

            _state = 1;
            return true;
        }

        private bool ConnectAndForward()
        {
            if (_connectTask == null)
            {
                // First call: match target by prefix
                var target = _targets.FirstOrDefault(t => t.Prefix == _matchedTarget) ?? _targets.FirstOrDefault();
                if (target == null) { _done = true; return false; }
                _matchedTarget = target.Name;
                _connectTask = ConnectAndSendAsync(target);
                return true;
            }
            if (!_connectTask.IsCompleted) return true;
            if (_connectTask.IsFaulted || _connectTask.IsCanceled) { _done = true; return false; }

            _connectTask = null;
            _state = 2;
            _ = ForwardRawAsync();
            return true;
        }

        private async Task ConnectAndSendAsync(ProxyTarget target)
        {
            _target = new TcpClient();
            await _target.ConnectAsync(target.Host, 443);
            _targetSsl = new SslStream(_target.GetStream(), false, (_, _, _, _) => true);
            await _targetSsl.AuthenticateAsClientAsync(target.Host);

            var raw = _requestBuffer.ToArray();
            var text = Encoding.UTF8.GetString(raw);
            text = Regex.Replace(text, @"^([A-Z]+) /\w+/", "$1 /", RegexOptions.Multiline);
            text = Regex.Replace(text, @"Host: .+\r\n", $"Host: {target.Host}\r\n", RegexOptions.IgnoreCase);

            if (text.Contains("stream") && !text.Contains("stream_options"))
            {
                var bp = text.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
                if (bp > 3)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(text[bp..]);
                        var nb = InjectIncludeUsage(doc.RootElement);
                        text = text[..bp] + JsonSerializer.Serialize(nb);
                    }
                    catch { }
                }
            }

            var he = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            byte[] all;
            if (he > 0)
            {
                var hd = text[..(he + 4)];
                var bd = text[(he + 4)..];
                var bb = Encoding.UTF8.GetBytes(bd);
                hd = Regex.Replace(hd, @"Content-Length:\s*\d+", $"Content-Length: {bb.Length}", RegexOptions.IgnoreCase);
                var hb = Encoding.UTF8.GetBytes(hd);
                all = new byte[hb.Length + bb.Length];
                Buffer.BlockCopy(hb, 0, all, 0, hb.Length);
                Buffer.BlockCopy(bb, 0, all, hb.Length, bb.Length);
            }
            else { all = Encoding.UTF8.GetBytes(text); }
            await _targetSsl.WriteAsync(all);
            await _targetSsl.FlushAsync();
        }

        private async Task ForwardRawAsync()
        {
            try
            {
                if (_targetSsl == null) return;
                var buf = new byte[8192];
                bool firstRead = true;
                while (true)
                {
                    int read = await _targetSsl.ReadAsync(buf, 0, buf.Length);
                    if (read == 0) break;
                    if (firstRead)
                    {
                        firstRead = false;
                        _responseStatus = ParseHttpStatus(buf, read);
                        if (_responseStatus > 0)
                            ProxyServer.Log($"[rsp] {_responseStatus} {_matchedTarget}");
                    }
                    _responseBuffer.Write(buf, 0, read);
                    await _clientStream.WriteAsync(buf, 0, read);
                    await _clientStream.FlushAsync();

                    // Detect chunked terminator "0\r\n\r\n" — stop reading
                    var all = _responseBuffer.GetBuffer();
                    var len = (int)_responseBuffer.Length;
                    if (FindBytes(all, len, "0\r\n\r\n"u8) >= 0)
                        break;
                }
            }
            catch { }
            finally
            {
                _server.ResponseFinished?.Invoke(_responseStatus, _matchedTarget);
                _finalizeToken();
                try { if (_client?.Client != null) _client.Client.Shutdown(SocketShutdown.Send); } catch { }
                _done = true;
            }
        }

        private static int ParseHttpStatus(byte[] buf, int len)
        {
            var text = Encoding.ASCII.GetString(buf, 0, Math.Min(len, 100));
            var m = Regex.Match(text, @"HTTP/\S+\s+(\d{3})");
            return m.Success && int.TryParse(m.Groups[1].Value, out var c) ? c : 0;
        }

        private void _finalizeToken()
        {
            if (_responseBuffer.Length > 0) ParseUsage(_responseBuffer.ToArray());
            if (_inputTokens > 0 || _outputTokens > 0)
            {
                ProxyServer.Log($"[token] {_matchedTarget} in={_inputTokens} out={_outputTokens}");
                _onToken(_inputTokens, _outputTokens, _matchedTarget);
            }
        }

        private void ParseUsage(byte[] data)
        {
            try
            {
                // Find header/body separator in bytes
                int sepIdx = -1, sepLen = 4;
                for (int i = 0; i <= data.Length - 4; i++)
                    if (data[i] == '\r' && data[i+1] == '\n' && data[i+2] == '\r' && data[i+3] == '\n')
                    { sepIdx = i; break; }
                if (sepIdx == -1) { sepLen = 2; for (int i = 0; i <= data.Length - 2; i++) if (data[i] == '\n' && data[i+1] == '\n') { sepIdx = i; break; } }
                if (sepIdx == -1) return;

                var hdrText = Encoding.ASCII.GetString(data, 0, sepIdx);
                bool isChunked = hdrText.Contains("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase);
                bool isGzip = hdrText.Contains("Content-Encoding: gzip", StringComparison.OrdinalIgnoreCase);

                int bs = sepIdx + sepLen;
                var body = new byte[data.Length - bs];
                Buffer.BlockCopy(data, bs, body, 0, body.Length);

                if (isChunked) body = Dechunk(body);
                if (isGzip)
                {
                    try
                    {
                        using var ms = new MemoryStream(body);
                        using var gz = new GZipStream(ms, CompressionMode.Decompress);
                        using var rs = new MemoryStream();
                        gz.CopyTo(rs);
                        body = rs.ToArray();
                    }
                    catch { }
                }

                var text = Encoding.UTF8.GetString(body);

                foreach (var line in text.Split('\n'))
                {
                    var t = line.Trim();
                    string js;
                    if (t.StartsWith("data: ")) js = t[6..];
                    else if (t.StartsWith("data:")) js = t[5..];
                    else continue;
                    if (js == "[DONE]") continue;
                    try { using var doc = JsonDocument.Parse(js); FindUsage(doc.RootElement); } catch { }
                }

                if (_inputTokens == 0 && _outputTokens == 0 && text.TrimStart().StartsWith('{'))
                {
                    try { using var doc = JsonDocument.Parse(text.Trim()); FindUsage(doc.RootElement); } catch { }
                }
            }
            catch { }
        }

        private void FindUsage(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in element.EnumerateObject())
                {
                    if (p.Name == "usage" && p.Value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var u in p.Value.EnumerateObject())
                        {
                            if (u.Name == "prompt_tokens" || u.Name == "input_tokens")
                            { long.TryParse(u.Value.GetRawText(), out var v); _inputTokens += v; }
                            else if (u.Name == "completion_tokens" || u.Name == "output_tokens")
                            { long.TryParse(u.Value.GetRawText(), out var v); _outputTokens += v; }
                        }
                    }
                    FindUsage(p.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray()) FindUsage(item);
        }

        private static byte[] Dechunk(byte[] data)
        {
            using var ms = new MemoryStream();
            int p = 0;
            while (p < data.Length)
            {
                int crlf = -1;
                for (int i = p; i < data.Length - 1; i++)
                    if (data[i] == '\r' && data[i+1] == '\n') { crlf = i; break; }
                if (crlf == -1) break;
                var hx = Encoding.ASCII.GetString(data, p, crlf - p).Trim();
                int sc = hx.IndexOf(';'); if (sc != -1) hx = hx[..sc];
                if (hx.Length == 0) { p = crlf + 2; continue; }
                int cs = Convert.ToInt32(hx, 16);
                if (cs == 0) break;
                p = crlf + 2;
                if (p + cs > data.Length) break;
                ms.Write(data, p, cs);
                p += cs + 2;
            }
            return ms.ToArray();
        }

        private static int FindBytes(byte[] data, int length, ReadOnlySpan<byte> pattern)
        {
            for (int i = 0; i <= length - pattern.Length; i++)
            {
                bool m = true;
                for (int j = 0; j < pattern.Length; j++) if (data[i + j] != pattern[j]) { m = false; break; }
                if (m) return i;
            }
            return -1;
        }

        private static JsonElement InjectIncludeUsage(JsonElement root)
        {
            using var ms = new MemoryStream();
            using var w = new Utf8JsonWriter(ms);
            w.WriteStartObject();
            foreach (var p in root.EnumerateObject())
            {
                if (p.Name == "stream_options")
                {
                    w.WriteStartObject("stream_options");
                    w.WriteBoolean("include_usage", true);
                    foreach (var sp in p.Value.EnumerateObject())
                        if (sp.Name != "include_usage") sp.WriteTo(w);
                    w.WriteEndObject();
                }
                else p.WriteTo(w);
            }
            if (!root.TryGetProperty("stream_options", out _) && root.TryGetProperty("stream", out var sv) && sv.GetBoolean())
            {
                w.WriteStartObject("stream_options");
                w.WriteBoolean("include_usage", true);
                w.WriteEndObject();
            }
            w.WriteEndObject(); w.Flush(); ms.Position = 0;
            using var doc = JsonDocument.Parse(ms);
            return doc.RootElement.Clone();
        }

        public void Dispose()
        {
            try { _targetSsl?.Close(); } catch { }
            try { _targetSsl?.Dispose(); } catch { }
            try { _client?.Close(); } catch { }
            try { _target?.Close(); } catch { }
            _client?.Dispose();
            _target?.Dispose();
        }
    }
}
