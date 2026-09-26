namespace PTScheduler.Domain.Entities;

/// <summary>
/// Połączenie kalendarza Google użytkownika (trener/współpracownik).
/// W instancji zarządzanej przez platformę token trzyma Portal („platform”);
/// w instalacji samodzielnej używamy konta z ustawień Google Meet („own”).
/// </summary>
public class CalendarConnection
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Mode { get; set; } = CalendarConnectionModes.Platform;
    public string? GoogleEmail { get; set; }

    /// <summary>Wizyty z aplikacji trafiają do kalendarza Google.</summary>
    public bool PushSessions { get; set; } = true;
    /// <summary>Zajęte terminy z kalendarza Google blokują rezerwacje.</summary>
    public bool BlockBusy { get; set; } = true;
    /// <summary>W tytule wydarzenia imię i nazwisko klienta (wyłącz, jeśli kalendarz widzą inni).</summary>
    public bool ShowClientName { get; set; } = true;
    /// <summary>Każda nowa wizyta dostaje link Google Meet (dla trenerów pracujących online).</summary>
    public bool CreateMeetLinks { get; set; }

    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSyncAt { get; set; }
    public string? LastError { get; set; }
    /// <summary>Google cofnął zgodę (np. trener odłączył aplikację w koncie Google).</summary>
    public bool NeedsReconnect { get; set; }
}

public static class CalendarConnectionModes
{
    public const string Platform = "platform";
    public const string Own = "own";
}
