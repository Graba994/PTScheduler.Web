namespace PTScheduler.Portal.Entities;

public enum UptimeState { Pending = 0, Up = 1, Down = 2, Paused = 3 }

/// <summary>
/// Monitor dostępności (jak w Uptime Kuma): adres sprawdzany co kilka sekund/minut z zewnątrz — przez DNS, proxy i SSL,
/// czyli tak, jak widzi go klient. Aplikacje trenerów dostają monitor automatycznie (<see cref="TenantId"/>).
/// </summary>
public class UptimeMonitor
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    /// <summary>Gdy ustawione — strona musi zawierać ten tekst (np. „Healthy”), sam kod 200 nie wystarczy.</summary>
    public string? Keyword { get; set; }
    public int IntervalSeconds { get; set; } = 60;
    public int TimeoutSeconds { get; set; } = 10;
    /// <summary>Ile nieudanych prób z rzędu, zanim monitor uzna awarię (chwilowy restart nie budzi w nocy).</summary>
    public int RetriesBeforeDown { get; set; } = 2;
    public bool IsPaused { get; set; }
    /// <summary>Widoczny na publicznej stronie /status.</summary>
    public bool IsPublic { get; set; }
    public int SortOrder { get; set; }
    public int? TenantId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public UptimeState State { get; set; } = UptimeState.Pending;
    public DateTime? LastCheckAt { get; set; }
    public DateTime? StateSince { get; set; }
    public int? LastResponseMs { get; set; }
    public string? LastError { get; set; }
    public int FailuresInRow { get; set; }
    public DateTime? CertExpiresAt { get; set; }
    /// <summary>Dzień, w którym poszło ostrzeżenie o kończącym się certyfikacie — jedno na dobę.</summary>
    public DateTime? CertAlertSentOn { get; set; }
}

/// <summary>Pojedyncze sprawdzenie — z nich paski heartbeat, uptime i wykres czasu odpowiedzi.</summary>
public class UptimeBeat
{
    public long Id { get; set; }
    public int MonitorId { get; set; }
    public DateTime At { get; set; }
    public bool Up { get; set; }
    public int? Ms { get; set; }
    public int? StatusCode { get; set; }
    public string? Error { get; set; }
}

/// <summary>Awaria od pierwszego „nie działa” do powrotu.</summary>
public class UptimeIncident
{
    public int Id { get; set; }
    public int MonitorId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? Error { get; set; }
}
