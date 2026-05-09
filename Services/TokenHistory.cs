using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TokenPet.Services;

public record DailyRecord(string Date, string Platform, int Calls, long InputTokens, long OutputTokens)
{
    public long Total => InputTokens + OutputTokens;
}

public class TokenHistory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _dataPath;
    private Dictionary<string, Dictionary<string, PlatformStats>> _records = new();

    public TokenHistory()
    {
        _dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pet_data", "token_history.json");
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_dataPath))
            {
                var json = File.ReadAllText(_dataPath);
                _records = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, PlatformStats>>>(json, JsonOptions)
                    ?? new();
            }
        }
        catch { _records = new(); }
    }

    public void Record(string platform, long inputTokens, long outputTokens)
    {
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        if (!_records.ContainsKey(date))
            _records[date] = new();
        if (!_records[date].ContainsKey(platform))
            _records[date][platform] = new();

        var stats = _records[date][platform];
        stats.Calls++;
        stats.In += inputTokens;
        stats.Out += outputTokens;

        Save();
    }

    public long GetTodayTotal()
    {
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        if (!_records.TryGetValue(date, out var platforms)) return 0;
        return platforms.Values.Sum(s => s.Total);
    }

    public int GetTodayCalls()
    {
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        if (!_records.TryGetValue(date, out var platforms)) return 0;
        return platforms.Values.Sum(s => s.Calls);
    }

    public long GetCumulativeTokens()
    {
        return _records.Values.SelectMany(d => d.Values).Sum(s => s.Total);
    }

    public int GetTotalCalls()
    {
        return _records.Values.SelectMany(d => d.Values).Sum(s => s.Calls);
    }

    public List<DailyRecord> GetDailyRecords()
    {
        var result = new List<DailyRecord>();
        foreach (var (date, platforms) in _records)
        {
            foreach (var (platform, stats) in platforms)
            {
                result.Add(new DailyRecord(date, platform, stats.Calls, stats.In, stats.Out));
            }
        }
        result.Sort((a, b) =>
        {
            int cmp = string.Compare(b.Date, a.Date, StringComparison.Ordinal);
            if (cmp != 0) return cmp;
            return string.Compare(a.Platform, b.Platform, StringComparison.Ordinal);
        });
        return result;
    }

    public void Clear()
    {
        _records.Clear();
        Save();
    }

    public Dictionary<string, PlatformStats> GetTotals()
    {
        var totals = new Dictionary<string, PlatformStats>();
        foreach (var (_, platforms) in _records)
        {
            foreach (var (name, stats) in platforms)
            {
                if (!totals.ContainsKey(name))
                    totals[name] = new();
                totals[name].Calls += stats.Calls;
                totals[name].In += stats.In;
                totals[name].Out += stats.Out;
            }
        }
        return totals;
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_dataPath);
            if (dir != null) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_records, JsonOptions);
            File.WriteAllText(_dataPath, json);
        }
        catch { }
    }
}

public class PlatformStats
{
    [JsonPropertyName("calls")]
    public int Calls { get; set; }

    [JsonPropertyName("in")]
    public long In { get; set; }

    [JsonPropertyName("out")]
    public long Out { get; set; }

    [JsonIgnore]
    public long Total => In + Out;
}
