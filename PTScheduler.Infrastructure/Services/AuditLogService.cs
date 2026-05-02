using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class AuditLogService(ApplicationDbContext db) : IAuditLogService
{
    public async Task LogAsync(string userId, string userEmail, string userRole, string action, string entityType, string? entityId = null, string? details = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Timestamp = DateTime.UtcNow,
            UserId = userId,
            UserEmail = userEmail,
            UserRole = userRole,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details
        });
        await db.SaveChangesAsync();
    }

    public async Task<List<AuditLogDto>> GetLogsAsync(int count = 500, string? search = null, string? entityTypeFilter = null)
    {
        var query = db.AuditLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(entityTypeFilter))
            query = query.Where(l => l.EntityType == entityTypeFilter);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(l =>
                l.UserEmail.Contains(search) ||
                l.Action.Contains(search) ||
                (l.Details != null && l.Details.Contains(search)));

        return await query
            .OrderByDescending(l => l.Timestamp)
            .Take(count)
            .Select(l => new AuditLogDto
            {
                Id = l.Id,
                Timestamp = l.Timestamp,
                UserId = l.UserId,
                UserEmail = l.UserEmail,
                UserRole = l.UserRole,
                Action = l.Action,
                EntityType = l.EntityType,
                EntityId = l.EntityId,
                Details = l.Details
            })
            .ToListAsync();
    }
}
