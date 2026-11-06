using Microsoft.EntityFrameworkCore;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

/// <summary>
/// Szkic notatki trenera z zapisanego treningu (seria po serii): co zrobiono, najlepszy ciężar,
/// rekordy. Z włączonym AI asystent pisze to ludzkim językiem — trener i tak zatwierdza.
/// Do AI idą tylko nazwy ćwiczeń i liczby, bez danych klienta.
/// </summary>
public sealed class WorkoutNoteDraftService(IDbContextFactory<ApplicationDbContext> dbFactory, PlatformAssistantService assistant)
{
    public sealed record Line(string Name, int Sets, string Detail, decimal TopKg, bool Record);

    /// <summary>Ćwiczenia zapisane tego dnia — puste, gdy treningu nie było.</summary>
    public async Task<List<Line>> GetLinesAsync(int clientId, DateOnly day)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var logs = await db.WorkoutLogs.AsNoTracking()
            .Where(l => l.ClientId == clientId && l.WorkoutDate == day)
            .OrderBy(l => l.Id)
            .Select(l => new
            {
                l.ExerciseId, Name = l.Exercise!.NamePl,
                Sets = l.Sets.OrderBy(s => s.SetNumber).Select(s => new { s.Reps, s.WeightKg, s.DurationSeconds, s.DistanceMeters }).ToList()
            })
            .ToListAsync();
        if (logs.Count == 0) return [];

        var ids = logs.Select(l => l.ExerciseId).Distinct().ToList();
        var before = await db.WorkoutLogs.AsNoTracking()
            .Where(l => l.ClientId == clientId && l.WorkoutDate < day && ids.Contains(l.ExerciseId))
            .SelectMany(l => l.Sets.Select(s => new { l.ExerciseId, s.WeightKg }))
            .GroupBy(x => x.ExerciseId)
            .Select(g => new { g.Key, Max = g.Max(x => x.WeightKg) })
            .ToDictionaryAsync(x => x.Key, x => x.Max);

        return logs.Select(l =>
        {
            var top = l.Sets.Select(s => s.WeightKg).DefaultIfEmpty(0).Max();
            string detail;
            if (l.Sets.Any(s => s.DistanceMeters > 0))
                detail = $"{l.Sets.Sum(s => s.DistanceMeters ?? 0) / 1000m:0.##} km";
            else if (l.Sets.All(s => s.WeightKg == 0) && l.Sets.Any(s => s.DurationSeconds > 0))
                detail = $"{l.Sets.Count} × {l.Sets.Max(s => s.DurationSeconds ?? 0)} s";
            else
                detail = $"{l.Sets.Count} × {string.Join("/", l.Sets.Select(s => s.Reps))}" + (top > 0 ? $" · do {top:0.##} kg" : "");
            var record = top > 0 && before.TryGetValue(l.ExerciseId, out var prev) && top > prev;
            return new Line(l.Name, l.Sets.Count, detail, top, record);
        }).ToList();
    }

    /// <summary>Gotowy szkic albo null, gdy tego dnia nie było zapisanego treningu.</summary>
    public async Task<string?> DraftAsync(int clientId, DateOnly day, string cacheKey)
    {
        var lines = await GetLinesAsync(clientId, day);
        if (lines.Count == 0) return null;
        var records = lines.Where(l => l.Record).Select(l => $"{l.Name} {l.TopKg:0.##} kg").ToList();
        var rules = "Zrobione: " + string.Join("; ", lines.Take(8).Select(l => $"{l.Name} {l.Detail}")) + "."
            + (records.Count > 0 ? $" Rekord: {string.Join(", ", records)}." : "");
        if (rules.Length > 590) rules = rules[..587] + "…";

        PlatformAssistantService.Config? config = null;
        try { config = await assistant.GetConfigAsync(); } catch { }
        if (config?.Ai != true) return rules;

        var facts = new Dictionary<string, string>
        {
            ["cwiczenia"] = Trim(string.Join(", ", lines.Select(l => l.Name))),
            ["serie_razem"] = lines.Sum(l => l.Sets).ToString(),
            ["szczegoly"] = Trim(string.Join("; ", lines.Take(5).Select(l => $"{l.Name} {l.Detail}"))),
            ["rekordy"] = records.Count == 0 ? "brak" : Trim(string.Join(", ", records)),
        };
        try { return await assistant.PhraseAsync(cacheKey, "notatka_po_treningu", facts, rules, true); }
        catch { return rules; }
    }

    private static string Trim(string s) => s.Length > 158 ? s[..155] + "…" : s;
}
