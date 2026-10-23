using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class ClientInvitationService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> userManager,
    IEmailService emailService,
    IEmailTemplateService emailTemplateService,
    ILogger<ClientInvitationService> logger) : IClientInvitationService
{
    public async Task<ClientInviteResult> SendInviteAsync(int clientId, string appBaseUrl, bool sendEmail = true)
    {
        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId);
        if (client is null) return new(false, false, null, "Nie znaleziono klienta.");

        var user = await userManager.FindByIdAsync(client.ApplicationUserId);
        if (user is null || string.IsNullOrEmpty(user.Email))
            return new(false, false, null, "Klient nie ma konta z adresem e-mail.");

        var link = await BuildLinkAsync(userManager, user, appBaseUrl);
        if (!sendEmail) return new(true, false, link, null);

        if (!await emailService.IsEnabledAsync())
            return new(true, false, link,
                "Wysyłka e-maili nie jest skonfigurowana — skopiuj link i wyślij go klientowi.");

        try
        {
            var trainer = string.IsNullOrEmpty(client.TrainerUserId) ? null : await userManager.FindByIdAsync(client.TrainerUserId);
            var trainerName = trainer is null ? "" : $"{trainer.FirstName} {trainer.LastName}".Trim();
            var vars = new Dictionary<string, string>
            {
                ["ClientName"] = client.FirstName ?? "",
                ["TrainerName"] = string.IsNullOrEmpty(trainerName) ? "Twój trener" : trainerName,
                ["ClientEmail"] = user.Email,
                ["InviteLink"] = link,
                ["ValidDays"] = ClientInvite.ValidDays.ToString()
            };
            var (subject, html) = await emailTemplateService.RenderAsync("client-invite", vars);
            await emailService.SendAsync(user.Email, $"{client.FirstName} {client.LastName}".Trim(), subject, html);
            return new(true, true, link, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Wysyłka zaproszenia do klienta {ClientId} nie powiodła się.", clientId);
            return new(true, false, link, "Nie udało się wysłać e-maila — skopiuj link i wyślij go klientowi.");
        }
    }

    public async Task<ClientAccountStatus?> GetAccountStatusAsync(int clientId)
    {
        await using var db = dbFactory.CreateDbContext();
        var userId = await db.Clients.Where(c => c.Id == clientId).Select(c => c.ApplicationUserId).FirstOrDefaultAsync();
        if (userId is null) return null;
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return null;

        var lastLogin = await db.LoginLogs
            .Where(l => l.UserId == userId && l.Success)
            .OrderByDescending(l => l.LoginTime)
            .Select(l => (DateTime?)l.LoginTime)
            .FirstOrDefaultAsync();
        var passkeys = await userManager.GetPasskeysAsync(user);
        return new(lastLogin?.ToUniversalTime(), passkeys.Count);
    }

    /// <summary>
    /// Link „ustaw hasło”. Token jest powiązany ze znacznikiem bezpieczeństwa użytkownika,
    /// więc po ustawieniu hasła (zmiana znacznika) link przestaje działać — jednorazowy.
    /// </summary>
    internal static async Task<string> BuildLinkAsync(UserManager<ApplicationUser> userManager, ApplicationUser user, string appBaseUrl)
    {
        var token = await userManager.GenerateUserTokenAsync(user, ClientInvite.TokenProvider, ClientInvite.Purpose);
        var code = Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        return $"{appBaseUrl.TrimEnd('/')}{ClientInvite.PagePath}?uid={Uri.EscapeDataString(user.Id)}&code={code}";
    }

    /// <summary>RFC 4648 §5 base64url (bez paddingu) — zgodne z WebEncoders.Base64UrlDecode.</summary>
    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
