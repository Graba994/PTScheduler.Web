using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Components.Account;

internal static class LoginSupport
{
    public const string DefaultLanding = "/app";

    /// <summary>
    /// Cel po zalogowaniu. Przyjmuje ścieżkę lokalną („/train”) albo pełny adres tej samej
    /// aplikacji (RedirectToLogin przekazuje NavigationManager.Uri). Wszystko inne — obce
    /// hosty, „//host”, „/\host” — kończy się na domyślnej stronie, żeby nie było open redirect.
    /// </summary>
    public static string SafeReturnUrl(string? returnUrl, string baseUri)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)) return DefaultLanding;

        var path = returnUrl.Trim();
        if (Uri.TryCreate(path, UriKind.Absolute, out var abs) && abs.Scheme is "http" or "https")
        {
            if (!Uri.TryCreate(baseUri, UriKind.Absolute, out var root)
                || !string.Equals(abs.Authority, root.Authority, StringComparison.OrdinalIgnoreCase))
                return DefaultLanding;
            path = abs.PathAndQuery + abs.Fragment;
        }

        if (!path.StartsWith('/') || path.StartsWith("//") || path.StartsWith("/\\"))
            return DefaultLanding;
        if (path == "/" || path.StartsWith("/Account/", StringComparison.OrdinalIgnoreCase))
            return DefaultLanding;
        return path;
    }

    public static async Task RecordAsync(ApplicationDbContext db, IAppClock clock, ILogger logger,
        HttpContext http, string userId, bool success)
    {
        var ua = http.Request.Headers.UserAgent.ToString();
        try
        {
            db.LoginLogs.Add(new LoginLog
            {
                UserId = userId,
                LoginTime = clock.UtcNow,
                IpAddress = http.Connection.RemoteIpAddress?.ToString(),
                UserAgent = ua.Length > 500 ? ua[..500] : ua,
                Success = success
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Log logowań jest pomocniczy — jego błąd nie może blokować logowania.
            logger.LogWarning(ex, "Nie udało się zapisać wpisu logowania użytkownika {UserId}.", userId);
        }
    }

    /// <summary>Komunikat o blokadzie konta z czasem do jej końca.</summary>
    public static string LockoutMessage(DateTimeOffset? lockoutEnd, DateTime utcNow)
    {
        if (lockoutEnd is null || lockoutEnd.Value.UtcDateTime <= utcNow)
            return "Konto jest tymczasowo zablokowane. Spróbuj ponownie za chwilę.";
        if (lockoutEnd.Value.UtcDateTime > utcNow.AddYears(1))
            return "Konto jest zablokowane. Skontaktuj się z trenerem.";
        var minutes = (int)Math.Ceiling((lockoutEnd.Value.UtcDateTime - utcNow).TotalMinutes);
        return $"Za dużo nieudanych prób. Konto jest zablokowane — spróbuj ponownie za {minutes} min.";
    }
}
