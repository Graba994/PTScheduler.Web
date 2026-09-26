using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services.Google;

public class GoogleCalendarService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IGoogleCalendarApi api,
    IGoogleTokenBroker broker,
    IGoogleMeetService meet,
    IAppClock clock,
    ILogger<GoogleCalendarService> logger) : IGoogleCalendarService
{
    /// <summary>Okno synchronizacji: wczoraj … +60 dni.</summary>
    internal const int PastDays = 1;
    internal const int FutureDays = 60;

    public async Task<CalendarConnectionDto> GetStatusAsync(string userId)
    {
        await using var db = dbFactory.CreateDbContext();
        var c = await db.CalendarConnections.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId);
        var available = broker.IsManaged
            ? (await broker.GetStatusAsync(userId))?.Available ?? c is not null
            : meet.IsConfigured;
        var now = clock.LocalNow;
        return new CalendarConnectionDto
        {
            Available = available,
            Mode = broker.IsManaged ? CalendarConnectionModes.Platform : CalendarConnectionModes.Own,
            Connected = c is not null,
            NeedsReconnect = c?.NeedsReconnect ?? false,
            GoogleEmail = c?.GoogleEmail,
            PushSessions = c?.PushSessions ?? true,
            BlockBusy = c?.BlockBusy ?? true,
            ShowClientName = c?.ShowClientName ?? true,
            CreateMeetLinks = c?.CreateMeetLinks ?? false,
            LastSyncAt = c?.LastSyncAt,
            LastError = c?.LastError,
            UpcomingBusyBlocks = c is null ? 0 : await db.CalendarBusyBlocks.CountAsync(b => b.UserId == userId && b.EndTime > now)
        };
    }

    public Task<(string? Url, string? Error)> StartConnectAsync(string userId, string returnUrl) =>
        broker.AuthorizeAsync(userId, returnUrl);

    public async Task<(bool Ok, string? Error)> CompleteConnectAsync(string userId)
    {
        var status = await broker.GetStatusAsync(userId);
        if (status is null) return (false, "Platforma chwilowo nie odpowiada. Odśwież stronę za chwilę.");
        if (!status.Connected) return (false, "Google nie potwierdził połączenia. Spróbuj jeszcze raz.");

        await using var db = dbFactory.CreateDbContext();
        var c = await db.CalendarConnections.FirstOrDefaultAsync(x => x.UserId == userId);
        if (c is null)
        {
            c = new CalendarConnection { UserId = userId };
            db.CalendarConnections.Add(c);
        }
        c.Mode = CalendarConnectionModes.Platform;
        c.GoogleEmail = status.Email;
        c.NeedsReconnect = false;
        c.LastError = null;
        c.ConnectedAt = clock.UtcNow;
        await db.SaveChangesAsync();
        await ResetFingerprintsAsync(db, userId);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> EnableOwnAccountAsync(string userId)
    {
        if (broker.IsManaged) return (false, "W tej instalacji łączysz własne konto przyciskiem „Połącz z Google”.");
        if (await meet.GetOwnAccessTokenAsync() is null)
            return (false, "Najpierw połącz konto Google w ustawieniach Google Meet (Client ID, Client Secret, Autoryzuj).");

        await using var db = dbFactory.CreateDbContext();
        var c = await db.CalendarConnections.FirstOrDefaultAsync(x => x.UserId == userId);
        if (c is null)
        {
            c = new CalendarConnection { UserId = userId };
            db.CalendarConnections.Add(c);
        }
        c.Mode = CalendarConnectionModes.Own;
        c.NeedsReconnect = false;
        c.LastError = null;
        c.ConnectedAt = clock.UtcNow;
        await db.SaveChangesAsync();
        await ResetFingerprintsAsync(db, userId);
        return (true, null);
    }

    public async Task DisconnectAsync(string userId)
    {
        await using var db = dbFactory.CreateDbContext();
        var c = await db.CalendarConnections.FirstOrDefaultAsync(x => x.UserId == userId);
        if (c?.Mode == CalendarConnectionModes.Platform) await broker.DisconnectAsync(userId);
        if (c is not null) db.CalendarConnections.Remove(c);
        db.CalendarBusyBlocks.RemoveRange(db.CalendarBusyBlocks.Where(b => b.UserId == userId));
        await db.SaveChangesAsync();
        await ResetFingerprintsAsync(db, userId);
    }

    public async Task SaveOptionsAsync(string userId, bool pushSessions, bool blockBusy, bool showClientName, bool createMeetLinks)
    {
        await using var db = dbFactory.CreateDbContext();
        var c = await db.CalendarConnections.FirstOrDefaultAsync(x => x.UserId == userId);
        if (c is null) return;
        var nameChanged = c.ShowClientName != showClientName;
        c.PushSessions = pushSessions;
        c.BlockBusy = blockBusy;
        c.ShowClientName = showClientName;
        c.CreateMeetLinks = createMeetLinks;
        if (!blockBusy) db.CalendarBusyBlocks.RemoveRange(db.CalendarBusyBlocks.Where(b => b.UserId == userId));
        await db.SaveChangesAsync();
        if (nameChanged) await ResetFingerprintsAsync(db, userId);
    }

    public async Task<List<BusyBlockDto>> GetBusyBlocksAsync(string userId, DateTime from, DateTime to)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.CalendarBusyBlocks.AsNoTracking()
            .Where(b => b.UserId == userId && b.StartTime < to && b.EndTime > from)
            .OrderBy(b => b.StartTime)
            .Select(b => new BusyBlockDto(b.StartTime, b.EndTime, b.IsAllDay))
            .ToListAsync();
    }

    public async Task SyncDueAsync(TimeSpan interval, CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var due = clock.UtcNow - interval;
        var users = await db.CalendarConnections.AsNoTracking()
            .Where(c => !c.NeedsReconnect && (c.LastSyncAt == null || c.LastSyncAt <= due))
            .Select(c => c.UserId)
            .ToListAsync(ct);
        foreach (var userId in users)
        {
            ct.ThrowIfCancellationRequested();
            await SyncAsync(userId, ct);
        }
    }

    public async Task<CalendarSyncResult> SyncAsync(string userId, CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var conn = await db.CalendarConnections.FirstOrDefaultAsync(c => c.UserId == userId, ct);
        if (conn is null) return CalendarSyncResult.Fail("Kalendarz nie jest połączony.");

        string? token;
        if (conn.Mode == CalendarConnectionModes.Own)
            token = await meet.GetOwnAccessTokenAsync();
        else
        {
            var (t, revoked) = await broker.GetAccessTokenAsync(userId, ct);
            token = t;
            if (revoked)
            {
                conn.NeedsReconnect = true;
                return await FailAsync(db, conn, "Google cofnął dostęp do kalendarza — połącz konto ponownie.");
            }
        }
        if (token is null) return await FailAsync(db, conn, "Nie udało się uzyskać dostępu do Google — spróbujemy ponownie za kilka minut.");

        try
        {
            var result = await SyncCoreAsync(db, conn, token, ct);
            conn.LastSyncAt = clock.UtcNow;
            conn.LastError = null;
            await db.SaveChangesAsync(ct);
            return result;
        }
        catch (GoogleApiException ex)
        {
            // Odrzucony token: przy następnej próbie Portal wyda nowy. O cofniętej zgodzie
            // informuje Portal (Revoked) — dopiero wtedy prosimy trenera o ponowne połączenie.
            if (ex.Status == System.Net.HttpStatusCode.Unauthorized) broker.Invalidate(userId);
            logger.LogWarning("Google Calendar: synchronizacja {User} nie powiodła się: {Message}", userId, ex.Message);
            return await FailAsync(db, conn, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Google Calendar: brak połączenia (użytkownik {User}).", userId);
            return await FailAsync(db, conn, "Brak połączenia z Google — spróbujemy ponownie.");
        }
    }

    private async Task<CalendarSyncResult> FailAsync(ApplicationDbContext db, CalendarConnection conn, string error)
    {
        conn.LastError = error.Length > 500 ? error[..500] : error;
        // Nieudana próba też przesuwa licznik — inaczej usługa w tle pukałaby co minutę.
        conn.LastSyncAt = clock.UtcNow;
        await db.SaveChangesAsync();
        return CalendarSyncResult.Fail(error);
    }

    private async Task<CalendarSyncResult> SyncCoreAsync(ApplicationDbContext db, CalendarConnection conn, string token, CancellationToken ct)
    {
        var fromWall = clock.Today.AddDays(-PastDays).ToDateTime(TimeOnly.MinValue);
        var toWall = clock.Today.AddDays(FutureDays + 1).ToDateTime(TimeOnly.MinValue);
        var events = await api.ListAsync(token, clock.ToUtc(fromWall), clock.ToUtc(toWall), ct);

        var app = GoogleCalendarApi.InstanceKey;
        var ours = events.Where(e => e.App == app && e.SessionId is not null).ToList();
        var oursIds = ours.Select(e => e.Id).ToHashSet();

        // --- Google → aplikacja: zajęte terminy ---
        var blocks = 0;
        db.CalendarBusyBlocks.RemoveRange(db.CalendarBusyBlocks.Where(b => b.UserId == conn.UserId));
        if (conn.BlockBusy)
        {
            foreach (var e in events)
            {
                if (e.App is not null && e.SessionId is not null) continue; // wizyty z aplikacji (także z innych instancji) nie blokują same siebie
                if (e.Status == "cancelled" || e.Transparency == "transparent") continue;
                var block = ToBlock(conn.UserId, e);
                if (block is null || block.EndTime <= block.StartTime) continue;
                db.CalendarBusyBlocks.Add(block);
                blocks++;
            }
        }

        // --- aplikacja → Google: wizyty ---
        int created = 0, updated = 0, deleted = 0;
        if (conn.PushSessions)
        {
            var sessions = await db.Sessions
                .Include(s => s.SessionType)
                .Include(s => s.Client)
                .Where(s => s.TrainerUserId == conn.UserId && s.StartTime >= fromWall && s.StartTime < toWall)
                .ToListAsync(ct);

            var bySession = ours.GroupBy(e => e.SessionId!.Value).ToDictionary(g => g.Key, g => g.ToList());
            var keep = new HashSet<string>();

            foreach (var s in sessions)
            {
                bySession.TryGetValue(s.Id, out var linked);
                if (s.Status == SessionStatus.Cancelled)
                {
                    foreach (var e in linked ?? []) { await api.DeleteAsync(token, e.Id, ct); deleted++; }
                    if (s.CalendarEventId is not null && (linked is null || linked.All(e => e.Id != s.CalendarEventId)) && s.CalendarSyncFingerprint is not null)
                    {
                        await api.DeleteAsync(token, s.CalendarEventId, ct);
                        deleted++;
                    }
                    if (s.CalendarSyncFingerprint is not null || linked is { Count: > 0 })
                    {
                        s.CalendarEventId = null;
                        s.CalendarSyncFingerprint = null;
                    }
                    continue;
                }

                var write = BuildEvent(s, conn.ShowClientName, app);
                var fp = Fingerprint(write);
                var primary = linked?.FirstOrDefault(e => e.Id == s.CalendarEventId) ?? linked?.FirstOrDefault();

                // Duplikaty tej samej wizyty (np. po przywróceniu kopii zapasowej) — zostaje jedno wydarzenie.
                foreach (var dup in (linked ?? []).Where(e => e != primary)) { await api.DeleteAsync(token, dup.Id, ct); deleted++; }

                if (primary is not null && s.CalendarSyncFingerprint == fp && s.CalendarEventId == primary.Id)
                {
                    keep.Add(primary.Id);
                    continue;
                }

                var targetId = primary?.Id ?? s.CalendarEventId;
                if (targetId is not null && await api.PatchAsync(token, targetId, write, ct))
                {
                    updated++;
                }
                else
                {
                    targetId = await api.InsertAsync(token, write, ct);
                    created++;
                }
                s.CalendarEventId = targetId;
                s.CalendarSyncFingerprint = fp;
                keep.Add(targetId);
            }

            // Wydarzenia wizyt, których już nie ma w tym oknie (usunięte albo przeniesione daleko).
            // Wspólne konto Google kilku trenerów (tryb „own”): cudzych wizyt nie ruszamy.
            var inWindow = sessions.Select(s => s.Id).ToHashSet();
            var candidates = ours.Where(e => !keep.Contains(e.Id) && !inWindow.Contains(e.SessionId!.Value)).ToList();
            if (candidates.Count > 0)
            {
                var ids = candidates.Select(e => e.SessionId!.Value).Distinct().ToList();
                var owners = await db.Sessions.Where(s => ids.Contains(s.Id))
                    .Select(s => new { s.Id, s.TrainerUserId })
                    .ToDictionaryAsync(s => s.Id, s => s.TrainerUserId, ct);
                foreach (var e in candidates)
                {
                    if (owners.TryGetValue(e.SessionId!.Value, out var owner) && owner != conn.UserId) continue;
                    await api.DeleteAsync(token, e.Id, ct);
                    deleted++;
                }
            }
        }

        await db.SaveChangesAsync(ct);
        return new CalendarSyncResult(true, created, updated, deleted, blocks, null);
    }

    private CalendarBusyBlock? ToBlock(string userId, GoogleEvent e)
    {
        if (e.Start is DateTimeOffset start && e.End is DateTimeOffset end)
            return new CalendarBusyBlock
            {
                UserId = userId,
                StartTime = clock.ToWallClock(start.UtcDateTime),
                EndTime = clock.ToWallClock(end.UtcDateTime)
            };
        if (e.StartDate is DateOnly sd)
            return new CalendarBusyBlock
            {
                UserId = userId,
                StartTime = sd.ToDateTime(TimeOnly.MinValue),
                EndTime = (e.EndDate ?? sd.AddDays(1)).ToDateTime(TimeOnly.MinValue),
                IsAllDay = true
            };
        return null;
    }

    private GoogleEventWrite BuildEvent(Session s, bool showClientName, string app)
    {
        var typeName = s.SessionType?.Name ?? "Wizyta";
        var clientName = $"{s.Client?.FirstName} {s.Client?.LastName}".Trim();
        var summary = showClientName && clientName.Length > 0 ? $"{typeName} — {clientName}" : typeName;
        if (s.Status == SessionStatus.AwaitingPackage) summary += " (czeka na pakiet)";

        var lines = new List<string> { $"{typeName}, {s.SessionType?.DurationMinutes ?? 60} min" };
        if (!string.IsNullOrWhiteSpace(s.MeetingUrl)) lines.Add($"Google Meet: {s.MeetingUrl}");
        lines.Add("Wizyta z aplikacji PTScheduler — zmiany wprowadzaj w aplikacji, kalendarz zaktualizuje się sam.");

        var startUtc = clock.ToUtc(s.StartTime);
        return new GoogleEventWrite(summary, string.Join("\n", lines), startUtc,
            startUtc.AddMinutes(s.SessionType?.DurationMinutes ?? 60), app, s.Id);
    }

    internal static string Fingerprint(GoogleEventWrite w)
    {
        var raw = $"{w.Summary}|{w.Description}|{w.StartUtc:O}|{w.EndUtc:O}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..32];
    }

    /// <summary>Po zmianie konta albo formatu wydarzeń każda wizyta zostanie wysłana ponownie.</summary>
    private static async Task ResetFingerprintsAsync(ApplicationDbContext db, string userId)
    {
        var rows = await db.Sessions.Where(s => s.TrainerUserId == userId && s.CalendarSyncFingerprint != null).ToListAsync();
        foreach (var s in rows) s.CalendarSyncFingerprint = null;
        await db.SaveChangesAsync();
    }
}
