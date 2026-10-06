namespace PTScheduler.Portal.Entities;

/// <summary>
/// Pozycja „oferty trenera” dopisana przez administratora: gratis albo dodatkowo płatna usługa
/// (jednorazowo, co miesiąc, co rok). Może mieć efekt w aplikacji — SMS-y albo większe limity wideo —
/// i trafia do zestawienia oraz PDF-u oferty.
/// </summary>
public class TenantOfferItem
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    /// <summary><see cref="OfferItemKind"/>: gratis albo dodatkowo płatne.</summary>
    public string Kind { get; set; } = OfferItemKind.Gift;
    public string Name { get; set; } = "";
    /// <summary>Opis widoczny w ofercie i na PDF.</summary>
    public string? Description { get; set; }
    /// <summary>Notatka tylko dla administratora.</summary>
    public string? InternalNote { get; set; }

    /// <summary>Usługa z katalogu, z której pozycja powstała (opcjonalnie).</summary>
    public int? ServiceItemId { get; set; }
    public ServiceItem? ServiceItem { get; set; }

    public int Quantity { get; set; } = 1;
    /// <summary>Cena za sztukę. Dla gratisu — wartość katalogowa (pokazywana jako „w gratisie”).</summary>
    public decimal UnitPrice { get; set; }
    /// <summary><see cref="OfferBilling"/>: jednorazowo, co miesiąc, co rok.</summary>
    public string Billing { get; set; } = OfferBilling.OneTime;

    /// <summary><see cref="OfferEffect"/>: co pozycja zmienia w aplikacji trenera.</summary>
    public string Effect { get; set; } = OfferEffect.None;
    /// <summary>Wielkość efektu na sztukę (SMS-y, GB).</summary>
    public int EffectAmount { get; set; }
    /// <summary>Kiedy dopisano jednorazowe kredyty (żeby nie dopisać ich drugi raz).</summary>
    public DateTime? EffectAppliedAt { get; set; }

    public DateTime StartsAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndsAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    /// <summary>Jednorazowa opłata doliczona do rachunku / zapłacona.</summary>
    public DateTime? SettledAt { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public decimal Total => UnitPrice * Quantity;
    public int TotalEffect => EffectAmount * Quantity;

    public bool IsActiveAt(DateTime utc) =>
        CancelledAt is null && StartsAt <= utc && (EndsAt is null || EndsAt > utc);
}

public static class OfferItemKind
{
    public const string Gift = "gift";
    public const string Charge = "charge";
}

public static class OfferBilling
{
    public const string OneTime = "one_time";
    public const string Monthly = "monthly";
    public const string Yearly = "yearly";
}

public static class OfferEffect
{
    public const string None = "none";
    /// <summary>Jednorazowo: kredyty SMS (nie wygasają). Cyklicznie: więcej SMS w miesięcznym limicie.</summary>
    public const string Sms = "sms";
    /// <summary>Więcej GB przestrzeni na wideo, dopóki pozycja jest aktywna.</summary>
    public const string VideoStorageGb = "video_storage_gb";
    /// <summary>Więcej GB transferu wideo miesięcznie, dopóki pozycja jest aktywna.</summary>
    public const string VideoBandwidthGb = "video_bandwidth_gb";
}
