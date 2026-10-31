namespace PTScheduler.Application.DTOs;

public class WebPushSettingsDto
{
    public string PublicKey { get; set; } = "";
    public string PrivateKey { get; set; } = "";
    public string Subject { get; set; } = "mailto:admin@ptscheduler.app";
    public bool IsConfigured => !string.IsNullOrEmpty(PublicKey) && !string.IsNullOrEmpty(PrivateKey);
}

public class PushSubscriptionDto
{
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
}

public class PushMessageDto
{
    public string Title { get; set; } = "Hantly";
    public string Body { get; set; } = "";
    public string? Url { get; set; }
    public string? Icon { get; set; }
    /// <summary>
    /// Rodzaj powiadomienia (<c>NotificationTypes.Push…</c>). Gdy odbiorca wyłączył ten rodzaj push
    /// w ustawieniach konta, wiadomość nie jest wysyłana. Null — zawsze wysyłamy.
    /// </summary>
    public string? Category { get; set; }
    /// <summary>Przyciski w powiadomieniu (zwykle najwyżej 2 — tyle pokazuje większość systemów).</summary>
    public List<PushActionDto>? Actions { get; set; }
}

/// <summary>Wynik wysyłki do wszystkich urządzeń użytkownika.</summary>
public sealed record PushSendReport(int Sent, int Failed, string? LastError);

/// <summary>
/// Przycisk w powiadomieniu push. Z <see cref="Kind"/> akcja wykonuje się bez otwierania aplikacji
/// (podpisany token, ważny 3 dni, tylko dla odbiorcy); z samym <see cref="Url"/> — otwiera ten adres.
/// </summary>
public class PushActionDto
{
    public string Action { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Url { get; set; }
    /// <summary>„approve” (akceptacja rezerwacji) albo „reply” (gotowa odpowiedź na czacie).</summary>
    public string? Kind { get; set; }
    public int TargetId { get; set; }
    public string? Arg { get; set; }
}
