using Microsoft.EntityFrameworkCore;
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
    INotificationPreferencesService notificationPrefs) : ISessionPackageService
{
    public async Task<List<SessionPackageDto>> GetPackagesAsync(int clientId)
    {
        await ExpireOldPackagesAsync();
        await using var db = dbFactory.CreateDbContext();
        var list = await db.SessionPackages
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

        try { await SendPackageAssignedEmailAsync(dto.ClientId, package); } catch { }

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
    {
        await using var db = dbFactory.CreateDbContext();
        var p = await db.SessionPackages.FindAsync(packageId);
        if (p is null || p.Status != PackageStatus.Active) return;

        p.UsedSessions++;
        if (p.UsedSessions >= p.TotalSessions)
            p.Status = PackageStatus.Depleted;

        await db.SaveChangesAsync();
    }

    public async Task ReturnCreditAsync(int packageId)
    {
        await using var db = dbFactory.CreateDbContext();
        var p = await db.SessionPackages.FindAsync(packageId);
        if (p is null || p.Status == PackageStatus.Cancelled) return;

        if (p.UsedSessions > 0)
            p.UsedSessions--;

        if (p.Status == PackageStatus.Depleted && p.UsedSessions < p.TotalSessions)
            p.Status = PackageStatus.Active;

        await db.SaveChangesAsync();
    }

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
            await db.SaveChangesAsync();

        return toExpire.Count;
    }

    public async Task<List<ExpiringPackageDto>> GetExpiringAsync(int daysAhead = 14, string? trainerUserId = null)
    {
        await ExpireOldPackagesAsync();
        await using var db = dbFactory.CreateDbContext();
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(daysAhead);

        var packages = await db.SessionPackages
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
        var html = BuildPackageAssignedHtml(clientName, package.Name, package.TotalSessions, sessionType?.Name ?? "", package.ExpiresAt);
        await emailService.SendAsync(clientUser.Email, clientName, $"Nowy pakiet — {package.Name}", html);
    }

    private static string BuildPackageAssignedHtml(string clientName, string packageName, int totalSessions, string sessionType, DateTime? expiresAt) => $"""
        <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;padding:32px 24px">
          <div style="background:#16A34A;border-radius:8px 8px 0 0;padding:24px;text-align:center">
            <h2 style="color:white;margin:0;font-size:20px">Nowy pakiet sesji</h2>
          </div>
          <div style="border:1px solid #e5e7eb;border-top:none;border-radius:0 0 8px 8px;padding:24px">
            <p style="color:#374151;font-size:15px">Cześć <strong>{clientName}</strong>!</p>
            <p style="color:#374151;font-size:15px">Otrzymałeś nowy pakiet treningowy:</p>
            <table style="width:100%;border-collapse:collapse;margin:16px 0">
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px;width:40%">Nazwa pakietu</td><td style="padding:8px 0;font-size:14px;font-weight:600">{packageName}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Typ sesji</td><td style="padding:8px 0;font-size:14px;font-weight:600">{sessionType}</td></tr>
              <tr><td style="padding:8px 0;color:#6b7280;font-size:14px">Liczba sesji</td><td style="padding:8px 0;font-size:14px;font-weight:600">{totalSessions}</td></tr>
              {(expiresAt.HasValue ? $"<tr><td style=\"padding:8px 0;color:#6b7280;font-size:14px\">Ważny do</td><td style=\"padding:8px 0;font-size:14px;font-weight:600\">{expiresAt.Value:dd.MM.yyyy}</td></tr>" : "")}
            </table>
          </div>
        </div>
        """;

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
        Status = p.Status
    };
}
