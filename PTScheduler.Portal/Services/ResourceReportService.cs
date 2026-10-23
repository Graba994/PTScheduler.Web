using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Raport zasobów z zapisanych próbek: serwer (pamięć, procesor, dyski i prognoza ich zapełnienia),
/// każda instancja względem swoich limitów, zapas na kolejnych trenerów i ostrzeżenia.
/// </summary>
public class ResourceReportService(IDbContextFactory<PortalDbContext> dbFactory)
{
    public enum Period { Day, Week, Month }

    public sealed record Point(DateTime At, double Value);
    public sealed record Warning(string Severity, string Text, int? TenantId = null);

    public sealed class HostView
    {
        public long? MemUsed { get; init; }
        public long? MemTotal { get; init; }
        public double? MemPct => MemUsed is { } u && MemTotal is > 0 ? u * 100.0 / MemTotal.Value : null;
        public double? Load1h { get; init; }
        public double? Cores { get; init; }
        public long? DiskUsed { get; init; }
        public long? DiskTotal { get; init; }
        public double? DiskPct => DiskUsed is { } u && DiskTotal is > 0 ? u * 100.0 / DiskTotal.Value : null;
        public double? DiskGrowthPerDay { get; init; }
        public double? DiskDaysLeft { get; init; }
        public long? BackupUsed { get; init; }
        public long? BackupTotal { get; init; }
        public double? BackupPct => BackupUsed is { } u && BackupTotal is > 0 ? u * 100.0 / BackupTotal.Value : null;
        public double? BackupDaysLeft { get; init; }
        public List<Point> Mem { get; init; } = [];
        public List<Point> Cpu { get; init; } = [];
        public List<Point> Disk { get; init; } = [];
    }

    public sealed class TenantRow
    {
        public int Id { get; init; }
        public string Slug { get; init; } = "";
        public string Company { get; init; } = "";
        public long? Mem { get; init; }
        public long? WebMem { get; init; }
        public long? DbMem { get; init; }
        public long? MemLimit { get; init; }
        public double? MemPct => Mem is { } m && MemLimit is > 0 ? m * 100.0 / MemLimit.Value : null;
        public double? CpuAvg { get; init; }
        public double? CpuPeak { get; init; }
        public double? CpuLimit { get; init; }
        public double? CpuPeakPct => CpuPeak is { } p && CpuLimit is > 0 ? p * 100 / CpuLimit.Value : null;
        public long? DbSize { get; init; }
        public long? Files { get; init; }
        public long? Disk => DbSize is null && Files is null ? null : (DbSize ?? 0) + (Files ?? 0);
        public double? DiskGrowthPerMonth { get; init; }
        public List<Point> MemSeries { get; init; } = [];
        public List<Point> DiskSeries { get; init; } = [];
        public string Tone { get; set; } = "ok";
        public List<string> Flags { get; } = [];
    }

    public sealed class Report
    {
        public Period Period { get; init; }
        public DateTime? LastSampleAt { get; init; }
        public HostView? Host { get; init; }
        public List<TenantRow> Tenants { get; init; } = [];
        public int ActiveTenants { get; init; }
        public long? AvgTenantMem { get; init; }
        public int? MoreTenants { get; init; }
        public long CommittedLimits { get; init; }
        public List<Warning> Warnings { get; } = [];
    }

    public static (TimeSpan Range, TimeSpan Bucket) Span(Period p) => p switch
    {
        Period.Day => (TimeSpan.FromDays(1), TimeSpan.FromMinutes(15)),
        Period.Month => (TimeSpan.FromDays(30), TimeSpan.FromHours(6)),
        _ => (TimeSpan.FromDays(7), TimeSpan.FromHours(1))
    };

    public async Task<Report> BuildAsync(Period period, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var (range, bucket) = Span(period);
        var from = now - (range > TimeSpan.FromDays(30) ? range : TimeSpan.FromDays(30));
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var samples = await db.ResourceSamples.AsNoTracking().Where(s => s.At >= from).OrderBy(s => s.At).ToListAsync(ct);
        var tenants = await db.Tenants.AsNoTracking().Where(t => t.Status == TenantStatus.Active).OrderBy(t => t.Slug).ToListAsync(ct);

        var hostSamples = samples.Where(s => s.TenantId is null).ToList();
        var lastHost = hostSamples.LastOrDefault();
        var hourAgo = now.AddHours(-1);
        HostView? host = null;
        if (lastHost is not null)
        {
            var (diskSlope, diskLeft) = Forecast(hostSamples.Where(s => s.At >= now.AddDays(-14)).Select(s => (s.At, s.DiskUsedBytes, s.DiskTotalBytes)));
            var (_, backupLeft) = Forecast(hostSamples.Where(s => s.At >= now.AddDays(-14)).Select(s => (s.At, s.BackupDiskUsedBytes, s.BackupDiskTotalBytes)));
            var inRange = hostSamples.Where(s => s.At >= now - range).ToList();
            host = new HostView
            {
                MemUsed = lastHost.MemoryBytes,
                MemTotal = lastHost.MemoryLimitBytes,
                Load1h = Avg(hostSamples.Where(s => s.At >= hourAgo).Select(s => s.CpuCores)),
                Cores = lastHost.CpuLimitCores,
                DiskUsed = lastHost.DiskUsedBytes,
                DiskTotal = lastHost.DiskTotalBytes,
                DiskGrowthPerDay = diskSlope,
                DiskDaysLeft = diskLeft,
                BackupUsed = lastHost.BackupDiskUsedBytes,
                BackupTotal = lastHost.BackupDiskTotalBytes,
                BackupDaysLeft = backupLeft,
                Mem = Series(inRange, bucket, s => s.MemoryBytes is { } u && s.MemoryLimitBytes is > 0 ? u * 100.0 / s.MemoryLimitBytes.Value : null),
                Cpu = Series(inRange, bucket, s => s.CpuCores is { } c && s.CpuLimitCores is > 0 ? c * 100 / s.CpuLimitCores.Value : null),
                Disk = Series(inRange, bucket, s => s.DiskUsedBytes is { } u && s.DiskTotalBytes is > 0 ? u * 100.0 / s.DiskTotalBytes.Value : null)
            };
        }

        var rows = new List<TenantRow>();
        foreach (var t in tenants)
        {
            var ts = samples.Where(s => s.TenantId == t.Id).ToList();
            var recent = ts.Where(s => s.At >= hourAgo).ToList();
            var last = recent.LastOrDefault();
            var disk = ts.Where(s => s.DbSizeBytes is not null || s.FilesBytes is not null).ToList();
            var lastDisk = disk.LastOrDefault();
            double? growth = null;
            if (disk.Count >= 2 && (lastDisk!.At - disk[0].At).TotalDays >= 1)
            {
                var delta = Total(lastDisk) - Total(disk[0]);
                growth = delta / (lastDisk.At - disk[0].At).TotalDays * 30;
            }
            var inRange = ts.Where(s => s.At >= now - range).ToList();
            rows.Add(new TenantRow
            {
                Id = t.Id,
                Slug = t.Slug,
                Company = t.CompanyName,
                Mem = recent.Count > 0 ? (long?)recent.Average(s => s.MemoryBytes ?? 0) : null,
                WebMem = last?.WebMemoryBytes,
                DbMem = last?.DbMemoryBytes,
                MemLimit = last?.MemoryLimitBytes is { } l && (lastHost?.MemoryLimitBytes is not { } total || l < total) ? l : null,
                CpuAvg = Avg(recent.Select(s => s.CpuCores)),
                CpuPeak = ts.Where(s => s.At >= now.AddDays(-1)).Select(s => s.CpuCores).Max(),
                CpuLimit = last?.CpuLimitCores,
                DbSize = lastDisk?.DbSizeBytes,
                Files = lastDisk?.FilesBytes,
                DiskGrowthPerMonth = growth,
                MemSeries = Series(inRange, bucket, s => s.MemoryBytes is { } m && s.MemoryLimitBytes is > 0 ? m * 100.0 / s.MemoryLimitBytes.Value : null),
                DiskSeries = Series(disk.Where(s => s.At >= now - range).ToList(), bucket, s => Total(s) / 1048576.0)
            });
        }

        var withMem = rows.Where(r => r.Mem is > 0).ToList();
        long? avg = withMem.Count > 0 ? (long)withMem.Average(r => r.Mem!.Value) : null;
        int? more = null;
        if (host?.MemTotal is { } memTotal && host.MemUsed is { } memUsed && avg is > 0)
            more = (int)Math.Max(0, (memTotal - memUsed - memTotal * 0.1) / avg.Value);

        var report = new Report
        {
            Period = period,
            LastSampleAt = samples.LastOrDefault()?.At,
            Host = host,
            Tenants = rows,
            ActiveTenants = tenants.Count,
            AvgTenantMem = avg,
            MoreTenants = more,
            CommittedLimits = rows.Sum(r => r.MemLimit ?? 0)
        };
        AddWarnings(report);
        return report;
    }

    private static void AddWarnings(Report r)
    {
        var h = r.Host;
        if (h is not null)
        {
            if (h.MemPct >= 90) r.Warnings.Add(new("danger", $"Pamięć serwera zajęta w {h.MemPct:0}% — instancje mogą zwalniać albo być zabijane przez system."));
            else if (h.MemPct >= 80) r.Warnings.Add(new("warn", $"Pamięć serwera zajęta w {h.MemPct:0}%."));
            if (h.DiskPct >= 90 || h.DiskDaysLeft < 14) r.Warnings.Add(new("danger", DiskText("Dysk Dockera", h.DiskPct, h.DiskDaysLeft)));
            else if (h.DiskPct >= 80 || h.DiskDaysLeft < 45) r.Warnings.Add(new("warn", DiskText("Dysk Dockera", h.DiskPct, h.DiskDaysLeft)));
            if (h.BackupPct >= 90 || h.BackupDaysLeft < 14) r.Warnings.Add(new("danger", DiskText("Dysk z kopiami", h.BackupPct, h.BackupDaysLeft)));
            else if (h.BackupPct >= 80 || h.BackupDaysLeft < 45) r.Warnings.Add(new("warn", DiskText("Dysk z kopiami", h.BackupPct, h.BackupDaysLeft)));
            if (h.Load1h is { } load && h.Cores is > 0 && load >= h.Cores) r.Warnings.Add(new("warn", $"Procesor przeciążony: średnie obciążenie {load:0.0} przy {h.Cores:0} rdzeniach."));
        }
        if (r.MoreTenants is <= 2) r.Warnings.Add(new("warn", r.MoreTenants == 0
            ? "Na tym serwerze nie zmieści się już kolejny trener — dołóż pamięci albo drugi serwer."
            : $"Zmieszczą się jeszcze tylko {r.MoreTenants} {PTScheduler.Portal.Components.Shared.PanelUi.Plural(r.MoreTenants.Value, "trener", "trenerów", "trenerów")}."));

        foreach (var t in r.Tenants)
        {
            if (t.MemPct >= 95) { t.Tone = "danger"; t.Flags.Add("pamięć prawie pełna — może się restartować"); r.Warnings.Add(new("danger", $"{t.Slug}: pamięć zajęta w {t.MemPct:0}% limitu — aplikacja może się restartować. Podnieś limit albo sprawdź logi.", t.Id)); }
            else if (t.MemPct >= 85) { t.Tone = "warn"; t.Flags.Add("blisko limitu pamięci"); r.Warnings.Add(new("warn", $"{t.Slug}: pamięć zajęta w {t.MemPct:0}% limitu.", t.Id)); }
            if (t.CpuPeakPct >= 90) { if (t.Tone == "ok") t.Tone = "warn"; t.Flags.Add("procesor na granicy limitu"); }
            if (t.DiskGrowthPerMonth is > 1024L * 1024 * 1024) { if (t.Tone == "ok") t.Tone = "warn"; t.Flags.Add("szybko rośnie na dysku"); }
        }
    }

    private static string DiskText(string name, double? pct, double? daysLeft) =>
        $"{name} zajęty w {pct:0}%" + (daysLeft is { } d ? d < 1 ? " — zapełni się w ciągu doby." : $" — przy obecnym tempie zapełni się za ok. {d:0} dni." : ".");

    private static long Total(ResourceSample s) => (s.DbSizeBytes ?? 0) + (s.FilesBytes ?? 0);

    private static double? Avg(IEnumerable<double?> values)
    {
        var list = values.Where(v => v is not null).Select(v => v!.Value).ToList();
        return list.Count > 0 ? list.Average() : null;
    }

    /// <summary>Średnie w przedziałach czasu — wykres nie zależy od tego, ile próbek akurat zapisano.</summary>
    private static List<Point> Series(List<ResourceSample> samples, TimeSpan bucket, Func<ResourceSample, double?> value) =>
        samples.Select(s => (Bucket: new DateTime(s.At.Ticks - s.At.Ticks % bucket.Ticks, DateTimeKind.Utc), V: value(s)))
            .Where(x => x.V is not null)
            .GroupBy(x => x.Bucket)
            .Select(g => new Point(g.Key, g.Average(x => x.V!.Value)))
            .OrderBy(p => p.At)
            .ToList();

    /// <summary>Tempo wzrostu (bajty/dobę, regresja liniowa z 14 dni) i za ile dni dysk się zapełni.</summary>
    private static (double? PerDay, double? DaysLeft) Forecast(IEnumerable<(DateTime At, long? Used, long? Total)> points)
    {
        var list = points.Where(p => p.Used is not null && p.Total is > 0).ToList();
        if (list.Count < 10 || (list[^1].At - list[0].At).TotalDays < 2) return (null, null);
        var t0 = list[0].At;
        var xs = list.Select(p => (p.At - t0).TotalDays).ToList();
        var ys = list.Select(p => (double)p.Used!.Value).ToList();
        var mx = xs.Average();
        var my = ys.Average();
        var num = xs.Zip(ys, (x, y) => (x - mx) * (y - my)).Sum();
        var den = xs.Sum(x => (x - mx) * (x - mx));
        if (den <= 0) return (null, null);
        var slope = num / den;
        var free = list[^1].Total!.Value - list[^1].Used!.Value;
        return (slope, slope > 0 ? free / slope : null);
    }
}
