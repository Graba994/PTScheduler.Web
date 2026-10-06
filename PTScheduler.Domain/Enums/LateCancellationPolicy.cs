namespace PTScheduler.Domain.Enums;

/// <summary>
/// Co się dzieje, gdy klient odwołuje wizytę później niż okno bezpłatnego
/// odwołania (TrainerConfig.CancellationWindowHours).
/// </summary>
public enum LateCancellationPolicy
{
    /// <summary>Klient nie odwoła sam — musi skontaktować się z trenerem.</summary>
    Block = 0,
    /// <summary>Klient może odwołać, ale sesja przepada (zostaje pobrana z pakietu).</summary>
    ChargeSession = 1,
    /// <summary>Bez konsekwencji — sesja wraca do pakietu.</summary>
    Refund = 2
}
