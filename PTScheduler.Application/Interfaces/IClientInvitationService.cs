namespace PTScheduler.Application.Interfaces;

/// <summary>
/// Zaproszenie klienta do aplikacji: jednorazowy link do ustawienia hasła (ważny kilka dni),
/// wysyłany e-mailem albo przekazywany przez trenera np. SMS-em / komunikatorem.
/// </summary>
public interface IClientInvitationService
{
    Task<ClientInviteResult> SendInviteAsync(int clientId, string appBaseUrl, bool sendEmail = true);
    Task<ClientAccountStatus?> GetAccountStatusAsync(int clientId);
}

public record ClientInviteResult(bool Success, bool EmailSent, string? Link, string? Error);

public record ClientAccountStatus(DateTime? LastLoginUtc, int PasskeyCount);

public static class ClientInvite
{
    /// <summary>Nazwa dostawcy tokenów zarejestrowanego w Identity (dłuższa ważność niż reset hasła).</summary>
    public const string TokenProvider = "ClientInvite";
    public const string Purpose = "SetPassword";
    public const int ValidDays = 7;
    public const string PagePath = "/Account/Invite";
}
