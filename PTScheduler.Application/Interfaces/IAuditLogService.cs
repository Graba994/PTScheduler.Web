using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IAuditLogService
{
    Task LogAsync(string userId, string userEmail, string userRole, string action, string entityType, string? entityId = null, string? details = null);
    Task<List<AuditLogDto>> GetLogsAsync(int count = 500, string? search = null, string? entityTypeFilter = null);
}
