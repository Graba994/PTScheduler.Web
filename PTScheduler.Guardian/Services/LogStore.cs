using System.Text.Json;

namespace PTScheduler.Guardian.Services;

public class LogStore
{
    private readonly string _dir;
    private readonly Lock _lock = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public LogStore(IConfiguration config)
    {
        _dir = Environment.GetEnvironmentVariable("GUARDIAN_LOG_DIR")
            ?? config["Guardian:LogDir"]
            ?? "/opt/ptscheduler/guardian/logs";
        Directory.CreateDirectory(_dir);
    }

    public void Save(UpgradeJob job)
    {
        lock (_lock)
        {
            var path = Path.Combine(_dir, $"{job.Id}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(job, JsonOpts));
        }
    }

    public UpgradeJob? Load(string id)
    {
        var path = Path.Combine(_dir, $"{id}.json");
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<UpgradeJob>(File.ReadAllText(path), JsonOpts); }
        catch { return null; }
    }

    public List<UpgradeJob> GetHistory(int limit = 20)
    {
        if (!Directory.Exists(_dir)) return [];
        return Directory.GetFiles(_dir, "*.json")
            .OrderByDescending(f => f)
            .Take(limit)
            .Select(f =>
            {
                try { return JsonSerializer.Deserialize<UpgradeJob>(File.ReadAllText(f), JsonOpts); }
                catch { return null; }
            })
            .Where(j => j is not null)
            .Cast<UpgradeJob>()
            .ToList();
    }
}
