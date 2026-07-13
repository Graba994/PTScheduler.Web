using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IWebPushService
{
    Task<WebPushSettingsDto> GetSettingsAsync();
    Task SaveSettingsAsync(WebPushSettingsDto dto);
    Task<string> GetPublicKeyAsync();
    Task SubscribeAsync(string userId, PushSubscriptionDto dto);
    Task UnsubscribeAsync(string userId, string endpoint);
    Task<int> GetSubscriptionCountAsync(string userId);
    Task SendAsync(string userId, PushMessageDto message);
    Task SendToAllAsync(PushMessageDto message);
    (string PublicKey, string PrivateKey) GenerateVapidKeys();
}
