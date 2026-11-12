using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Exceptions;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

/// <summary>
/// „Szablon tygodnia”: wizyty z wybranego tygodnia kopiowane na kolejne tygodnie (te same dni, godziny, klienci
/// i rodzaje treningu). Najpierw podgląd — co wejdzie, co koliduje, co już jest — potem jedno kliknięcie.
/// Wizyty z serii pomijamy (seria sama tworzy kolejne terminy), treningi w parze kopiujemy razem z partnerem.
/// </summary>
public class WeekTemplateService(IDbContextFactory<ApplicationDbContext> dbFactory, ISessionService sessions,
    ITrainerAvailabilityService availability, IAppClock clock)
{
    public sealed record Source(int SessionId, int ClientId, string ClientName, int SessionTypeId, string TypeName, int Minutes,
        DateTime Start, int? PartnerClientId, string? Skip);

    public enum TargetState { Ok, Conflict, Exists, Past }

    public sealed record Target(int SessionId, DateTime Start, TargetState State, string? Note);

    /// <summary>Wizyty trenera w tygodniu od <paramref name="weekStart"/> (bez odwołanych i bez drugiej osoby z pary).</summary>
    public async Task<List<Source>> SourcesAsync(string trainerUserId, DateOnly weekStart)
    {
        var from = weekStart.ToDateTime(TimeOnly.MinValue);
        var to = from.AddDays(7);
        var list = await sessions.GetSessionsAsync(from, to, trainerUserId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var ids = list.Select(s => s.Id).ToList();
        var inSeries = (await db.Sessions.AsNoTracking().Where(s => ids.Contains(s.Id) && s.SeriesId != null).Select(s => s.Id).ToListAsync()).ToHashSet();
        return list
            .Where(s => s.Status != SessionStatus.Cancelled && !s.IsPairFollower)
            .OrderBy(s => s.StartTime)
            .Select(s => new Source(s.Id, s.ClientId, s.DisplayName, s.SessionTypeId, s.SessionTypeName, s.DurationMinutes, s.StartTime,
                s.IsPair ? s.PartnerClientId : null,
                inSeries.Contains(s.Id) ? "w serii — seria sama doda kolejne terminy" : null))
            .ToList();
    }

    /// <summary>Podgląd: dla każdej wizyty i każdego z <paramref name="weeks"/> kolejnych tygodni — czy termin jest wolny.</summary>
    public async Task<List<Target>> PreviewAsync(string trainerUserId, IReadOnlyList<Source> picked, int weeks)
    {
        var now = clock.LocalNow;
        var result = new List<Target>();
        await using var db = await dbFactory.CreateDbContextAsync();
        foreach (var s in picked)
        {
            for (var w = 1; w <= weeks; w++)
            {
                var start = s.Start.AddDays(7 * w);
                if (start <= now) { result.Add(new(s.SessionId, start, TargetState.Past, "termin już minął")); continue; }
                var exists = await db.Sessions.AnyAsync(x => x.ClientId == s.ClientId && x.StartTime == start && x.Status != SessionStatus.Cancelled);
                if (exists) { result.Add(new(s.SessionId, start, TargetState.Exists, "już zaplanowana")); continue; }
                var conflict = await availability.FindConflictAsync(trainerUserId, start, s.Minutes);
                result.Add(conflict is null
                    ? new(s.SessionId, start, TargetState.Ok, null)
                    : new(s.SessionId, start, TargetState.Conflict, string.IsNullOrWhiteSpace(conflict.ClientName) ? "zajęte" : $"zajęte: {conflict.ClientName}"));
            }
        }
        return result;
    }

    public sealed record Outcome(int Created, int Skipped, List<string> Errors);

    /// <summary>Tworzy wizyty tylko w wolnych terminach z podglądu (kolizje i istniejące pomija).</summary>
    public async Task<Outcome> ApplyAsync(string trainerUserId, IReadOnlyList<Source> picked, int weeks, bool notifyClients)
    {
        var targets = await PreviewAsync(trainerUserId, picked, weeks);
        var bySource = picked.ToDictionary(s => s.SessionId);
        int created = 0, skipped = 0;
        var errors = new List<string>();
        foreach (var t in targets)
        {
            if (t.State != TargetState.Ok) { skipped++; continue; }
            var s = bySource[t.SessionId];
            var dto = new CreateSessionDto { ClientId = s.ClientId, SessionTypeId = s.SessionTypeId, TrainerUserId = trainerUserId, StartTime = t.Start };
            try
            {
                if (s.PartnerClientId is { } partner)
                    await sessions.CreatePairSessionAsync(dto, partner, allowAwaitingPackage: true, allowOverlap: false, sendConfirmation: notifyClients);
                else
                    await sessions.CreateSessionAsync(dto, allowAwaitingPackage: true, allowOverlap: false, sendConfirmation: notifyClients);
                created++;
            }
            catch (SlotConflictException) { skipped++; }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                skipped++;
                if (errors.Count < 5) errors.Add($"{s.ClientName} {t.Start:dd.MM HH:mm}: {ex.Message}");
            }
        }
        return new(created, skipped, errors);
    }
}
