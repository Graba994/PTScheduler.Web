using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PTScheduler.Infrastructure.Services;

public class ClientReportService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> userManager,
    IBrandingService brandingService,
    IWebRootPathProvider webRootPathProvider) : IClientReportService
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public async Task<(byte[] Bytes, string FileName)> GenerateMonthlyReportAsync(int clientId, int year, int month)
    {
        await using var db = dbFactory.CreateDbContext();

        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId)
            ?? throw new InvalidOperationException($"Klient {clientId} nie istnieje.");

        var user = await userManager.FindByIdAsync(client.ApplicationUserId);
        var branding = await brandingService.GetAsync();

        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1);

        var sessions = await db.Sessions
            .AsNoTracking()
            .Include(s => s.SessionType)
            .Where(s => s.ClientId == clientId && s.StartTime >= monthStart && s.StartTime < monthEnd)
            .OrderBy(s => s.StartTime)
            .ToListAsync();

        // Resolve trainer names for sessions
        var trainerIds = sessions.Select(s => s.TrainerUserId).Distinct().ToList();
        var trainers = await userManager.Users
            .Where(u => trainerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => ResolveName(u));

        var measurements = await db.BodyMeasurements
            .AsNoTracking()
            .Where(m => m.ClientId == clientId && m.MeasurementDate >= DateOnly.FromDateTime(monthStart) && m.MeasurementDate < DateOnly.FromDateTime(monthEnd))
            .OrderBy(m => m.MeasurementDate)
            .ToListAsync();

        // For trend, also pull the last measurement BEFORE the period to compare against
        var prevMeasurement = await db.BodyMeasurements
            .AsNoTracking()
            .Where(m => m.ClientId == clientId && m.MeasurementDate < DateOnly.FromDateTime(monthStart))
            .OrderByDescending(m => m.MeasurementDate)
            .FirstOrDefaultAsync();

        var notes = await db.TrainerNotes
            .AsNoTracking()
            .Where(n => n.ClientId == clientId && n.CreatedAt >= monthStart && n.CreatedAt < monthEnd)
            .OrderBy(n => n.CreatedAt)
            .ToListAsync();

        // Resolve logo path on disk for embedding
        byte[]? logoBytes = null;
        try
        {
            if (!string.IsNullOrEmpty(branding.LogoPath))
            {
                var rel = branding.LogoPath.TrimStart('/');
                var abs = Path.Combine(webRootPathProvider.WebRootPath, rel);
                if (File.Exists(abs)) logoBytes = await File.ReadAllBytesAsync(abs);
            }
        }
        catch { /* best-effort, ignore */ }

        var data = new ReportData(
            CompanyName: branding.CompanyName ?? "PTScheduler",
            LogoBytes: logoBytes,
            ClientFullName: $"{client.FirstName} {client.LastName}".Trim(),
            ClientEmail: user?.Email ?? "",
            ClientPhone: client.Phone,
            ClientGoal: client.TrainingGoal,
            Year: year,
            Month: month,
            Sessions: sessions,
            TrainerNames: trainers,
            Measurements: measurements,
            PreviousMeasurement: prevMeasurement,
            Notes: notes
        );

        var pdfBytes = Document.Create(c => Compose(c, data)).GeneratePdf();
        var fileName = $"Raport_{Slug(data.ClientFullName)}_{year:D4}-{month:D2}.pdf";
        return (pdfBytes, fileName);
    }

    // ---- composition ----

    private record ReportData(
        string CompanyName,
        byte[]? LogoBytes,
        string ClientFullName,
        string ClientEmail,
        string? ClientPhone,
        string? ClientGoal,
        int Year,
        int Month,
        List<Session> Sessions,
        Dictionary<string, string> TrainerNames,
        List<BodyMeasurement> Measurements,
        BodyMeasurement? PreviousMeasurement,
        List<TrainerNote> Notes);

    private static void Compose(IDocumentContainer container, ReportData d)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.5f, Unit.Centimetre);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(t => t.FontSize(10).FontFamily("Lato"));

            page.Header().Element(c => Header(c, d));
            page.Content().PaddingVertical(0.6f, Unit.Centimetre).Element(c => Body(c, d));
            page.Footer().Element(c => Footer(c, d));
        });
    }

    private static void Header(IContainer c, ReportData d)
    {
        c.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                if (d.LogoBytes is not null)
                {
                    col.Item().Height(40).Image(d.LogoBytes).FitArea();
                    col.Item().PaddingTop(4).Text(d.CompanyName).FontSize(11).SemiBold();
                }
                else
                {
                    col.Item().Text(d.CompanyName).FontSize(16).SemiBold().FontColor(Colors.Blue.Darken2);
                }
            });

            row.ConstantItem(220).AlignRight().Column(col =>
            {
                col.Item().Text("RAPORT MIESIĘCZNY").FontSize(9).LetterSpacing(0.1f).FontColor(Colors.Grey.Darken1);
                col.Item().PaddingTop(2).Text(MonthYearLabel(d.Year, d.Month)).FontSize(20).Bold();
                col.Item().PaddingTop(2).Text(d.ClientFullName).FontSize(11).FontColor(Colors.Grey.Darken2);
            });
        });
    }

    private static void Body(IContainer c, ReportData d)
    {
        c.Column(col =>
        {
            col.Spacing(18);

            // Client info card
            col.Item().Element(c => ClientCard(c, d));

            // Stats
            col.Item().Element(c => StatsRow(c, d));

            // Sessions
            col.Item().Element(c => SessionsSection(c, d));

            // Measurements
            if (d.Measurements.Count > 0 || d.PreviousMeasurement is not null)
                col.Item().Element(c => MeasurementsSection(c, d));

            // Notes
            if (d.Notes.Count > 0)
                col.Item().Element(c => NotesSection(c, d));
        });
    }

    private static void ClientCard(IContainer c, ReportData d) =>
        c.Background(Colors.Grey.Lighten4).Padding(12).Column(col =>
        {
            col.Item().Text(d.ClientFullName).FontSize(14).SemiBold();

            col.Item().PaddingTop(2).Row(row =>
            {
                if (!string.IsNullOrEmpty(d.ClientEmail))
                    row.AutoItem().PaddingRight(12).Text(t =>
                    {
                        t.Span("✉ ").FontColor(Colors.Grey.Darken1);
                        t.Span(d.ClientEmail).FontSize(9.5f);
                    });
                if (!string.IsNullOrEmpty(d.ClientPhone))
                    row.AutoItem().Text(t =>
                    {
                        t.Span("☎ ").FontColor(Colors.Grey.Darken1);
                        t.Span(d.ClientPhone!).FontSize(9.5f);
                    });
                row.RelativeItem();
            });

            if (!string.IsNullOrWhiteSpace(d.ClientGoal))
            {
                col.Item().PaddingTop(4).Text(t =>
                {
                    t.Span("Cel: ").FontColor(Colors.Grey.Darken1).SemiBold();
                    t.Span(d.ClientGoal!).FontSize(9.5f);
                });
            }
        });

    private static void StatsRow(IContainer c, ReportData d)
    {
        var total = d.Sessions.Count;
        var completed = d.Sessions.Count(s => s.Status == SessionStatus.Completed);
        var cancelled = d.Sessions.Count(s => s.Status is SessionStatus.Cancelled or SessionStatus.NoShow);
        var totalMinutes = d.Sessions.Where(s => s.Status == SessionStatus.Completed).Sum(s => s.SessionType.DurationMinutes);

        c.Row(row =>
        {
            row.RelativeItem().Element(c => StatBox(c, total.ToString(), "Wszystkich", Colors.Blue.Darken2));
            row.RelativeItem().PaddingLeft(8).Element(c => StatBox(c, completed.ToString(), "Ukończone", Colors.Green.Darken1));
            row.RelativeItem().PaddingLeft(8).Element(c => StatBox(c, cancelled.ToString(), "Anulowane / NoShow", Colors.Red.Darken1));
            row.RelativeItem().PaddingLeft(8).Element(c => StatBox(c, totalMinutes.ToString(), "Min. treningu", Colors.Grey.Darken3));
        });
    }

    private static void StatBox(IContainer c, string value, string label, string accentColor) =>
        c.Border(0.6f).BorderColor(Colors.Grey.Lighten2).Background(Colors.White).Padding(10).Column(col =>
        {
            col.Item().AlignCenter().Text(value).FontSize(22).Bold().FontColor(accentColor);
            col.Item().AlignCenter().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1).LetterSpacing(0.06f);
        });

    private static void SessionsSection(IContainer c, ReportData d) =>
        c.Column(col =>
        {
            col.Item().PaddingBottom(6).Text("TRENINGI W TYM MIESIĄCU").FontSize(10).SemiBold().LetterSpacing(0.08f).FontColor(Colors.Grey.Darken2);

            if (d.Sessions.Count == 0)
            {
                col.Item().Background(Colors.Grey.Lighten4).Padding(12).AlignCenter()
                    .Text("Brak treningów w tym miesiącu.").FontColor(Colors.Grey.Darken1).Italic();
                return;
            }

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(60);  // date
                    c.RelativeColumn(2);   // type
                    c.ConstantColumn(50);  // duration
                    c.ConstantColumn(70);  // status
                    c.RelativeColumn(2);   // trainer
                });

                table.Header(h =>
                {
                    h.Cell().Element(HeaderCell).Text("Data");
                    h.Cell().Element(HeaderCell).Text("Typ sesji");
                    h.Cell().Element(HeaderCell).AlignRight().Text("Czas");
                    h.Cell().Element(HeaderCell).Text("Status");
                    h.Cell().Element(HeaderCell).Text("Trener");
                });

                foreach (var s in d.Sessions)
                {
                    table.Cell().Element(BodyCell).Text(s.StartTime.ToString("dd.MM HH:mm")).FontSize(9.5f);
                    table.Cell().Element(BodyCell).Text(s.SessionType?.Name ?? "—").FontSize(9.5f);
                    table.Cell().Element(BodyCell).AlignRight().Text($"{s.SessionType?.DurationMinutes ?? 0}'").FontSize(9.5f);
                    table.Cell().Element(BodyCell).Text(StatusLabel(s.Status)).FontSize(9.5f).FontColor(StatusColor(s.Status));
                    table.Cell().Element(BodyCell).Text(d.TrainerNames.GetValueOrDefault(s.TrainerUserId, "—")).FontSize(9.5f);
                }

                static IContainer HeaderCell(IContainer c) =>
                    c.DefaultTextStyle(x => x.SemiBold().FontSize(8).FontColor(Colors.Grey.Darken1).LetterSpacing(0.04f))
                     .BorderBottom(0.6f).BorderColor(Colors.Grey.Lighten1).PaddingVertical(5).PaddingHorizontal(2);

                static IContainer BodyCell(IContainer c) =>
                    c.BorderBottom(0.4f).BorderColor(Colors.Grey.Lighten3).PaddingVertical(5).PaddingHorizontal(2);
            });
        });

    private static void MeasurementsSection(IContainer c, ReportData d)
    {
        var first = d.PreviousMeasurement ?? d.Measurements.FirstOrDefault();
        var last = d.Measurements.LastOrDefault() ?? first;
        if (first is null || last is null) return;

        c.Column(col =>
        {
            col.Item().PaddingBottom(6).Text("POMIARY CIAŁA").FontSize(10).SemiBold().LetterSpacing(0.08f).FontColor(Colors.Grey.Darken2);

            // Comparison line of "Pierwsze (data) → Ostatnie (data)"
            var firstLabel = first.MeasurementDate.ToString("dd.MM.yyyy");
            var lastLabel = last.MeasurementDate.ToString("dd.MM.yyyy");
            col.Item().PaddingBottom(8).Text(t =>
            {
                t.Span("Porównanie ").FontColor(Colors.Grey.Darken1);
                t.Span(firstLabel).SemiBold();
                t.Span(" → ").FontColor(Colors.Grey.Darken1);
                t.Span(lastLabel).SemiBold();
                if (d.PreviousMeasurement is not null)
                    t.Span("  (poprzedni pomiar przed okresem raportu)").FontSize(8).FontColor(Colors.Grey.Darken1).Italic();
            });

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2);
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                });

                table.Header(h =>
                {
                    h.Cell().Element(HeaderCell).Text("Wskaźnik");
                    h.Cell().Element(HeaderCell).AlignRight().Text("Pierwsze");
                    h.Cell().Element(HeaderCell).AlignRight().Text("Ostatnie");
                    h.Cell().Element(HeaderCell).AlignRight().Text("Zmiana");
                });

                MeasRow(table, "Waga", first.WeightKg, last.WeightKg, "kg", upIsGood: false);
                MeasRow(table, "Tkanka tłuszczowa", first.BodyFatPercent, last.BodyFatPercent, "%", upIsGood: false);
                MeasRow(table, "Talia", first.WaistCm, last.WaistCm, "cm", upIsGood: false);
                MeasRow(table, "Klatka piersiowa", first.ChestCm, last.ChestCm, "cm", upIsGood: true);
                MeasRow(table, "Biodra", first.HipsCm, last.HipsCm, "cm", upIsGood: false);
                MeasRow(table, "Udo", first.ThighCm, last.ThighCm, "cm", upIsGood: true);
                MeasRow(table, "Ramię", first.ArmCm, last.ArmCm, "cm", upIsGood: true);

                static IContainer HeaderCell(IContainer c) =>
                    c.DefaultTextStyle(x => x.SemiBold().FontSize(8).FontColor(Colors.Grey.Darken1).LetterSpacing(0.04f))
                     .BorderBottom(0.6f).BorderColor(Colors.Grey.Lighten1).PaddingVertical(5).PaddingHorizontal(2);
            });
        });
    }

    private static void MeasRow(QuestPDF.Fluent.TableDescriptor t, string label, decimal? a, decimal? b, string unit, bool upIsGood)
    {
        IContainer Cell(IContainer c) => c.BorderBottom(0.4f).BorderColor(Colors.Grey.Lighten3).PaddingVertical(5).PaddingHorizontal(2);

        t.Cell().Element(Cell).Text(label).FontSize(9.5f);
        t.Cell().Element(Cell).AlignRight().Text(a is null ? "—" : $"{a:0.##} {unit}").FontSize(9.5f);
        t.Cell().Element(Cell).AlignRight().Text(b is null ? "—" : $"{b:0.##} {unit}").FontSize(9.5f);

        if (a is null || b is null || a == b)
        {
            t.Cell().Element(Cell).AlignRight().Text("—").FontSize(9.5f).FontColor(Colors.Grey.Darken1);
        }
        else
        {
            var delta = b.Value - a.Value;
            var positive = delta > 0;
            var isGood = upIsGood ? positive : !positive;
            var color = isGood ? Colors.Green.Darken1 : Colors.Red.Darken1;
            var arrow = positive ? "↑" : "↓";
            var sign = positive ? "+" : "";
            t.Cell().Element(Cell).AlignRight().Text($"{arrow} {sign}{delta:0.##} {unit}").FontSize(9.5f).FontColor(color).SemiBold();
        }
    }

    private static void NotesSection(IContainer c, ReportData d) =>
        c.Column(col =>
        {
            col.Item().PaddingBottom(6).Text("NOTATKI TRENERA").FontSize(10).SemiBold().LetterSpacing(0.08f).FontColor(Colors.Grey.Darken2);

            foreach (var n in d.Notes)
            {
                col.Item().PaddingBottom(6).Row(row =>
                {
                    row.ConstantItem(70).Text(n.CreatedAt.ToString("dd.MM.yyyy")).FontSize(9).FontColor(Colors.Grey.Darken1).SemiBold();
                    row.RelativeItem().Text(n.Content).FontSize(9.5f);
                });
            }
        });

    private static void Footer(IContainer c, ReportData d) =>
        c.BorderTop(0.4f).BorderColor(Colors.Grey.Lighten2).PaddingTop(8).Row(row =>
        {
            row.RelativeItem().Text(t =>
            {
                t.Span("Wygenerowano ").FontSize(8).FontColor(Colors.Grey.Darken1);
                t.Span(DateTime.Now.ToString("dd.MM.yyyy HH:mm", Pl)).FontSize(8).SemiBold().FontColor(Colors.Grey.Darken2);
                t.Span(" · ").FontSize(8).FontColor(Colors.Grey.Lighten1);
                t.Span(d.CompanyName).FontSize(8).FontColor(Colors.Grey.Darken1);
            });
            row.AutoItem().Text(t =>
            {
                t.Span("Strona ").FontSize(8).FontColor(Colors.Grey.Darken1);
                t.CurrentPageNumber().FontSize(8).SemiBold();
                t.Span(" / ").FontSize(8).FontColor(Colors.Grey.Lighten1);
                t.TotalPages().FontSize(8).SemiBold();
            });
        });

    // ---- helpers ----

    private static string MonthYearLabel(int year, int month)
    {
        var label = new DateTime(year, month, 1).ToString("LLLL yyyy", Pl);
        return char.ToUpper(label[0], Pl) + label[1..];
    }

    private static string StatusLabel(SessionStatus s) => s switch
    {
        SessionStatus.Scheduled       => "Zaplanowana",
        SessionStatus.Completed       => "Ukończona",
        SessionStatus.Cancelled       => "Anulowana",
        SessionStatus.NoShow          => "Nieobecność",
        SessionStatus.AwaitingPackage => "Bez pakietu",
        _                             => s.ToString()
    };

    private static string StatusColor(SessionStatus s) => s switch
    {
        SessionStatus.Completed       => Colors.Green.Darken2,
        SessionStatus.Cancelled       => Colors.Red.Darken1,
        SessionStatus.NoShow          => Colors.Orange.Darken1,
        SessionStatus.AwaitingPackage => Colors.Amber.Darken3,
        _                             => Colors.Blue.Darken1
    };

    private static string ResolveName(ApplicationUser u)
    {
        var n = $"{u.FirstName} {u.LastName}".Trim();
        return string.IsNullOrEmpty(n) ? (u.UserName ?? u.Email ?? "—") : n;
    }

    private static string Slug(string s)
    {
        var clean = new string(s.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ' ').ToArray());
        return clean.Trim().Replace(' ', '_').ToLowerInvariant() is { Length: > 0 } x ? x : "klient";
    }
}
