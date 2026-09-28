namespace PTScheduler.Domain.Entities;

/// <summary>
/// Automatyczna wiadomość do klientów (powitanie, odzyskiwanie, urodziny). Jedna reguła na rodzaj
/// (<see cref="Constants.AutomationKinds"/>); trener włącza ją i ustawia treść, kanały, opóźnienie i kupon.
/// </summary>
public class AutomationRule
{
    public int Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    /// <summary>Od kiedy włączona — powitanie nie trafia do klientów założonych wcześniej.</summary>
    public DateTime? EnabledAt { get; set; }

    /// <summary>Dni: po założeniu konta (powitanie), bez wizyty (odzyskiwanie), po końcu pakietu.</summary>
    public int DelayDays { get; set; }
    /// <summary>Po ilu dniach ta sama wiadomość może trafić znowu do tej samej osoby.</summary>
    public int CooldownDays { get; set; } = 60;

    public bool ViaEmail { get; set; } = true;
    public bool ViaPush { get; set; } = true;
    public bool ViaSms { get; set; }

    public string Subject { get; set; } = string.Empty;
    /// <summary>Treść z polami {Imie}, {Trener}, {Kupon}, {Rabat}, {WaznyDo}.</summary>
    public string Message { get; set; } = string.Empty;
    public string ButtonText { get; set; } = string.Empty;

    /// <summary>0 = bez kuponu. Kupon jednorazowy, osobny dla każdego klienta.</summary>
    public int CouponPercent { get; set; }
    public int CouponValidDays { get; set; } = 14;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Wysłana automatyczna wiadomość — i czy klient potem wrócił (rezerwacja albo zakup).</summary>
public class AutomationLog
{
    public int Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public int ClientId { get; set; }
    public Client? Client { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    /// <summary>Kanały, którymi faktycznie wyszło, np. „email,push”.</summary>
    public string Channels { get; set; } = string.Empty;
    public string? CouponCode { get; set; }
    /// <summary>Klient zarezerwował trening albo kupił pakiet po tej wiadomości.</summary>
    public DateTime? ReturnedAt { get; set; }
}
