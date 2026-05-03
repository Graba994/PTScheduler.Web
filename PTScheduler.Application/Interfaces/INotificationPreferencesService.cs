using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface INotificationPreferencesService
{
    Task<NotificationPreferencesDto> GetAsync(string userId);
    Task SaveAsync(string userId, NotificationPreferencesDto dto);
    Task<bool> IsEnabledAsync(string userId, string notificationType);
}
