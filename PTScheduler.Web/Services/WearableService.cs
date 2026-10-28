using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Wearables;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

/// <summary>
/// Zegarek klienta (Garmin Connect, plan Starter). Hantly jest u Garmina jednym partnerem — przez Portal:
/// Portal prowadzi logowanie do Garmina (OAuth2 PKCE), odbiera dane, które Garmin wysyła sam (push), i przekazuje
/// je do aplikacji studia. Tutaj: zgoda i połączenie klienta, zapis danych, gotowość na dziś i dopięcie
/// aktywności do wizyty. Danych z zegarka nie wysyłamy do AI z imieniem ani kontaktem klienta.
/// </summary>
public sealed class WearableService(IDbContextFactory<ApplicationDbContext> dbFactory, IAppClock clock, ILogger<WearableService> logger)
{
    public const string Garmin = "garmin";

    public sealed record Summary(WearableLink? Link, IReadOnlyList<WearableDay> Days, IReadOnlyList<WearableActivity> Activities,
        ReadinessRules.Result Readiness);

    private static (string Url, string Slug, string? Secret)? Portal()
    {
        var url = Environment.GetEnvironmentVariable("PORTAL_URL");
        var slug = Environment.GetEnvironmentVariable("TENANT_SLUG");
        return string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(slug)
            ? null : (url.TrimEnd('/'), slug, Environment.GetEnvironmentVariable("TENANT_INTERNAL_SECRET"));
    }

    private static async Task<(bool Ok, JsonDocument? Json, string? Error)> CallPortalAsync(HttpMethod method, string path, object? body)
    {
        if (Portal() is not { } p) return (false, null, "Aplikacja nie jest połączona z platformą.");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        using var req = new HttpRequestMessage(method, $"{p.Url}/api/garmin/{p.Slug}{path}");
        if (body is not null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        if (!string.IsNullOrEmpty(p.Secret)) req.Headers.TryAddWithoutValidation("X-Internal-Secret", p.Secret);
        using var resp = await http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        JsonDocument? json = null;
        try { if (!string.IsNullOrWhiteSpace(text)) json = JsonDocument.Parse(text); } catch (JsonException) { }
        var error = json?.RootElement.TryGetProperty("error", out var e) == true ? e.GetString() : null;
        return (resp.IsSuccessStatusCode, json, resp.IsSuccessStatusCode ? null : error ?? $"Platforma odpowiedziała błędem ({(int)resp.StatusCode}).");
    }

    /// <summary>Czy Garmin jest włączony na platformie (Portal ma klucze programu Garmin Connect).</summary>
    public async Task<bool> AvailableAsync()
    {
        try
        {
            var (ok, json, _) = await CallPortalAsync(HttpMethod.Get, "/status", null);
            return ok && json?.RootElement.TryGetProperty("enabled", out var en) == true && en.GetBoolean();
        }
        catch (Exception ex) { logger.LogDebug(ex, "Status Garmina z Portalu niedostępny."); return false; }
    }

    public async Task<WearableLink?> LinkAsync(int clientId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.WearableLinks.AsNoTracking().FirstOrDefaultAsync(l => l.ClientId == clientId && l.Provider == Garmin);
    }

    /// <summary>Zgoda klienta + adres logowania do Garmina (przez Portal). Po powrocie Portal potwierdzi połączenie.</summary>
    public async Task<(string? Url, string? Error)> StartConnectAsync(int clientId, string returnUrl, bool aiConsent = false)
    {
        try
        {
            var (ok, json, error) = await CallPortalAsync(HttpMethod.Post, "/start", new { clientId, returnUrl });
            if (!ok || json?.RootElement.TryGetProperty("url", out var u) != true) return (null, error ?? "Nie udało się rozpocząć łączenia.");
            await using var db = await dbFactory.CreateDbContextAsync();
            var link = await db.WearableLinks.FirstOrDefaultAsync(l => l.ClientId == clientId && l.Provider == Garmin);
            if (link is null) db.WearableLinks.Add(link = new WearableLink { ClientId = clientId, Provider = Garmin });
            link.ConsentAt = DateTime.UtcNow;
            link.AiConsentAt = aiConsent ? DateTime.UtcNow : null;
            await db.SaveChangesAsync();
            return (u.GetString(), null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Start łączenia Garmina dla klienta {ClientId} nie powiódł się.", clientId);
            return (null, "Nie udało się połączyć z platformą. Spróbuj za chwilę.");
        }
    }

    /// <summary>Zgoda na AI włączana i wycofywana przez klienta w każdej chwili — działa od razu.</summary>
    public async Task SetAiConsentAsync(int clientId, bool on)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var link = await db.WearableLinks.FirstOrDefaultAsync(l => l.ClientId == clientId && l.Provider == Garmin);
        if (link is null) return;
        link.AiConsentAt = on ? DateTime.UtcNow : null;
        await db.SaveChangesAsync();
    }

    /// <summary>Odłączenie (wycofanie zgody): Portal wyrejestrowuje użytkownika u Garmina; dane usuwamy, jeśli klient chce.</summary>
    public async Task<string?> DisconnectAsync(int clientId, bool deleteData)
    {
        string? error = null;
        try { (_, _, error) = await CallPortalAsync(HttpMethod.Post, "/disconnect", new { clientId }); }
        catch (Exception ex) { logger.LogWarning(ex, "Odłączenie Garmina w Portalu nie powiodło się ({ClientId}).", clientId); error = "Platforma nie odpowiedziała"; }
        await SetLinkAsync(clientId, false, null);
        if (deleteData) await DeleteDataAsync(clientId);
        return error is null ? null : $"{error} — połączenie odłączyliśmy u siebie; Garmin przestanie wysyłać dane po odłączeniu aplikacji w Garmin Connect.";
    }

    public async Task DeleteDataAsync(int clientId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.WearableDays.RemoveRange(await db.WearableDays.Where(d => d.ClientId == clientId).ToListAsync());
        db.WearableActivities.RemoveRange(await db.WearableActivities.Where(a => a.ClientId == clientId).ToListAsync());
        await db.SaveChangesAsync();
    }

    /// <summary>Wywołanie z Portalu: połączono / odłączono (np. klient odłączył aplikację w Garmin Connect).</summary>
    public async Task SetLinkAsync(int clientId, bool connected, string? deviceName)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Clients.AnyAsync(c => c.Id == clientId)) return;
        var link = await db.WearableLinks.FirstOrDefaultAsync(l => l.ClientId == clientId && l.Provider == Garmin);
        if (link is null) db.WearableLinks.Add(link = new WearableLink { ClientId = clientId, Provider = Garmin });
        if (connected) { link.ConnectedAt = DateTime.UtcNow; link.DisconnectedAt = null; }
        else
        {
            if (link.ConnectedAt is not null) link.DisconnectedAt = DateTime.UtcNow;
            link.AiConsentAt = null; // odłączenie wycofuje też zgodę na AI
        }
        if (!string.IsNullOrWhiteSpace(deviceName)) link.DeviceName = deviceName.Trim();
        await db.SaveChangesAsync();
    }

    /// <summary>Wywołanie z Portalu: rekordy jednego rodzaju dla jednego klienta. Zwraca liczbę zapisanych.</summary>
    public async Task<int> IngestAsync(int clientId, string kind, JsonElement records)
    {
        if (records.ValueKind != JsonValueKind.Array) return 0;
        await using var db = await dbFactory.CreateDbContextAsync();
        var link = await db.WearableLinks.FirstOrDefaultAsync(l => l.ClientId == clientId && l.Provider == Garmin);
        // Bez połączenia (albo po odłączeniu) nic nie zapisujemy — zgoda jest warunkiem.
        if (link is not { IsConnected: true }) return 0;

        var saved = 0;
        if (kind == "activities")
        {
            var sessions = await SessionsAroundAsync(db, clientId);
            foreach (var r in records.EnumerateArray())
            {
                if (GarminParser.ActivityOf(r) is not { } a) continue;
                var row = await db.WearableActivities.FirstOrDefaultAsync(x => x.Provider == Garmin && x.ExternalId == a.ExternalId);
                if (row is null) db.WearableActivities.Add(row = new WearableActivity { Provider = Garmin, ExternalId = a.ExternalId });
                row.ClientId = clientId;
                row.Name = Cut(a.Name, 150);
                row.Type = Cut(a.Type, 60);
                row.StartUtc = a.StartUtc;
                row.DurationSeconds = a.DurationSeconds;
                row.AverageHeartRate = a.AverageHeartRate;
                row.MaxHeartRate = a.MaxHeartRate;
                row.Kilocalories = a.Kilocalories;
                row.DistanceMeters = a.DistanceMeters;
                row.DeviceName = a.DeviceName is null ? row.DeviceName : Cut(a.DeviceName, 100);
                row.SessionId = ReadinessRules.MatchSession(clock.ToWallClock(a.StartUtc), a.DurationSeconds, sessions);
                row.UpdatedAt = DateTime.UtcNow;
                if (a.DeviceName is not null) link.DeviceName = Cut(a.DeviceName, 100);
                saved++;
            }
        }
        else if (GarminParser.Kinds.Contains(kind))
        {
            foreach (var r in records.EnumerateArray())
            {
                if (GarminParser.Day(kind, r) is not { } p) continue;
                var row = db.WearableDays.Local.FirstOrDefault(d => d.ClientId == clientId && d.Provider == Garmin && d.Date == p.Date)
                    ?? await db.WearableDays.FirstOrDefaultAsync(d => d.ClientId == clientId && d.Provider == Garmin && d.Date == p.Date);
                if (row is null) db.WearableDays.Add(row = new WearableDay { ClientId = clientId, Provider = Garmin, Date = p.Date });
                Merge(row, p);
                saved++;
            }
        }
        if (saved > 0) link.LastDataAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return saved;
    }

    /// <summary>Uzupełnia dzień o pola z tego rodzaju danych (puste pola nie kasują wcześniejszych wartości).</summary>
    public static void Merge(WearableDay row, GarminParser.DayPart p)
    {
        row.Steps = p.Steps ?? row.Steps;
        row.RestingHeartRate = p.RestingHeartRate ?? row.RestingHeartRate;
        row.AverageStress = p.AverageStress ?? row.AverageStress;
        row.BodyBatteryHigh = p.BodyBatteryHigh ?? row.BodyBatteryHigh;
        row.BodyBatteryLow = p.BodyBatteryLow ?? row.BodyBatteryLow;
        row.ActiveKilocalories = p.ActiveKilocalories ?? row.ActiveKilocalories;
        row.SleepMinutes = p.SleepMinutes ?? row.SleepMinutes;
        row.DeepSleepMinutes = p.DeepSleepMinutes ?? row.DeepSleepMinutes;
        row.LightSleepMinutes = p.LightSleepMinutes ?? row.LightSleepMinutes;
        row.RemSleepMinutes = p.RemSleepMinutes ?? row.RemSleepMinutes;
        row.AwakeMinutes = p.AwakeMinutes ?? row.AwakeMinutes;
        row.SleepScore = p.SleepScore ?? row.SleepScore;
        row.HrvLastNight = p.HrvLastNight ?? row.HrvLastNight;
        row.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<List<(int Id, DateTime Start, DateTime End)>> SessionsAroundAsync(ApplicationDbContext db, int clientId)
    {
        var from = clock.LocalNow.AddDays(-45);
        return (await db.Sessions.AsNoTracking()
                .Where(s => s.ClientId == clientId && s.StartTime >= from && s.Status != SessionStatus.Cancelled)
                .Select(s => new { s.Id, s.StartTime, s.SessionType.DurationMinutes }).ToListAsync())
            .Select(s => (s.Id, s.StartTime, s.StartTime.AddMinutes(Math.Max(15, s.DurationMinutes)))).ToList();
    }

    public async Task<Summary> SummaryAsync(int clientId, int days = 28)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var link = await db.WearableLinks.AsNoTracking().FirstOrDefaultAsync(l => l.ClientId == clientId && l.Provider == Garmin);
        var since = clock.Today.AddDays(-days);
        var rows = await db.WearableDays.AsNoTracking().Where(d => d.ClientId == clientId && d.Date >= since).OrderByDescending(d => d.Date).ToListAsync();
        var acts = await db.WearableActivities.AsNoTracking().Where(a => a.ClientId == clientId)
            .OrderByDescending(a => a.StartUtc).Take(20).ToListAsync();
        return new Summary(link, rows, acts, ReadinessRules.For(clock.Today, rows.Select(ToReadinessDay).ToList()));
    }

    /// <summary>Gotowość kilku klientów naraz (ekran Dziś) — tylko z połączonym zegarkiem i świeżymi danymi.</summary>
    public async Task<Dictionary<int, ReadinessRules.Result>> ReadinessAsync(IReadOnlyCollection<int> clientIds)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await ReadinessAsync(db, clock.Today, clientIds);
    }

    /// <summary>Gotowość na <paramref name="today"/> dla klientów z połączonym zegarkiem i świeżymi danymi (bez „brak danych”).</summary>
    public static async Task<Dictionary<int, ReadinessRules.Result>> ReadinessAsync(ApplicationDbContext db, DateOnly today, IReadOnlyCollection<int> clientIds)
    {
        if (clientIds.Count == 0) return [];
        var connected = await db.WearableLinks.AsNoTracking()
            .Where(l => clientIds.Contains(l.ClientId) && l.ConnectedAt != null && l.DisconnectedAt == null).Select(l => l.ClientId).ToListAsync();
        if (connected.Count == 0) return [];
        var since = today.AddDays(-29);
        var rows = await db.WearableDays.AsNoTracking().Where(d => connected.Contains(d.ClientId) && d.Date >= since).ToListAsync();
        return rows.GroupBy(r => r.ClientId)
            .Select(g => (g.Key, ReadinessRules.For(today, g.Select(ToReadinessDay).ToList())))
            .Where(x => x.Item2.Level != ReadinessRules.Level.Unknown)
            .ToDictionary(x => x.Key, x => x.Item2);
    }

    /// <summary>Klienci z połączonym zegarkiem — do ikonki Garmina przy nazwisku.</summary>
    public async Task<HashSet<int>> ConnectedClientIdsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return (await db.WearableLinks.AsNoTracking().Where(l => l.ConnectedAt != null && l.DisconnectedAt == null)
            .Select(l => l.ClientId).ToListAsync()).ToHashSet();
    }

    /// <summary>
    /// Podpis źródła wymagany przez wytyczne marki Garmina: „Garmin [model]” (bez modelu — samo „Garmin”),
    /// umieszczany bezpośrednio pod tytułem widoku z danymi, nigdy w stopce.
    /// </summary>
    public static string Attribution(string? device)
    {
        var d = device?.Trim();
        if (string.IsNullOrEmpty(d)) return "Garmin";
        return d.StartsWith("Garmin", StringComparison.OrdinalIgnoreCase) ? d : $"Garmin {d}";
    }

    /// <summary>Dopisek przy wnioskach liczonych z danych (gotowość, podsumowania) — „Insights derived in part from Garmin device-sourced data”.</summary>
    public const string DerivedNote = "wnioski częściowo z danych z urządzenia Garmin";

    /// <summary>Jedna linijka dla trenera: „Garmin: gotowość niska — krótki sen (5 h 10 min), HRV poniżej normy”.</summary>
    public static string Line(ReadinessRules.Result r, int reasons = 2) =>
        $"Garmin: gotowość {ReadinessRules.Label(r.Level)}" + (r.Reasons.Count > 0 && r.Level != ReadinessRules.Level.Good ? $" — {string.Join(", ", r.Reasons.Take(reasons))}" : "");

    public static ReadinessRules.Day ToReadinessDay(WearableDay d) =>
        new(d.Date, d.SleepMinutes, d.SleepScore, d.RestingHeartRate, d.HrvLastNight, d.BodyBatteryHigh);

    private static string Cut(string v, int max) => v.Length > max ? v[..max] : v;
}
