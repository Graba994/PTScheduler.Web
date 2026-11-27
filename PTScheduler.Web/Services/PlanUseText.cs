using PTScheduler.Domain.Enums;

namespace PTScheduler.Web.Services;

/// <summary>Krótkie etykiety i ikony trybu planu (z trenerem / samodzielnie / oba) — wspólne dla list planów.</summary>
public static class PlanUseText
{
    public static string Short(PlanUse use) => use switch
    {
        PlanUse.WithTrainer => "z trenerem",
        PlanUse.Solo => "samodzielnie",
        _ => "z trenerem i solo"
    };

    public static string Icon(PlanUse use) => use switch
    {
        PlanUse.WithTrainer => "bi-people",
        PlanUse.Solo => "bi-person-walking",
        _ => "bi-arrow-left-right"
    };

    /// <summary>Klasa CSS plakietki (.cpl-use use-…).</summary>
    public static string Css(PlanUse use) => "use-" + use.ToString().ToLowerInvariant();
}
