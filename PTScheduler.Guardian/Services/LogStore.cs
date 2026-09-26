using System.Text.Json;

namespace PTScheduler.Guardian.Services;

/// <summary>
/// Zadania Guardiana jako pliki JSON (historia widoczna w Portalu).
/// Zapis atomowy (plik tymczasowy + podmiana), żeby Portal nigdy nie czytał połowy pliku,
/// i serializacja pod blokadą logu zadania — instancje aktualizują się równolegle.
/// </summary>
public class LogStore
{
    private const int KeepJobs = 300;

    private readonly string _dir;
    private readonly Lock _lock = new();
    private readonly ILogger<LogStore> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public LogStore(IConfiguration config, ILogger<LogStore> logger)
    {
        _logger = logger;
        _dir = Environment.GetEnvironmentVariable("GUARDIAN_LOG_DIR")
            ?? config["Guardian:LogDir"]
            ?? "/opt/ptscheduler/guardian/logs";
        Directory.CreateDirectory(_dir);
    }

    public void Save(UpgradeJob job)
    {
        string json;
        lock (job.Log) json = JsonSerializer.Serialize(job, JsonOpts);

        lock (_lock)
        {
            var path = PathFor(job.Id);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
    }

    public UpgradeJob? Load(string id)
    {
        if (!IsSafeId(id)) return null;
        var path = PathFor(id);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<UpgradeJob>(File.ReadAllText(path), JsonOpts); }
        catch { return null; }
    }

    public List<UpgradeJob> GetHistory(int limit = 20)
    {
        if (!Directory.Exists(_dir)) return [];
        return Directory.GetFiles(_dir, "*.json")
            .OrderByDescending(f => f, StringComparer.Ordinal)
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

    /// <summary>
    /// Zadania, które zostały „w toku”, bo Guardian się zrestartował w ich trakcie — inaczej Portal
    /// czekałby na nie w nieskończoność. Oznaczamy je jako nieudane z czytelnym powodem.
    /// </summary>
    public void RecoverInterrupted()
    {
        foreach (var job in GetHistory(50).Where(j => j.Status == UpgradeStatus.Running))
        {
            job.Status = UpgradeStatus.Failed;
            job.CompletedAt ??= DateTime.UtcNow;
            job.Error = "Zadanie przerwane — Guardian został zrestartowany w jego trakcie. Uruchom je ponownie.";
            job.Log.Add(new LogEntry { Level = "error", Stage = job.Stage.ToString(), Message = job.Error });
            Save(job);
            _logger.LogWarning("Oznaczono przerwane zadanie {Job} jako nieudane.", job.Id);
        }
    }

    /// <summary>Zostawia ostatnie {KeepJobs} zadań.</summary>
    public void Prune()
    {
        try
        {
            foreach (var f in Directory.GetFiles(_dir, "*.json").OrderByDescending(f => f, StringComparer.Ordinal).Skip(KeepJobs))
                File.Delete(f);
            foreach (var tmp in Directory.GetFiles(_dir, "*.tmp"))
                File.Delete(tmp);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Nie udało się posprzątać starych logów zadań."); }
    }

    private string PathFor(string id) => Path.Combine(_dir, $"{id}.json");

    /// <summary>Id zadania trafia do ścieżki pliku — tylko bezpieczne znaki (bez „../”).</summary>
    public static bool IsSafeId(string id) =>
        id.Length is > 0 and <= 64 && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
}
