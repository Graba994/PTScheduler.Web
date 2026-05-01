using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class SessionPackageService(ApplicationDbContext db) : ISessionPackageService
{
    public async Task<List<SessionPackageDto>> GetPackagesAsync(int clientId)
    {
        await ExpireOldPackagesAsync();
        var list = await db.SessionPackages
            .Include(p => p.SessionType)
            .Where(p => p.ClientId == clientId)
            .OrderByDescending(p => p.PurchasedAt)
            .ToListAsync();
        return list.Select(MapToDto).ToList();
    }

    public async Task<SessionPackageDto?> GetPackageAsync(int id)
    {
        var p = await db.SessionPackages.Include(p => p.SessionType).FirstOrDefaultAsync(p => p.Id == id);
        return p is null ? null : MapToDto(p);
    }

    public async Task<SessionPackageDto> CreatePackageAsync(CreateSessionPackageDto dto)
    {
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

        return (await GetPackageAsync(package.Id))!;
    }

    public async Task MarkPaidAsync(int packageId)
    {
        var p = await db.SessionPackages.FindAsync(packageId)
            ?? throw new InvalidOperationException("Pakiet nie istnieje.");
        p.IsPaid = true;
        p.PaidAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task CancelPackageAsync(int packageId)
    {
        var p = await db.SessionPackages.FindAsync(packageId)
            ?? throw new InvalidOperationException("Pakiet nie istnieje.");
        p.Status = PackageStatus.Cancelled;
        await db.SaveChangesAsync();
    }

    public async Task DeductCreditAsync(int packageId)
    {
        var p = await db.SessionPackages.FindAsync(packageId);
        if (p is null || p.Status != PackageStatus.Active) return;

        p.UsedSessions++;
        if (p.UsedSessions >= p.TotalSessions)
            p.Status = PackageStatus.Depleted;

        await db.SaveChangesAsync();
    }

    public async Task ReturnCreditAsync(int packageId)
    {
        var p = await db.SessionPackages.FindAsync(packageId);
        if (p is null || p.Status == PackageStatus.Cancelled) return;

        if (p.UsedSessions > 0)
            p.UsedSessions--;

        if (p.Status == PackageStatus.Depleted && p.UsedSessions < p.TotalSessions)
            p.Status = PackageStatus.Active;

        await db.SaveChangesAsync();
    }

    public async Task<int> ExpireOldPackagesAsync()
    {
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

    public async Task<List<ExpiringPackageDto>> GetExpiringAsync(int daysAhead = 14)
    {
        await ExpireOldPackagesAsync();
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(daysAhead);

        var packages = await db.SessionPackages
            .Include(p => p.Client)
            .Where(p => p.Status == PackageStatus.Active
                     && p.ExpiresAt.HasValue
                     && p.ExpiresAt.Value > now
                     && p.ExpiresAt.Value <= cutoff)
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

    private static SessionPackageDto MapToDto(SessionPackage p) => new()
    {
        Id = p.Id,
        ClientId = p.ClientId,
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
