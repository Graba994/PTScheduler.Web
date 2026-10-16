namespace PTScheduler.Domain.Entities;

public class EmailSettings
{
    public int Id { get; set; } = 1;
    public bool IsEnabled { get; set; }
    public string Provider { get; set; } = "Custom";
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool UseTls { get; set; } = true;
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "PTScheduler";
    /// <summary>Adres, na który trafiają odpowiedzi klientów (przy wysyłce przez serwer platformy).</summary>
    public string? ReplyTo { get; set; }

    // ── Stan własnej skrzynki (gdy nie działa, maile idą awaryjnie przez serwer platformy) ──
    /// <summary>Ostatnia udana wysyłka z własnej skrzynki (zapisywana po błędzie, nie przy każdym mailu).</summary>
    public DateTime? LastOkAt { get; set; }
    /// <summary>Ostatni błąd własnej skrzynki.</summary>
    public DateTime? LastErrorAt { get; set; }
    public string? LastError { get; set; }
    /// <summary>Ile maili od ostatniego sukcesu wyszło awaryjnie przez serwer platformy.</summary>
    public int FallbackCount { get; set; }

    /// <summary>Własna skrzynka zawodzi: ostatnio był błąd i od tego czasu nic z niej nie wyszło (liczymy 3 dni).</summary>
    public static bool IsFailing(DateTime? lastErrorAt, DateTime? lastOkAt, DateTime utcNow) =>
        lastErrorAt is { } err && (lastOkAt is null || lastOkAt < err) && utcNow - err < TimeSpan.FromDays(3);
}
