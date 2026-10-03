using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Security;

/// <summary>
/// E-mail „Nowe logowanie na Twoim koncie”, gdy ktoś zaloguje się z urządzenia (system + przeglądarka),
/// z którego wcześniej nie było udanego logowania. Pierwsze logowanie w ogóle nie wysyła wiadomości.
/// Działa w tle — logowanie nigdy na nią nie czeka.
/// </summary>
public sealed class NewDeviceLoginNotifier(IServiceScopeFactory scopes, ILogger<NewDeviceLoginNotifier> logger)
{
    /// <summary>Jak daleko wstecz pamiętamy urządzenia.</summary>
    public static readonly TimeSpan Memory = TimeSpan.FromDays(180);

    public void NotifyInBackground(string userId, string? userAgent, string? ip, string baseUrl) =>
        _ = Task.Run(async () =>
        {
            try { await NotifyAsync(userId, userAgent, ip, baseUrl); }
            catch (Exception ex) { logger.LogWarning(ex, "Powiadomienie o nowym logowaniu dla {UserId} nie wyszło.", userId); }
        });

    private async Task NotifyAsync(string userId, string? userAgent, string? ip, string baseUrl)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var clock = sp.GetRequiredService<IAppClock>();
        var dbFactory = sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        var device = DeviceNames.Describe(userAgent).Label;

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var since = clock.UtcNow - Memory;
            // Bieżące logowanie jest już zapisane — szukamy wcześniejszych udanych.
            var earlier = await db.LoginLogs.AsNoTracking()
                .Where(l => l.UserId == userId && l.Success && l.LoginTime >= since)
                .OrderByDescending(l => l.LoginTime)
                .Skip(1)
                .Select(l => l.UserAgent)
                .Take(200)
                .ToListAsync();
            if (earlier.Count == 0) return; // pierwsze logowanie — nie straszymy
            if (earlier.Any(ua => DeviceNames.Describe(ua).Label == device)) return;
        }

        var email = sp.GetRequiredService<IEmailService>();
        if (!await email.IsEnabledAsync()) return;
        var user = await sp.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(userId);
        if (user?.Email is null) return;

        var name = string.IsNullOrWhiteSpace(user.FirstName) ? "" : user.FirstName;
        var (subject, html) = await sp.GetRequiredService<IEmailTemplateService>().RenderAsync("new-device-login", new Dictionary<string, string>
        {
            ["Name"] = name.Length > 0 ? name : user.Email,
            ["Device"] = device,
            ["When"] = clock.ToWallClock(clock.UtcNow).ToString("d MMMM yyyy, HH:mm", new System.Globalization.CultureInfo("pl-PL")),
            ["Ip"] = string.IsNullOrWhiteSpace(ip) ? "—" : ip,
            ["SecureLink"] = baseUrl.TrimEnd('/') + "/Account/Manage/LoginHistory"
        });
        await email.SendAsync(user.Email, $"{user.FirstName} {user.LastName}".Trim(), subject, html);
        logger.LogInformation("Wysłano powiadomienie o logowaniu z nowego urządzenia ({Device}) do {UserId}.", device, userId);
    }
}
