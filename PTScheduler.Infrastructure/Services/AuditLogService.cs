using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class AuditLogService(IDbContextFactory<ApplicationDbContext> dbFactory) : IAuditLogService
{
    public async Task LogAsync(string userId, string userEmail, string userRole, string action, string entityType, string? entityId = null, string? details = null, AuditSeverity severity = AuditSeverity.Info)
    {
        await using var db = dbFactory.CreateDbContext();
        db.AuditLogs.Add(new AuditLog
        {
            Timestamp = DateTime.UtcNow,
            UserId = userId,
            UserEmail = userEmail,
            UserRole = userRole,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            Severity = severity
        });
        await db.SaveChangesAsync();
    }

    public async Task<List<AuditLogDto>> GetLogsAsync(int count = 500, string? search = null, string? entityTypeFilter = null, AuditSeverity? severityFilter = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var query = db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(entityTypeFilter))
            query = query.Where(l => l.EntityType == entityTypeFilter);

        if (severityFilter.HasValue)
            query = query.Where(l => l.Severity == severityFilter.Value);

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
                Details = l.Details,
                Severity = l.Severity
            })
            .ToListAsync();
    }

    public async Task<List<string>> GetDistinctEntityTypesAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.AuditLogs.AsNoTracking()
            .Select(l => l.EntityType)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync();
    }
}
