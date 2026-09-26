using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class ClientAttentionService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IAppClock clock) : IClientAttentionService
{
    public async Task<List<ClientAttentionDto>> GetAsync(string? trainerUserId, bool includeTraining)
    {
        await using var db = dbFactory.CreateDbContext();

        var clients = await db.Clients.AsNoTracking()
            .Where(c => c.Status == ClientStatus.Active
                     && (trainerUserId == null || c.TrainerUserId == trainerUserId))
            .Select(c => new { c.Id, c.FirstName, c.LastName })
            .ToListAsync();
        if (clients.Count == 0) return [];
        var ids = clients.Select(c => c.Id).ToList();

        // Wizyty: StartTime to zegar ścienny → porównujemy z LocalNow.
        var now = clock.LocalNow;
        var sessions = await db.Sessions.AsNoTracking()
            .Where(s => ids.Contains(s.ClientId)
                     && s.Status != SessionStatus.Cancelled
                     && s.Status != SessionStatus.NoShow)
            .Select(s => new { s.ClientId, s.StartTime })
            .ToListAsync();
        var lastVisit = sessions.Where(s => s.StartTime < now)
            .GroupBy(s => s.ClientId).ToDictionary(g => g.Key, g => g.Max(s => s.StartTime));
        var hasUpcoming = sessions.Where(s => s.StartTime >= now).Select(s => s.ClientId).ToHashSet();

        // Pakiety: ExpiresAt/PurchasedAt jak w SessionPackageService (UTC).
        var utcNow = clock.UtcNow;
        var packages = await db.SessionPackages.AsNoTracking()
            .Where(p => ids.Contains(p.ClientId) && p.Status != PackageStatus.Cancelled)
            .Select(p => new { p.ClientId, p.Name, p.Status, p.TotalSessions, p.UsedSessions, p.ExpiresAt, p.PurchasedAt })
            .ToListAsync();
        var packagesByClient = packages.GroupBy(p => p.ClientId).ToDictionary(g => g.Key, g => g.ToList());

        Dictionary<int, DateTime> planAssigned = [];
        Dictionary<int, DateOnly> lastWorkout = [];
        Dictionary<int, List<DateOnly>> uncommented = [];
        if (includeTraining)
        {
            planAssigned = (await db.TrainingPlans.AsNoTracking()
                    .Where(p => p.ClientId != null && !p.IsTemplate && ids.Contains(p.ClientId!.Value))
                    .Select(p => new { ClientId = p.ClientId!.Value, p.CreatedAt })
                    .ToListAsync())
                .GroupBy(p => p.ClientId).ToDictionary(g => g.Key, g => g.Min(p => p.CreatedAt));
            lastWorkout = (await db.WorkoutLogs.AsNoTracking()
                    .Where(w => ids.Contains(w.ClientId))
                    .GroupBy(w => w.ClientId)
                    .Select(g => new { ClientId = g.Key, Last = g.Max(w => w.WorkoutDate) })
                    .ToListAsync())
                .ToDictionary(x => x.ClientId, x => x.Last);

            // Świeże treningi (ostatnie dni) bez komentarza trenera — okazja do informacji zwrotnej.
            var since = clock.Today.AddDays(-AttentionRules.NewWorkoutDays);
            var recentDays = await db.WorkoutLogs.AsNoTracking()
                .Where(w => ids.Contains(w.ClientId) && w.WorkoutDate >= since)
                .Select(w => new { w.ClientId, w.WorkoutDate })
                .Distinct()
                .ToListAsync();
            var commented = (await db.WorkoutComments.AsNoTracking()
                    .Where(c => ids.Contains(c.ClientId) && c.ByTrainer && c.WorkoutDate >= since)
                    .Select(c => new { c.ClientId, c.WorkoutDate })
                    .Distinct()
                    .ToListAsync())
                .Select(x => (x.ClientId, x.WorkoutDate)).ToHashSet();
            uncommented = recentDays
                .Where(d => !commented.Contains((d.ClientId, d.WorkoutDate)))
                .GroupBy(d => d.ClientId)
                .ToDictionary(g => g.Key, g => g.Select(d => d.WorkoutDate).OrderByDescending(d => d).ToList());
        }

        // Ankiety: nieprzejrzana ankieta zdrowotna oraz niepokojące ankiety po treningu (7 dni).
        var surveyFrom = clock.UtcNow.AddDays(-7);
        var surveys = await db.SurveyResponses.AsNoTracking()
            .Where(r => ids.Contains(r.ClientId) && r.ReviewedAt == null
                     && (r.Kind == SurveyKind.HealthIntake
                         || (r.Kind == SurveyKind.PostWorkout && r.FlagCount > 0 && r.SubmittedAt >= surveyFrom)))
            .Select(r => new { r.ClientId, r.Kind, r.FlagCount, r.WorkoutDate })
            .ToListAsync();
        var surveysByClient = surveys.GroupBy(s => s.ClientId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<ClientAttentionDto>();
        foreach (var c in clients)
        {
            var reasons = new List<AttentionReason>();

            // ── Dawno nie był, nic nie zaplanowane ─────────────────────────────
            if (lastVisit.TryGetValue(c.Id, out var lv) && !hasUpcoming.Contains(c.Id))
            {
                var days = (now.Date - lv.Date).Days;
                if (days >= AttentionRules.NoVisitDays)
                    reasons.Add(new(AttentionKind.NoVisit, AttentionSeverity.Warning,
                        $"Ostatnia wizyta {AttentionRules.Days(days)} temu, nic nie zaplanowane"));
            }

            // ── Pakiety ────────────────────────────────────────────────────────
            if (packagesByClient.TryGetValue(c.Id, out var pkgs))
            {
                var active = pkgs.Where(p => p.Status == PackageStatus.Active
                                          && (p.ExpiresAt == null || p.ExpiresAt > utcNow)).ToList();
                var remaining = active.Sum(p => Math.Max(0, p.TotalSessions - p.UsedSessions));

                if (remaining == 0)
                {
                    // Pakiet się skończył niedawno i nie ma następnego.
                    var lastEnded = pkgs.Max(p => p.ExpiresAt is { } e && e < utcNow ? e : p.PurchasedAt);
                    if ((utcNow - lastEnded).TotalDays <= AttentionRules.PackageEndedLookbackDays)
                        reasons.Add(new(AttentionKind.PackageEmpty, AttentionSeverity.Danger,
                            "Pakiet się skończył — brak aktywnego"));
                }
                else if (remaining <= AttentionRules.LowCreditsThreshold)
                {
                    reasons.Add(new(AttentionKind.LowCredits, AttentionSeverity.Warning,
                        $"Zostały {AttentionRules.Visits(remaining)} w pakiecie"));
                }

                foreach (var p in active.Where(p => p.ExpiresAt is not null))
                {
                    var left = p.TotalSessions - p.UsedSessions;
                    var daysLeft = (int)Math.Ceiling((p.ExpiresAt!.Value - utcNow).TotalDays);
                    if (left > 0 && daysLeft <= AttentionRules.ExpiringDaysTrainer)
                        reasons.Add(new(AttentionKind.PackageExpiring,
                            daysLeft <= 3 ? AttentionSeverity.Danger : AttentionSeverity.Warning,
                            $"„{p.Name}” wygasa za {AttentionRules.Days(Math.Max(daysLeft, 0))} — niewykorzystane: {left}"));
                }
            }

            // ── Plan treningowy bez treningów ─────────────────────────────────
            if (includeTraining && planAssigned.TryGetValue(c.Id, out var assignedAt))
            {
                var today = clock.Today;
                if (lastWorkout.TryGetValue(c.Id, out var lw))
                {
                    var days = today.DayNumber - lw.DayNumber;
                    if (days >= AttentionRules.NotTrainingDays)
                        reasons.Add(new(AttentionKind.NotTraining, AttentionSeverity.Warning,
                            $"Nie ćwiczy według planu od {AttentionRules.Days(days)}",
                            $"/trainer/activity/{c.Id}"));
                }
                else
                {
                    var days = (clock.UtcNow - assignedAt).Days;
                    if (days >= AttentionRules.NotTrainingDays)
                        reasons.Add(new(AttentionKind.NotTraining, AttentionSeverity.Warning,
                            $"Plan przypisany {AttentionRules.Days(days)} temu — brak treningów",
                            $"/trainer/activity/{c.Id}"));
                }
            }

            if (surveysByClient.TryGetValue(c.Id, out var sv))
            {
                var link = $"/clients/{c.Id}?tab=surveys";
                if (sv.FirstOrDefault(s => s.Kind == SurveyKind.HealthIntake) is { } health)
                    reasons.Add(health.FlagCount > 0
                        ? new(AttentionKind.HealthSurvey, AttentionSeverity.Danger,
                            $"Ankieta zdrowotna: {health.FlagCount} odp. wymagających uwagi — przejrzyj", link)
                        : new(AttentionKind.HealthSurvey, AttentionSeverity.Info,
                            "Nowa ankieta zdrowotna — przejrzyj", link));
                var flagged = sv.Where(s => s.Kind == SurveyKind.PostWorkout).ToList();
                if (flagged.Count > 0)
                    reasons.Add(new(AttentionKind.WorkoutSurveyFlag, AttentionSeverity.Warning,
                        flagged.Count == 1 && flagged[0].WorkoutDate is { } d
                            ? $"Uwaga w ankiecie po treningu {d:dd.MM} (np. ból)"
                            : $"{flagged.Count} {(flagged.Count <= 4 ? "ankiety" : "ankiet")} po treningu z uwagami", link));
            }

            if (uncommented.TryGetValue(c.Id, out var fresh) && fresh.Count > 0)
            {
                var label = fresh.Count == 1
                    ? $"Nowy trening {fresh[0]:dd.MM} — dodaj komentarz"
                    : $"{fresh.Count} nowe treningi bez komentarza";
                reasons.Add(new(AttentionKind.NewWorkout, AttentionSeverity.Info, label, $"/trainer/activity/{c.Id}"));
            }

            if (reasons.Count > 0)
                result.Add(new ClientAttentionDto
                {
                    ClientId = c.Id,
                    ClientName = $"{c.FirstName} {c.LastName}".Trim(),
                    Reasons = reasons.OrderByDescending(r => r.Severity).ToList()
                });
        }

        return result
            .OrderByDescending(r => r.Severity)
            .ThenByDescending(r => r.Reasons.Count)
            .ThenBy(r => r.ClientName)
            .ToList();
    }
}
