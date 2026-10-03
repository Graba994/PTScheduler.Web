using PTScheduler.Application.DTOs;
using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.Interfaces;

public interface IAuditLogService
{
    Task LogAsync(string userId, string userEmail, string userRole, string action, string entityType, string? entityId = null, string? details = null, AuditSeverity severity = AuditSeverity.Info);
    Task<List<AuditLogDto>> GetLogsAsync(int count = 500, string? search = null, string? entityTypeFilter = null, AuditSeverity? severityFilter = null);
    Task<List<string>> GetDistinctEntityTypesAsync();
}
