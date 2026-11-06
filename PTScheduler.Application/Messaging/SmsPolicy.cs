using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;

namespace PTScheduler.Application.Messaging;

/// <summary>
/// Push zamiast SMS: klient z aplikacją i włączonymi powiadomieniami dostaje powiadomienie, a SMS idzie tylko wtedy,
/// gdy push nie dotarł albo klient sam poprosił o SMS-y także przy aplikacji (Moje konto → Powiadomienia).
/// </summary>
public static class SmsPolicy
{
    public static bool PushReplacesSms(bool pushConfigured, int subscriptions, bool pushOptIn, bool smsWithApp) =>
        pushConfigured && subscriptions > 0 && pushOptIn && !smsWithApp;

    /// <param name="pushType">Rodzaj powiadomienia push, który zastępuje SMS (np. <see cref="NotificationTypes.PushReminders"/>).</param>
    public static async Task<bool> PushReplacesSmsAsync(IWebPushService push, INotificationPreferencesService prefs, bool pushConfigured,
        string userId, string pushType)
    {
        if (!pushConfigured || string.IsNullOrEmpty(userId)) return false;
        if (await prefs.IsEnabledAsync(userId, NotificationTypes.SmsWithApp)) return false;
        if (!await prefs.IsEnabledAsync(userId, pushType)) return false;
        return await push.GetSubscriptionCountAsync(userId) > 0;
    }
}
