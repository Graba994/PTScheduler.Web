namespace PTScheduler.Web.Services;

/// <summary>Limity prób na formularzach logowania i e-maili na żądanie (na adres IP).</summary>
public static class AuthRateLimits
{
    public const int AuthPermit = 10;
    public static readonly TimeSpan AuthWindow = TimeSpan.FromMinutes(1);
    public const int EmailPermit = 5;
    public static readonly TimeSpan EmailWindow = TimeSpan.FromMinutes(15);

    private static readonly string[] CredentialPaths =
    [
        "/Account/Login", // także /Account/LoginWith2fa i /Account/LoginWithRecoveryCode
        "/Account/Register",
        "/Account/Invite",
        "/Account/ResetPassword",
        "/Account/PasskeyRequestOptions"
    ];

    private static readonly string[] EmailPaths =
    [
        "/Account/ForgotPassword",
        "/Account/ResendEmailConfirmation"
    ];

    public static bool IsCredentialAttempt(string path) =>
        CredentialPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    public static bool IsEmailTrigger(string path) =>
        EmailPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
