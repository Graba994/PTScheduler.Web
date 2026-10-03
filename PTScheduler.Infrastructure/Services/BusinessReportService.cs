using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

/// <summary>
/// Raporty biznesowe trenera. Przychód = opłacone pakiety (w tym karnety, które co okres tworzą pakiet)
/// + opłacone treningi poza pakietem. Godziny wizyt są na zegarze ściennym, płatności w UTC.
/// </summary>
public class BusinessReportService(IDbContextFactory<ApplicationDbContext> dbFactory, IAppClock clock) : IBusinessReportService
{
    private static readonly string[] DayNames = ["Pon", "Wt", "Śr", "Czw", "Pt", "Sob", "Niedz"];
    private const int ForecastDays = 30;

    private static string MonthLabel(DateTime m, CultureInfo culture)
    {
        var name = culture.DateTimeFormat.AbbreviatedMonthNames[m.Month - 1].TrimEnd('.');
        return (name.Length > 0 ? char.ToUpper(name[0], culture) + name[1..] : m.Month.ToString()) + " " + m.ToString("yy");
    }

    private sealed record Visit(int ClientId, string TrainerUserId, DateTime StartTime, SessionStatus Status, bool Late, int Minutes);

    public async Task<BusinessReportDto> GetAsync(int periodDays, string? trainerUserId)
    {
        periodDays = Math.Clamp(periodDays, 7, 365);
        await using var db = dbFactory.CreateDbContext();

        var now = clock.LocalNow;
        var nowUtc = clock.UtcNow;
        var today = clock.Today;
        var period = TimeSpan.FromDays(periodDays);
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var loadFrom = new[] { now - 3 * period, monthStart.AddMonths(-6), now.AddDays(-56) }.Min();

        // ── Wizyty ──
        var visitsQuery = db.Sessions.AsNoTracking().Where(s => s.StartTime >= loadFrom && s.StartTime < now.AddDays(15));
        if (trainerUserId is not null) visitsQuery = visitsQuery.Where(s => s.TrainerUserId == trainerUserId);
        var visits = await visitsQuery
            .Select(s => new Visit(s.ClientId, s.TrainerUserId, s.StartTime, s.Status, s.IsLateCancellation, s.SessionType.DurationMinutes))
            .ToListAsync();

        var clientsQuery = db.Clients.AsNoTracking();
        if (trainerUserId is not null) clientsQuery = clientsQuery.Where(c => c.TrainerUserId == trainerUserId);
        var clientNames = await clientsQuery
            .Select(c => new { c.Id, Name = (c.FirstName + " " + c.LastName).Trim() })
            .ToDictionaryAsync(c => c.Id, c => c.Name);
        string NameOf(int id) => clientNames.TryGetValue(id, out var n) && n.Length > 0 ? n : "Klient";

        HashSet<int> ActiveBetween(DateTime from, DateTime to) =>
            visits.Where(v => v.Status == SessionStatus.Completed && v.StartTime >= from && v.StartTime < to).Select(v => v.ClientId).ToHashSet();

        var current = ActiveBetween(now - period, now);
        var previous = ActiveBetween(now - 2 * period, now - period);
        var beforePrevious = ActiveBetween(now - 3 * period, now - 2 * period);

        var firstVisitQuery = db.Sessions.AsNoTracking().Where(s => s.Status == SessionStatus.Completed);
        if (trainerUserId is not null) firstVisitQuery = firstVisitQuery.Where(s => s.TrainerUserId == trainerUserId);
        var firstVisits = await firstVisitQuery.GroupBy(s => s.ClientId)
            .Select(g => new { ClientId = g.Key, First = g.Min(s => s.StartTime) })
            .ToDictionaryAsync(x => x.ClientId, x => x.First);
        var newClients = current.Count(id => firstVisits.TryGetValue(id, out var f) && f >= now - period);

        var upcoming = visits.Where(v => v.StartTime >= now && v.Status is SessionStatus.Scheduled or SessionStatus.AwaitingPackage)
            .Select(v => v.ClientId).ToHashSet();
        var lastVisit = visits.Where(v => v.Status == SessionStatus.Completed)
            .GroupBy(v => v.ClientId).ToDictionary(g => g.Key, g => g.Max(v => v.StartTime));
        var lost = previous.Where(id => !current.Contains(id) && !upcoming.Contains(id))
            .Select(id => new ClientRefDto(id, NameOf(id), lastVisit.TryGetValue(id, out var lv) ? lv : null))
            .OrderByDescending(c => c.LastVisit).ToList();

        var culture = new CultureInfo("pl-PL");
        var byMonth = new List<MonthlyRetentionDto>();
        for (var i = 5; i >= 0; i--)
        {
            var m = monthStart.AddMonths(-i);
            var active = ActiveBetween(m, m.AddMonths(1));
            var before = ActiveBetween(m.AddMonths(-1), m);
            byMonth.Add(new MonthlyRetentionDto(MonthLabel(m, culture), active.Count, BusinessReportRules.Retention(before, active)));
        }

        // ── Przychód ──
        var packagesQuery = db.SessionPackages.AsNoTracking().Where(p => p.Status != PackageStatus.Cancelled);
        if (trainerUserId is not null)
            packagesQuery = packagesQuery.Where(p => p.Client.TrainerUserId == trainerUserId || p.CreatedByUserId == trainerUserId);
        var packages = await packagesQuery
            .Select(p => new
            {
                p.Id, p.ClientId, p.Name, p.TotalSessions, p.UsedSessions, p.PricePerSession, p.IsPaid, p.PaidAt,
                p.PurchasedAt, p.ExpiresAt, p.Status, p.IsHidden
            })
            .ToListAsync();
        var membershipPackageIds = (await db.MembershipPeriods.AsNoTracking().Where(mp => mp.PackageId != null)
            .Select(mp => mp.PackageId!.Value).ToListAsync()).ToHashSet();

        var offPackageQuery = db.Sessions.AsNoTracking().Where(s => s.PaidAt != null && s.OffPackagePayment != null && s.PaidAt >= nowUtc - 2 * period);
        if (trainerUserId is not null) offPackageQuery = offPackageQuery.Where(s => s.TrainerUserId == trainerUserId);
        var offPackage = await offPackageQuery
            .Select(s => new { s.ClientId, PaidAt = s.PaidAt!.Value, Price = s.SessionType.SinglePrice ?? 0m })
            .ToListAsync();

        List<(int ClientId, decimal Amount)> PaymentsBetween(DateTime fromUtc, DateTime toUtc) =>
            packages.Where(p => p.IsPaid && (p.PaidAt ?? p.PurchasedAt) >= fromUtc && (p.PaidAt ?? p.PurchasedAt) < toUtc)
                .Select(p => (p.ClientId, p.TotalSessions * p.PricePerSession))
                .Concat(offPackage.Where(o => o.PaidAt >= fromUtc && o.PaidAt < toUtc).Select(o => (o.ClientId, o.Price)))
                .ToList();

        var paymentsNow = PaymentsBetween(nowUtc - period, nowUtc);
        var paymentsPrev = PaymentsBetween(nowUtc - 2 * period, nowUtc - period);
        var revenue = paymentsNow.Sum(p => p.Amount);
        var revenuePrev = paymentsPrev.Sum(p => p.Amount);
        var paying = paymentsNow.Where(p => p.Amount > 0).Select(p => p.ClientId).Distinct().Count();
        var payingPrev = paymentsPrev.Where(p => p.Amount > 0).Select(p => p.ClientId).Distinct().Count();
        var topClients = paymentsNow.GroupBy(p => p.ClientId)
            .Select(g => new ClientRevenueDto(g.Key, NameOf(g.Key), g.Sum(p => p.Amount)))
            .Where(c => c.Revenue > 0).OrderByDescending(c => c.Revenue).Take(5).ToList();
        var months = periodDays / 30.0;

        // ── Obłożenie grafiku ──
        var rulesQuery = db.TrainerAvailabilities.AsNoTracking().Where(r => r.IsActive);
        if (trainerUserId is not null) rulesQuery = rulesQuery.Where(r => r.TrainerUserId == trainerUserId);
        var rulesByTrainer = (await rulesQuery.ToListAsync()).GroupBy(r => r.TrainerUserId).ToList();
        int AvailableOn(DateOnly d) => rulesByTrainer.Sum(g => BusinessReportRules.AvailableMinutes(g, d));

        // Trening w parze to dwie wizyty o tej samej godzinie — w grafiku liczymy go raz.
        var booked = visits.Where(v => v.Status != SessionStatus.Cancelled)
            .GroupBy(v => (v.TrainerUserId, v.StartTime))
            .Select(g => (Date: DateOnly.FromDateTime(g.Key.StartTime), Minutes: g.Max(v => v.Minutes)))
            .ToList();

        OccupancyDto Occupancy(DateOnly from, DateOnly to)
        {
            var available = 0;
            for (var d = from; d <= to; d = d.AddDays(1)) available += AvailableOn(d);
            var used = booked.Where(b => b.Date >= from && b.Date <= to).Sum(b => b.Minutes);
            return new OccupancyDto { From = from, To = to, AvailableMinutes = available, BookedMinutes = used, Percent = BusinessReportRules.Percent(used, available) };
        }

        var byWeekday = Enumerable.Range(0, 7).Select(i =>
        {
            int available = 0, used = 0;
            for (var d = today.AddDays(-56); d < today; d = d.AddDays(1))
            {
                if (((int)d.DayOfWeek + 6) % 7 != i) continue;
                available += AvailableOn(d);
                var day = d;
                used += booked.Where(b => b.Date == day).Sum(b => b.Minutes);
            }
            return new WeekdayOccupancyDto(DayNames[i], available, used, BusinessReportRules.Percent(used, available));
        }).ToList();

        // ── Prognoza ──
        var membershipsQuery = db.Memberships.AsNoTracking().Where(m => m.Status == MembershipStatus.Active && !m.CancelAtPeriodEnd);
        if (trainerUserId is not null) membershipsQuery = membershipsQuery.Where(m => m.Client.TrainerUserId == trainerUserId);
        var memberships = await membershipsQuery
            .Select(m => new { m.NextBillingDate, Price = m.PriceOverride ?? m.Plan.Price, m.Plan.PeriodMonths })
            .ToListAsync();
        var renewing = memberships.Where(m => m.NextBillingDate >= today && m.NextBillingDate <= today.AddDays(ForecastDays)).ToList();

        var standalone = packages.Where(p => !membershipPackageIds.Contains(p.Id)).ToList();
        var ending = standalone
            .Where(p => p.Status == PackageStatus.Active && p.TotalSessions > 0)
            .Where(p => p.TotalSessions - p.UsedSessions <= 2 || (p.ExpiresAt is { } e && e <= nowUtc.AddDays(ForecastDays)))
            .Select(p => new EndingPackageDto(p.ClientId, NameOf(p.ClientId), p.Name, Math.Max(0, p.TotalSessions - p.UsedSessions), p.ExpiresAt, p.TotalSessions * p.PricePerSession))
            .OrderBy(p => p.Remaining).ThenBy(p => p.ExpiresAt)
            .ToList();
        var repurchase = BusinessReportRules.RepurchaseRate(standalone
            .Where(p => p.PurchasedAt >= nowUtc.AddDays(-365))
            .Select(p => (p.ClientId, p.PurchasedAt, p.Status is PackageStatus.Depleted or PackageStatus.Expired || p.UsedSessions >= p.TotalSessions)));
        var endingValue = ending.Sum(p => p.Value);
        var activePackages = packages.Where(p => p.Status == PackageStatus.Active).ToList();

        var forecast = new ForecastDto
        {
            Days = ForecastDays,
            MembershipRenewals = renewing.Count,
            MembershipAmount = renewing.Sum(m => m.Price),
            MonthlyRecurring = Math.Round(memberships.Sum(m => m.Price / Math.Max(1, m.PeriodMonths)), 2),
            EndingPackages = ending.Count,
            EndingPackagesValue = endingValue,
            RepurchaseRatePercent = repurchase,
            ExpectedPackageAmount = Math.Round(endingValue * (repurchase ?? 0) / 100m, 2),
            PrepaidSessions = activePackages.Where(p => p.IsPaid).Sum(p => Math.Max(0, p.TotalSessions - p.UsedSessions)),
            PrepaidValue = activePackages.Where(p => p.IsPaid).Sum(p => Math.Max(0, p.TotalSessions - p.UsedSessions) * p.PricePerSession),
            UnpaidAmount = packages.Where(p => !p.IsPaid && !p.IsHidden).Sum(p => p.TotalSessions * p.PricePerSession),
            EndingList = ending.Take(8).ToList()
        };

        // ── Odwołania ──
        var inPeriod = visits.Where(v => v.StartTime >= now - period && v.StartTime < now && v.Status != SessionStatus.AwaitingPackage).ToList();
        static bool IsMissed(Visit v) => v.Status is SessionStatus.Cancelled or SessionStatus.NoShow;
        var missed = inPeriod.Where(IsMissed).ToList();
        var slots = inPeriod
            .GroupBy(v => (Day: ((int)v.StartTime.DayOfWeek + 6) % 7, v.StartTime.Hour))
            .Select(g => new { g.Key.Day, g.Key.Hour, Missed = g.Count(IsMissed), Total = g.Count() })
            .ToList();
        var hours = inPeriod.Select(v => v.StartTime.Hour).DefaultIfEmpty(7).ToList();
        int firstHour = Math.Min(hours.Min(), 7), lastHour = Math.Max(hours.Max(), 20);
        var heatmap = Enumerable.Range(0, 7)
            .Select(d => Enumerable.Range(firstHour, lastHour - firstHour + 1)
                .Select(h => slots.FirstOrDefault(s => s.Day == d && s.Hour == h)?.Missed ?? 0).ToArray())
            .ToArray();

        return new BusinessReportDto
        {
            PeriodDays = periodDays,
            ActiveClients = current.Count,
            ActiveClientsPrevious = previous.Count,
            NewClients = newClients,
            RetentionPercent = BusinessReportRules.Retention(previous, current),
            RetentionPercentPrevious = BusinessReportRules.Retention(beforePrevious, previous),
            LostClients = lost,
            RetentionByMonth = byMonth,

            Revenue = revenue,
            RevenuePrevious = revenuePrev,
            PayingClients = paying,
            AvgRevenuePerClient = paying > 0 ? Math.Round(revenue / paying, 2) : 0,
            AvgRevenuePerClientPrevious = payingPrev > 0 ? Math.Round(revenuePrev / payingPrev, 2) : 0,
            AvgMonthlyRevenuePerActiveClient = current.Count > 0 && months > 0 ? Math.Round(revenue / current.Count / (decimal)months, 2) : 0,
            TopClients = topClients,

            PastOccupancy = Occupancy(today.AddDays(-28), today.AddDays(-1)),
            UpcomingOccupancy = Occupancy(today, today.AddDays(13)),
            OccupancyByWeekday = byWeekday,

            Forecast = forecast,

            CancelledCount = missed.Count(v => v.Status == SessionStatus.Cancelled),
            NoShowCount = missed.Count(v => v.Status == SessionStatus.NoShow),
            LateCancelledCount = missed.Count(v => v.Late),
            CancellationRatePercent = BusinessReportRules.Percent(missed.Count, inPeriod.Count),
            TopCancelledSlots = slots.Where(s => s.Missed > 0)
                .OrderByDescending(s => s.Missed).ThenByDescending(s => (double)s.Missed / s.Total).Take(5)
                .Select(s => new CancellationSlotDto(DayNames[s.Day], s.Hour, s.Missed, s.Total, BusinessReportRules.Percent(s.Missed, s.Total)))
                .ToList(),
            CancellationHeatmap = heatmap,
            HeatmapFirstHour = firstHour,
            HeatmapLastHour = lastHour
        };
    }
}
