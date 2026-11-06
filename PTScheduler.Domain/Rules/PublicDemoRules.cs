namespace PTScheduler.Domain.Rules;

/// <summary>
/// Publiczne demo (instancja pokazowa dla odwiedzających hantly.pl): czego goście nie mogą otworzyć.
/// Blokujemy wszystko, co zmienia konto (hasło, e-mail, 2FA, usunięcie), wydaje pieniądze albo łączy się
/// z prawdziwym światem (sklep, płatności, poczta, SMS, Google, wideo, kopie, eksport) i zakładanie nowych kont.
/// </summary>
public static class PublicDemoRules
{
    private static readonly string[] Blocked =
    [
        "/Account/Manage", "/Account/Register", "/Account/ForgotPassword", "/Account/ResetPassword", "/Account/ExternalLogin",
        "/admin/sklep", "/admin/upgrade", "/admin/payments", "/admin/email", "/admin/sms", "/admin/push", "/admin/users",
        "/admin/permissions", "/admin/backup", "/admin/export", "/admin/google-calendar", "/admin/google-meet", "/admin/video",
        "/admin/settings", "/admin/modules", "/admin/audit-logs",
    ];

    /// <summary>Czy gość demo może otworzyć tę ścieżkę (bez adresu serwera, np. „/Account/Manage/Email?x=1”).</summary>
    public static bool IsBlockedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var p = path.Split('?', '#')[0].TrimEnd('/');
        if (!p.StartsWith('/')) p = "/" + p;
        return Blocked.Any(b => p.Equals(b, StringComparison.OrdinalIgnoreCase) || p.StartsWith(b + "/", StringComparison.OrdinalIgnoreCase));
    }

    public const string BlockedMessage = "W wersji demo ta część jest wyłączona — w Twojej aplikacji działa normalnie.";
    public const string PaymentMessage = "W wersji demo płatności są wyłączone — nic nie zostanie pobrane.";
}
