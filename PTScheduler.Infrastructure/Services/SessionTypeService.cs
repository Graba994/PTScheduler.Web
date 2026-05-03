using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class SessionTypeService(IDbContextFactory<ApplicationDbContext> dbFactory) : ISessionTypeService
{
    public async Task<List<SessionTypeDto>> GetAllAsync(bool includeInactive = false)
    {
        await using var db = dbFactory.CreateDbContext();
        var query = db.SessionTypes.AsQueryable();
        if (!includeInactive) query = query.Where(t => t.IsActive);
        var types = await query.OrderBy(t => t.Name).ToListAsync();

        var ids = types.Select(t => t.Id).ToList();
        var counts = await db.Sessions
            .Where(s => ids.Contains(s.SessionTypeId))
            .GroupBy(s => s.SessionTypeId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        return types.Select(t => MapToDto(t, counts.GetValueOrDefault(t.Id, 0))).ToList();
    }

    public async Task<SessionTypeDto> CreateAsync(CreateSessionTypeDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var entity = new SessionType
        {
            Name = dto.Name.Trim(),
            DurationMinutes = dto.DurationMinutes,
            IsGroup = dto.IsGroup,
            MaxParticipants = dto.IsGroup ? dto.MaxParticipants : null,
            IsActive = true
        };
        db.SessionTypes.Add(entity);
        await db.SaveChangesAsync();
        return MapToDto(entity, 0);
    }

    public async Task UpdateAsync(int id, CreateSessionTypeDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var entity = await db.SessionTypes.FindAsync(id)
            ?? throw new InvalidOperationException("Typ sesji nie istnieje.");
        entity.Name = dto.Name.Trim();
        entity.DurationMinutes = dto.DurationMinutes;
        entity.IsGroup = dto.IsGroup;
        entity.MaxParticipants = dto.IsGroup ? dto.MaxParticipants : null;
        await db.SaveChangesAsync();
    }

    public async Task SetActiveAsync(int id, bool isActive)
    {
        await using var db = dbFactory.CreateDbContext();
        var entity = await db.SessionTypes.FindAsync(id)
            ?? throw new InvalidOperationException("Typ sesji nie istnieje.");
        entity.IsActive = isActive;
        await db.SaveChangesAsync();
    }

    private static SessionTypeDto MapToDto(SessionType t, int sessionCount) => new()
    {
        Id = t.Id,
        Name = t.Name,
        DurationMinutes = t.DurationMinutes,
        IsGroup = t.IsGroup,
        MaxParticipants = t.MaxParticipants,
        IsActive = t.IsActive,
        SessionCount = sessionCount
    };
}
