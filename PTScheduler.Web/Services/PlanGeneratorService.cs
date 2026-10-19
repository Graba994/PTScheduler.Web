using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Surveys;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;
using G = PTScheduler.Domain.Rules.PlanGeneratorRules;

namespace PTScheduler.Web.Services;

/// <summary>
/// „Zaproponuj plan” w kreatorze: historia klienta (ostatnie treningi z RPE, cel, częstotliwość, ankieta zdrowotna,
/// zgłoszony ból) → propozycja z reguł (<see cref="PlanGeneratorRules"/>), opcjonalnie dopracowana przez AI w Portalu
/// (włącza administrator platformy). Do AI nie idą imiona ani dane kontaktowe — tylko cel, wiek w przedziale, obszary
/// urazów, liczby z treningów i lista ćwiczeń. Odpowiedź AI jest sprawdzana; przy błędzie zostaje plan z reguł.
/// Nic nie zapisuje się samo — trener wstawia propozycję do edytora i dopiero ją zapisuje.
/// </summary>
public class PlanGeneratorService(IDbContextFactory<ApplicationDbContext> dbFactory, RetentionService retention,
    PlatformAssistantService assistant, IAppClock clock, ILogger<PlanGeneratorService> logger)
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");
    private const int HistoryDays = 180;

    public sealed record Profile(int ClientId, string FirstName, string? Goal, int? Age, double SessionsPerWeek, int WorkoutDays,
        int KnownExercises, List<string> HealthAreas, G.Focus SuggestedFocus, int SuggestedDays);

    public sealed record Proposal(G.Result Plan, bool FromAi, string? AiRationale, string? AiNote);

    public async Task<Profile?> ProfileAsync(int clientId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var c = await db.Clients.AsNoTracking().FirstOrDefaultAsync(x => x.Id == clientId);
        if (c is null) return null;
        var today = clock.Today;
        int? age = c.DateOfBirth is { } dob ? today.Year - dob.Year - (today < dob.AddYears(today.Year - dob.Year) ? 1 : 0) : null;
        var from = clock.LocalNow.AddDays(-56);
        var sessions = await db.Sessions.CountAsync(s => s.ClientId == clientId && s.StartTime >= from && s.StartTime <= clock.LocalNow
                                                       && (s.Status == SessionStatus.Completed || s.Status == SessionStatus.Scheduled));
        var since = today.AddDays(-HistoryDays);
        var logs = await db.WorkoutLogs.AsNoTracking().Where(l => l.ClientId == clientId && l.WorkoutDate >= since)
            .Select(l => new { l.WorkoutDate, l.ExerciseId }).ToListAsync();
        var workoutDays = logs.Select(l => l.WorkoutDate).Distinct().Count();
        var recentWorkoutDays = logs.Where(l => l.WorkoutDate >= today.AddDays(-56)).Select(l => l.WorkoutDate).Distinct().Count();
        var perWeek = Math.Round(Math.Max(sessions, recentWorkoutDays) / 8.0, 1);
        var (notes, pregnancy, heart) = await retention.HealthNotesAsync(clientId);
        var areas = HealthAreas(notes, pregnancy, heart);
        var focus = G.FocusFromGoal(c.TrainingGoal);
        var days = perWeek >= 3.5 ? 4 : perWeek >= 2.5 ? 3 : 2;
        return new Profile(c.Id, c.FirstName, c.TrainingGoal, age, perWeek, workoutDays, logs.Select(l => l.ExerciseId).Distinct().Count(),
            areas, focus, days);
    }

    /// <summary>Obszary z notatek zdrowotnych (bez treści notatek) — do pokazania trenerowi i do AI.</summary>
    private static List<string> HealthAreas(List<HealthPlanRules.Note> notes, bool pregnancy, bool heart)
    {
        var probe = new[]
        {
            ("przysiad", "quadriceps"), ("martwy ciąg", "lower back"), ("wyciskanie nad głowę", "shoulders"),
            ("pompki", "forearms"), ("skoki", "calves"), ("hip thrust", "adductors"), ("szrugsy", "neck")
        };
        var areas = new List<string>();
        foreach (var (name, muscle) in probe)
            if (HealthPlanRules.Conflict(notes, pregnancy, heart, new(name, muscle, "", ExerciseCategory.Strength)) is { } a && !areas.Contains(a))
                areas.Add(a);
        return areas;
    }

    public async Task<Proposal> ProposeAsync(int clientId, string trainerUserId, G.Options options, bool useAi, string? trainerHint)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var catalog = (await db.Exercises.AsNoTracking()
                .Where(e => e.Visibility == ExerciseVisibility.Public || e.OwnerTrainerUserId == trainerUserId)
                .Select(e => new { e.Id, e.NamePl, e.PrimaryMuscles, e.SecondaryMuscles, e.Category, e.Equipment, e.Force, e.Mechanic, e.Level, e.Tracking })
                .ToListAsync())
            .Select(e => new G.Candidate(e.Id, e.NamePl, e.PrimaryMuscles, e.SecondaryMuscles, e.Category, e.Equipment, e.Force, e.Mechanic, e.Level, e.Tracking))
            .ToList();
        var history = await HistoryAsync(db, clientId);
        var (notes, pregnancy, heart) = await retention.HealthNotesAsync(clientId);
        string? Conflict(G.Candidate c) => HealthPlanRules.Conflict(notes, pregnancy, heart, new(c.Name, c.PrimaryMuscles, c.SecondaryMuscles, c.Category));

        var plan = G.Build(options, catalog, history, Conflict);
        if (!useAi || plan.Days.Count == 0) return new Proposal(plan, false, null, null);

        var profile = await ProfileAsync(clientId);
        try
        {
            var surveys = await SurveyFactsAsync(db, clientId);
            var refined = await RefineWithAiAsync(plan, options, profile, catalog, history, Conflict, trainerHint, surveys);
            return refined ?? new Proposal(plan, false, null, "AI nie odpowiedziało poprawnie — pokazuję plan z historii (bez AI).");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Generator planu: AI niedostępne.");
            return new Proposal(plan, false, null, "AI jest teraz niedostępne — pokazuję plan z historii (bez AI).");
        }
    }

    /// <summary>Historia: dni treningowe ćwiczenia, ostatni najcięższy zestaw i średnie RPE z ankiet po tych treningach.</summary>
    private async Task<Dictionary<int, G.History>> HistoryAsync(ApplicationDbContext db, int clientId)
    {
        var since = clock.Today.AddDays(-HistoryDays);
        var logs = await db.WorkoutLogs.AsNoTracking().Where(l => l.ClientId == clientId && l.WorkoutDate >= since)
            .Select(l => new { l.ExerciseId, l.WorkoutDate, Sets = l.Sets.Select(s => new { s.Reps, s.WeightKg }).ToList() })
            .ToListAsync();
        var rpeByDay = new Dictionary<DateOnly, decimal>();
        var posts = await db.SurveyResponses.AsNoTracking()
            .Where(r => r.ClientId == clientId && r.Kind == SurveyKind.PostWorkout && r.WorkoutDate != null && r.WorkoutDate >= since)
            .Select(r => new { r.WorkoutDate, r.AnswersJson }).ToListAsync();
        foreach (var p in posts)
            if (SurveyJson.Deserialize(p.AnswersJson, new List<SurveyAnswer>()).FirstOrDefault(a => a.Key == SurveyKeys.Rpe)?.Number is decimal rpe)
                rpeByDay[p.WorkoutDate!.Value] = rpe;

        var result = new Dictionary<int, G.History>();
        foreach (var g in logs.Where(l => l.Sets.Count > 0).GroupBy(l => l.ExerciseId))
        {
            var dates = g.Select(l => l.WorkoutDate).Distinct().OrderBy(d => d).ToList();
            var last = g.Where(l => l.WorkoutDate == dates[^1]).SelectMany(l => l.Sets).ToList();
            var top = last.OrderByDescending(s => s.WeightKg).ThenByDescending(s => s.Reps).First();
            var rpes = dates.TakeLast(3).Where(rpeByDay.ContainsKey).Select(d => rpeByDay[d]).ToList();
            result[g.Key] = new G.History(g.Key, dates.Count, top.WeightKg, top.Reps, rpes.Count > 0 ? Math.Round(rpes.Average(), 1) : null, dates[^1]);
        }
        return result;
    }

    /// <summary>
    /// Ankiety po treningach z ostatnich 8 tygodni: średnie (energia, sen, zakwasy, zadowolenie), gdzie bolało
    /// i ostatnie komentarze — bez imienia i nazwiska klienta, adresów i telefonów.
    /// </summary>
    private async Task<object?> SurveyFactsAsync(ApplicationDbContext db, int clientId)
    {
        var since = clock.Today.AddDays(-56);
        var name = await db.Clients.AsNoTracking().Where(c => c.Id == clientId).Select(c => new { c.FirstName, c.LastName }).FirstOrDefaultAsync();
        var rows = await db.SurveyResponses.AsNoTracking()
            .Where(r => r.ClientId == clientId && r.Kind == SurveyKind.PostWorkout && r.WorkoutDate != null && r.WorkoutDate >= since)
            .OrderByDescending(r => r.WorkoutDate).Take(24).Select(r => r.AnswersJson).ToListAsync();
        var answers = rows.Select(j => SurveyJson.Deserialize(j, new List<SurveyAnswer>())).ToList();
        decimal? Avg(string key)
        {
            var v = answers.Select(a => a.FirstOrDefault(x => x.Key == key)?.Number).OfType<decimal>().ToList();
            return v.Count > 0 ? Math.Round(v.Average(), 1) : null;
        }
        var comments = answers.Select(a => a.FirstOrDefault(x => x.Key == SurveyKeys.Comment)?.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t)).Take(6)
            .Select(t => Mask(InboxSafe(t)!, name?.FirstName, name?.LastName)).ToList();
        // Komentarze do dni treningowych (trener ↔ klient) — np. „kolano dało znać przy wykrokach”.
        var dayComments = (await db.WorkoutComments.AsNoTracking()
                .Where(c => c.ClientId == clientId && c.WorkoutDate >= since)
                .OrderByDescending(c => c.CreatedAt).Take(8).Select(c => new { c.ByTrainer, c.Text }).ToListAsync())
            .Select(c => (c.ByTrainer ? "trener: " : "podopieczny: ") + Mask(InboxSafe(c.Text) ?? "", name?.FirstName, name?.LastName)).ToList();
        var pain = answers.Select(a => a.FirstOrDefault(x => x.Key == SurveyKeys.PainLocation)?.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim().ToLowerInvariant()).Distinct().Take(5).ToList();
        return new
        {
            ankiet = rows.Count,
            rpe = Avg(SurveyKeys.Rpe),
            energia = Avg(SurveyKeys.Energy),
            sen = Avg(SurveyKeys.Sleep),
            zakwasy = Avg(SurveyKeys.Soreness),
            zadowolenie = Avg(SurveyKeys.Satisfaction),
            bol_gdzie = pain,
            komentarze = comments,
            komentarze_do_treningow = dayComments
        };
    }

    private static string Mask(string text, string? first, string? last)
    {
        foreach (var n in new[] { first, last }.Where(n => n is { Length: >= 2 }))
            text = System.Text.RegularExpressions.Regex.Replace(text, System.Text.RegularExpressions.Regex.Escape(n!), "[klient]",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return text.Length <= 200 ? text : text[..200];
    }

    // ── AI ───────────────────────────────────────────────────────────────────────────────

    private sealed record AiPlan(string? Name, string? Purpose, string? Rationale, List<AiDay>? Days);
    private sealed record AiDay(string? Label, List<AiExercise>? Exercises);
    private sealed record AiExercise(int Id, int Sets, string? Reps, decimal? WeightKg, int? RestSeconds, string? Note);

    private async Task<Proposal?> RefineWithAiAsync(G.Result plan, G.Options o, Profile? profile, List<G.Candidate> catalog,
        Dictionary<int, G.History> history, Func<G.Candidate, string?> conflict, string? hint, object? surveys)
    {
        // Kandydaci dla AI: ćwiczenia z planu, znane klientowi i po kilka bezpiecznych na każdą partię — z limitem.
        var planIds = plan.Days.SelectMany(d => d.Exercises).Select(e => e.ExerciseId).ToHashSet();
        var safe = catalog.Where(c => conflict(c) is null && c.Category is ExerciseCategory.Strength or ExerciseCategory.Powerlifting or ExerciseCategory.Cardio or ExerciseCategory.Plyometrics).ToList();
        var pool = safe.Where(c => planIds.Contains(c.Id) || history.ContainsKey(c.Id)).ToList();
        foreach (var muscle in safe.SelectMany(c => c.PrimaryMuscles.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).Distinct())
            pool.AddRange(safe.Where(c => c.Level != ExerciseLevel.Expert && c.PrimaryMuscles.StartsWith(muscle, StringComparison.OrdinalIgnoreCase)).Take(4));
        pool = pool.DistinctBy(c => c.Id).Take(110).ToList();
        var byId = pool.ToDictionary(c => c.Id);

        var facts = new Dictionary<string, object?>
        {
            ["cel"] = InboxSafe(profile?.Goal),
            ["profil"] = G.FocusLabel(o.Focus),
            ["wiek"] = profile?.Age is int a ? $"{a / 10 * 10}–{a / 10 * 10 + 9}" : null,
            ["dni_w_tygodniu"] = o.DaysPerWeek,
            ["minut_na_trening"] = o.Minutes,
            ["sprzet"] = o.Kit switch { G.Kit.Gym => "siłownia", G.Kit.Dumbbells => "hantle / dom", _ => "własnym ciężarem ciała (bez sprzętu)" },
            ["treningi_tygodniowo_ostatnio"] = profile?.SessionsPerWeek,
            ["urazy_obszary"] = profile?.HealthAreas,
            ["uwagi_trenera"] = InboxSafe(hint),
        };
        var payload = new
        {
            facts,
            surveys,
            draft = plan.Days.Select(d => new { label = d.Label, exercises = d.Exercises.Select(e => new { id = e.ExerciseId, name = e.Name, sets = e.Sets, reps = e.Reps, weightKg = e.WeightKg, restSeconds = e.RestSeconds }) }),
            history = history.Values.Where(h => byId.ContainsKey(h.ExerciseId)).OrderByDescending(h => h.TimesDone).Take(25)
                .Select(h => new { id = h.ExerciseId, times = h.TimesDone, lastKg = h.LastWeightKg, lastReps = h.LastReps, rpe = h.AvgRpe }),
            exercises = pool.Select(c => new { id = c.Id, name = c.Name, muscles = c.PrimaryMuscles, equipment = c.Equipment })
        };

        var (text, fromAi) = await assistant.PlanAsync(JsonSerializer.Serialize(payload));
        if (!fromAi || string.IsNullOrWhiteSpace(text)) return null;
        var json = ExtractJson(text);
        if (json is null) return null;
        AiPlan? ai;
        try { ai = JsonSerializer.Deserialize<AiPlan>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch (JsonException) { return null; }
        if (ai?.Days is not { Count: >= 1 and <= 7 }) return null;

        var rulePicks = plan.Days.SelectMany(d => d.Exercises).GroupBy(e => e.ExerciseId).ToDictionary(g => g.Key, g => g.First());
        var days = new List<G.Day>();
        foreach (var d in ai.Days)
        {
            var picks = new List<G.Pick>();
            foreach (var e in d.Exercises ?? [])
            {
                if (!byId.TryGetValue(e.Id, out var c) || picks.Any(p => p.ExerciseId == e.Id)) continue;
                var sets = Math.Clamp(e.Sets, 1, 8);
                var reps = string.IsNullOrWhiteSpace(e.Reps) ? null : e.Reps.Trim()[..Math.Min(12, e.Reps.Trim().Length)];
                var rest = Math.Clamp(e.RestSeconds ?? 90, 15, 300);
                history.TryGetValue(e.Id, out var h);
                rulePicks.TryGetValue(e.Id, out var rule);
                // Ciężar: tylko w rozsądnym zakresie od historii — inaczej ciężar z reguł.
                var weight = e.WeightKg is > 0 and < 400 ? e.WeightKg : null;
                if (h is { LastWeightKg: > 0 } && weight is decimal w && (w > h.LastWeightKg * 1.2m || w < h.LastWeightKg * 0.6m)) weight = rule?.WeightKg ?? h.LastWeightKg;
                if (h is null && weight is not null) weight = null; // nowego ćwiczenia AI nie zna — ciężar dobiera trener
                var why = rule?.Why ?? (h is null ? "nowe — dobierz ciężar na pierwszym treningu" : $"znane ({h.TimesDone}×)");
                if (!string.IsNullOrWhiteSpace(e.Note)) why = $"{why} · {e.Note.Trim()[..Math.Min(120, e.Note.Trim().Length)]}";
                picks.Add(new G.Pick(c.Id, c.Name, sets, c.Tracking == ExerciseTracking.Time ? null : reps,
                    c.Tracking == ExerciseTracking.Time ? 40 : null, weight, rest, why, h is not null));
                if (picks.Count >= 10) break;
            }
            if (picks.Count >= 2) days.Add(new G.Day(Clip(d.Label, 60) ?? $"Dzień {days.Count + 1}", picks));
        }
        if (days.Count == 0) return null;
        var result = plan with
        {
            Name = Clip(ai.Name, 120) ?? plan.Name,
            Purpose = Clip(ai.Purpose, 800) ?? plan.Purpose,
            Days = days
        };
        return new Proposal(result, true, Clip(ai.Rationale, 900), null);
    }

    /// <summary>Do AI bez adresów e-mail i telefonów, najwyżej 300 znaków.</summary>
    private static string? InboxSafe(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = System.Text.RegularExpressions.Regex.Replace(text, @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", "[e-mail]");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(?<!\w)\+?\d[\d \-]{7,}\d(?!\w)", "[telefon]");
        return s.Length <= 300 ? s : s[..300];
    }

    private static string? Clip(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length <= max ? s.Trim() : s.Trim()[..max];

    private static string? ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    /// <summary>Propozycja → plan w edytorze (nic nie zapisuje).</summary>
    public static PlanEditDto ToEditDto(G.Result plan, int clientId, Dictionary<int, (string? Thumb, ExerciseTracking Tracking)> meta) => new()
    {
        Name = plan.Name,
        Purpose = plan.Purpose,
        ClientId = clientId,
        Notes = "Propozycja z historii klienta — sprawdź przed zapisaniem.",
        Days = plan.Days.Select((d, di) => new PlanDayEditDto
        {
            Order = di,
            Label = d.Label,
            Exercises = d.Exercises.Select((e, ei) => new PlanExerciseEditDto
            {
                ExerciseId = e.ExerciseId,
                ExerciseNamePl = e.Name,
                ThumbnailUrl = meta.GetValueOrDefault(e.ExerciseId).Thumb,
                Tracking = meta.GetValueOrDefault(e.ExerciseId).Tracking,
                Order = ei,
                Sets = e.Sets,
                Reps = e.Reps,
                TargetWeightKg = e.WeightKg,
                TargetDurationSeconds = e.DurationSeconds,
                RestSeconds = e.RestSeconds
            }).ToList()
        }).ToList()
    };

    public async Task<Dictionary<int, (string? Thumb, ExerciseTracking Tracking)>> MetaAsync(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.Exercises.AsNoTracking().Where(e => list.Contains(e.Id))
            .Select(e => new { e.Id, e.ImageUrls, e.Tracking, e.Equipment, e.Category, e.NameEn, e.NamePl }).ToListAsync();
        return rows.ToDictionary(r => r.Id, r => ((string?)r.ImageUrls.Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
            r.Tracking ?? ExerciseTrackingRules.Guess(r.Category, r.Equipment, r.NameEn, r.NamePl)));
    }
}
