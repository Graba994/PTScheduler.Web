using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class SessionPackageService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IEmailService emailService,
    IEmailTemplateService emailTemplateService,
    INotificationPreferencesService notificationPrefs,
    IAuditLogService auditLog,
    ILogger<SessionPackageService> logger) : ISessionPackageService
{
    public async Task<List<SessionPackageDto>> GetPackagesAsync(int clientId)
    {
        await ExpireOldPackagesAsync();
        await using var db = dbFactory.CreateDbContext();
        var list = await db.SessionPackages
            .AsNoTracking()
            .Include(p => p.SessionType)
            .Include(p => p.Client)
            .Where(p => p.ClientId == clientId)
            .OrderByDescending(p => p.PurchasedAt)
            .ToListAsync();
        return list.Select(MapToDto).ToList();
    }

    public async Task<List<SessionPackageDto>> GetAllPackagesAsync(string? trainerUserId = null)
    {
        await ExpireOldPackagesAsync();
        await using var db = dbFactory.CreateDbContext();
        var list = await db.SessionPackages
            .AsNoTracking()
            .Include(p => p.SessionType)
            .Include(p => p.Client)
            .Where(p => trainerUserId == null || p.Client.TrainerUserId == trainerUserId)
            .OrderByDescending(p => p.PurchasedAt)
            .ToListAsync();
        return list.Select(MapToDto).ToList();
    }

    public async Task<SessionPackageDto?> GetPackageAsync(int id)
    {
        await using var db = dbFactory.CreateDbContext();
        var p = await db.SessionPackages
            .AsNoTracking()
            .Include(p => p.SessionType)
            .Include(p => p.Client)
            .FirstOrDefaultAsync(p => p.Id == id);
        return p is null ? null : MapToDto(p);
    }

    public async Task<SessionPackageDto> CreatePackageAsync(CreateSessionPackageDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var sessionType = await db.SessionTypes.FindAsync(dto.SessionTypeId)
            ?? throw new InvalidOperationException("Typ sesji nie istnieje.");

        var package = new SessionPackage
        {
            ClientId = dto.ClientId,
            CreatedByUserId = dto.CreatedByUserId,
            Name = string.IsNullOrWhiteSpace(dto.Name)
                ? $"Pakiet {dto.TotalSessions}×{sessionType.Name}"
                : dto.Name,
            SessionTypeId = dto.SessionTypeId,
            TotalSessions = dto.TotalSessions,
            PricePerSession = dto.PricePerSession,
            ExpiresAt = dto.ExpiresAt,
            Notes = dto.Notes,
            IsHidden = dto.IsHidden,
            PurchasedAt = DateTime.UtcNow,
            Status = PackageStatus.Active
        };

        db.SessionPackages.Add(package);
        await db.SaveChangesAsync();

        var awaitingSessions = await db.Sessions
            .Where(s => s.ClientId == dto.ClientId
                     && s.SessionTypeId == dto.SessionTypeId
                     && s.Status == SessionStatus.AwaitingPackage)
            .OrderBy(s => s.StartTime)
            .ToListAsync();

        foreach (var session in awaitingSessions)
        {
            if (package.UsedSessions >= package.TotalSessions) break;
            session.PackageId = package.Id;
            session.Status = SessionStatus.Scheduled;
            package.UsedSessions++;
        }

        if (package.UsedSessions >= package.TotalSessions)
            package.Status = PackageStatus.Depleted;

        if (awaitingSessions.Count > 0)
            await db.SaveChangesAsync();

        try { await SendPackageAssignedEmailAsync(dto.ClientId, package); }
        catch (Exception ex) { logger.LogWarning(ex, "Błąd wysyłki emaila o przypisaniu pakietu (PackageId={Id})", package.Id); }

        return (await GetPackageAsync(package.Id))!;
    }

    public async Task MarkPaidAsync(int packageId)
    {
        await using var db = dbFactory.CreateDbContext();
        var p = await db.SessionPackages.FindAsync(packageId)
            ?? throw new InvalidOperationException("Pakiet nie istnieje.");
        p.IsPaid = true;
        p.PaidAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task CancelPackageAsync(int packageId)
    {
        await using var db = dbFactory.CreateDbContext();
        var p = await db.SessionPackages.FindAsync(packageId)
            ?? throw new InvalidOperationException("Pakiet nie istnieje.");
        p.Status = PackageStatus.Cancelled;
        await db.SaveChangesAsync();
    }

    public async Task DeductCreditAsync(int packageId)
        // Retry na wypadek równoległego zapisu na tym samym pakiecie — token xmin
        // wykryje konflikt, a my ponawiamy na świeżym stanie (bez podwójnego zliczenia,
        // bo każda próba czyta aktualne UsedSessions).
        => await ConcurrencyRetry.ExecuteAsync(async () =>
        {
            await using var db = dbFactory.CreateDbContext();
            var p = await db.SessionPackages.FindAsync(packageId);
            if (p is null || p.Status != PackageStatus.Active) return;

            p.UsedSessions++;
            if (p.UsedSessions >= p.TotalSessions)
                p.Status = PackageStatus.Depleted;

            await db.SaveChangesAsync();
        });

    public async Task ReturnCreditAsync(int packageId)
        => await ConcurrencyRetry.ExecuteAsync(async () =>
        {
            await using var db = dbFactory.CreateDbContext();
            var p = await db.SessionPackages.FindAsync(packageId);
            if (p is null || p.Status == PackageStatus.Cancelled) return;

            if (p.UsedSessions > 0)
                p.UsedSessions--;

            if (p.Status == PackageStatus.Depleted && p.UsedSessions < p.TotalSessions)
                p.Status = PackageStatus.Active;

            await db.SaveChangesAsync();
        });

    public async Task UpdatePackageAsync(int id, UpdateSessionPackageDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var p = await db.SessionPackages.FindAsync(id)
            ?? throw new InvalidOperationException("Pakiet nie istnieje.");

        if (dto.TotalSessions < p.UsedSessions)
            throw new InvalidOperationException($"Nie można zmniejszyć liczby sesji poniżej {p.UsedSessions} (już wykorzystanych).");

        p.Name = dto.Name.Trim();
        p.TotalSessions = dto.TotalSessions;
        p.PricePerSession = dto.PricePerSession;
        p.ExpiresAt = dto.ExpiresAt;
        p.Notes = dto.Notes;
        p.IsHidden = dto.IsHidden;

        if (p.Status != PackageStatus.Cancelled)
        {
            if (p.UsedSessions >= p.TotalSessions)
                p.Status = PackageStatus.Depleted;
            else if (dto.ExpiresAt.HasValue && dto.ExpiresAt.Value < DateTime.UtcNow)
                p.Status = PackageStatus.Expired;
            else
                p.Status = PackageStatus.Active;
        }

        await db.SaveChangesAsync();
    }

    public async Task<int> ExpireOldPackagesAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var now = DateTime.UtcNow;
        var toExpire = await db.SessionPackages
            .Where(p => p.Status == PackageStatus.Active
                        && p.ExpiresAt.HasValue
                        && p.ExpiresAt.Value < now)
            .ToListAsync();

        foreach (var p in toExpire)
            p.Status = PackageStatus.Expired;

        if (toExpire.Count > 0)
        {
            await db.SaveChangesAsync();
            try
            {
                await auditLog.LogAsync("system", "system", "System", "PackagesExpired", "SessionPackage", null,
                    $"Automatycznie wygaszono {toExpire.Count} pakiet(ów): {string.Join(", ", toExpire.Select(p => $"#{p.Id}"))}");
            }
            catch (Exception ex) { logger.LogError(ex, "Audit log write failed for package expiry batch"); }
        }

        return toExpire.Count;
    }

    public async Task<List<ExpiringPackageDto>> GetExpiringAsync(int daysAhead = 14, string? trainerUserId = null)
    {
        await ExpireOldPackagesAsync();
        await using var db = dbFactory.CreateDbContext();
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(daysAhead);

        var packages = await db.SessionPackages
            .AsNoTracking()
            .Include(p => p.Client)
            .Where(p => p.Status == PackageStatus.Active
                     && p.ExpiresAt.HasValue
                     && p.ExpiresAt.Value > now
                     && p.ExpiresAt.Value <= cutoff
                     && (trainerUserId == null || p.Client.TrainerUserId == trainerUserId))
            .OrderBy(p => p.ExpiresAt)
            .ToListAsync();

        return packages.Select(p => new ExpiringPackageDto
        {
            PackageId = p.Id,
            ClientId = p.ClientId,
            ClientName = $"{p.Client.FirstName} {p.Client.LastName}".Trim() is { Length: > 0 } n ? n : p.Client.ApplicationUserId,
            PackageName = p.Name,
            RemainingCredits = Math.Max(0, p.TotalSessions - p.UsedSessions),
            ExpiresAt = p.ExpiresAt!.Value,
            DaysLeft = (int)Math.Ceiling((p.ExpiresAt.Value - now).TotalDays)
        }).ToList();
    }

    private async Task SendPackageAssignedEmailAsync(int clientId, SessionPackage package)
    {
        if (!await emailService.IsEnabledAsync()) return;
        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.FindAsync(clientId);
        if (client is null) return;
        if (!await notificationPrefs.IsEnabledAsync(client.ApplicationUserId, NotificationTypes.PackageAssigned)) return;
        var clientUser = await db.Users.FirstOrDefaultAsync(u => u.Id == client.ApplicationUserId);
        if (clientUser?.Email is null) return;
        var clientName = $"{client.FirstName} {client.LastName}".Trim() is { Length: > 0 } n ? n : clientUser.Email;
        var sessionType = await db.SessionTypes.FindAsync(package.SessionTypeId);
        var expiresRow = package.ExpiresAt.HasValue
            ? $"<tr><td style=\"padding:8px 0;color:#6b7280;font-size:14px\">Ważny do</td><td style=\"padding:8px 0;font-size:14px;font-weight:600\">{package.ExpiresAt.Value:dd.MM.yyyy}</td></tr>"
            : "";
        var vars = new Dictionary<string, string>
        {
            ["ClientName"] = clientName,
            ["PackageName"] = package.Name,
            ["SessionType"] = sessionType?.Name ?? "",
            ["TotalSessions"] = package.TotalSessions.ToString(),
            ["ExpiresAt"] = package.ExpiresAt?.ToString("dd.MM.yyyy") ?? "",
            ["ExpiresRow"] = expiresRow
        };
        var (subject, html) = await emailTemplateService.RenderAsync("package-assigned", vars);
        await emailService.SendAsync(clientUser.Email, clientName, subject, html);
    }

    private static SessionPackageDto MapToDto(SessionPackage p) => new()
    {
        Id = p.Id,
        ClientId = p.ClientId,
        ClientName = p.Client is not null
            ? $"{p.Client.FirstName} {p.Client.LastName}".Trim() is { Length: > 0 } n ? n : p.Client.ApplicationUserId
            : string.Empty,
        CreatedByUserId = p.CreatedByUserId,
        Name = p.Name,
        Notes = p.Notes,
        SessionTypeId = p.SessionTypeId,
        SessionTypeName = p.SessionType.Name,
        DurationMinutes = p.SessionType.DurationMinutes,
        TotalSessions = p.TotalSessions,
        UsedSessions = p.UsedSessions,
        PricePerSession = p.PricePerSession,
        IsPaid = p.IsPaid,
        PaidAt = p.PaidAt,
        PaymentReference = p.PaymentReference,
        PurchasedAt = p.PurchasedAt,
        ExpiresAt = p.ExpiresAt,
        Status = p.Status,
        IsHidden = p.IsHidden
    };
}
