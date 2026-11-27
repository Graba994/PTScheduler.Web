using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Rules;

namespace PTScheduler.Web.Services;

// Publiczne demo: nakładki na usługi, które łączą się ze światem. Gdy demo jest włączone (PublicDemoService),
// nic nie wychodzi — e-mail, SMS, push, płatność ani faktura do KSeF; w każdej innej instancji przekazują
// wywołanie dalej bez zmian. Rejestracja: DemoGuards.AddDemoGuards (Program.cs).

public static class DemoGuards
{
    /// <summary>Podmienia rejestracje usług na nakładki demo (oryginalna klasa tworzona jak dotąd).</summary>
    public static IServiceCollection AddDemoGuards(this IServiceCollection services)
    {
        Wrap<IEmailService, DemoEmailGuard>(services);
        Wrap<ISmsService, DemoSmsGuard>(services);
        Wrap<IWebPushService, DemoPushGuard>(services);
        Wrap<IPaymentService, DemoPaymentGuard>(services);
        Wrap<IKsefService, DemoKsefGuard>(services);
        return services;
    }

    private static void Wrap<TService, TGuard>(IServiceCollection services) where TService : class where TGuard : class, TService
    {
        var original = services.LastOrDefault(d => d.ServiceType == typeof(TService))
            ?? throw new InvalidOperationException($"Brak rejestracji {typeof(TService).Name}.");
        var impl = original.ImplementationType ?? throw new InvalidOperationException($"{typeof(TService).Name}: nakładka demo wymaga rejestracji przez typ.");
        services.Remove(original);
        services.Add(new ServiceDescriptor(typeof(TService),
            sp => ActivatorUtilities.CreateInstance<TGuard>(sp, (TService)ActivatorUtilities.CreateInstance(sp, impl)), original.Lifetime));
    }
}

public sealed class DemoEmailGuard(IEmailService inner, PublicDemoService demo, ILogger<DemoEmailGuard> logger) : IEmailService
{
    private const string Off = "W wersji demo e-maile nie są wysyłane.";

    public Task SendAsync(string toAddress, string toName, string subject, string htmlBody) => SendAsync(toAddress, toName, subject, htmlBody, null);

    public Task SendAsync(string toAddress, string toName, string subject, string htmlBody, IReadOnlyList<EmailInlineImage>? images)
    {
        if (!demo.IsOn) return inner.SendAsync(toAddress, toName, subject, htmlBody, images);
        logger.LogInformation("Demo: pominięto e-mail „{Subject}”.", subject);
        return Task.CompletedTask;
    }

    public Task<(bool Success, string? Error)> TestAsync(string testAddress) =>
        demo.IsOn ? Task.FromResult<(bool, string?)>((false, Off)) : inner.TestAsync(testAddress);

    public Task<(bool Success, string? Error)> TestOwnAsync(EmailSettingsDto settings, string testAddress) =>
        demo.IsOn ? Task.FromResult<(bool, string?)>((false, Off)) : inner.TestOwnAsync(settings, testAddress);

    public Task<bool> IsEnabledAsync() => inner.IsEnabledAsync();
    public Task<string> GetDeliveryModeAsync() => inner.GetDeliveryModeAsync();
    public Task<(int Limit, int SentToday)?> GetPlatformQuotaAsync() => inner.GetPlatformQuotaAsync();
}

public sealed class DemoSmsGuard(ISmsService inner, PublicDemoService demo) : ISmsService
{
    private const string Off = "W wersji demo SMS-y nie są wysyłane.";

    public Task<bool> IsEnabledAsync() => inner.IsEnabledAsync();
    public Task<(bool Success, string? Error)> TestAsync(string phone) =>
        demo.IsOn ? Task.FromResult<(bool, string?)>((false, Off)) : inner.TestAsync(phone);
    public Task<SmsResult> SendReminderAsync(string phone, string message, int maxPerMonth) =>
        demo.IsOn ? Task.FromResult(new SmsResult(false, false, Off)) : inner.SendReminderAsync(phone, message, maxPerMonth);
    public Task<(int Sent, int Max)> GetQuotaStatusAsync(int maxPerMonth) => inner.GetQuotaStatusAsync(maxPerMonth);
    public Task<CentralizedSmsStatus?> GetCentralizedStatusAsync() => inner.GetCentralizedStatusAsync();
}

/// <summary>Wspólne konto demo widzi wielu gości naraz — powiadomienia trafiłyby na cudze telefony.</summary>
public sealed class DemoPushGuard(IWebPushService inner, PublicDemoService demo) : IWebPushService
{
    public Task<WebPushSettingsDto> GetSettingsAsync() => inner.GetSettingsAsync();
    public Task SaveSettingsAsync(WebPushSettingsDto dto) => demo.IsOn ? Task.CompletedTask : inner.SaveSettingsAsync(dto);
    public Task<string> GetPublicKeyAsync() => inner.GetPublicKeyAsync();
    public Task SubscribeAsync(string userId, PushSubscriptionDto dto) => demo.IsOn ? Task.CompletedTask : inner.SubscribeAsync(userId, dto);
    public Task UnsubscribeAsync(string userId, string endpoint) => inner.UnsubscribeAsync(userId, endpoint);
    public Task<int> GetSubscriptionCountAsync(string userId) => inner.GetSubscriptionCountAsync(userId);
    public Task SendAsync(string userId, PushMessageDto message) => demo.IsOn ? Task.CompletedTask : inner.SendAsync(userId, message);
    public Task<PushSendReport> SendWithReportAsync(string userId, PushMessageDto message) =>
        demo.IsOn ? Task.FromResult(new PushSendReport(0, 0, "W wersji demo powiadomienia nie są wysyłane.")) : inner.SendWithReportAsync(userId, message);
    public Task SendToAllAsync(PushMessageDto message) => demo.IsOn ? Task.CompletedTask : inner.SendToAllAsync(message);
    public (string PublicKey, string PrivateKey) GenerateVapidKeys() => inner.GenerateVapidKeys();
}

public sealed class DemoPaymentGuard(IPaymentService inner, PublicDemoService demo) : IPaymentService
{
    private Task<PaymentInitResult>? Blocked() =>
        demo.IsOn ? Task.FromResult(new PaymentInitResult(false, null, PublicDemoRules.PaymentMessage)) : null;

    public Task<PaymentInitResult> StartCourseCheckoutAsync(string userId, int courseId, string providerKey, string appBaseUrl, string buyerEmail, string customerIp, string? couponCode = null, InvoiceBuyerDto? invoiceBuyer = null) =>
        Blocked() ?? inner.StartCourseCheckoutAsync(userId, courseId, providerKey, appBaseUrl, buyerEmail, customerIp, couponCode, invoiceBuyer);
    public Task<PaymentInitResult> StartMembershipPeriodCheckoutAsync(string userId, int periodId, string providerKey, string appBaseUrl, string buyerEmail, string customerIp) =>
        Blocked() ?? inner.StartMembershipPeriodCheckoutAsync(userId, periodId, providerKey, appBaseUrl, buyerEmail, customerIp);
    public Task<PaymentInitResult> StartMembershipPlanCheckoutAsync(string userId, int planId, string providerKey, string appBaseUrl, string buyerEmail, string customerIp, string? couponCode = null, InvoiceBuyerDto? invoiceBuyer = null) =>
        Blocked() ?? inner.StartMembershipPlanCheckoutAsync(userId, planId, providerKey, appBaseUrl, buyerEmail, customerIp, couponCode, invoiceBuyer);
    public Task<PaymentInitResult> StartPackageCheckoutAsync(string userId, int packageOfferId, string providerKey, string appBaseUrl, string buyerEmail, string customerIp, string? couponCode = null, InvoiceBuyerDto? invoiceBuyer = null, int? partnerClientId = null) =>
        Blocked() ?? inner.StartPackageCheckoutAsync(userId, packageOfferId, providerKey, appBaseUrl, buyerEmail, customerIp, couponCode, invoiceBuyer, partnerClientId);
    public Task<PaymentInitResult> StartSessionCheckoutAsync(string userId, int sessionId, string providerKey, string appBaseUrl, string buyerEmail, string customerIp) =>
        Blocked() ?? inner.StartSessionCheckoutAsync(userId, sessionId, providerKey, appBaseUrl, buyerEmail, customerIp);
    public Task<PaymentInitResult> StartGiftVoucherCheckoutAsync(string userId, int voucherId, string providerKey, string appBaseUrl, string buyerEmail, string customerIp) =>
        Blocked() ?? inner.StartGiftVoucherCheckoutAsync(userId, voucherId, providerKey, appBaseUrl, buyerEmail, customerIp);

    public Task<PaymentNotifyResponse> HandleNotifyAsync(string providerKey, string rawBody, IReadOnlyDictionary<string, string> headers) =>
        inner.HandleNotifyAsync(providerKey, rawBody, headers);
    public Task<bool> CompleteSimulatorAsync(string extOrderId, bool paid) => inner.CompleteSimulatorAsync(extOrderId, paid);
    public Task<OrderDto?> GetOrderByExtAsync(string extOrderId) => inner.GetOrderByExtAsync(extOrderId);
    public Task<List<PaymentOptionDto>> GetEnabledOptionsAsync() => inner.GetEnabledOptionsAsync();
    public Task<List<OrderDto>> GetMyOrdersAsync(string userId) => inner.GetMyOrdersAsync(userId);
    public Task<List<OrderDto>> GetPaidOrdersAsync(DateTime from) => inner.GetPaidOrdersAsync(from);
}

public sealed class DemoKsefGuard(IKsefService inner, PublicDemoService demo) : IKsefService
{
    private const string Off = "W wersji demo faktury nie trafiają do KSeF.";

    public Task SaveTokenAsync(string? token) => demo.IsOn ? Task.CompletedTask : inner.SaveTokenAsync(token);
    public Task<(bool Ok, string Message)> TestConnectionAsync() =>
        demo.IsOn ? Task.FromResult((false, Off)) : inner.TestConnectionAsync();
    public Task<(bool Ok, string? Error)> SetBuyerAsync(int orderId, InvoiceBuyerDto buyer) => inner.SetBuyerAsync(orderId, buyer);
    public Task<(bool Ok, string Message)> SendOrderInvoiceAsync(int orderId) =>
        demo.IsOn ? Task.FromResult((false, Off)) : inner.SendOrderInvoiceAsync(orderId);
    public Task<int> RefreshPendingAsync(CancellationToken ct = default) => demo.IsOn ? Task.FromResult(0) : inner.RefreshPendingAsync(ct);
}
