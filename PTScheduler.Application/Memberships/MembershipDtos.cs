using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.Memberships;

public class MembershipPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SessionTypeId { get; set; }
    public string SessionTypeName { get; set; } = string.Empty;
    public int SessionsPerPeriod { get; set; } = 8;
    public int PeriodMonths { get; set; } = 1;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "PLN";
    public bool CarryOverUnused { get; set; }
    public bool AvailableInShop { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int ActiveMembers { get; set; }

    public string PeriodLabel => PeriodMonths switch
    {
        1 => "miesiąc",
        3 => "kwartał",
        6 => "pół roku",
        12 => "rok",
        _ => $"{PeriodMonths} mies."
    };
}

public class MembershipPeriodDto
{
    public int Id { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal Amount { get; set; }
    public MembershipPeriodStatus Status { get; set; }
    public DateOnly DueDate { get; set; }
    public DateTime? PaidAt { get; set; }
    public int? PackageId { get; set; }
    public int SessionsTotal { get; set; }
    public int SessionsUsed { get; set; }
}

public class MembershipDto
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public int PlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public MembershipStatus Status { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "PLN";
    public int SessionsPerPeriod { get; set; }
    public int PeriodMonths { get; set; }
    public DateOnly CurrentPeriodStart { get; set; }
    public DateOnly NextBillingDate { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public decimal UnpaidAmount { get; set; }
    public List<MembershipPeriodDto> Periods { get; set; } = [];
}

/// <summary>Kalendarz rozliczeń karnetu — czyste obliczenia dat.</summary>
public static class MembershipBilling
{
    /// <summary>Dni od początku okresu na opłacenie należności.</summary>
    public const int GraceDays = 7;

    public static DateOnly PeriodEnd(DateOnly start, int months) => start.AddMonths(Math.Max(1, months)).AddDays(-1);

    public static DateOnly NextStart(DateOnly start, int months) => start.AddMonths(Math.Max(1, months));

    public static DateOnly DueDate(DateOnly start) => start.AddDays(GraceDays);
}
