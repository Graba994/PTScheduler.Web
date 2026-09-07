using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class DataExportService(IDbContextFactory<ApplicationDbContext> dbFactory) : IDataExportService
{
    public async Task<byte[]> ExportClientsCsvAsync(string? trainerUserId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var q = db.Clients.AsNoTracking().AsQueryable();
        if (trainerUserId is not null)
            q = q.Where(c => c.TrainerUserId == trainerUserId);

        var clients = await q.OrderBy(c => c.LastName).ThenBy(c => c.FirstName).ToListAsync();

        var users = await db.Users.AsNoTracking()
            .Where(u => clients.Select(c => c.ApplicationUserId).Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? "");

        var sb = new StringBuilder();
        sb.AppendLine("Id;Imię;Nazwisko;Email;Telefon;Cel treningowy;Data urodzenia;Status;Utworzono;Samorezerwacja");
        foreach (var c in clients)
        {
            users.TryGetValue(c.ApplicationUserId, out var email);
            sb.Append(c.Id).Append(';');
            sb.Append(Esc(c.FirstName)).Append(';');
            sb.Append(Esc(c.LastName)).Append(';');
            sb.Append(Esc(email ?? "")).Append(';');
            sb.Append(Esc(c.Phone)).Append(';');
            sb.Append(Esc(c.TrainingGoal)).Append(';');
            sb.Append(c.DateOfBirth?.ToString("yyyy-MM-dd") ?? "").Append(';');
            sb.Append(c.Status).Append(';');
            sb.Append(c.CreatedAt.ToString("yyyy-MM-dd HH:mm")).Append(';');
            sb.AppendLine(c.AllowSelfBooking ? "Tak" : "Nie");
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    public async Task<byte[]> ExportSessionsCsvAsync(DateTime from, DateTime to, string? trainerUserId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var q = db.Sessions.AsNoTracking()
            .Include(s => s.Client)
            .Include(s => s.SessionType)
            .Where(s => s.StartTime >= from && s.StartTime < to);
        if (trainerUserId is not null)
            q = q.Where(s => s.TrainerUserId == trainerUserId);

        var sessions = await q.OrderBy(s => s.StartTime).ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("Id;Klient;Typ sesji;Data;Godzina;Czas (min);Status;Powód anulowania;Notatki;Google Meet");
        foreach (var s in sessions)
        {
            sb.Append(s.Id).Append(';');
            sb.Append(Esc($"{s.Client.FirstName} {s.Client.LastName}".Trim())).Append(';');
            sb.Append(Esc(s.SessionType.Name)).Append(';');
            sb.Append(s.StartTime.ToString("yyyy-MM-dd")).Append(';');
            sb.Append(s.StartTime.ToString("HH:mm")).Append(';');
            sb.Append(s.SessionType.DurationMinutes).Append(';');
            sb.Append(StatusPl(s.Status)).Append(';');
            sb.Append(Esc(s.CancellationReason)).Append(';');
            sb.Append(Esc(s.Notes)).Append(';');
            sb.AppendLine(s.MeetingUrl ?? "");
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    public async Task<byte[]> ExportPackagesCsvAsync(string? trainerUserId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var q = db.SessionPackages.AsNoTracking().Include(p => p.Client).AsQueryable();
        if (trainerUserId is not null)
            q = q.Where(p => p.Client.TrainerUserId == trainerUserId);

        var packages = await q.OrderByDescending(p => p.PurchasedAt).ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("Id;Klient;Nazwa pakietu;Sesje ogółem;Wykorzystane;Pozostałe;Cena;Opłacony;Wygasa;Zakupiono;Status");
        foreach (var p in packages)
        {
            var totalPrice = p.PricePerSession * p.TotalSessions;
            sb.Append(p.Id).Append(';');
            sb.Append(Esc($"{p.Client.FirstName} {p.Client.LastName}".Trim())).Append(';');
            sb.Append(Esc(p.Name)).Append(';');
            sb.Append(p.TotalSessions).Append(';');
            sb.Append(p.UsedSessions).Append(';');
            sb.Append(p.TotalSessions - p.UsedSessions).Append(';');
            sb.Append(totalPrice.ToString("0.00", CultureInfo.InvariantCulture)).Append(';');
            sb.Append(p.IsPaid ? "Tak" : "Nie").Append(';');
            sb.Append(p.ExpiresAt?.ToString("yyyy-MM-dd") ?? "").Append(';');
            sb.Append(p.PurchasedAt.ToString("yyyy-MM-dd")).Append(';');
            sb.AppendLine(PackageStatusPl(p.Status));
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    public async Task<byte[]> ExportMeasurementsCsvAsync(string? trainerUserId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var q = db.BodyMeasurements.AsNoTracking().Include(m => m.Client).AsQueryable();
        if (trainerUserId is not null)
            q = q.Where(m => m.Client.TrainerUserId == trainerUserId);

        var measurements = await q.OrderByDescending(m => m.MeasurementDate).ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("Id;Klient;Data;Waga (kg);Tkanka tłuszczowa (%);Klatka (cm);Talia (cm);Biodra (cm);Udo (cm);Ramię (cm);Notatki");
        foreach (var m in measurements)
        {
            sb.Append(m.Id).Append(';');
            sb.Append(Esc($"{m.Client.FirstName} {m.Client.LastName}".Trim())).Append(';');
            sb.Append(m.MeasurementDate.ToString("yyyy-MM-dd")).Append(';');
            sb.Append(Dec(m.WeightKg)).Append(';');
            sb.Append(Dec(m.BodyFatPercent)).Append(';');
            sb.Append(Dec(m.ChestCm)).Append(';');
            sb.Append(Dec(m.WaistCm)).Append(';');
            sb.Append(Dec(m.HipsCm)).Append(';');
            sb.Append(Dec(m.ThighCm)).Append(';');
            sb.Append(Dec(m.ArmCm)).Append(';');
            sb.AppendLine(Esc(m.Notes));
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    public async Task<byte[]> ExportOrdersCsvAsync(DateTime from, DateTime to)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.CreatedAt >= from && o.CreatedAt < to)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        var userIds = orders.Select(o => o.ApplicationUserId).Distinct().ToList();
        var emails = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? "");

        var sb = new StringBuilder();
        sb.AppendLine("Id;Email kupującego;Typ;Kwota;Waluta;Kupon;Rabat;Kwota oryginalna;Status;Provider;Utworzono;Opłacono");
        foreach (var o in orders)
        {
            emails.TryGetValue(o.ApplicationUserId, out var email);
            sb.Append(o.Id).Append(';');
            sb.Append(Esc(email ?? "")).Append(';');
            sb.Append(o.Kind).Append(';');
            sb.Append(o.Amount.ToString("0.00", CultureInfo.InvariantCulture)).Append(';');
            sb.Append(o.Currency).Append(';');
            sb.Append(Esc(o.CouponCode)).Append(';');
            sb.Append(o.DiscountAmount?.ToString("0.00", CultureInfo.InvariantCulture) ?? "").Append(';');
            sb.Append(o.OriginalAmount?.ToString("0.00", CultureInfo.InvariantCulture) ?? "").Append(';');
            sb.Append(o.Status).Append(';');
            sb.Append(o.Provider).Append(';');
            sb.Append(o.CreatedAt.ToString("yyyy-MM-dd HH:mm")).Append(';');
            sb.AppendLine(o.PaidAt?.ToString("yyyy-MM-dd HH:mm") ?? "");
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string Esc(string? v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        if (v.Contains(';') || v.Contains('"') || v.Contains('\n'))
            return $"\"{v.Replace("\"", "\"\"")}\"";
        return v;
    }

    private static string Dec(decimal? v) =>
        v?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";

    private static string PackageStatusPl(Domain.Enums.PackageStatus s) => s switch
    {
        Domain.Enums.PackageStatus.Active => "Aktywny",
        Domain.Enums.PackageStatus.Depleted => "Wykorzystany",
        Domain.Enums.PackageStatus.Expired => "Wygasły",
        Domain.Enums.PackageStatus.Cancelled => "Anulowany",
        _ => s.ToString()
    };

    private static string StatusPl(Domain.Enums.SessionStatus s) => s switch
    {
        Domain.Enums.SessionStatus.Scheduled => "Zaplanowana",
        Domain.Enums.SessionStatus.Completed => "Ukończona",
        Domain.Enums.SessionStatus.Cancelled => "Anulowana",
        Domain.Enums.SessionStatus.NoShow => "Nieobecność",
        Domain.Enums.SessionStatus.AwaitingPackage => "Bez pakietu",
        _ => s.ToString()
    };
}
