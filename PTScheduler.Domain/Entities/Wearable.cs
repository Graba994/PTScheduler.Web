namespace PTScheduler.Domain.Entities;

/// <summary>
/// Zegarek klienta połączony przez Portal (Garmin Connect). Klient łączy go sam, po wyraźnej zgodzie na
/// udostępnienie danych trenerowi; może ją w każdej chwili wycofać (odłączenie, opcjonalnie z usunięciem danych).
/// </summary>
public class WearableLink
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    /// <summary>„garmin”.</summary>
    public string Provider { get; set; } = "garmin";
    /// <summary>Zgoda klienta (moment kliknięcia „Połącz” z zaznaczoną zgodą).</summary>
    public DateTime ConsentAt { get; set; } = DateTime.UtcNow;
    /// <summary>Osobna, dobrowolna zgoda na analizę z pomocą AI (Garmin wymaga jej wprost). Null = bez AI — dane z zegarka nie trafiają do modelu.</summary>
    public DateTime? AiConsentAt { get; set; }
    public DateTime? ConnectedAt { get; set; }
    public DateTime? DisconnectedAt { get; set; }
    /// <summary>Model urządzenia z ostatniej aktywności — Garmin wymaga go przy pokazywaniu danych.</summary>
    public string? DeviceName { get; set; }
    public DateTime? LastDataAt { get; set; }
    /// <summary>Klient chce powiadomienie po treningu z podsumowaniem z zegarka (gdy studio to włączyło).</summary>
    public bool NotifySessions { get; set; } = true;
    /// <summary>Klient chce poniedziałkowy raport tygodnia z zegarka (gdy studio to włączyło).</summary>
    public bool NotifyWeekly { get; set; } = true;
    /// <summary>Poniedziałek tygodnia, za który wysłano ostatni raport — bez powtórek.</summary>
    public DateOnly? WeeklyReportSentFor { get; set; }

    public bool IsConnected => ConnectedAt is not null && DisconnectedAt is null;
}

/// <summary>Jeden dzień z zegarka: dzienne podsumowanie, sen i HRV scalone w jeden wiersz (klient + źródło + data).</summary>
public class WearableDay
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    public string Provider { get; set; } = "garmin";
    public DateOnly Date { get; set; }

    public int? Steps { get; set; }
    public int? RestingHeartRate { get; set; }
    public int? AverageStress { get; set; }
    public int? BodyBatteryHigh { get; set; }
    public int? BodyBatteryLow { get; set; }
    public int? ActiveKilocalories { get; set; }

    public int? SleepMinutes { get; set; }
    public int? DeepSleepMinutes { get; set; }
    public int? LightSleepMinutes { get; set; }
    public int? RemSleepMinutes { get; set; }
    public int? AwakeMinutes { get; set; }
    public int? SleepScore { get; set; }

    /// <summary>Średnie HRV z ostatniej nocy (ms).</summary>
    public int? HrvLastNight { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Aktywność z zegarka (bieg, rower, siłownia…). Pokrywająca się z wizytą dostaje jej Id.</summary>
public class WearableActivity
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    public string Provider { get; set; } = "garmin";
    /// <summary>Identyfikator po stronie dostawcy (Garmin: summaryId) — ponowne dostarczenie aktualizuje wiersz.</summary>
    public string ExternalId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTime StartUtc { get; set; }
    public int DurationSeconds { get; set; }
    public int? AverageHeartRate { get; set; }
    public int? MaxHeartRate { get; set; }
    public int? Kilocalories { get; set; }
    public int? DistanceMeters { get; set; }
    public string? DeviceName { get; set; }
    public int? SessionId { get; set; }
    /// <summary>Kiedy klient dostał podsumowanie tego treningu (powiadomienie) — każdy trening raz.</summary>
    public DateTime? SummarySentAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Dzień planu wysłany na zegarek klienta (Garmin Training API): trening w Garmin Connect zaplanowany na dzień.</summary>
public class WearableWorkout
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    public int? PlanDayId { get; set; }
    public int? SessionId { get; set; }
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
    public long GarminWorkoutId { get; set; }
    public long? GarminScheduleId { get; set; }
    public string? SentByUserId { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
