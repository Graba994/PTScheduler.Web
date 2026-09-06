using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class PackageOfferService(IDbContextFactory<ApplicationDbContext> dbFactory) : IPackageOfferService
{
    public async Task<List<PackageOfferDto>> GetAllAsync(bool includeInactive = false)
    {
        await using var db = dbFactory.CreateDbContext();
        var q = db.PackageOffers.Include(o => o.SessionType).AsNoTracking();
        if (!includeInactive) q = q.Where(o => o.IsActive);
        return await q.OrderBy(o => o.SortOrder).ThenBy(o => o.Name)
            .Select(o => Map(o)).ToListAsync();
    }

    public async Task<List<PackageOfferDto>> GetActiveOffersAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.PackageOffers.Include(o => o.SessionType).AsNoTracking()
            .Where(o => o.IsActive)
            .OrderBy(o => o.SortOrder).ThenBy(o => o.Name)
            .Select(o => Map(o)).ToListAsync();
    }

    public async Task<PackageOfferDto?> GetAsync(int id)
    {
        await using var db = dbFactory.CreateDbContext();
        var o = await db.PackageOffers.Include(x => x.SessionType).AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);
        return o is null ? null : Map(o);
    }

    public async Task<int> CreateAsync(SavePackageOfferDto dto, string userId)
    {
        await using var db = dbFactory.CreateDbContext();
        var entity = new PackageOffer
        {
            Name = dto.Name,
            Description = dto.Description,
            ImageUrl = dto.ImageUrl,
            SessionTypeId = dto.SessionTypeId,
            SessionsCount = dto.SessionsCount,
            Price = dto.Price,
            Currency = dto.Currency,
            ValidDays = dto.ValidDays,
            IsActive = dto.IsActive,
            IsFeatured = dto.IsFeatured,
            SortOrder = dto.SortOrder,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        db.PackageOffers.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    public async Task UpdateAsync(int id, SavePackageOfferDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var entity = await db.PackageOffers.FindAsync(id)
            ?? throw new InvalidOperationException("Pakiet nie istnieje.");
        entity.Name = dto.Name;
        entity.Description = dto.Description;
        entity.ImageUrl = dto.ImageUrl;
        entity.SessionTypeId = dto.SessionTypeId;
        entity.SessionsCount = dto.SessionsCount;
        entity.Price = dto.Price;
        entity.Currency = dto.Currency;
        entity.ValidDays = dto.ValidDays;
        entity.IsActive = dto.IsActive;
        entity.IsFeatured = dto.IsFeatured;
        entity.SortOrder = dto.SortOrder;
        await db.SaveChangesAsync();
    }

    public async Task SetActiveAsync(int id, bool active)
    {
        await using var db = dbFactory.CreateDbContext();
        var entity = await db.PackageOffers.FindAsync(id);
        if (entity is null) return;
        entity.IsActive = active;
        await db.SaveChangesAsync();
    }

    private static PackageOfferDto Map(PackageOffer o) => new()
    {
        Id = o.Id,
        Name = o.Name,
        Description = o.Description,
        ImageUrl = o.ImageUrl,
        SessionTypeId = o.SessionTypeId,
        SessionTypeName = o.SessionType?.Name ?? "",
        DurationMinutes = o.SessionType?.DurationMinutes ?? 0,
        SessionsCount = o.SessionsCount,
        Price = o.Price,
        Currency = o.Currency,
        ValidDays = o.ValidDays,
        IsActive = o.IsActive,
        IsFeatured = o.IsFeatured,
        SortOrder = o.SortOrder,
        CreatedAt = o.CreatedAt
    };
}
