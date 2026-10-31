using FluentAssertions;
using PTScheduler.Application.Security;
using PTScheduler.Domain.Rules;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

public class PublicDemoTests
{
    [Theory]
    [InlineData("/Account/Manage", true)]
    [InlineData("/account/manage/email?x=1", true)]
    [InlineData("/admin/sklep", true)]
    [InlineData("/admin/payments/", true)]
    [InlineData("Account/Register", true)]
    [InlineData("/admin/session-types", false)]
    [InlineData("/admin/sklepik", false)]
    [InlineData("/dzis", false)]
    [InlineData("/clients/12", false)]
    [InlineData("", false)]
    public void Blocks_Account_Money_And_Outside_World(string path, bool blocked) =>
        PublicDemoRules.IsBlockedPath(path).Should().Be(blocked);

    private const string Secret = "sekret-instancji-123";
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("demo-trainer")]
    [InlineData("demo-client")]
    public void Demo_Token_Keeps_Its_Target(string target)
    {
        var token = TenantSso.CreateToken("demo", "gosc", target, Secret, Now);
        var sso = PortalSsoToken.Validate(token, Secret, "demo", Now.AddSeconds(3));
        sso!.Target.Should().Be(target);
        PortalSsoToken.IsDemoTarget(sso.Target).Should().BeTrue();
    }

    [Fact]
    public void Unknown_Target_Falls_Back_To_Root_Not_Demo() =>
        PortalSsoToken.Validate(TenantSso.CreateToken("demo", "a", "cokolwiek", Secret, Now), Secret, "demo", Now)!.Target.Should().Be("root");
}

public class DemoGuardTests
{
    private sealed class TempRoot : PTScheduler.Application.Interfaces.IWebRootPathProvider
    {
        public string WebRootPath { get; } = Path.Combine(Path.GetTempPath(), "pt-demo-" + Guid.NewGuid().ToString("N"));
    }

    private sealed class CountingEmail : PTScheduler.Application.Interfaces.IEmailService
    {
        public static int Sent;
        public Task SendAsync(string toAddress, string toName, string subject, string htmlBody) { Sent++; return Task.CompletedTask; }
        public Task SendAsync(string toAddress, string toName, string subject, string htmlBody, IReadOnlyList<PTScheduler.Application.Interfaces.EmailInlineImage>? images) { Sent++; return Task.CompletedTask; }
        public Task<(bool Success, string? Error)> TestAsync(string testAddress) => Task.FromResult<(bool, string?)>((true, null));
        public Task<(bool Success, string? Error)> TestOwnAsync(PTScheduler.Application.DTOs.EmailSettingsDto settings, string testAddress) => Task.FromResult<(bool, string?)>((true, null));
        public Task<bool> IsEnabledAsync() => Task.FromResult(true);
        public Task<string> GetDeliveryModeAsync() => Task.FromResult("own");
        public Task<(int Limit, int SentToday)?> GetPlatformQuotaAsync() => Task.FromResult<(int, int)?>(null);
    }

    [Fact]
    public async Task Email_Goes_Out_Only_When_Demo_Is_Off()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<PTScheduler.Application.Interfaces.IEmailService, CountingEmail>(services);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<PTScheduler.Application.Interfaces.ISmsService, NoSms>(services);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<PTScheduler.Application.Interfaces.IWebPushService, NoPush>(services);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<PTScheduler.Application.Interfaces.IPaymentService, NoPay>(services);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<PTScheduler.Application.Interfaces.IKsefService, NoKsef>(services);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<PTScheduler.Application.Interfaces.IWebRootPathProvider, TempRoot>(services);
        Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions.AddLogging(services);
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton<PTScheduler.Web.Services.PublicDemoService>(services);
        PTScheduler.Web.Services.DemoGuards.AddDemoGuards(services);
        using var sp = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var demo = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<PTScheduler.Web.Services.PublicDemoService>(sp);
        var email = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<PTScheduler.Application.Interfaces.IEmailService>(sp);
        CountingEmail.Sent = 0;

        await email.SendAsync("a@b.pl", "A", "x", "y");
        CountingEmail.Sent.Should().Be(1);

        demo.Set(true, null);
        await email.SendAsync("a@b.pl", "A", "x", "y");
        CountingEmail.Sent.Should().Be(1, "w trybie demo e-mail nie wychodzi");
        var pay = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<PTScheduler.Application.Interfaces.IPaymentService>(sp);
        (await pay.StartPackageCheckoutAsync("u", 1, "p24", "https://x", "a@b.pl", "1.1.1.1")).Ok.Should().BeFalse();
        demo.IsOn.Should().BeTrue();
    }

    private sealed class NoSms : PTScheduler.Application.Interfaces.ISmsService
    {
        public Task<bool> IsEnabledAsync() => Task.FromResult(true);
        public Task<(bool Success, string? Error)> TestAsync(string phone) => Task.FromResult<(bool, string?)>((true, null));
        public Task<PTScheduler.Application.Interfaces.SmsResult> SendReminderAsync(string phone, string message, int maxPerMonth) => Task.FromResult(new PTScheduler.Application.Interfaces.SmsResult(true, false, null));
        public Task<(int Sent, int Max)> GetQuotaStatusAsync(int maxPerMonth) => Task.FromResult((0, maxPerMonth));
        public Task<PTScheduler.Application.Interfaces.CentralizedSmsStatus?> GetCentralizedStatusAsync() => Task.FromResult<PTScheduler.Application.Interfaces.CentralizedSmsStatus?>(null);
    }
    private sealed class NoPush : PTScheduler.Application.Interfaces.IWebPushService
    {
        public Task<PTScheduler.Application.DTOs.WebPushSettingsDto> GetSettingsAsync() => Task.FromResult(new PTScheduler.Application.DTOs.WebPushSettingsDto());
        public Task SaveSettingsAsync(PTScheduler.Application.DTOs.WebPushSettingsDto dto) => Task.CompletedTask;
        public Task<string> GetPublicKeyAsync() => Task.FromResult("");
        public Task SubscribeAsync(string userId, PTScheduler.Application.DTOs.PushSubscriptionDto dto) => Task.CompletedTask;
        public Task UnsubscribeAsync(string userId, string endpoint) => Task.CompletedTask;
        public Task<int> GetSubscriptionCountAsync(string userId) => Task.FromResult(0);
        public Task SendAsync(string userId, PTScheduler.Application.DTOs.PushMessageDto message) => Task.CompletedTask;
        public Task<PTScheduler.Application.DTOs.PushSendReport> SendWithReportAsync(string userId, PTScheduler.Application.DTOs.PushMessageDto message) => Task.FromResult(new PTScheduler.Application.DTOs.PushSendReport(1, 0, null));
        public Task SendToAllAsync(PTScheduler.Application.DTOs.PushMessageDto message) => Task.CompletedTask;
        public (string PublicKey, string PrivateKey) GenerateVapidKeys() => ("", "");
    }
    private sealed class NoPay : PTScheduler.Application.Interfaces.IPaymentService
    {
        private static Task<PTScheduler.Application.Interfaces.PaymentInitResult> Ok() => Task.FromResult(new PTScheduler.Application.Interfaces.PaymentInitResult(true, "https://pay", null));
        public Task<PTScheduler.Application.Interfaces.PaymentInitResult> StartCourseCheckoutAsync(string userId, int courseId, string providerKey, string appBaseUrl, string buyerEmail, string customerIp, string? couponCode = null, PTScheduler.Application.Interfaces.InvoiceBuyerDto? invoiceBuyer = null) => Ok();
        public Task<PTScheduler.Application.Interfaces.PaymentInitResult> StartMembershipPeriodCheckoutAsync(string userId, int periodId, string providerKey, string appBaseUrl, string buyerEmail, string customerIp) => Ok();
        public Task<PTScheduler.Application.Interfaces.PaymentInitResult> StartMembershipPlanCheckoutAsync(string userId, int planId, string providerKey, string appBaseUrl, string buyerEmail, string customerIp, string? couponCode = null, PTScheduler.Application.Interfaces.InvoiceBuyerDto? invoiceBuyer = null) => Ok();
        public Task<PTScheduler.Application.Interfaces.PaymentInitResult> StartPackageCheckoutAsync(string userId, int packageOfferId, string providerKey, string appBaseUrl, string buyerEmail, string customerIp, string? couponCode = null, PTScheduler.Application.Interfaces.InvoiceBuyerDto? invoiceBuyer = null, int? partnerClientId = null) => Ok();
        public Task<PTScheduler.Application.Interfaces.PaymentInitResult> StartSessionCheckoutAsync(string userId, int sessionId, string providerKey, string appBaseUrl, string buyerEmail, string customerIp) => Ok();
        public Task<PTScheduler.Application.Interfaces.PaymentInitResult> StartGiftVoucherCheckoutAsync(string userId, int voucherId, string providerKey, string appBaseUrl, string buyerEmail, string customerIp) => Ok();
        public Task<PTScheduler.Application.Interfaces.PaymentNotifyResponse> HandleNotifyAsync(string providerKey, string rawBody, IReadOnlyDictionary<string, string> headers) => Task.FromResult(new PTScheduler.Application.Interfaces.PaymentNotifyResponse(true));
        public Task<bool> CompleteSimulatorAsync(string extOrderId, bool paid) => Task.FromResult(true);
        public Task<PTScheduler.Application.DTOs.OrderDto?> GetOrderByExtAsync(string extOrderId) => Task.FromResult<PTScheduler.Application.DTOs.OrderDto?>(null);
        public Task<List<PTScheduler.Application.Interfaces.PaymentOptionDto>> GetEnabledOptionsAsync() => Task.FromResult(new List<PTScheduler.Application.Interfaces.PaymentOptionDto>());
        public Task<List<PTScheduler.Application.DTOs.OrderDto>> GetMyOrdersAsync(string userId) => Task.FromResult(new List<PTScheduler.Application.DTOs.OrderDto>());
        public Task<List<PTScheduler.Application.DTOs.OrderDto>> GetPaidOrdersAsync(DateTime from) => Task.FromResult(new List<PTScheduler.Application.DTOs.OrderDto>());
    }
    private sealed class NoKsef : PTScheduler.Application.Interfaces.IKsefService
    {
        public Task SaveTokenAsync(string? token) => Task.CompletedTask;
        public Task<(bool Ok, string Message)> TestConnectionAsync() => Task.FromResult((true, ""));
        public Task<(bool Ok, string? Error)> SetBuyerAsync(int orderId, PTScheduler.Application.Interfaces.InvoiceBuyerDto buyer) => Task.FromResult<(bool, string?)>((true, null));
        public Task<(bool Ok, string Message)> SendOrderInvoiceAsync(int orderId) => Task.FromResult((true, ""));
        public Task<int> RefreshPendingAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
