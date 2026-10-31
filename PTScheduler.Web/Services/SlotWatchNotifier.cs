using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Scheduling;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

/// <summary>
/// Gdy ktoś odwoła wizytę, a termin nadal jest wolny (≥2 h przed, ≤14 dni), zawiadamia klientów tego trenera,
/// którzy zaznaczyli „Powiadom mnie o wolnym terminie” w pasujących dniach i godzinach. Push z przyciskiem
/// „Zarezerwuj” (e-mail, gdy push nie dotarł); jedna wiadomość na klienta na 12 h. Każde odwołanie sprawdzamy raz.
/// </summary>
public class SlotWatchNotifier(IServiceScopeFactory scopeFactory, ILogger<SlotWatchNotifier> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");
    private const int MaxPerSlot = 10;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMinutes(3), ct);
        while (!ct.IsCancellationRequested)
        {
            try { await RunCycleAsync(); }
            catch (Exception ex) { logger.LogError(ex, "Slot watch cycle failed"); }
            await Task.Delay(Interval, ct);
        }
    }

    private async Task RunCycleAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var clock = sp.GetRequiredService<IAppClock>();
        var utcNow = clock.UtcNow;
        var since = utcNow.AddHours(-12);

        var freed = await db.Sessions.Include(s => s.SessionType)
            .Where(s => s.Status == SessionStatus.Cancelled && s.FreedNotifiedAt == null && s.CancelledAt != null && s.CancelledAt >= since)
            .ToListAsync();
        if (freed.Count == 0) return;

        if (await db.SlotWatches.AnyAsync(w => w.IsActive))
        {
            var entitlements = sp.GetRequiredService<EntitlementService>();
            var push = sp.GetRequiredService<IWebPushService>();
            var email = sp.GetRequiredService<IEmailService>();
            var pushOn = entitlements.IsAllowed("PushNotifications") && (await push.GetSettingsAsync()).IsConfigured;
            var emailOn = entitlements.IsAllowed("EmailReminders") && await email.IsEnabledAsync();
            if (pushOn || emailOn)
            {
                foreach (var group in freed.GroupBy(s => (s.TrainerUserId, s.StartTime)))
                {
                    try { await NotifySlotAsync(sp, db, clock, group.ToList(), pushOn, emailOn); }
                    catch (Exception ex) { logger.LogWarning(ex, "Slot watch: slot {Start} failed.", group.Key.StartTime); }
                }
            }
        }

        foreach (var s in freed) s.FreedNotifiedAt = utcNow;
        await db.SaveChangesAsync();
    }

    private async Task NotifySlotAsync(IServiceProvider sp, ApplicationDbContext db, IAppClock clock,
        List<PTScheduler.Domain.Entities.Session> group, bool pushOn, bool emailOn)
    {
        var first = group[0];
        var start = first.StartTime;
        var minutes = first.SessionType.DurationMinutes;
        if (!SlotWatchRules.InWindow(start, clock.LocalNow)) return;

        var watchers = await db.SlotWatches.Include(w => w.Client)
            .Where(w => w.IsActive && w.Client.TrainerUserId == first.TrainerUserId
                     && w.Client.Status == ClientStatus.Active && w.Client.AllowSelfBooking)
            .ToListAsync();
        var cancelledBy = group.Select(s => s.ClientId).ToHashSet();
        var utcNow = clock.UtcNow;
        watchers = watchers
            .Where(w => !cancelledBy.Contains(w.ClientId)
                     && SlotWatchRules.Matches(w.Days, w.FromTime, w.ToTime, start, minutes)
                     && SlotWatchRules.CanNotify(w.LastNotifiedAt, utcNow))
            .OrderBy(w => w.LastNotifiedAt ?? DateTime.MinValue)
            .ToList();
        if (watchers.Count == 0) return;

        // Termin musi być naprawdę do wzięcia — tak, jak widzi go klient przy rezerwacji.
        var slots = await sp.GetRequiredService<ITrainerAvailabilityService>()
            .GetAvailableSlotsAsync(first.TrainerUserId, DateOnly.FromDateTime(start), minutes);
        if (!slots.Any(x => x.IsAvailable && x.Start == start)) return;

        var push = sp.GetRequiredService<IWebPushService>();
        var email = sp.GetRequiredService<IEmailService>();
        var templates = sp.GetRequiredService<IEmailTemplateService>();
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var link = $"/my?termin={start:yyyy-MM-ddTHH:mm}&typ={first.SessionTypeId}";
        var day = start.ToString("dddd d MMMM", Pl);
        var whenText = $"{char.ToUpper(day[0])}{day[1..]}, {start:HH:mm}";
        var dayStart = start.Date;
        var dayEnd = dayStart.AddDays(1);
        var notified = 0;

        foreach (var w in watchers)
        {
            if (notified >= MaxPerSlot) break;
            var busyThatDay = await db.Sessions.AnyAsync(s => s.ClientId == w.ClientId && s.Status != SessionStatus.Cancelled
                                                            && s.StartTime >= dayStart && s.StartTime < dayEnd);
            if (busyThatDay || string.IsNullOrEmpty(w.Client.ApplicationUserId)) continue;

            var sent = false;
            if (pushOn)
            {
                try
                {
                    var report = await push.SendWithReportAsync(w.Client.ApplicationUserId, new PushMessageDto
                    {
                        Category = NotificationTypes.PushSessions,
                        Title = "Zwolnił się termin",
                        Body = $"{whenText} ({first.SessionType.Name}) — pasuje do Twoich godzin. Kto pierwszy, ten lepszy.",
                        Url = link,
                        Actions = [new PushActionDto { Action = "book", Title = "Zarezerwuj", Url = link }]
                    });
                    sent = report.Sent > 0;
                }
                catch (Exception ex) { logger.LogWarning(ex, "Slot watch: push to client {ClientId} failed.", w.ClientId); }
            }
            if (!sent && emailOn)
            {
                var user = await users.FindByIdAsync(w.Client.ApplicationUserId);
                if (!string.IsNullOrEmpty(user?.Email))
                {
                    try
                    {
                        var (subject, html) = await templates.RenderAsync("slot-freed", new Dictionary<string, string>
                        {
                            ["ClientName"] = w.Client.FirstName,
                            ["SessionDate"] = whenText,
                            ["SessionType"] = first.SessionType.Name,
                            ["BookLink"] = AppUrls.Absolute(link)
                        });
                        await email.SendAsync(user.Email, $"{w.Client.FirstName} {w.Client.LastName}".Trim(), subject, html);
                        sent = true;
                    }
                    catch (Exception ex) { logger.LogWarning(ex, "Slot watch: email to client {ClientId} failed.", w.ClientId); }
                }
            }
            if (!sent) continue;
            w.LastNotifiedAt = utcNow;
            notified++;
        }
        if (notified > 0) logger.LogInformation("Slot watch: {Count} clients told about {Start}.", notified, start);
    }
}
