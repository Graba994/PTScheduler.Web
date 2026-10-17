using System.Text;
using System.Text.RegularExpressions;
using Npgsql;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

public enum LogLevelKind { Debug, Info, Warn, Error }

/// <summary>Kontener, którego logi można obejrzeć w panelu.</summary>
public sealed record LogSource(string Container, string Label, string Group, string Hint, string State, string Status, string Image, string? TenantSlug)
{
    public bool Running => State == "running";
}

/// <summary>
/// Wpis logu: nagłówek (np. „info: Microsoft.Hosting.Lifetime[14]”) razem z liniami, które do niego należą
/// (wcięta treść, stos wywołań wyjątku). Dzięki temu filtr „tylko błędy” pokazuje cały błąd, nie jedną linię.
/// </summary>
public sealed class LogEntry
{
    public DateTime? TimeUtc { get; init; }
    public LogLevelKind Level { get; set; }
    public string? Category { get; init; }
    public bool FromStderr { get; init; }
    public List<string> Lines { get; } = new();

    public bool Matches(string query) =>
        (Category?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        || Lines.Any(l => l.Contains(query, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Które kontenery pokazać w logach i jak czytać ich zawartość.</summary>
public sealed class ContainerLogService(DockerService docker, TenantService tenants, IConfiguration config)
{
    public const string GroupPlatform = "Platforma";
    public const string GroupTenants = "Trenerzy";
    public const string GroupOther = "Inne";

    /// <summary>
    /// Kontenery platformy (Portal, Guardian, baza Portalu), instancje trenerów oraz pozostałe kontenery „pt…”
    /// (instancja testowa, kopie po aktualizacjach). Inne kontenery serwera nie są pokazywane.
    /// </summary>
    public async Task<List<LogSource>> GetSourcesAsync()
    {
        var all = await docker.ListAllContainersAsync();
        var byName = all.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var result = new List<LogSource>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(DockerContainer c, string label, string group, string hint, string? slug = null)
        {
            if (!used.Add(c.Name)) return;
            result.Add(new LogSource(c.Name, label, group, hint, c.State, c.Status, c.Image, slug));
        }

        // Własny kontener rozpoznajemy po nazwie hosta (Docker ustawia ją na skrócone ID kontenera).
        var host = Environment.MachineName;
        var self = all.FirstOrDefault(c => host.Length >= 12 && c.Id.StartsWith(host[..12], StringComparison.OrdinalIgnoreCase))
                   ?? (byName.TryGetValue(config["Portal:ContainerName"] ?? "ptportal", out var p) ? p : null);
        if (self is not null) Add(self, "Portal", GroupPlatform, "ten panel");

        if (byName.TryGetValue(config["Guardian:ContainerName"] ?? "ptguardian", out var guardian))
            Add(guardian, "Guardian", GroupPlatform, "aktualizacje i naprawy");

        var dbHost = PortalDbHost();
        if (dbHost is not null && byName.TryGetValue(dbHost, out var db))
            Add(db, "Baza Portalu", GroupPlatform, "PostgreSQL");

        foreach (var t in (await tenants.GetAllAsync()).Where(t => t.Status != TenantStatus.Destroyed).OrderBy(t => t.CompanyName))
        {
            var name = string.IsNullOrWhiteSpace(t.CompanyName) ? t.Slug : t.CompanyName;
            if (byName.TryGetValue(t.WebContainerName ?? $"pt-{t.Slug}-web", out var web)) Add(web, name, GroupTenants, "aplikacja", t.Slug);
            if (byName.TryGetValue(t.DbContainerName ?? $"pt-{t.Slug}-db", out var tdb)) Add(tdb, name, GroupTenants, "baza danych", t.Slug);
        }

        foreach (var c in all.Where(c => c.Name.StartsWith("pt", StringComparison.OrdinalIgnoreCase)))
            Add(c, c.Name, GroupOther, OtherHint(c.Name));

        return result;
    }

    /// <summary>Kontener z listy albo null — logi czytamy tylko z kontenerów, które panel sam pokazuje.</summary>
    public async Task<LogSource?> FindAsync(string? container)
    {
        if (string.IsNullOrWhiteSpace(container)) return null;
        return (await GetSourcesAsync()).FirstOrDefault(s => string.Equals(s.Container, container, StringComparison.OrdinalIgnoreCase));
    }

    public Task<List<ContainerLogLine>> ReadAsync(string container, int tail, DateTime? sinceUtc, CancellationToken ct = default) =>
        docker.GetContainerLogLinesAsync(container, tail, sinceUtc, ct);

    private string? PortalDbHost()
    {
        var cs = config.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs)) return null;
        try { return new NpgsqlConnectionStringBuilder(cs).Host?.Split(',')[0]; }
        catch { return null; }
    }

    private static string OtherHint(string name) =>
        name.Contains("-old-", StringComparison.OrdinalIgnoreCase) || name.Contains("-prev", StringComparison.OrdinalIgnoreCase) ? "kopia po aktualizacji"
        : name.StartsWith("pt-test", StringComparison.OrdinalIgnoreCase) ? "instancja testowa"
        : name.EndsWith("-db", StringComparison.OrdinalIgnoreCase) ? "baza danych"
        : "kontener";

    // ── Czytanie logów ──────────────────────────────────────────────────────

    private static readonly (string Prefix, LogLevelKind Level)[] AspNetPrefixes =
    {
        ("trce: ", LogLevelKind.Debug), ("dbug: ", LogLevelKind.Debug), ("info: ", LogLevelKind.Info),
        ("warn: ", LogLevelKind.Warn), ("fail: ", LogLevelKind.Error), ("crit: ", LogLevelKind.Error)
    };

    private static readonly Regex ErrorRx = new(
        @"\b(ERROR|FATAL|PANIC|CRITICAL)\b|Unhandled exception|^\s*(\w+\.)*\w*Exception\b|\[(ERR|FTL|ERROR|FATAL)\]|^\w*Error: ",
        RegexOptions.Compiled);

    private static readonly Regex WarnRx = new(@"\bWARN(ING)?\b|\[(WRN|WARN)\]", RegexOptions.Compiled);

    /// <summary>
    /// Składa linie w wpisy i nadaje im poziom. Wcięta linia należy do ostatniego wpisu z tego samego strumienia —
    /// Docker czyta stdout i stderr osobno, więc sąsiednie linie z różnych strumieni potrafią zamienić się miejscami.
    /// </summary>
    public static List<LogEntry> Parse(IEnumerable<ContainerLogLine> lines)
    {
        var entries = new List<LogEntry>();
        LogEntry? lastOut = null, lastErr = null;
        foreach (var line in lines)
        {
            var text = line.Text;
            var current = line.FromStderr ? lastErr : lastOut;
            if (current is not null && text.Length > 0 && (text[0] == ' ' || text[0] == '\t') && current.Lines.Count < 400)
            {
                // Konsola ASP.NET wcina treść o 6 spacji — na wąskim ekranie to zmarnowane miejsce.
                current.Lines.Add(current.Category is not null && text.StartsWith("      ", StringComparison.Ordinal) ? text[6..] : text);
                // Stos wyjątku bez nagłówka ASP.NET (np. „Unhandled exception.”) — cały wpis jest błędem.
                if (current.Category is null && current.Level < LogLevelKind.Error && ErrorRx.IsMatch(text))
                    current.Level = LogLevelKind.Error;
                continue;
            }

            var prefix = AspNetPrefixes.FirstOrDefault(p => text.StartsWith(p.Prefix, StringComparison.Ordinal));
            var entry = prefix.Prefix is not null
                ? new LogEntry { TimeUtc = line.TimeUtc, Level = prefix.Level, Category = text[prefix.Prefix.Length..].Trim(), FromStderr = line.FromStderr }
                : new LogEntry { TimeUtc = line.TimeUtc, Level = Classify(text), FromStderr = line.FromStderr };
            if (prefix.Prefix is null) entry.Lines.Add(text);
            entries.Add(entry);
            if (line.FromStderr) lastErr = entry; else lastOut = entry;
        }
        return entries;
    }

    private static LogLevelKind Classify(string text) =>
        ErrorRx.IsMatch(text) ? LogLevelKind.Error
        : WarnRx.IsMatch(text) ? LogLevelKind.Warn
        : LogLevelKind.Info;

    private static readonly TimeZoneInfo Warsaw = FindWarsaw();

    private static TimeZoneInfo FindWarsaw()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"); }
        catch { return TimeZoneInfo.Local; }
    }

    public static DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Warsaw);

    /// <summary>Plik do pobrania: czas lokalny + oryginalna linia.</summary>
    public static string ToText(string container, IEnumerable<ContainerLogLine> lines)
    {
        var sb = new StringBuilder();
        sb.Append("# Logi kontenera ").Append(container).Append(" — pobrane ")
          .Append(ToLocal(DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss")).Append(" (czas polski)\n");
        foreach (var l in lines)
        {
            sb.Append(l.TimeUtc is { } t ? ToLocal(t).ToString("yyyy-MM-dd HH:mm:ss.fff") : "                       ")
              .Append(l.FromStderr ? " ! " : "   ")
              .Append(l.Text).Append('\n');
        }
        return sb.ToString();
    }
}
