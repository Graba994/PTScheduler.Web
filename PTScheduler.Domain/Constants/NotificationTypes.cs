namespace PTScheduler.Domain.Constants;

public static class NotificationTypes
{
    public const string SessionBooked = nameof(SessionBooked);
    public const string SessionCancelledByTrainer = nameof(SessionCancelledByTrainer);
    public const string SessionRescheduled = nameof(SessionRescheduled);
    public const string PackageAssigned = nameof(PackageAssigned);
    public const string ClientCancelledSession = nameof(ClientCancelledSession);
    public const string NewClientPending = nameof(NewClientPending);
    public const string ExpiringPackages = nameof(ExpiringPackages);
    public const string SessionReminders = nameof(SessionReminders);
    /// <summary>Automatyczne wiadomości od trenera: powitanie, „dawno Cię nie było”, życzenia urodzinowe.</summary>
    public const string TrainerMessages = nameof(TrainerMessages);

    /// <summary>Push: przypomnienie dzień przed treningiem.</summary>
    public const string PushReminders = nameof(PushReminders);
    /// <summary>Push: rezerwacje, zmiany i odwołania wizyt, prośby o termin.</summary>
    public const string PushSessions = nameof(PushSessions);
    /// <summary>Push: pakiety i karnety (nowe, kończące się, płatności).</summary>
    public const string PushPackages = nameof(PushPackages);
    /// <summary>Push: wiadomości z czatu i komentarze do treningów.</summary>
    public const string PushMessages = nameof(PushMessages);
    /// <summary>Push: automatyczne wiadomości od trenera (powitanie, urodziny, powroty).</summary>
    public const string PushTrainerMessages = nameof(PushTrainerMessages);
    /// <summary>Push (trener): aktywność klientów — ankiety, pomiary, opinie.</summary>
    public const string PushClientActivity = nameof(PushClientActivity);
    /// <summary>SMS: przypomnienie dzień przed treningiem (klient może zrezygnować).</summary>
    public const string SmsReminders = nameof(SmsReminders);
    /// <summary>SMS: automatyczne wiadomości od trenera.</summary>
    public const string SmsTrainerMessages = nameof(SmsTrainerMessages);
}
