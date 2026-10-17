using Microsoft.EntityFrameworkCore;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;

namespace PTScheduler.Web.Services;

public sealed record TeamMember(string Id, string Name, string Role, int WeekDone, int WeekPlanned, DateTime? Next);

public sealed record AttentionItem(string Icon, string Tone, string Text, string Href);

/// <summary>Liczby dla „Centrum studia” — dashboard administratora (i konta technicznego).</summary>
public sealed class AdminOverview
{
    public int ActiveClients { get; init; }
    public int NewClients30 { get; init; }
    public int PendingClients { get; init; }
    public int WeekDone { get; init; }
    public int WeekPlanned { get; init; }
    public int TodayCount { get; init; }
    public int PackagesSoldMonth { get; init; }
    public decimal PackagesValueMonth { get; init; }
    public decimal PackagesValuePrevMonth { get; init; }
    public decimal OnlineRevenueMonth { get; init; }
    public List<TeamMember> Team { get; init; } = [];
    public List<AttentionItem> Attention { get; init; } = [];
    public Dictionary<string, int> UsersByRole { get; init; } = [];
}

/// <summary>Stan techniczny instalacji — tylko dla konta technicznego (Root).</summary>
public sealed record TechInfo(
    string Version, bool Managed, string? Slug, string PlanName, int MaxClients, int MaxTrainers,
    int Users, int AuditWarnings7d, DateTime? LastAuditAt, bool SwitchingEnabled);

public sealed class AdminDashboardService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    EntitlementService entitlements,
    AccountSwitchService switcher)
{
    public async Task<AdminOverview> GetOverviewAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var today = StudioClock.Today;
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var weekEnd = weekStart.AddDays(7);
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var prevMonthStart = monthStart.AddMonths(-1);
        var monthStartUtc = DateTime.SpecifyKind(monthStart, DateTimeKind.Utc);
        var prevMonthStartUtc = DateTime.SpecifyKind(prevMonthStart, DateTimeKind.Utc);
        var since30 = DateTime.UtcNow.AddDays(-30);

        var activeClients = await db.Clients.CountAsync(c => c.Status == ClientStatus.Active);
        var newClients = await db.Clients.CountAsync(c => c.CreatedAt >= since30);
        var pending = await db.Clients.CountAsync(c => c.Status == ClientStatus.Pending);

        var week = await db.Sessions.AsNoTracking()
            .Where(s => s.StartTime >= weekStart && s.StartTime < weekEnd
                && s.Status != SessionStatus.Cancelled && !s.SharesPackageSlot)
            .Select(s => new { s.TrainerUserId, s.StartTime, s.Status }).ToListAsync();
        var upcoming = await db.Sessions.AsNoTracking()
            .Where(s => s.StartTime >= StudioClock.Now && s.Status == SessionStatus.Scheduled)
            .GroupBy(s => s.TrainerUserId)
            .Select(g => new { TrainerUserId = g.Key, Next = g.Min(s => s.StartTime) }).ToListAsync();

        var packages = await db.SessionPackages.AsNoTracking()
            .Where(p => p.PurchasedAt >= prevMonthStartUtc)
            .Select(p => new { p.PurchasedAt, Value = p.TotalSessions * p.PricePerSession }).ToListAsync();
        var online = await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Paid && (o.PaidAt ?? o.CreatedAt) >= monthStartUtc)
            .SumAsync(o => (decimal?)o.Amount) ?? 0;

        // Zespół: trenerzy i asystenci z obłożeniem w tym tygodniu.
        var staff = await (from u in db.Users.AsNoTracking()
                           join ur in db.UserRoles on u.Id equals ur.UserId
                           join r in db.Roles on ur.RoleId equals r.Id
                           where r.Name == Roles.Trainer || r.Name == Roles.Subordinate
                           select new { u.Id, u.FirstName, u.LastName, u.Email, Role = r.Name! }).ToListAsync();
        var team = staff.GroupBy(s => s.Id).Select(g =>
        {
            var s = g.First();
            var mine = week.Where(w => w.TrainerUserId == s.Id).ToList();
            var name = $"{s.FirstName} {s.LastName}".Trim();
            return new TeamMember(s.Id, name.Length > 0 ? name : s.Email ?? "", s.Role,
                mine.Count(w => w.Status == SessionStatus.Completed), mine.Count,
                upcoming.FirstOrDefault(u => u.TrainerUserId == s.Id)?.Next);
        }).OrderByDescending(t => t.WeekPlanned).ThenBy(t => t.Name).ToList();

        // Wymaga uwagi — tylko rzeczy, które da się od razu załatwić jednym kliknięciem.
        var attention = new List<AttentionItem>();
        if (pending > 0)
            attention.Add(new("bi-person-fill-exclamation", "warn", $"{pending} {Pl(pending, "klient czeka", "klientów czeka", "klientów czeka")} na zatwierdzenie", "/clients"));
        var unmarked = await db.Sessions.CountAsync(s => s.Status == SessionStatus.Scheduled && s.StartTime < StudioClock.Now && s.StartTime >= today.AddDays(-7));
        if (unmarked > 0)
            attention.Add(new("bi-calendar-x", "warn", $"{unmarked} {Pl(unmarked, "wizyta bez statusu", "wizyty bez statusu", "wizyt bez statusu")} z ostatnich 7 dni", "/sessions"));
        var unpaid = await db.SessionPackages.CountAsync(p => p.Status == PackageStatus.Active && !p.IsPaid);
        if (unpaid > 0)
            attention.Add(new("bi-cash-coin", "danger", $"{unpaid} {Pl(unpaid, "nieopłacony pakiet", "nieopłacone pakiety", "nieopłaconych pakietów")}", "/finance"));
        var soonUtc = DateTime.UtcNow.AddDays(7);
        var expiring = await db.SessionPackages.CountAsync(p => p.Status == PackageStatus.Active && p.ExpiresAt != null && p.ExpiresAt <= soonUtc);
        if (expiring > 0)
            attention.Add(new("bi-hourglass-split", "info", $"{expiring} {Pl(expiring, "pakiet wygasa", "pakiety wygasają", "pakietów wygasa")} w ciągu 7 dni", "/clients"));
        if (!staff.Any(s => s.Role == Roles.Trainer))
            attention.Add(new("bi-person-badge", "warn", "Nie ma jeszcze profilu trenera — klienci nie mogą się zapisać online", "/admin/users"));

        var roles = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
                           group ur by r.Name into g select new { Role = g.Key!, Count = g.Count() }).ToListAsync();

        return new AdminOverview
        {
            ActiveClients = activeClients,
            NewClients30 = newClients,
            PendingClients = pending,
            WeekDone = week.Count(w => w.Status == SessionStatus.Completed),
            WeekPlanned = week.Count,
            TodayCount = week.Count(w => w.StartTime.Date == today),
            PackagesSoldMonth = packages.Count(p => p.PurchasedAt >= monthStartUtc),
            PackagesValueMonth = packages.Where(p => p.PurchasedAt >= monthStartUtc).Sum(p => p.Value),
            PackagesValuePrevMonth = packages.Where(p => p.PurchasedAt < monthStartUtc).Sum(p => p.Value),
            OnlineRevenueMonth = online,
            Team = team,
            Attention = attention,
            UsersByRole = roles.ToDictionary(r => r.Role, r => r.Count)
        };
    }

    public async Task<TechInfo> GetTechAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var since = DateTime.UtcNow.AddDays(-7);
        var warnings = await db.AuditLogs.CountAsync(a => a.Timestamp >= since && a.Severity >= AuditSeverity.Warning);
        var last = await db.AuditLogs.OrderByDescending(a => a.Timestamp).Select(a => (DateTime?)a.Timestamp).FirstOrDefaultAsync();
        var version = typeof(AdminDashboardService).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "?";
        var plan = entitlements.Current;
        return new TechInfo(
            version.Split('+') is [var v, var sha, ..] ? $"{v} ({sha[..Math.Min(7, sha.Length)]})" : version,
            PlatformConnection.IsManaged, PlatformConnection.Slug, plan.Name, plan.MaxClients, plan.MaxTrainers,
            await db.Users.CountAsync(), warnings, last, switcher.Enabled);
    }

    public static string Pl(int n, string one, string few, string many) => SetupGuide.Pl(n, one, few, many);
}
