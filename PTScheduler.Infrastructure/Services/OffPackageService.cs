using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

/// <summary>
/// Rezerwacja treningu spoza pakietu. Wizyta powstaje jak zwykle (SessionService — kolizje, pakiety,
/// Google Meet), a potem dostaje sposób płatności: online (termin trzymany 15 minut) albo u trenera
/// (od razu albo jako prośba do akceptacji — patrz <see cref="OffPackageRules"/>).
/// </summary>
public class OffPackageService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ISessionService sessions,
    IPaymentService payments,
    ITrainerConfigService trainerConfig,
    IWebPushService push,
    IEmailService email,
    IAuditLogService audit,
    ILogger<OffPackageService> logger) : IOffPackageService
{
    public async Task<OffPackageOptionsDto> GetOptionsAsync(int clientId, int sessionTypeId)
    {
        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId)
            ?? throw new InvalidOperationException("Nie znaleziono klienta.");
        var type = await db.SessionTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == sessionTypeId)
            ?? throw new InvalidOperationException("Nie ma takiego rodzaju treningu.");
        var policy = await PolicyAsync(client.TrainerUserId);
        var providers = await payments.GetEnabledOptionsAsync();
        var unpaid = await UnpaidVisitsAsync(db, clientId);

        return new OffPackageOptionsDto(
            type.RequiresPackage,
            !type.RequiresPackage && OffPackageRules.CanPayOnline(policy, providers.Count > 0, type.SinglePrice),
            type.SinglePrice,
            type.RequiresPackage ? AtTrainerMode.Disabled : OffPackageRules.AtTrainer(policy, client.TrustedForDeferredPayment, unpaid),
            unpaid,
            policy.UnpaidLimit,
            providers);
    }

    public async Task<SessionDto> BookAtTrainerAsync(string clientUserId, CreateSessionDto dto, int? partnerClientId = null)
    {
        var opts = await ValidateAsync(clientUserId, dto);
        if (opts.AtTrainer == AtTrainerMode.Disabled)
            throw new InvalidOperationException(opts.RequiresPackage
                ? "Ten trening jest dostępny tylko w pakiecie."
                : "Trener nie przyjmuje rezerwacji z płatnością u siebie — wybierz pakiet albo płatność online.");

        var instant = opts.AtTrainer == AtTrainerMode.Instant;
        var created = partnerClientId is int pid
            ? await sessions.CreatePairSessionAsync(dto, pid, sendConfirmation: instant)
            : await sessions.CreateSessionAsync(dto, sendConfirmation: instant);

        // Pakiet zdążył pokryć trening (np. pakiet pary) — nic więcej nie trzeba.
        if (created.Status != SessionStatus.AwaitingPackage) return created;

        await using var db = dbFactory.CreateDbContext();
        foreach (var s in await GroupAsync(db, created.Id))
        {
            if (s.Status != SessionStatus.AwaitingPackage) continue;
            s.OffPackagePayment = OffPackageRules.PayAtTrainer;
            if (instant) s.Status = SessionStatus.Scheduled;
            else s.AwaitingApproval = true;
        }
        await db.SaveChangesAsync();

        await NotifyTrainerAsync(created, instant
            ? ("Nowa rezerwacja — płatność u Ciebie", $"{created.ClientName}: {created.SessionTypeName}, {created.StartTime:dd.MM HH:mm}. Klient zapłaci przy treningu.")
            : ("Prośba o termin do akceptacji", $"{created.ClientName}: {created.SessionTypeName}, {created.StartTime:dd.MM HH:mm}. Klient chce zapłacić u Ciebie."));
        await LogAsync(clientUserId, "Client", instant ? "OffPackageBooked" : "OffPackageRequested", created.Id,
            $"{created.SessionTypeName} {created.StartTime:dd.MM.yyyy HH:mm} — płatność u trenera{(instant ? "" : ", do akceptacji")}");

        return (await sessions.GetSessionAsync(created.Id))!;
    }

    public async Task<PaymentInitResult> BookOnlineAsync(string clientUserId, CreateSessionDto dto, string providerKey,
        string appBaseUrl, string buyerEmail, string customerIp)
    {
        var opts = await ValidateAsync(clientUserId, dto);
        if (!opts.CanPayOnline) return new(false, null, "Płatność online za pojedynczy trening jest niedostępna.");

        var created = await sessions.CreateSessionAsync(dto, sendConfirmation: false);
        if (created.Status != SessionStatus.AwaitingPackage)
            return new(true, $"{appBaseUrl.TrimEnd('/')}/my", null); // pokrył go pakiet — płacić nie trzeba

        await using (var db = dbFactory.CreateDbContext())
        {
            var s = await db.Sessions.FirstAsync(x => x.Id == created.Id);
            s.OffPackagePayment = OffPackageRules.PayOnline;
            s.HoldUntil = DateTime.UtcNow.Add(OffPackageRules.OnlineHold);
            await db.SaveChangesAsync();
        }

        var pay = await payments.StartSessionCheckoutAsync(clientUserId, created.Id, providerKey, appBaseUrl, buyerEmail, customerIp);
        if (!pay.Ok)
        {
            // Bez płatności termin wraca do puli od razu, a nie po 15 minutach.
            await CancelAsync(created.Id, "Nie udało się rozpocząć płatności");
            return pay;
        }
        await LogAsync(clientUserId, "Client", "OffPackageOnline", created.Id,
            $"{created.SessionTypeName} {created.StartTime:dd.MM.yyyy HH:mm} — płatność online, termin trzymany 15 min");
        return pay;
    }

    public async Task ApproveAsync(int sessionId, string actorUserId)
    {
        await using var db = dbFactory.CreateDbContext();
        var group = await GroupAsync(db, sessionId);
        if (group.Count == 0 || !group.Any(s => s.AwaitingApproval))
            throw new InvalidOperationException("Ta prośba jest już rozpatrzona.");
        foreach (var s in group.Where(s => s.AwaitingApproval))
        {
            s.AwaitingApproval = false;
            if (s.Status == SessionStatus.AwaitingPackage) s.Status = SessionStatus.Scheduled;
        }
        await db.SaveChangesAsync();

        var dto = (await sessions.GetSessionAsync(sessionId))!;
        await NotifyClientsAsync(db, group, "Trener potwierdził termin",
            $"{dto.SessionTypeName}, {dto.StartTime:dddd d.MM, HH:mm}. Płatność u trenera przy treningu.");
        await LogAsync(actorUserId, "Trainer", "OffPackageApproved", sessionId, $"{dto.ClientName}: {dto.StartTime:dd.MM.yyyy HH:mm}");
    }

    public async Task RejectAsync(int sessionId, string actorUserId, string? reason)
    {
        await using var db = dbFactory.CreateDbContext();
        var group = await GroupAsync(db, sessionId);
        if (group.Count == 0 || !group.Any(s => s.AwaitingApproval))
            throw new InvalidOperationException("Ta prośba jest już rozpatrzona.");
        var why = string.IsNullOrWhiteSpace(reason) ? "Trener nie potwierdził terminu" : reason.Trim();
        foreach (var s in group)
        {
            s.AwaitingApproval = false;
            s.Status = SessionStatus.Cancelled;
            s.CancelledAt = DateTime.UtcNow;
            s.CancellationReason = why;
        }
        await db.SaveChangesAsync();

        var first = group[0];
        await NotifyClientsAsync(db, group, "Termin nie został potwierdzony",
            $"{first.StartTime:dddd d.MM, HH:mm}: {why}. Wybierz inny termin albo kup pakiet.");
        await LogAsync(actorUserId, "Trainer", "OffPackageRejected", sessionId, why);
    }

    public async Task MarkPaidAsync(int sessionId, string actorUserId, string via)
    {
        await using var db = dbFactory.CreateDbContext();
        var s = await db.Sessions.FirstOrDefaultAsync(x => x.Id == sessionId)
            ?? throw new InvalidOperationException("Nie znaleziono wizyty.");
        if (s.AwaitingApproval) throw new InvalidOperationException("Najpierw zaakceptuj termin.");
        if (s.PaidAt is not null) return;
        s.PaidAt = DateTime.UtcNow;
        s.PaidVia = via;
        s.OffPackagePayment ??= OffPackageRules.PayAtTrainer;
        s.HoldUntil = null;
        // Dawna wizyta „bez pakietu” — po opłacie jest zwykłym, potwierdzonym treningiem.
        if (s.Status == SessionStatus.AwaitingPackage) s.Status = SessionStatus.Scheduled;
        await db.SaveChangesAsync();
        await LogAsync(actorUserId, "Trainer", "OffPackagePaid", sessionId, $"Opłacone: {via}");
    }

    public async Task<int> ExpireHoldsAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var now = DateTime.UtcNow;
        var expired = await db.Sessions
            .Where(s => s.HoldUntil != null && s.HoldUntil < now && s.PaidAt == null && s.Status == SessionStatus.AwaitingPackage)
            .ToListAsync();
        foreach (var s in expired)
        {
            s.Status = SessionStatus.Cancelled;
            s.CancelledAt = now;
            s.CancellationReason = "Rezerwacja nieopłacona w ciągu 15 minut";
        }
        if (expired.Count > 0) await db.SaveChangesAsync();
        return expired.Count;
    }

    public async Task SetTrustedAsync(int clientId, bool trusted)
    {
        await using var db = dbFactory.CreateDbContext();
        var c = await db.Clients.FirstOrDefaultAsync(x => x.Id == clientId)
            ?? throw new InvalidOperationException("Nie znaleziono klienta.");
        c.TrustedForDeferredPayment = trusted;
        await db.SaveChangesAsync();
    }

    public async Task<OffPackageCountsDto> GetCountsAsync(string? trainerUserId = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var q = db.Sessions.AsNoTracking().Where(s => trainerUserId == null || s.TrainerUserId == trainerUserId);
        var requests = await q.CountAsync(s => s.AwaitingApproval && s.Status == SessionStatus.AwaitingPackage && !s.SharesPackageSlot);
        var unpaid = await q.CountAsync(s => s.OffPackagePayment == OffPackageRules.PayAtTrainer && s.PaidAt == null && !s.AwaitingApproval
            && (s.Status == SessionStatus.Scheduled || s.Status == SessionStatus.Completed));
        return new(requests, unpaid);
    }

    // ── pomocnicze ──

    private async Task<OffPackageOptionsDto> ValidateAsync(string clientUserId, CreateSessionDto dto)
    {
        await using (var db = dbFactory.CreateDbContext())
        {
            var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.ApplicationUserId == clientUserId);
            if (client is null || client.Id != dto.ClientId)
                throw new InvalidOperationException("Możesz rezerwować tylko swoje treningi.");
        }
        return await GetOptionsAsync(dto.ClientId, dto.SessionTypeId);
    }

    private async Task<OffPackagePolicy> PolicyAsync(string? trainerUserId)
    {
        var cfg = string.IsNullOrEmpty(trainerUserId) ? new TrainerConfigDto() : await trainerConfig.GetAsync(trainerUserId);
        return new OffPackagePolicy(cfg.OffPackageOnline, cfg.OffPackageAtTrainer, cfg.OffPackageNeedsApproval, cfg.OffPackageUnpaidLimit);
    }

    // Nieopłacone treningi „u trenera” (także prośby w toku) — do limitu z ustawień.
    private static Task<int> UnpaidVisitsAsync(ApplicationDbContext db, int clientId) =>
        db.Sessions.CountAsync(s => s.ClientId == clientId && s.OffPackagePayment == OffPackageRules.PayAtTrainer
            && s.PaidAt == null && s.Status != SessionStatus.Cancelled && !s.SharesPackageSlot);

    // Wizyta razem z wizytą partnera (trening w parze).
    private static async Task<List<Session>> GroupAsync(ApplicationDbContext db, int sessionId)
    {
        var s = await db.Sessions.FirstOrDefaultAsync(x => x.Id == sessionId);
        if (s is null) return [];
        if (s.PairGroupId is not Guid g) return [s];
        return await db.Sessions.Where(x => x.PairGroupId == g).OrderBy(x => x.Id).ToListAsync();
    }

    private async Task CancelAsync(int sessionId, string reason)
    {
        await using var db = dbFactory.CreateDbContext();
        var s = await db.Sessions.FirstOrDefaultAsync(x => x.Id == sessionId);
        if (s is null) return;
        s.Status = SessionStatus.Cancelled;
        s.CancelledAt = DateTime.UtcNow;
        s.CancellationReason = reason;
        await db.SaveChangesAsync();
    }

    private async Task NotifyTrainerAsync(SessionDto s, (string Title, string Body) msg)
    {
        try { await push.SendAsync(s.TrainerUserId, new PushMessageDto { Title = msg.Title, Body = msg.Body, Url = "/sessions" }); }
        catch (Exception ex) { logger.LogWarning(ex, "Powiadomienie trenera o rezerwacji poza pakietem nie wyszło (SessionId={Id})", s.Id); }
    }

    private async Task NotifyClientsAsync(ApplicationDbContext db, List<Session> group, string title, string body)
    {
        var clientIds = group.Select(s => s.ClientId).Distinct().ToList();
        var people = await db.Clients.Where(c => clientIds.Contains(c.Id))
            .Join(db.Users, c => c.ApplicationUserId, u => u.Id, (c, u) => new { u.Id, u.Email, c.FirstName })
            .ToListAsync();
        var emailOn = await SafeAsync(email.IsEnabledAsync);
        foreach (var p in people)
        {
            try { await push.SendAsync(p.Id, new PushMessageDto { Title = title, Body = body, Url = "/my" }); }
            catch (Exception ex) { logger.LogWarning(ex, "Push do klienta nie wyszedł"); }
            if (!emailOn || string.IsNullOrEmpty(p.Email)) continue;
            try
            {
                var html = $"<p>Cześć {System.Net.WebUtility.HtmlEncode(p.FirstName)},</p><p><b>{System.Net.WebUtility.HtmlEncode(title)}</b></p>"
                         + $"<p>{System.Net.WebUtility.HtmlEncode(body)}</p><p>Szczegóły znajdziesz w aplikacji, w zakładce „Wizyty”.</p>";
                await email.SendAsync(p.Email, p.FirstName, title, html);
            }
            catch (Exception ex) { logger.LogWarning(ex, "E-mail do klienta nie wyszedł"); }
        }
    }

    private static async Task<bool> SafeAsync(Func<Task<bool>> f)
    {
        try { return await f(); } catch { return false; }
    }

    private async Task LogAsync(string userId, string role, string action, int sessionId, string details)
    {
        try { await audit.LogAsync(userId, userId, role, action, "Session", sessionId.ToString(), details); }
        catch (Exception ex) { logger.LogWarning(ex, "Nie udało się zapisać historii zmian ({Action})", action); }
    }
}
