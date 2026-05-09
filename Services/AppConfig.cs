using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TokenPet.Services;

public class AppConfig
{
    private static readonly string JsonPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "pet_data", "pet_config.json");
    private static readonly string CfgPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "pet_data", "pet_config.cfg");

    public int TotalCalls { get; set; }
    public long TotalTokens { get; set; }
    public string ActiveModel { get; set; } = "N/A";
    public string Endpoint { get; set; } = "N/A";
    public double WindowX { get; set; } = -1;
    public double WindowY { get; set; } = -1;
    public double PetScale { get; set; } = 0.85;
    public string ActivePetId { get; set; } = "";
    public bool ProxyEnabled { get; set; }
    public int ProxyPort { get; set; } = 11435;

    public void Load()
    {
        try
        {
            if (File.Exists(JsonPath))
            {
                var json = File.ReadAllText(JsonPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                if (cfg != null) CopyFrom(cfg);
                return;
            }

            if (File.Exists(CfgPath))
                LoadFromCfg(CfgPath);
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(JsonPath);
            if (dir != null) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(JsonPath, json);
        }
        catch { }
    }

    private void LoadFromCfg(string path)
    {
        try
        {
            var content = File.ReadAllText(path);
            var section = "";

            foreach (var line in content.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    section = trimmed[1..^1];
                    continue;
                }

                var eq = trimmed.IndexOf('=');
                if (eq < 0) continue;
                var key = trimmed[..eq].Trim();
                var value = trimmed[(eq + 1)..].Trim().Trim('"');

                switch (section)
                {
                    case "stats":
                        if (key == "total_calls") int.TryParse(value, out var tc);
                        if (key == "total_tokens") long.TryParse(value, out var tt);
                        if (key == "active_model") ActiveModel = value;
                        if (key == "endpoint") Endpoint = value;
                        break;
                    case "window":
                        if (key == "position")
                        {
                            var match = Regex.Match(value, @"\((-?\d+),\s*(-?\d+)\)");
                            if (match.Success)
                            {
                                WindowX = double.Parse(match.Groups[1].Value);
                                WindowY = double.Parse(match.Groups[2].Value);
                            }
                        }
                        break;
                    case "pet":
                        if (key == "scale") double.TryParse(value, out var s);
                        if (key == "active_id") ActivePetId = value;
                        break;
                    case "proxy":
                        if (key == "enabled") bool.TryParse(value, out var en);
                        if (key == "port") int.TryParse(value, out var pn);
                        break;
                }
            }
        }
        catch { }
    }

    private void CopyFrom(AppConfig other)
    {
        TotalCalls = other.TotalCalls;
        TotalTokens = other.TotalTokens;
        ActiveModel = other.ActiveModel;
        Endpoint = other.Endpoint;
        WindowX = other.WindowX;
        WindowY = other.WindowY;
        PetScale = other.PetScale;
        ActivePetId = other.ActivePetId;
        ProxyEnabled = other.ProxyEnabled;
        ProxyPort = other.ProxyPort;
    }
}
