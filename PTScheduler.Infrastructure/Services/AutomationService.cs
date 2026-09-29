using System.Net;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

/// <summary>
/// Automatyczne wiadomości do klientów. Każdy przebieg: najpierw oznacza „powroty” (rezerwacja albo zakup
/// po wiadomości), potem wysyła należne wiadomości — każdą raz (historia w AutomationLogs).
/// Klient może je wyłączyć w ustawieniach powiadomień („Wiadomości od trenera”).
/// </summary>
public class AutomationService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IAppClock clock,
    IEmailService email,
    IEmailTemplateService templates,
    IWebPushService push,
    ISmsService sms,
    INotificationPreferencesService prefs,
    IBrandingService branding,
    ILogger<AutomationService> logger) : IAutomationService
{
    /// <summary>Tyle dni po wiadomości liczymy rezerwację albo zakup jako „powrót”.</summary>
    public const int ReturnWindowDays = 30;

    public async Task<List<AutomationRuleDto>> GetRulesAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var saved = await db.AutomationRules.AsNoTracking().ToDictionaryAsync(r => r.Kind);
        return AutomationKinds.All.Select(d => saved.TryGetValue(d.Kind, out var r) ? ToDto(r) : FromDefault(d)).ToList();
    }

    public async Task SaveRuleAsync(AutomationRuleDto dto)
    {
        var def = AutomationKinds.Find(dto.Kind) ?? throw new ArgumentException("Nieznany rodzaj automatyzacji.");
        await using var db = dbFactory.CreateDbContext();
        var rule = await db.AutomationRules.FirstOrDefaultAsync(r => r.Kind == def.Kind);
        if (rule is null)
        {
            rule = new AutomationRule { Kind = def.Kind };
            db.AutomationRules.Add(rule);
        }
        if (dto.Enabled && !rule.Enabled) rule.EnabledAt = clock.UtcNow;
        rule.Enabled = dto.Enabled;
        rule.DelayDays = Math.Clamp(dto.DelayDays, 0, 365);
        rule.CooldownDays = Math.Clamp(dto.CooldownDays, 1, 3650);
        rule.ViaEmail = dto.ViaEmail;
        rule.ViaPush = dto.ViaPush;
        rule.ViaSms = dto.ViaSms;
        rule.Subject = string.IsNullOrWhiteSpace(dto.Subject) ? def.Subject : dto.Subject.Trim();
        rule.Message = string.IsNullOrWhiteSpace(dto.Message) ? def.Message : dto.Message.Trim();
        rule.ButtonText = string.IsNullOrWhiteSpace(dto.ButtonText) ? def.ButtonText : dto.ButtonText.Trim();
        rule.CouponPercent = Math.Clamp(dto.CouponPercent, 0, 90);
        rule.CouponValidDays = Math.Clamp(dto.CouponValidDays, 1, 365);
        rule.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<List<AutomationLogDto>> GetLogAsync(int take = 100)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.AutomationLogs.AsNoTracking()
            .OrderByDescending(l => l.SentAt).Take(take)
            .Select(l => new AutomationLogDto(l.Id, l.Kind, l.ClientId,
                (l.Client!.FirstName + " " + l.Client.LastName).Trim(), l.SentAt, l.Channels, l.CouponCode, l.ReturnedAt))
            .ToListAsync();
    }

    public async Task<List<AutomationStatsDto>> GetStatsAsync(int days = 90)
    {
        var from = clock.UtcNow.AddDays(-days);
        await using var db = dbFactory.CreateDbContext();
        var rows = await db.AutomationLogs.AsNoTracking().Where(l => l.SentAt >= from)
            .GroupBy(l => l.Kind)
            .Select(g => new { g.Key, Sent = g.Count(), Returned = g.Count(l => l.ReturnedAt != null) })
            .ToListAsync();
        return rows.Select(r => new AutomationStatsDto(r.Key, r.Sent, r.Returned)).ToList();
    }

    public async Task<int> RunAsync(AutomationChannels channels)
    {
        await MarkReturnsAsync();

        await using var db = dbFactory.CreateDbContext();
        var rules = await db.AutomationRules.AsNoTracking().Where(r => r.Enabled).ToListAsync();
        if (rules.Count == 0) return 0;
        if (!channels.Email && !channels.Push && !channels.Sms) return 0;

        var now = clock.UtcNow;
        var wallNow = clock.ToWallClock(now);
        var today = clock.Today;

        var clients = await db.Clients.AsNoTracking()
            .Where(c => c.Status == ClientStatus.Active && c.ApplicationUserId != "")
            .ToListAsync();
        if (clients.Count == 0) return 0;
        var ids = clients.Select(c => c.Id).ToList();

        var sessions = await db.Sessions.AsNoTracking().Where(s => ids.Contains(s.ClientId))
            .Select(s => new { s.ClientId, s.StartTime, s.Status, s.PackageId })
            .ToListAsync();
        var packages = await db.SessionPackages.AsNoTracking()
            .Where(p => ids.Contains(p.ClientId) || (p.PartnerClientId != null && ids.Contains(p.PartnerClientId.Value)))
            .Select(p => new { p.Id, p.ClientId, p.PartnerClientId, p.Status, p.TotalSessions, p.UsedSessions, p.PurchasedAt, p.ExpiresAt })
            .ToListAsync();
        var logs = await db.AutomationLogs.AsNoTracking().Where(l => ids.Contains(l.ClientId))
            .Select(l => new { l.ClientId, l.Kind, l.SentAt }).ToListAsync();

        DateTime? LastSent(int clientId, string kind) =>
            logs.Where(l => l.ClientId == clientId && l.Kind == kind).Select(l => (DateTime?)l.SentAt).Max();

        var due = new List<(AutomationRule Rule, Client Client)>();
        foreach (var rule in rules)
        {
            foreach (var c in clients)
            {
                var isDue = rule.Kind switch
                {
                    AutomationKinds.Welcome1 or AutomationKinds.Welcome2 or AutomationKinds.Welcome3 =>
                        rule.EnabledAt is { } enabledAt
                        && LastSent(c.Id, rule.Kind) is null
                        && AutomationRules.WelcomeDue(c.CreatedAt, enabledAt, rule.DelayDays, now),

                    AutomationKinds.WinBack => AutomationRules.WinBackDue(
                        sessions.Where(s => s.ClientId == c.Id && s.Status == SessionStatus.Completed)
                            .Select(s => (DateTime?)clock.ToUtc(s.StartTime)).Max(),
                        sessions.Any(s => s.ClientId == c.Id && s.StartTime >= wallNow
                            && s.Status is SessionStatus.Scheduled or SessionStatus.AwaitingPackage),
                        LastSent(c.Id, rule.Kind), rule.DelayDays, rule.CooldownDays, now),

                    AutomationKinds.PackageEnded => PackageEndedFor(c.Id, rule, now),

                    AutomationKinds.Birthday =>
                        AutomationRules.IsBirthday(c.DateOfBirth, today)
                        && (LastSent(c.Id, rule.Kind) is not { } last || (now - last).TotalDays >= rule.CooldownDays),

                    _ => false
                };
                if (isDue) due.Add((rule, c));
            }
        }

        bool PackageEndedFor(int clientId, AutomationRule rule, DateTime nowUtc)
        {
            var mine = packages.Where(p => p.ClientId == clientId || p.PartnerClientId == clientId).ToList();
            if (mine.Count == 0) return false;
            var hasActive = mine.Any(p => p.Status == PackageStatus.Active && p.TotalSessions > p.UsedSessions
                                          && (p.ExpiresAt == null || p.ExpiresAt > nowUtc));
            DateTime? endedAt = null;
            foreach (var p in mine.Where(p => p.Status is PackageStatus.Depleted or PackageStatus.Expired))
            {
                var end = p.Status == PackageStatus.Expired && p.ExpiresAt is { } exp
                    ? exp
                    : sessions.Where(s => s.PackageId == p.Id).Select(s => (DateTime?)clock.ToUtc(s.StartTime)).Max() ?? p.PurchasedAt;
                if (endedAt is null || end > endedAt) endedAt = end;
            }
            return AutomationRules.PackageEndedDue(endedAt, hasActive, LastSent(clientId, rule.Kind), rule.DelayDays, nowUtc);
        }

        if (due.Count == 0) return 0;

        var brand = await branding.GetAsync();
        var studio = string.IsNullOrWhiteSpace(brand.CompanyName) ? "naszym studiu" : brand.CompanyName;
        var userIds = due.Select(d => d.Client.ApplicationUserId).Concat(due.Select(d => d.Client.TrainerUserId ?? "")).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new { u.Email, Name = (u.FirstName + " " + u.LastName).Trim() });

        var sent = 0;
        foreach (var (rule, client) in due)
        {
            try
            {
                var def = AutomationKinds.Find(rule.Kind)!;
                var user = users.GetValueOrDefault(client.ApplicationUserId);
                // Klient wybiera kanały w Moje konto → Powiadomienia („Od trenera”: e-mail / push / SMS).
                var canEmail = channels.Email && rule.ViaEmail && !string.IsNullOrWhiteSpace(user?.Email)
                               && await prefs.IsEnabledAsync(client.ApplicationUserId, NotificationTypes.TrainerMessages);
                var canPush = channels.Push && rule.ViaPush
                              && await prefs.IsEnabledAsync(client.ApplicationUserId, NotificationTypes.PushTrainerMessages);
                var canSms = channels.Sms && rule.ViaSms && !string.IsNullOrWhiteSpace(client.Phone)
                             && await prefs.IsEnabledAsync(client.ApplicationUserId, NotificationTypes.SmsTrainerMessages);
                if (!canEmail && !canPush && !canSms) continue;

                string? code = null;
                DateTime? validUntil = null;
                if (rule.CouponPercent > 0)
                {
                    validUntil = now.AddDays(rule.CouponValidDays);
                    code = await CreateCouponAsync(rule, client, validUntil.Value);
                }

                var trainer = client.TrainerUserId is { } tid ? users.GetValueOrDefault(tid)?.Name : null;
                var values = new Dictionary<string, string>
                {
                    ["Imie"] = client.FirstName,
                    ["Trener"] = string.IsNullOrWhiteSpace(trainer) ? "Twój trener" : trainer,
                    ["Studio"] = studio,
                    ["Kupon"] = code ?? "",
                    ["Rabat"] = rule.CouponPercent > 0 ? $"{rule.CouponPercent}%" : "",
                    ["WaznyDo"] = validUntil is { } v ? clock.ToWallClock(v).ToString("dd.MM.yyyy") : ""
                };
                var subject = AutomationRules.Fill(rule.Subject, values);
                var text = AutomationRules.Fill(rule.Message, values);
                var link = channels.AppBaseUrl.TrimEnd('/') + def.LinkPath;

                var used = new List<string>();
                if (canEmail)
                {
                    try
                    {
                        var body = string.Join("", text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                            .Select(p => $"<p style=\"color:#374151;font-size:15px\">{WebUtility.HtmlEncode(p.Trim())}</p>"));
                        var (subj, html) = await templates.RenderAsync("automation", new Dictionary<string, string>
                        {
                            ["Subject"] = subject, ["ClientName"] = client.FirstName, ["Body"] = body,
                            ["ButtonText"] = WebUtility.HtmlEncode(rule.ButtonText), ["Link"] = link
                        });
                        await email.SendAsync(user!.Email!, $"{client.FirstName} {client.LastName}".Trim(), subj, html);
                        used.Add("email");
                    }
                    catch (Exception ex) { logger.LogWarning(ex, "Automatyzacja {Kind}: e-mail do klienta {Id} nie wyszedł.", rule.Kind, client.Id); }
                }
                if (canPush)
                {
                    try
                    {
                        var r = await push.SendWithReportAsync(client.ApplicationUserId, new PushMessageDto
                        {
                            Category = PTScheduler.Domain.Constants.NotificationTypes.PushTrainerMessages,
                            Title = subject,
                            Body = text.Replace('\n', ' ') is { Length: > 180 } t ? t[..177] + "…" : text.Replace('\n', ' '),
                            Url = def.LinkPath
                        });
                        if (r.Sent > 0) used.Add("push");
                    }
                    catch (Exception ex) { logger.LogWarning(ex, "Automatyzacja {Kind}: push do klienta {Id} nie wyszedł.", rule.Kind, client.Id); }
                }
                if (canSms)
                {
                    var plain = text.Replace('\n', ' ');
                    if (plain.Length > 280) plain = plain[..277] + "…";
                    var r = await sms.SendReminderAsync(client.Phone!, $"{plain} {link}", channels.MaxSmsPerMonth);
                    if (r.Success) used.Add("sms");
                }

                if (used.Count == 0) continue;
                db.AutomationLogs.Add(new AutomationLog
                {
                    Kind = rule.Kind, ClientId = client.Id, SentAt = now, Channels = string.Join(",", used), CouponCode = code
                });
                await db.SaveChangesAsync();
                sent++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Automatyzacja {Kind} dla klienta {Id} nie powiodła się.", rule.Kind, client.Id);
            }
        }
        return sent;
    }

    /// <summary>Po wiadomości klient zarezerwował trening albo kupił pakiet/karnet — zapisujemy pierwszy taki moment.</summary>
    private async Task MarkReturnsAsync()
    {
        var trackable = AutomationKinds.All.Where(k => k.TracksReturn).Select(k => k.Kind).ToList();
        var since = clock.UtcNow.AddDays(-ReturnWindowDays);
        await using var db = dbFactory.CreateDbContext();
        var open = await db.AutomationLogs.Where(l => l.ReturnedAt == null && l.SentAt >= since && trackable.Contains(l.Kind)).ToListAsync();
        if (open.Count == 0) return;

        var ids = open.Select(l => l.ClientId).Distinct().ToList();
        var bookings = await db.Sessions.AsNoTracking()
            .Where(s => ids.Contains(s.ClientId) && s.CreatedAt >= since && s.Status != SessionStatus.Cancelled)
            .Select(s => new { s.ClientId, At = s.CreatedAt }).ToListAsync();
        var purchases = await db.SessionPackages.AsNoTracking()
            .Where(p => ids.Contains(p.ClientId) && p.PurchasedAt >= since)
            .Select(p => new { p.ClientId, At = p.PurchasedAt }).ToListAsync();
        var events = bookings.Concat(purchases).ToList();

        foreach (var log in open)
        {
            var first = events.Where(e => e.ClientId == log.ClientId && e.At > log.SentAt).Select(e => (DateTime?)e.At).Min();
            if (first is not null) log.ReturnedAt = first;
        }
        await db.SaveChangesAsync();
    }

    private async Task<string> CreateCouponAsync(AutomationRule rule, Client client, DateTime validUntil)
    {
        var prefix = rule.Kind switch
        {
            AutomationKinds.Birthday => "URODZINY",
            AutomationKinds.PackageEnded => "DALEJ",
            _ => "WRACAJ"
        };
        await using var db = dbFactory.CreateDbContext();
        for (var attempt = 0; ; attempt++)
        {
            var code = $"{prefix}-{RandomCode(5)}";
            if (await db.Coupons.AnyAsync(c => c.Code == code) && attempt < 5) continue;
            db.Coupons.Add(new Coupon
            {
                Code = code,
                Description = $"Automatyczny kupon ({AutomationKinds.Find(rule.Kind)?.Title}) — {client.FirstName} {client.LastName}".Trim(),
                DiscountType = "percent",
                DiscountValue = rule.CouponPercent,
                ValidFrom = clock.UtcNow.AddMinutes(-1),
                ValidUntil = validUntil,
                MaxUses = 1,
                MaxUsesPerUser = 1,
                Scope = "all",
                IsActive = true
            });
            await db.SaveChangesAsync();
            return code;
        }
    }

    private static string RandomCode(int length)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // bez 0/O i 1/I
        return new string(Enumerable.Range(0, length).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
    }

    private static AutomationRuleDto ToDto(AutomationRule r) => new()
    {
        Kind = r.Kind, Enabled = r.Enabled, DelayDays = r.DelayDays, CooldownDays = r.CooldownDays,
        ViaEmail = r.ViaEmail, ViaPush = r.ViaPush, ViaSms = r.ViaSms,
        Subject = r.Subject, Message = r.Message, ButtonText = r.ButtonText,
        CouponPercent = r.CouponPercent, CouponValidDays = r.CouponValidDays
    };

    private static AutomationRuleDto FromDefault(AutomationKinds.Default d) => new()
    {
        Kind = d.Kind, Enabled = false, DelayDays = d.DelayDays, CooldownDays = d.CooldownDays,
        ViaEmail = true, ViaPush = true, ViaSms = d.ViaSms,
        Subject = d.Subject, Message = d.Message, ButtonText = d.ButtonText,
        CouponPercent = d.CouponPercent, CouponValidDays = 14
    };
}
