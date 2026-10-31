namespace PTScheduler.Application.DTOs;

/// <summary>Raport biznesowy trenera za wybrany okres (np. ostatnie 90 dni) z porównaniem do poprzedniego.</summary>
public sealed class BusinessReportDto
{
    public int PeriodDays { get; init; }

    // ── Klienci ──
    public int ActiveClients { get; init; }
    public int ActiveClientsPrevious { get; init; }
    public int NewClients { get; init; }
    public int? RetentionPercent { get; init; }
    public int? RetentionPercentPrevious { get; init; }
    /// <summary>Trenowali w poprzednim okresie, w tym nie byli i nie mają nic zaplanowanego.</summary>
    public List<ClientRefDto> LostClients { get; init; } = [];
    /// <summary>Retencja miesiąc do miesiąca (ostatnie 6 miesięcy).</summary>
    public List<MonthlyRetentionDto> RetentionByMonth { get; init; } = [];

    // ── Przychód ──
    public decimal Revenue { get; init; }
    public decimal RevenuePrevious { get; init; }
    public int PayingClients { get; init; }
    public decimal AvgRevenuePerClient { get; init; }
    public decimal AvgRevenuePerClientPrevious { get; init; }
    /// <summary>Średni przychód na aktywnego klienta w przeliczeniu na miesiąc.</summary>
    public decimal AvgMonthlyRevenuePerActiveClient { get; init; }
    public List<ClientRevenueDto> TopClients { get; init; } = [];

    // ── Obłożenie grafiku ──
    public OccupancyDto PastOccupancy { get; init; } = new();
    public OccupancyDto UpcomingOccupancy { get; init; } = new();
    /// <summary>Obłożenie wg dnia tygodnia (poniedziałek..niedziela) w ostatnich tygodniach.</summary>
    public List<WeekdayOccupancyDto> OccupancyByWeekday { get; init; } = [];

    // ── Prognoza (najbliższe 30 dni) ──
    public ForecastDto Forecast { get; init; } = new();

    // ── Odwołania ──
    public int CancelledCount { get; init; }
    public int NoShowCount { get; init; }
    public int LateCancelledCount { get; init; }
    public int? CancellationRatePercent { get; init; }
    public List<CancellationSlotDto> TopCancelledSlots { get; init; } = [];
    /// <summary>Mapa odwołań: [dzień tygodnia 0=pon..6=niedz][godzina] = liczba odwołań.</summary>
    public int[][] CancellationHeatmap { get; init; } = [];
    public int HeatmapFirstHour { get; init; }
    public int HeatmapLastHour { get; init; }
}

public sealed record ClientRefDto(int ClientId, string Name, DateTime? LastVisit);
public sealed record ClientRevenueDto(int ClientId, string Name, decimal Revenue);
public sealed record MonthlyRetentionDto(string Label, int ActiveClients, int? RetentionPercent);
public sealed record WeekdayOccupancyDto(string Day, int AvailableMinutes, int BookedMinutes, int? Percent);
public sealed record CancellationSlotDto(string Day, int Hour, int Cancelled, int Total, int? RatePercent);

public sealed class OccupancyDto
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public int AvailableMinutes { get; init; }
    public int BookedMinutes { get; init; }
    public int? Percent { get; init; }
    /// <summary>Wolne godziny w grafiku (dostępne minus zajęte).</summary>
    public int FreeMinutes => Math.Max(0, AvailableMinutes - BookedMinutes);
}

public sealed class ForecastDto
{
    public int Days { get; init; } = 30;
    /// <summary>Karnety, które odnowią się w najbliższych dniach.</summary>
    public int MembershipRenewals { get; init; }
    public decimal MembershipAmount { get; init; }
    /// <summary>Miesięczny przychód z aktywnych karnetów.</summary>
    public decimal MonthlyRecurring { get; init; }
    /// <summary>Pakiety, które wkrótce się skończą (mało treningów albo koniec ważności).</summary>
    public int EndingPackages { get; init; }
    public decimal EndingPackagesValue { get; init; }
    /// <summary>Jak często klienci kupują kolejny pakiet (historycznie).</summary>
    public int? RepurchaseRatePercent { get; init; }
    public decimal ExpectedPackageAmount { get; init; }
    public decimal Total => MembershipAmount + ExpectedPackageAmount;
    /// <summary>Opłacone, jeszcze niewykorzystane treningi — pieniądze już są, treningi do zrobienia.</summary>
    public int PrepaidSessions { get; init; }
    public decimal PrepaidValue { get; init; }
    /// <summary>Nieopłacone pakiety — do odebrania od klientów.</summary>
    public decimal UnpaidAmount { get; init; }
    public List<EndingPackageDto> EndingList { get; init; } = [];
}

public sealed record EndingPackageDto(int ClientId, string ClientName, string PackageName, int Remaining, DateTime? ExpiresAt, decimal Value);
