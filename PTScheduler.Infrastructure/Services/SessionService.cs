using PTScheduler.Application.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Exceptions;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class SessionService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IEmailService emailService,
    IEmailTemplateService emailTemplateService,
    INotificationPreferencesService notificationPrefs,
    IGoogleMeetService googleMeetService,
    ITrainerAvailabilityService availability,
    IAppClock clock,
    ILogger<SessionService> logger) : ISessionService
{
    public async Task<List<SessionDto>> GetSessionsAsync(DateTime from, DateTime to, string? trainerUserId = null, int? clientId = null)
    {
        // StartTime jest zegarem ściennym (kolumna timestamp without time zone),
        // więc granice zakresu też muszą nim być. Normalizacja Kind, nie konwersja —
        // wartość godziny pozostaje nietknięta.
        from = DateTime.SpecifyKind(from, DateTimeKind.Unspecified);
        to   = DateTime.SpecifyKind(to,   DateTimeKind.Unspecified);
        await using var db = dbFactory.CreateDbContext();
        var query = db.Sessions
            .AsNoTracking()
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .Where(s => s.StartTime >= from && s.StartTime < to);

        if (trainerUserId is not null)
        {
            var subordinateIds = await db.Users
                .Where(u => u.SupervisorId == trainerUserId)
                .Select(u => u.Id)
                .ToListAsync();

            var visibleTrainerIds = subordinateIds.Append(trainerUserId).ToList();
            query = query.Where(s => visibleTrainerIds.Contains(s.TrainerUserId));
        }

        if (clientId.HasValue)
            query = query.Where(s => s.ClientId == clientId.Value);

        var sessions = await query.OrderBy(s => s.StartTime).ToListAsync();
        var trainerIds = sessions.Select(s => s.TrainerUserId).Distinct().ToList();
        var trainers = await db.Users
            .Where(u => trainerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim().NullIfEmpty() ?? u.Email ?? "Trener");

        return await WithPairInfoAsync(db, sessions.Select(s => MapToDto(s, trainers)).ToList());
    }

    public async Task<List<SessionDto>> GetPastSessionsAsync(string? trainerUserId = null, int? clientId = null, int count = 50)
    {
        await using var db = dbFactory.CreateDbContext();
        // Zegar ścienny — StartTime nim jest. UtcNow dawałoby granicę przesuniętą
        // o offset strefy, więc sesja sprzed godziny mogła jeszcze uchodzić za przyszłą.
        var now = clock.LocalNow;
        var query = db.Sessions
            .AsNoTracking()
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .Where(s => s.StartTime < now || s.Status != SessionStatus.Scheduled);

        if (trainerUserId is not null)
        {
            var subordinateIds = await db.Users
                .Where(u => u.SupervisorId == trainerUserId)
                .Select(u => u.Id)
                .ToListAsync();
            var visibleIds = subordinateIds.Append(trainerUserId).ToList();
            query = query.Where(s => visibleIds.Contains(s.TrainerUserId));
        }

        if (clientId.HasValue)
            query = query.Where(s => s.ClientId == clientId.Value);

        var sessions = await query.OrderByDescending(s => s.StartTime).Take(count).ToListAsync();
        var trainerIds = sessions.Select(s => s.TrainerUserId).Distinct().ToList();
        var trainers = await db.Users
            .Where(u => trainerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id,
                u => $"{u.FirstName} {u.LastName}".Trim().NullIfEmpty() ?? u.Email ?? "Trener");
        return await WithPairInfoAsync(db, sessions.Select(s => MapToDto(s, trainers)).ToList());
    }

    public async Task<SessionDto?> GetSessionAsync(int id)
    {
        await using var db = dbFactory.CreateDbContext();
        var session = await db.Sessions
            .AsNoTracking()
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (session is null) return null;

        var trainer = await db.Users.FirstOrDefaultAsync(u => u.Id == session.TrainerUserId);
        var trainerName = $"{trainer?.FirstName} {trainer?.LastName}".Trim().NullIfEmpty() ?? trainer?.Email ?? session.TrainerUserId;
        return (await WithPairInfoAsync(db, [MapToDto(session, new Dictionary<string, string> { [session.TrainerUserId] = trainerName })]))[0];
    }

    public async Task<SessionDto> CreateSessionAsync(CreateSessionDto dto, bool allowAwaitingPackage = true, bool allowOverlap = false, bool sendConfirmation = true)
    {
        // Godzina przychodzi z <input type="datetime-local"> jako zegar ścienny.
        // Zapisujemy ją bez konwersji — 14:00 wpisane przez trenera to 14:00
        // w studiu, niezależnie od strefy kontenera.
        dto.StartTime = DateTime.SpecifyKind(dto.StartTime, DateTimeKind.Unspecified);
        await using var db = dbFactory.CreateDbContext();
        var sessionType = await db.SessionTypes.FindAsync(dto.SessionTypeId)
            ?? throw new InvalidOperationException("Typ sesji nie istnieje.");

        // Kontrola kolizji terminów. Bez tego trener mógł po cichu umówić dwóch
        // klientów na tę samą godzinę. allowOverlap pozwala na świadome nałożenie.
        if (!allowOverlap)
        {
            var conflict = await availability.FindConflictAsync(
                dto.TrainerUserId, dto.StartTime, sessionType.DurationMinutes);
            if (conflict is not null) throw new SlotConflictException(conflict);
        }

        // Pakiet musi być jeszcze ważny w dniu wizyty — wcześniej rezerwacja na
        // termin po dacie ważności pobierała sesję z pakietu, który do tego czasu wygaśnie.
        var package = await FindOwnPackageAsync(db, dto.ClientId, dto.SessionTypeId, clock.ToUtc(dto.StartTime));

        if (package is null && !allowAwaitingPackage)
            throw new InvalidOperationException("Nie masz aktywnego pakietu dla tego rodzaju sesji.");

        var session = new Session
        {
            ClientId      = dto.ClientId,
            SessionTypeId = dto.SessionTypeId,
            TrainerUserId = dto.TrainerUserId,
            StartTime     = dto.StartTime,
            Status        = package is not null ? SessionStatus.Scheduled : SessionStatus.AwaitingPackage,
            PackageId     = package?.Id,
            Notes         = dto.Notes,
            CreatedAt     = DateTime.UtcNow
        };

        db.Sessions.Add(session);

        if (package is not null)
        {
            package.UsedSessions++;
            if (package.UsedSessions >= package.TotalSessions)
                package.Status = PackageStatus.Depleted;
        }

        await db.SaveChangesAsync();

        try
        {
            if (await googleMeetService.CanCreateMeetingsAsync(session.TrainerUserId))
            {
                var client = await db.Clients.FindAsync(session.ClientId);
                var clientUser = client is not null
                    ? await db.Users.FirstOrDefaultAsync(u => u.Id == client.ApplicationUserId)
                    : null;
                var result = await googleMeetService.CreateMeetingAsync(
                    $"{sessionType.Name} — {client?.FirstName} {client?.LastName}".Trim(),
                    $"Sesja treningowa: {sessionType.Name}, {sessionType.DurationMinutes} min",
                    clock.ToUtc(session.StartTime), // StartTime to zegar ścienny, a Google dostaje UTC
                    sessionType.DurationMinutes,
                    clientUser?.Email,
                    session.TrainerUserId,
                    session.Id);
                if (result is not null)
                {
                    session.MeetingUrl = result.MeetingUrl;
                    session.CalendarEventId = result.CalendarEventId;
                    await db.SaveChangesAsync();
                }
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Błąd tworzenia Google Meet (SessionId={Id})", session.Id); }

        // Prośba o termin (poza pakietem, do akceptacji trenera) nie dostaje maila „zarezerwowano”.
        if (sendConfirmation)
        {
            try { await SendBookingConfirmationAsync(session, sessionType); }
            catch (Exception ex) { logger.LogWarning(ex, "Błąd wysyłki emaila potwierdzającego rezerwację (SessionId={Id})", session.Id); }
        }
        return (await GetSessionAsync(session.Id))!;
    }

    // Własny pakiet osoby (nie pakiet pary). Musi być ważny w dniu wizyty — wcześniej rezerwacja
    // na termin po dacie ważności pobierała sesję z pakietu, który do tego czasu wygaśnie.
    private static Task<SessionPackage?> FindOwnPackageAsync(ApplicationDbContext db, int clientId, int sessionTypeId, DateTime sessionStartUtc) =>
        db.SessionPackages
            .Where(p => p.ClientId == clientId
                     && p.PartnerClientId == null
                     && p.SessionTypeId == sessionTypeId
                     && p.Status == PackageStatus.Active
                     && p.UsedSessions < p.TotalSessions
                     && (p.ExpiresAt == null || p.ExpiresAt >= sessionStartUtc))
            .OrderBy(p => p.ExpiresAt ?? DateTime.MaxValue)
            .FirstOrDefaultAsync();

    // Wspólny pakiet tej pary (kupiony przez którąkolwiek z osób).
    private static Task<SessionPackage?> FindPairPackageAsync(ApplicationDbContext db, int clientA, int clientB, int sessionTypeId, DateTime sessionStartUtc) =>
        db.SessionPackages
            .Where(p => p.PartnerClientId != null
                     && ((p.ClientId == clientA && p.PartnerClientId == clientB) || (p.ClientId == clientB && p.PartnerClientId == clientA))
                     && p.SessionTypeId == sessionTypeId
                     && p.Status == PackageStatus.Active
                     && p.UsedSessions < p.TotalSessions
                     && (p.ExpiresAt == null || p.ExpiresAt >= sessionStartUtc))
            .OrderBy(p => p.ExpiresAt ?? DateTime.MaxValue)
            .FirstOrDefaultAsync();

    private static void Consume(SessionPackage package)
    {
        package.UsedSessions++;
        if (package.UsedSessions >= package.TotalSessions)
            package.Status = PackageStatus.Depleted;
    }

    public async Task<SessionDto> CreatePairSessionAsync(CreateSessionDto dto, int partnerClientId, bool allowAwaitingPackage = true, bool allowOverlap = false, bool sendConfirmation = true)
    {
        if (partnerClientId == dto.ClientId)
            throw new InvalidOperationException("Wybierz drugą osobę do pary.");
        dto.StartTime = DateTime.SpecifyKind(dto.StartTime, DateTimeKind.Unspecified);
        await using var db = dbFactory.CreateDbContext();
        var sessionType = await db.SessionTypes.FindAsync(dto.SessionTypeId)
            ?? throw new InvalidOperationException("Typ sesji nie istnieje.");
        if (!await db.Clients.AnyAsync(c => c.Id == partnerClientId))
            throw new InvalidOperationException("Nie znaleziono partnera.");

        // Jeden trening = jedna kontrola kolizji (dwie wizyty zajmują tę samą godzinę).
        if (!allowOverlap)
        {
            var conflict = await availability.FindConflictAsync(dto.TrainerUserId, dto.StartTime, sessionType.DurationMinutes);
            if (conflict is not null) throw new SlotConflictException(conflict);
        }

        // Wspólny pakiet pary: za trening schodzi 1 (pobiera go wizyta osoby rezerwującej).
        // Bez niego każda osoba korzysta z własnego pakietu.
        var startUtc = clock.ToUtc(dto.StartTime);
        var pairPackage = await FindPairPackageAsync(db, dto.ClientId, partnerClientId, dto.SessionTypeId, startUtc);
        var ownPackage = pairPackage is null ? await FindOwnPackageAsync(db, dto.ClientId, dto.SessionTypeId, startUtc) : null;
        var partnerPackage = pairPackage is null ? await FindOwnPackageAsync(db, partnerClientId, dto.SessionTypeId, startUtc) : null;

        if (pairPackage is null && ownPackage is null && !allowAwaitingPackage)
            throw new InvalidOperationException("Nie masz aktywnego pakietu dla tego rodzaju sesji.");

        var groupId = Guid.NewGuid();
        var lead = new Session
        {
            ClientId = dto.ClientId,
            SessionTypeId = dto.SessionTypeId,
            TrainerUserId = dto.TrainerUserId,
            StartTime = dto.StartTime,
            PackageId = pairPackage?.Id ?? ownPackage?.Id,
            Status = (pairPackage ?? ownPackage) is not null ? SessionStatus.Scheduled : SessionStatus.AwaitingPackage,
            Notes = dto.Notes,
            PairGroupId = groupId,
            CreatedAt = DateTime.UtcNow
        };
        var partner = new Session
        {
            ClientId = partnerClientId,
            SessionTypeId = dto.SessionTypeId,
            TrainerUserId = dto.TrainerUserId,
            StartTime = dto.StartTime,
            PackageId = pairPackage?.Id ?? partnerPackage?.Id,
            SharesPackageSlot = pairPackage is not null,
            Status = (pairPackage ?? partnerPackage) is not null ? SessionStatus.Scheduled : SessionStatus.AwaitingPackage,
            Notes = dto.Notes,
            PairGroupId = groupId,
            CreatedAt = DateTime.UtcNow
        };
        if (pairPackage is not null) Consume(pairPackage);
        if (ownPackage is not null) Consume(ownPackage);
        if (partnerPackage is not null) Consume(partnerPackage);

        // Osobne zapisy, żeby wizyta rezerwującego miała niższe Id (prowadząca w kalendarzu).
        db.Sessions.Add(lead);
        await db.SaveChangesAsync();
        db.Sessions.Add(partner);
        await db.SaveChangesAsync();

        try
        {
            if (await googleMeetService.CanCreateMeetingsAsync(lead.TrainerUserId))
            {
                var names = await db.Clients.Where(c => c.Id == lead.ClientId || c.Id == partner.ClientId)
                    .Select(c => new { c.Id, c.FirstName, c.LastName, c.ApplicationUserId }).ToListAsync();
                var leadClient = names.FirstOrDefault(c => c.Id == lead.ClientId);
                var leadUser = leadClient is not null ? await db.Users.FirstOrDefaultAsync(u => u.Id == leadClient.ApplicationUserId) : null;
                var title = string.Join(" + ", names.OrderBy(c => c.Id == lead.ClientId ? 0 : 1).Select(c => $"{c.FirstName} {c.LastName}".Trim()));
                var result = await googleMeetService.CreateMeetingAsync(
                    $"{sessionType.Name} — {title}",
                    $"Trening w parze: {sessionType.Name}, {sessionType.DurationMinutes} min",
                    startUtc, sessionType.DurationMinutes, leadUser?.Email, lead.TrainerUserId, lead.Id);
                if (result is not null)
                {
                    lead.MeetingUrl = result.MeetingUrl;
                    lead.CalendarEventId = result.CalendarEventId;
                    partner.MeetingUrl = result.MeetingUrl;
                    await db.SaveChangesAsync();
                }
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Błąd tworzenia Google Meet (SessionId={Id})", lead.Id); }

        foreach (var s in sendConfirmation ? new[] { lead, partner } : [])
        {
            try { await SendBookingConfirmationAsync(s, sessionType); }
            catch (Exception ex) { logger.LogWarning(ex, "Błąd wysyłki emaila potwierdzającego rezerwację (SessionId={Id})", s.Id); }
        }
        return (await GetSessionAsync(lead.Id))!;
    }

    public async Task UpdateStatusAsync(int id, SessionStatus status, string? cancellationReason = null, string? completionNotes = null,
        bool chargeSession = false, bool includePartner = false)
    {
        await using var db = dbFactory.CreateDbContext();
        var session = await db.Sessions
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new InvalidOperationException("Session not found.");

        // Trening w parze: includePartner zmienia status także wizyty drugiej osoby (np. „Odwołaj trening”
        // albo „Zakończ” dla obojga). Bez niego zmienia się tylko ta jedna wizyta.
        var siblings = session.PairGroupId is null ? [] : await db.Sessions
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .Where(s => s.PairGroupId == session.PairGroupId && s.Id != session.Id)
            .ToListAsync();
        var targets = new List<Session> { session };
        if (includePartner)
            targets.AddRange(siblings.Where(s => s.Status != SessionStatus.Cancelled && s.Status != status));

        var cfg = status == SessionStatus.NoShow ? await availability.GetConfigAsync(session.TrainerUserId) : null;
        foreach (var t in targets)
        {
            if (status == SessionStatus.Cancelled)
            {
                t.CancelledAt = DateTime.UtcNow;
                t.CancellationReason = cancellationReason;
                // chargeSession: trener odwołuje w imieniu klienta po terminie — sesja przepada.
                t.IsLateCancellation = chargeSession;
                if (!chargeSession)
                    await RefundPackageSlotAsync(db, t);
            }
            else if (status == SessionStatus.NoShow && t.Status != SessionStatus.NoShow)
            {
                if (!cfg!.NoShowChargesSession)
                    await RefundPackageSlotAsync(db, t);
            }

            if (status == SessionStatus.Completed && completionNotes is not null)
                t.Notes = completionNotes;

            t.Status = status;
        }
        await db.SaveChangesAsync();

        if (status == SessionStatus.Cancelled)
        {
            // Druga osoba z pary zostaje na treningu — spotkanie Google Meet zostaje, a ona dostaje informację.
            var remaining = siblings.Where(s => s.Status != SessionStatus.Cancelled).ToList();
            if (remaining.Count == 0)
            {
                foreach (var t in targets.Where(t => t.CalendarEventId is not null))
                {
                    try { await googleMeetService.DeleteMeetingAsync(t.CalendarEventId!, t.TrainerUserId); }
                    catch (Exception ex) { logger.LogWarning(ex, "Błąd usuwania Google Meet (SessionId={Id})", t.Id); }
                }
            }

            foreach (var t in targets)
            {
                try { await SendCancellationEmailAsync(t, cancellationReason); }
                catch (Exception ex) { logger.LogWarning(ex, "Błąd wysyłki emaila o anulowaniu (SessionId={Id})", t.Id); }
            }
            foreach (var r in remaining)
            {
                try { await SendPartnerCancelledAsync(r, session); }
                catch (Exception ex) { logger.LogWarning(ex, "Błąd wysyłki emaila do partnera (SessionId={Id})", r.Id); }
            }
        }
    }

    public async Task RescheduleAsync(int id, DateTime newStartTime, bool allowOverlap = false)
    {
        newStartTime = DateTime.SpecifyKind(newStartTime, DateTimeKind.Unspecified);
        await using var db = dbFactory.CreateDbContext();
        var session = await db.Sessions
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new InvalidOperationException("Sesja nie została znaleziona.");

        // Trening w parze przenosimy w całości — obie wizyty dostają nowy termin.
        var moved = new List<Session> { session };
        if (session.PairGroupId is not null)
            moved.AddRange(await db.Sessions
                .Include(s => s.Client)
                .Include(s => s.SessionType)
                .Where(s => s.PairGroupId == session.PairGroupId && s.Id != session.Id && s.Status != SessionStatus.Cancelled)
                .ToListAsync());

        // Kontrola kolizji, wykluczając samą przenoszoną sesję (i drugą wizytę tej samej pary) —
        // inaczej ich stary rekord (wciąż w bazie) kolidowałby z nowym terminem.
        if (!allowOverlap)
        {
            var conflict = await availability.FindConflictAsync(
                session.TrainerUserId, newStartTime, session.SessionType.DurationMinutes,
                excludeSessionId: session.Id, excludePairGroupId: session.PairGroupId);
            if (conflict is not null) throw new SlotConflictException(conflict);
        }

        var oldTime = session.StartTime;
        foreach (var m in moved) m.StartTime = newStartTime;
        await db.SaveChangesAsync();
        foreach (var m in moved)
        {
            try { await SendRescheduleEmailAsync(m, oldTime); }
            catch (Exception ex) { logger.LogWarning(ex, "Błąd wysyłki emaila o zmianie terminu (SessionId={Id})", m.Id); }
        }
    }

    public async Task RestoreAsync(int id)
    {
        await using var db = dbFactory.CreateDbContext();
        var session = await db.Sessions.FindAsync(id)
            ?? throw new InvalidOperationException("Sesja nie została znaleziona.");

        var wasStatus = session.Status;
        if (wasStatus != SessionStatus.Cancelled && wasStatus != SessionStatus.NoShow)
            throw new InvalidOperationException("Można przywrócić tylko anulowane lub nieobecne wizyty.");

        session.CancelledAt = null;
        session.CancellationReason = null;

        session.IsLateCancellation = false;

        // Wspólny pakiet pary: jeśli za ten trening pakiet pobrała już druga osoba, nic nie schodzi.
        // Gdy druga osoba odwołała, ta wizyta przejmuje pobranie.
        if (session.PairGroupId is not null && session.PackageId.HasValue && (session.SharesPackageSlot || session.PackageRefunded))
        {
            var covered = await db.Sessions.AnyAsync(x => x.PairGroupId == session.PairGroupId && x.Id != session.Id
                && x.PackageId == session.PackageId && !x.SharesPackageSlot && !x.PackageRefunded
                && x.Status != SessionStatus.Cancelled);
            if (covered)
            {
                session.SharesPackageSlot = true;
                session.PackageRefunded = false;
                session.Status = SessionStatus.Scheduled;
                await db.SaveChangesAsync();
                return;
            }
            if (session.SharesPackageSlot)
            {
                session.SharesPackageSlot = false;
                session.PackageRefunded = true;
            }
        }

        if (session.PackageRefunded && session.PackageId.HasValue)
        {
            var pkg = await db.SessionPackages.FindAsync(session.PackageId.Value);
            if (pkg is not null && pkg.Status != PackageStatus.Cancelled
                && (pkg.Status == PackageStatus.Active || pkg.Status == PackageStatus.Depleted))
            {
                pkg.UsedSessions++;
                if (pkg.UsedSessions >= pkg.TotalSessions && pkg.Status == PackageStatus.Active)
                    pkg.Status = PackageStatus.Depleted;
                session.Status = SessionStatus.Scheduled;
            }
            else
            {
                session.PackageId = null;
                session.Status = SessionStatus.AwaitingPackage;
            }
            session.PackageRefunded = false;
        }
        else
        {
            session.Status = session.PackageId.HasValue ? SessionStatus.Scheduled : SessionStatus.AwaitingPackage;
        }

        await db.SaveChangesAsync();
    }

    public async Task<CancellationDecision> GetClientCancellationAsync(int id, string clientUserId)
    {
        await using var db = dbFactory.CreateDbContext();
        var session = await db.Sessions.AsNoTracking().Include(x => x.Client)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (session is null || session.Client.ApplicationUserId != clientUserId)
            return new CancellationDecision(false, false, false, "Nie znaleziono wizyty.");
        if (session.Status is not (SessionStatus.Scheduled or SessionStatus.AwaitingPackage))
            return new CancellationDecision(false, false, false, "Tej wizyty nie można już odwołać.");
        var cfg = await availability.GetConfigAsync(session.TrainerUserId);
        return CancellationRules.ForClient(cfg, session.StartTime, clock.LocalNow);
    }

    public async Task<CancellationDecision> ClientCancelSessionAsync(int id, string clientUserId, string? reason = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var session = await db.Sessions
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new InvalidOperationException("Sesja nie została znaleziona.");

        // Serwer egzekwuje zasady niezależnie od UI: tylko własna wizyta, tylko
        // zaplanowana, i zgodnie z polityką odwołań trenera. Wcześniej metoda
        // ignorowała clientUserId i okno odwołania — dashboard klienta pozwalał
        // odwołać wizytę 5 minut przed startem z pełnym zwrotem sesji.
        if (session.Client.ApplicationUserId != clientUserId)
            throw new InvalidOperationException("Sesja nie została znaleziona.");
        if (session.Status is not (SessionStatus.Scheduled or SessionStatus.AwaitingPackage))
            throw new InvalidOperationException("Tej wizyty nie można już odwołać.");

        var cfg = await availability.GetConfigAsync(session.TrainerUserId);
        var decision = CancellationRules.ForClient(cfg, session.StartTime, clock.LocalNow);
        if (!decision.Allowed)
            throw new InvalidOperationException(decision.Message);

        session.Status = SessionStatus.Cancelled;
        session.CancelledAt = DateTime.UtcNow;
        session.CancellationReason = reason;
        session.IsLateCancellation = decision.IsLate;

        if (decision.RefundsSession)
            await RefundPackageSlotAsync(db, session);

        await db.SaveChangesAsync();

        try { await SendClientCancelledToTrainerAsync(session, reason); }
        catch (Exception ex) { logger.LogWarning(ex, "Błąd wysyłki emaila o anulowaniu przez klienta (SessionId={Id})", session.Id); }

        // Trening w parze: druga osoba zostaje na treningu i dostaje o tym informację.
        if (session.PairGroupId is not null)
        {
            var remaining = await db.Sessions.Include(s => s.Client).Include(s => s.SessionType)
                .Where(s => s.PairGroupId == session.PairGroupId && s.Id != session.Id && s.Status != SessionStatus.Cancelled)
                .ToListAsync();
            foreach (var r in remaining)
            {
                try { await SendPartnerCancelledAsync(r, session); }
                catch (Exception ex) { logger.LogWarning(ex, "Błąd wysyłki emaila do partnera (SessionId={Id})", r.Id); }
            }
        }
        return decision;
    }

    // Zwalnia miejsce sesji w pakiecie (jeśli jeszcze go nie zwolniła).
    // Wspólny pakiet pary: wizyta, która nie pobrała sesji (SharesPackageSlot), nie ma czego oddawać.
    // Gdy odwołuje osoba, której wizyta pobrała sesję, a druga zostaje — pobranie przechodzi na drugą.
    private static async Task RefundPackageSlotAsync(ApplicationDbContext db, Session session)
    {
        if (!session.PackageId.HasValue || session.PackageRefunded) return;
        if (session.SharesPackageSlot)
        {
            session.PackageRefunded = true;
            return;
        }
        if (session.PairGroupId is not null)
        {
            // Bez filtra statusu w SQL — status mógł już zostać zmieniony w tej samej operacji (w pamięci).
            var siblings = await db.Sessions.Where(x => x.PairGroupId == session.PairGroupId && x.Id != session.Id).ToListAsync();
            var heir = siblings.FirstOrDefault(x => x.SharesPackageSlot && x.PackageId == session.PackageId && !x.PackageRefunded
                && x.Status is SessionStatus.Scheduled or SessionStatus.Completed);
            if (heir is not null)
            {
                heir.SharesPackageSlot = false;
                session.SharesPackageSlot = true;
                session.PackageRefunded = true;
                return;
            }
        }
        var pkg = await db.SessionPackages.FindAsync(session.PackageId.Value);
        if (pkg is null || pkg.Status == PackageStatus.Cancelled) return;
        if (pkg.UsedSessions > 0) pkg.UsedSessions--;
        if (pkg.Status == PackageStatus.Depleted && pkg.UsedSessions < pkg.TotalSessions)
            pkg.Status = PackageStatus.Active;
        session.PackageRefunded = true;
    }

    public async Task<List<SessionTypeDto>> GetSessionTypesAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.SessionTypes
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.DurationMinutes)
            .Select(t => new SessionTypeDto
            {
                Id = t.Id,
                Name = t.Name,
                DurationMinutes = t.DurationMinutes,
                IsGroup = t.IsGroup,
                MaxParticipants = t.MaxParticipants,
                IsPair = t.IsPair,
                IsActive = t.IsActive,
                SinglePrice = t.SinglePrice,
                RequiresPackage = t.RequiresPackage
            })
            .ToListAsync();
    }

    public async Task<List<ClientSummaryDto>> GetClientsAsync(string? trainerUserId = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var clients = await db.Clients
            .AsNoTracking()
            .Where(c => trainerUserId == null || c.TrainerUserId == trainerUserId)
            .ToListAsync();

        var userIds = clients.Select(c => c.ApplicationUserId).ToList();
        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        return clients.Select(c =>
        {
            var user = users.GetValueOrDefault(c.ApplicationUserId);
            var fullName = $"{c.FirstName} {c.LastName}".Trim();
            return new ClientSummaryDto
            {
                Id = c.Id,
                ApplicationUserId = c.ApplicationUserId,
                FullName = string.IsNullOrEmpty(fullName) ? (user?.Email ?? "Klient") : fullName,
                Email = user?.Email ?? string.Empty
            };
        }).OrderBy(c => c.FullName).ToList();
    }

    public async Task<List<SessionDto>> GetClientSessionsAsync(int clientId, int count = 20)
    {
        await using var db = dbFactory.CreateDbContext();
        var sessions = await db.Sessions
            .AsNoTracking()
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .Where(s => s.ClientId == clientId)
            .OrderByDescending(s => s.StartTime)
            .Take(count)
            .ToListAsync();

        var trainerIds = sessions.Select(s => s.TrainerUserId).Distinct().ToList();
        var trainers = await db.Users
            .Where(u => trainerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id,
                u => $"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } n ? n : u.Email ?? "Trener");

        return await WithPairInfoAsync(db, sessions.Select(s => MapToDto(s, trainers)).ToList());
    }

    public async Task<List<SessionDto>> GetUpcomingAsync(string? trainerUserId = null, int? clientId = null, int count = 10)
    {
        await using var db = dbFactory.CreateDbContext();
        var now = clock.LocalNow;   // zegar ścienny — porównywany ze StartTime
        var query = db.Sessions
            .AsNoTracking()
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .Where(s => s.StartTime >= now
                        && (s.Status == SessionStatus.Scheduled || s.Status == SessionStatus.AwaitingPackage));

        if (trainerUserId is not null)
            query = query.Where(s => s.TrainerUserId == trainerUserId);

        if (clientId.HasValue)
            query = query.Where(s => s.ClientId == clientId.Value);

        var sessions = await query.OrderBy(s => s.StartTime).Take(count).ToListAsync();

        var trainerIds = sessions.Select(s => s.TrainerUserId).Distinct().ToList();
        var trainers = await db.Users
            .Where(u => trainerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id,
                u => $"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } n ? n : u.Email ?? "Trener");

        return await WithPairInfoAsync(db, sessions.Select(s => MapToDto(s, trainers)).ToList());
    }

    public async Task<List<SessionDto>> GetAwaitingPackageAsync(string? trainerUserId = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var now = clock.LocalNow;   // zegar ścienny — porównywany ze StartTime
        var query = db.Sessions
            .AsNoTracking()
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .Where(s => s.Status == SessionStatus.AwaitingPackage && s.StartTime >= now
                        && !s.AwaitingApproval && s.HoldUntil == null);   // prośby i płatności online mają osobne miejsca

        if (trainerUserId is not null)
            query = query.Where(s => s.TrainerUserId == trainerUserId);

        var sessions = await query.OrderBy(s => s.StartTime).ToListAsync();
        var trainerIds = sessions.Select(s => s.TrainerUserId).Distinct().ToList();
        var trainers = await db.Users
            .Where(u => trainerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id,
                u => $"{u.FirstName} {u.LastName}".Trim().NullIfEmpty() ?? u.Email ?? "Trener");
        return await WithPairInfoAsync(db, sessions.Select(s => MapToDto(s, trainers)).ToList());
    }

    private async Task SendBookingConfirmationAsync(Session session, SessionType sessionType)
    {
        if (!await emailService.IsEnabledAsync()) return;
        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.FindAsync(session.ClientId);
        if (client is null) return;
        if (!await notificationPrefs.IsEnabledAsync(client.ApplicationUserId, NotificationTypes.SessionBooked)) return;
        var clientUser = await db.Users.FirstOrDefaultAsync(u => u.Id == client.ApplicationUserId);
        if (clientUser?.Email is null) return;

        var trainer = await db.Users.FirstOrDefaultAsync(u => u.Id == session.TrainerUserId);
        var trainerName = $"{trainer?.FirstName} {trainer?.LastName}".Trim().NullIfEmpty() ?? trainer?.Email ?? "Trener";
        var clientName = $"{client.FirstName} {client.LastName}".Trim().NullIfEmpty() ?? clientUser.Email;
        var meetRow = !string.IsNullOrWhiteSpace(session.MeetingUrl)
            ? $"<tr><td style=\"padding:8px 0;color:#6b7280;font-size:14px\">Google Meet</td><td style=\"padding:8px 0;font-size:14px\"><a href=\"{session.MeetingUrl}\">{session.MeetingUrl}</a></td></tr>"
            : "";
        var vars = new Dictionary<string, string>
        {
            ["ClientName"] = clientName,
            ["TrainerName"] = trainerName,
            ["SessionType"] = sessionType.Name,
            ["SessionDate"] = session.StartTime.ToString("dddd, dd MMMM yyyy"),
            ["SessionTime"] = session.StartTime.ToString("HH:mm"),
            ["Duration"] = sessionType.DurationMinutes.ToString(),
            ["MeetingUrl"] = session.MeetingUrl ?? "",
            ["MeetingRow"] = meetRow
        };
        var (subject, html) = await emailTemplateService.RenderAsync("session-booked", vars);
        await emailService.SendAsync(clientUser.Email, clientName, subject, html);
    }

    private async Task SendCancellationEmailAsync(Session session, string? reason)
    {
        if (!await emailService.IsEnabledAsync()) return;
        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.FindAsync(session.ClientId);
        if (client is null) return;
        if (!await notificationPrefs.IsEnabledAsync(client.ApplicationUserId, NotificationTypes.SessionCancelledByTrainer)) return;
        var clientUser = await db.Users.FirstOrDefaultAsync(u => u.Id == session.Client.ApplicationUserId);
        if (clientUser?.Email is null) return;

        var trainer = await db.Users.FirstOrDefaultAsync(u => u.Id == session.TrainerUserId);
        var trainerName = $"{trainer?.FirstName} {trainer?.LastName}".Trim().NullIfEmpty() ?? trainer?.Email ?? "Trener";
        var clientName = $"{session.Client.FirstName} {session.Client.LastName}".Trim().NullIfEmpty() ?? clientUser.Email;
        var reasonRow = reason is not null
            ? $"<tr><td style=\"padding:8px 0;color:#6b7280;font-size:14px\">Powód</td><td style=\"padding:8px 0;font-size:14px\">{reason}</td></tr>"
            : "";
        var vars = new Dictionary<string, string>
        {
            ["ClientName"] = clientName,
            ["TrainerName"] = trainerName,
            ["SessionType"] = session.SessionType.Name,
            ["SessionDate"] = session.StartTime.ToString("dddd, dd MMMM yyyy"),
            ["SessionTime"] = session.StartTime.ToString("HH:mm"),
            ["Reason"] = reason ?? "",
            ["ReasonRow"] = reasonRow
        };
        var (subject, html) = await emailTemplateService.RenderAsync("session-cancelled", vars);
        await emailService.SendAsync(clientUser.Email, clientName, subject, html);
    }

    private async Task SendClientCancelledToTrainerAsync(Session session, string? reason)
    {
        if (!await emailService.IsEnabledAsync()) return;
        if (!await notificationPrefs.IsEnabledAsync(session.TrainerUserId, NotificationTypes.ClientCancelledSession)) return;
        await using var db = dbFactory.CreateDbContext();
        var trainer = await db.Users.FirstOrDefaultAsync(u => u.Id == session.TrainerUserId);
        if (trainer?.Email is null) return;
        var client = await db.Clients.FindAsync(session.ClientId);
        var clientName = client is not null ? $"{client.FirstName} {client.LastName}".Trim() : "Klient";
        var trainerName = $"{trainer.FirstName} {trainer.LastName}".Trim() is { Length: > 0 } n ? n : trainer.Email;
        var reasonRow = reason is not null
            ? $"<tr><td style=\"padding:8px 0;color:#6b7280;font-size:14px\">Powód</td><td style=\"padding:8px 0;font-size:14px\">{reason}</td></tr>"
            : "";
        var vars = new Dictionary<string, string>
        {
            ["ClientName"] = clientName,
            ["TrainerName"] = trainerName,
            ["SessionType"] = session.SessionType.Name,
            ["SessionDate"] = session.StartTime.ToString("dd.MM.yyyy"),
            ["SessionTime"] = session.StartTime.ToString("HH:mm"),
            ["Reason"] = reason ?? "",
            ["ReasonRow"] = reasonRow
        };
        var (subject, html) = await emailTemplateService.RenderAsync("session-cancelled-by-client", vars);
        await emailService.SendAsync(trainer.Email, trainerName, subject, html);
    }

    private async Task SendRescheduleEmailAsync(Session session, DateTime oldTime)
    {
        if (!await emailService.IsEnabledAsync()) return;
        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.FindAsync(session.ClientId);
        if (client is null) return;
        if (!await notificationPrefs.IsEnabledAsync(client.ApplicationUserId, NotificationTypes.SessionRescheduled)) return;
        var clientUser = await db.Users.FirstOrDefaultAsync(u => u.Id == client.ApplicationUserId);
        if (clientUser?.Email is null) return;
        var trainer = await db.Users.FirstOrDefaultAsync(u => u.Id == session.TrainerUserId);
        var trainerName = $"{trainer?.FirstName} {trainer?.LastName}".Trim() is { Length: > 0 } tn ? tn : trainer?.Email ?? "Trener";
        var clientName = $"{client.FirstName} {client.LastName}".Trim() is { Length: > 0 } cn ? cn : clientUser.Email;
        var sessionType = await db.SessionTypes.FindAsync(session.SessionTypeId);
        var vars = new Dictionary<string, string>
        {
            ["ClientName"] = clientName,
            ["TrainerName"] = trainerName,
            ["SessionType"] = sessionType?.Name ?? "",
            ["OldDate"] = oldTime.ToString("dd.MM.yyyy"),
            ["OldTime"] = oldTime.ToString("HH:mm"),
            ["NewDate"] = session.StartTime.ToString("dddd, dd MMMM yyyy"),
            ["NewTime"] = session.StartTime.ToString("HH:mm")
        };
        var (subject, html) = await emailTemplateService.RenderAsync("session-rescheduled", vars);
        await emailService.SendAsync(clientUser.Email, clientName, subject, html);
    }

    // Uzupełnia informacje o drugiej osobie z pary i o tym, która wizyta jest „prowadząca”
    // (najniższe Id wśród nieodwołanych) — trener widzi trening w kalendarzu raz.
    private static async Task<List<SessionDto>> WithPairInfoAsync(ApplicationDbContext db, List<SessionDto> dtos)
    {
        var groups = dtos.Where(d => d.PairGroupId.HasValue).Select(d => d.PairGroupId!.Value).Distinct().ToList();
        if (groups.Count == 0) return dtos;
        var rows = await db.Sessions.AsNoTracking()
            .Where(s => s.PairGroupId != null && groups.Contains(s.PairGroupId.Value))
            .Select(s => new { s.Id, s.PairGroupId, s.ClientId, s.Status, s.SharesPackageSlot, s.Client.FirstName, s.Client.LastName })
            .ToListAsync();
        var byGroup = rows.GroupBy(r => r.PairGroupId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var d in dtos.Where(d => d.PairGroupId.HasValue))
        {
            if (!byGroup.TryGetValue(d.PairGroupId!.Value, out var members)) continue;
            var partner = members.Where(m => m.Id != d.Id).OrderBy(m => m.Id).FirstOrDefault();
            if (partner is not null)
            {
                d.PartnerSessionId = partner.Id;
                d.PartnerClientId = partner.ClientId;
                d.PartnerName = $"{partner.FirstName} {partner.LastName}".Trim().NullIfEmpty() ?? "Partner";
                d.PartnerStatus = partner.Status;
                d.PackageShared = d.SharesPackageSlot || partner.SharesPackageSlot;
            }
            var active = members.Where(m => m.Status != SessionStatus.Cancelled).ToList();
            d.IsPairFollower = d.Id != (active.Count > 0 ? active : members).Min(m => m.Id);
        }
        return dtos;
    }

    private async Task SendPartnerCancelledAsync(Session remaining, Session cancelled)
    {
        if (!await emailService.IsEnabledAsync()) return;
        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.FindAsync(remaining.ClientId);
        if (client is null) return;
        if (!await notificationPrefs.IsEnabledAsync(client.ApplicationUserId, NotificationTypes.SessionCancelledByTrainer)) return;
        var clientUser = await db.Users.FirstOrDefaultAsync(u => u.Id == client.ApplicationUserId);
        if (clientUser?.Email is null) return;
        var partner = await db.Clients.FindAsync(cancelled.ClientId);
        var trainer = await db.Users.FirstOrDefaultAsync(u => u.Id == remaining.TrainerUserId);
        var trainerName = $"{trainer?.FirstName} {trainer?.LastName}".Trim().NullIfEmpty() ?? trainer?.Email ?? "Trener";
        var clientName = $"{client.FirstName} {client.LastName}".Trim().NullIfEmpty() ?? clientUser.Email;
        var sessionType = await db.SessionTypes.FindAsync(remaining.SessionTypeId);
        var vars = new Dictionary<string, string>
        {
            ["ClientName"] = clientName,
            ["PartnerName"] = partner?.FirstName.NullIfEmpty() ?? "Partner",
            ["TrainerName"] = trainerName,
            ["SessionType"] = sessionType?.Name ?? "",
            ["SessionDate"] = remaining.StartTime.ToString("dddd, dd MMMM yyyy"),
            ["SessionTime"] = remaining.StartTime.ToString("HH:mm")
        };
        var (subject, html) = await emailTemplateService.RenderAsync("pair-partner-cancelled", vars);
        await emailService.SendAsync(clientUser.Email, clientName, subject, html);
    }

    private static SessionDto MapToDto(Session s, Dictionary<string, string> trainers) => new()
    {
        Id = s.Id,
        ClientId = s.ClientId,
        ClientName = $"{s.Client.FirstName} {s.Client.LastName}".Trim().NullIfEmpty() ?? s.Client.ApplicationUserId,
        ClientEmail = string.Empty,
        SessionTypeId = s.SessionTypeId,
        SessionTypeName = s.SessionType.Name,
        DurationMinutes = s.SessionType.DurationMinutes,
        TrainerUserId = s.TrainerUserId,
        TrainerName = trainers.GetValueOrDefault(s.TrainerUserId, "Trener"),
        StartTime = s.StartTime,
        Status = s.Status,
        Notes = s.Notes,
        CancellationReason = s.CancellationReason,
        IsLateCancellation = s.IsLateCancellation,
        MeetingUrl = s.MeetingUrl,
        PairGroupId = s.PairGroupId,
        SharesPackageSlot = s.SharesPackageSlot,
        OffPackagePayment = s.OffPackagePayment,
        AwaitingApproval = s.AwaitingApproval,
        HoldUntil = s.HoldUntil,
        PaidAt = s.PaidAt,
        PaidVia = s.PaidVia
    };
}

file static class StringExtensions
{
    public static string? NullIfEmpty(this string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;
}
