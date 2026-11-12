using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Wearables;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

/// <summary>
/// Plan na zegarek: trener wysyła dzień planu na zegarek klienta (Garmin Training API przez Portal). Trening trafia do
/// Garmin Connect i jest zaplanowany na wybrany dzień; ponowna wysyłka tego samego dnia planu na tę samą datę go zastępuje.
/// Działa, gdy platforma ma Training API, studio włączyło moduł, a klient ma połączony zegarek.
/// </summary>
public sealed class WearableWorkoutService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IModuleSettingsService modules,
    EntitlementService entitlements,
    ILogger<WearableWorkoutService> logger)
{
    public sealed record SendResult(bool Ok, string Message, IReadOnlyList<string> Unmatched);

    private static (bool On, DateTime At)? _portal;

    /// <summary>Platforma (Training API) i moduł studia — bez sprawdzania konkretnego klienta.</summary>
    public async Task<bool> EnabledAsync()
    {
        if (!entitlements.IsAllowed("Wearables") || !(await modules.GetAsync()).WearableWorkoutsEnabled) return false;
        if (_portal is { } c && DateTime.UtcNow - c.At < TimeSpan.FromMinutes(2)) return c.On;
        var on = false;
        try
        {
            var (ok, json, _) = await WearableService.CallPortalAsync(HttpMethod.Get, "/status", null);
            on = ok && json?.RootElement.TryGetProperty("workouts", out var w) == true && w.ValueKind == JsonValueKind.True;
        }
        catch (Exception ex) { logger.LogDebug(ex, "Status planów na zegarek niedostępny."); }
        _portal = (on, DateTime.UtcNow);
        return on;
    }

    public async Task<bool> CanSendAsync(int clientId)
    {
        if (!await EnabledAsync()) return false;
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.WearableLinks.AnyAsync(l => l.ClientId == clientId && l.ConnectedAt != null && l.DisconnectedAt == null);
    }

    /// <summary>Klienci, którym można wysłać trening (lista w edytorze planów itp.).</summary>
    public async Task<HashSet<int>> ConnectedAsync()
    {
        if (!await EnabledAsync()) return [];
        await using var db = await dbFactory.CreateDbContextAsync();
        return (await db.WearableLinks.Where(l => l.ConnectedAt != null && l.DisconnectedAt == null).Select(l => l.ClientId).ToListAsync()).ToHashSet();
    }

    public async Task<SendResult> SendPlanDayAsync(int clientId, int planDayId, DateOnly date, string? userId, int? sessionId = null)
    {
        if (!await CanSendAsync(clientId)) return new(false, "Plan na zegarek jest niedostępny — klient nie ma połączonego zegarka albo moduł jest wyłączony.", []);
        await using var db = await dbFactory.CreateDbContextAsync();
        var day = await db.Set<PlanDay>().AsNoTracking()
            .Include(d => d.Plan)
            .Include(d => d.Exercises).ThenInclude(e => e.Exercise)
            .FirstOrDefaultAsync(d => d.Id == planDayId);
        if (day?.Plan is null) return new(false, "Nie ma takiego dnia planu.", []);
        if (day.Plan.ClientId is int owner && owner != clientId) return new(false, "Ten plan należy do innego klienta.", []);

        var items = day.Exercises.OrderBy(e => e.Order).Where(e => e.Exercise is not null)
            .Select(e => new GarminWorkoutBuilder.Item(e.Exercise!.NameEn, e.Exercise.NamePl, e.Exercise.Tracking ?? Domain.Enums.ExerciseTracking.WeightReps,
                e.Sets, e.Reps, e.TargetWeightKg, e.TargetDurationSeconds, e.TargetDistanceMeters, e.RestSeconds, e.Notes))
            .ToList();
        var label = string.IsNullOrWhiteSpace(day.Label) ? $"Dzień {day.Order + 1}" : day.Label.Trim();
        var name = $"{day.Plan.Name.Trim()} · {label}";
        var built = GarminWorkoutBuilder.Build(name, $"Plan od trenera na {date:dd.MM.yyyy}.", items);
        if (built.Workout is null) return new(false, built.Error ?? "Nie udało się przygotować treningu.", built.Unmatched);

        var previous = await db.WearableWorkouts.FirstOrDefaultAsync(w => w.ClientId == clientId && w.PlanDayId == planDayId && w.Date == date);
        var (ok, json, error) = await WearableService.CallPortalAsync(HttpMethod.Post, "/workout", new
        {
            clientId,
            workout = built.Workout,
            date = date.ToString("yyyy-MM-dd"),
            replaceWorkoutId = previous?.GarminWorkoutId
        }, timeoutSeconds: 45);
        long? workoutId = json?.RootElement.TryGetProperty("workoutId", out var wid) == true && wid.TryGetInt64(out var v) ? v : null;
        if (workoutId is null)
        {
            // Poprzedni trening Portal usuwa przed utworzeniem nowego — nie trzymamy martwego wpisu.
            if (previous is not null) { db.WearableWorkouts.Remove(previous); await db.SaveChangesAsync(); }
            return new(false, error ?? "Garmin nie przyjął treningu.", built.Unmatched);
        }
        if (previous is null) db.WearableWorkouts.Add(previous = new WearableWorkout { ClientId = clientId, PlanDayId = planDayId, Date = date });
        previous.Name = name.Length > 120 ? name[..120] : name;
        previous.GarminWorkoutId = workoutId.Value;
        previous.GarminScheduleId = json!.RootElement.TryGetProperty("scheduleId", out var sid) && sid.TryGetInt64(out var s) ? s : null;
        previous.SessionId = sessionId ?? previous.SessionId;
        previous.SentByUserId = userId;
        previous.SentAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        // Trening powstał, ale nie dało się go zaplanować — zapamiętany (ponowna wysyłka go zastąpi), trener widzi powód.
        if (!ok) return new(false, error ?? "Garmin nie zaplanował treningu.", built.Unmatched);

        var msg = $"Wysłane — „{name}” czeka w Garmin Connect na {date:dd.MM}; zegarek pobierze je przy najbliższej synchronizacji.";
        if (built.Unmatched.Count > 0)
            msg += $" Bez odpowiednika w katalogu Garmina ({built.Unmatched.Count}): {string.Join(", ", built.Unmatched.Take(4))}{(built.Unmatched.Count > 4 ? "…" : "")} — na zegarku pokażą się z nazwą w opisie.";
        return new(true, msg, built.Unmatched);
    }

    /// <summary>Treningi od dziś — lista u klienta i przy dniu planu.</summary>
    public async Task<List<WearableWorkout>> UpcomingAsync(int clientId, DateOnly today)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.WearableWorkouts.AsNoTracking().Where(w => w.ClientId == clientId && w.Date >= today).OrderBy(w => w.Date).Take(10).ToListAsync();
    }
}
