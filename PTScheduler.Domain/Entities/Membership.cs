using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Entities;

/// <summary>
/// Karnet cykliczny (oferta): N sesji w każdym okresie rozliczeniowym za stałą cenę,
/// np. „8 treningów / miesiąc”.
/// </summary>
public class MembershipPlan
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public int SessionTypeId { get; set; }
    public SessionType SessionType { get; set; } = null!;

    /// <summary>Liczba sesji w każdym okresie.</summary>
    public int SessionsPerPeriod { get; set; }
    /// <summary>Długość okresu w miesiącach (1 = miesięczny, 3 = kwartalny).</summary>
    public int PeriodMonths { get; set; } = 1;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "PLN";

    /// <summary>Niewykorzystane sesje przechodzą na kolejny okres.</summary>
    public bool CarryOverUnused { get; set; }

    /// <summary>Karnet widoczny w sklepie — klient może go kupić sam.</summary>
    public bool AvailableInShop { get; set; } = true;
    public bool IsActive { get; set; } = true;

    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Subskrypcja klienta na karnet cykliczny.</summary>
public class Membership
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    public int PlanId { get; set; }
    public MembershipPlan Plan { get; set; } = null!;

    public MembershipStatus Status { get; set; } = MembershipStatus.Active;

    /// <summary>Cena dla tego klienta (null = cena z karnetu).</summary>
    public decimal? PriceOverride { get; set; }

    /// <summary>Początek bieżącego okresu i dzień następnego rozliczenia (zegar studia, daty).</summary>
    public DateOnly CurrentPeriodStart { get; set; }
    public DateOnly NextBillingDate { get; set; }

    /// <summary>Klient zrezygnował — karnet skończy się z końcem bieżącego okresu.</summary>
    public bool CancelAtPeriodEnd { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CancelledAt { get; set; }

    public ICollection<MembershipPeriod> Periods { get; set; } = [];
}

/// <summary>Okres rozliczeniowy karnetu: należność i pakiet sesji na ten okres.</summary>
public class MembershipPeriod
{
    public int Id { get; set; }
    public int MembershipId { get; set; }
    public Membership Membership { get; set; } = null!;

    public DateOnly PeriodStart { get; set; }
    /// <summary>Ostatni dzień okresu (włącznie).</summary>
    public DateOnly PeriodEnd { get; set; }
    public decimal Amount { get; set; }

    public MembershipPeriodStatus Status { get; set; } = MembershipPeriodStatus.Due;
    /// <summary>Termin płatności (zwykle pierwszy dzień okresu + karencja).</summary>
    public DateOnly DueDate { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? PaymentReference { get; set; }

    /// <summary>Pakiet sesji utworzony dla tego okresu.</summary>
    public int? PackageId { get; set; }
    public SessionPackage? Package { get; set; }

    public DateTime? ReminderSentAt { get; set; }
}
