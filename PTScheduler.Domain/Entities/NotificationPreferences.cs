namespace PTScheduler.Domain.Entities;

public class NotificationPreferences
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;

    public bool SessionBooked { get; set; } = true;
    public bool SessionCancelledByTrainer { get; set; } = true;
    public bool SessionRescheduled { get; set; } = true;
    public bool PackageAssigned { get; set; } = true;
    /// <summary>Client opt-in for the 24h-before-session reminder email.</summary>
    public bool SessionReminders { get; set; } = true;

    public bool ClientCancelledSession { get; set; } = true;
    public bool NewClientPending { get; set; } = true;
    public bool ExpiringPackages { get; set; } = true;
    public bool TrainerMessages { get; set; } = true;

    public bool ShowHints { get; set; } = true;

    // ── Kanały push i SMS (e-mail to pola powyżej) ──
    /// <summary>Push: przypomnienie dzień przed treningiem.</summary>
    public bool PushReminders { get; set; } = true;
    /// <summary>Push: rezerwacje, zmiany i odwołania wizyt, prośby o termin.</summary>
    public bool PushSessions { get; set; } = true;
    /// <summary>Push: pakiety i karnety (nowe, kończące się, płatności).</summary>
    public bool PushPackages { get; set; } = true;
    /// <summary>Push: wiadomości z czatu i komentarze do treningów.</summary>
    public bool PushMessages { get; set; } = true;
    /// <summary>Push: automatyczne wiadomości od trenera (powitanie, urodziny, powroty).</summary>
    public bool PushTrainerMessages { get; set; } = true;
    /// <summary>Push (trener): aktywność klientów — ankiety, pomiary, opinie.</summary>
    public bool PushClientActivity { get; set; } = true;
    /// <summary>SMS: przypomnienie dzień przed treningiem (klient może zrezygnować).</summary>
    public bool SmsReminders { get; set; } = true;
    /// <summary>SMS: automatyczne wiadomości od trenera.</summary>
    public bool SmsTrainerMessages { get; set; } = true;
}
