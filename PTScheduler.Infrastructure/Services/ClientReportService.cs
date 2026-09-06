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
    IAppClock clock,
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

        // Granice miesiąca to zegar ścienny, tak samo jak Session.StartTime,
        // z którym są porównywane. Kolumna jest typu timestamp without time zone,
        // więc Kind=Unspecified jest tu właściwy — wcześniejszy Kind=Local
        // oznaczał strefę maszyny, czyli UTC w kontenerze.
        var monthStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
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
            Notes: notes,
            GeneratedAt: clock.LocalNow,   // zegar ścienny; statyczne Footer() nie ma dostępu do clock
            Theme: ThemePalette.For(branding.ThemeName)
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
        List<TrainerNote> Notes,
        DateTime GeneratedAt,
        ThemePalette Theme);

    /// <summary>
    /// Hex palette pulled from the active branding theme (mirrors --c-primary tokens
    /// from app.css). Only "light" variants are used since a PDF is always a light surface.
    /// </summary>
    private record ThemePalette(string Primary, string PrimaryDark, string PrimaryLight)
    {
        public static ThemePalette For(string? themeName)
        {
            // Strip "-dark" suffix if present — PDF doesn't have a dark mode
            var key = (themeName ?? "ocean").Replace("-dark", "").ToLowerInvariant();
            return key switch
            {
                "forest"   => new("#16A34A", "#15803D", "#DCFCE7"),
                "sunset"   => new("#EA580C", "#C2410C", "#FFEDD5"),
                "crimson"  => new("#DC2626", "#B91C1C", "#FEE2E2"),
                "lavender" => new("#9333EA", "#7E22CE", "#F3E8FF"),
                "slate"    => new("#475569", "#334155", "#F1F5F9"),
                "rose"     => new("#E11D48", "#BE123C", "#FFE4E6"),
                "teal"     => new("#0D9488", "#0F766E", "#CCFBF1"),
                "amber"    => new("#D97706", "#B45309", "#FEF3C7"),
                "indigo"   => new("#4F46E5", "#4338CA", "#E0E7FF"),
                _          => new("#0284C7", "#0369A1", "#E0F2FE"),  // ocean (default)
            };
        }
    }

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
        c.PaddingBottom(10).BorderBottom(1.5f).BorderColor(d.Theme.Primary).PaddingBottom(10).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                if (d.LogoBytes is not null)
                {
                    col.Item().Height(36).AlignLeft().Image(d.LogoBytes).FitArea();
                    col.Item().PaddingTop(3).Text(d.CompanyName).FontSize(10).FontColor(Colors.Grey.Darken2);
                }
                else
                {
                    col.Item().Text(d.CompanyName).FontSize(14).SemiBold().FontColor(d.Theme.PrimaryDark);
                }
            });

            row.ConstantItem(240).AlignRight().Column(col =>
            {
                col.Item().Text("Raport miesięczny").FontSize(9.5f).FontColor(Colors.Grey.Darken1);
                col.Item().PaddingTop(2).Text(MonthYearLabel(d.Year, d.Month)).FontSize(24).Bold().FontColor(d.Theme.Primary);
                col.Item().PaddingTop(2).Text(d.ClientFullName).FontSize(11).FontColor(Colors.Grey.Darken2);
            });
        });
    }

    /// <summary>
    /// Reusable section title — semibold, larger, with bottom margin and a subtle
    /// theme-colored accent bar on the left so titles read as branded headings.
    /// </summary>
    private static void SectionTitle(IContainer c, string title, ThemePalette theme) =>
        c.PaddingBottom(10).Row(row =>
        {
            row.AutoItem().Width(3).Background(theme.Primary);
            row.AutoItem().PaddingLeft(8).Text(title).FontSize(13).SemiBold().FontColor(Colors.Grey.Darken4);
        });

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
        c.Background(Colors.Grey.Lighten5).Padding(14).Column(col =>
        {
            col.Item().Text(d.ClientFullName).FontSize(14).SemiBold().FontColor(Colors.Grey.Darken4);

            col.Item().PaddingTop(3).Row(row =>
            {
                if (!string.IsNullOrEmpty(d.ClientEmail))
                    row.AutoItem().PaddingRight(14).Text(t =>
                    {
                        t.Span("Email: ").FontSize(9).FontColor(Colors.Grey.Darken1);
                        t.Span(d.ClientEmail).FontSize(9.5f);
                    });
                if (!string.IsNullOrEmpty(d.ClientPhone))
                    row.AutoItem().Text(t =>
                    {
                        t.Span("Tel.: ").FontSize(9).FontColor(Colors.Grey.Darken1);
                        t.Span(d.ClientPhone!).FontSize(9.5f);
                    });
                row.RelativeItem();
            });

            if (!string.IsNullOrWhiteSpace(d.ClientGoal))
            {
                col.Item().PaddingTop(5).Text(t =>
                {
                    t.Span("Cel: ").FontSize(9).FontColor(Colors.Grey.Darken1);
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
            row.RelativeItem().Element(c => StatBox(c, total.ToString(),         "Treningów",       d.Theme.PrimaryLight,  d.Theme.Primary));
            row.RelativeItem().PaddingLeft(8).Element(c => StatBox(c, completed.ToString(),     "Ukończonych",     Colors.Green.Lighten5, Colors.Green.Darken2));
            row.RelativeItem().PaddingLeft(8).Element(c => StatBox(c, cancelled.ToString(),     "Anulowanych",     Colors.Red.Lighten5,   Colors.Red.Darken2));
            row.RelativeItem().PaddingLeft(8).Element(c => StatBox(c, totalMinutes.ToString(),  "Minut treningu",  Colors.Grey.Lighten4,  Colors.Grey.Darken4));
        });
    }

    private static void StatBox(IContainer c, string value, string label, string bg, string accent) =>
        c.Background(bg).Padding(14).Column(col =>
        {
            col.Item().AlignCenter().Text(value).FontSize(26).Bold().FontColor(accent);
            col.Item().PaddingTop(2).AlignCenter().Text(label).FontSize(9).FontColor(Colors.Grey.Darken2);
        });

    private static void SessionsSection(IContainer c, ReportData d) =>
        c.Column(col =>
        {
            col.Item().Element(c => SectionTitle(c, "Treningi", d.Theme));

            if (d.Sessions.Count == 0)
            {
                col.Item().Background(Colors.Grey.Lighten5).Padding(20).AlignCenter()
                    .Text("Brak treningów w tym okresie.").FontSize(10).FontColor(Colors.Grey.Darken1);
                return;
            }

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(64);  // date
                    c.RelativeColumn(2);   // type
                    c.ConstantColumn(45);  // duration
                    c.ConstantColumn(80);  // status
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
                    table.Cell().Element(BodyCell).AlignRight().Text($"{s.SessionType?.DurationMinutes ?? 0} min").FontSize(9.5f);

                    // Status as a coloured pill instead of plain text
                    table.Cell().Element(BodyCell).AlignLeft().Element(c =>
                        c.Background(StatusBg(s.Status)).PaddingVertical(2).PaddingHorizontal(7)
                         .Text(StatusLabel(s.Status)).FontSize(8.5f).FontColor(StatusColor(s.Status)).SemiBold());

                    table.Cell().Element(BodyCell).Text(d.TrainerNames.GetValueOrDefault(s.TrainerUserId, "—")).FontSize(9.5f);
                }

                static IContainer HeaderCell(IContainer c) =>
                    c.DefaultTextStyle(x => x.SemiBold().FontSize(8.5f).FontColor(Colors.Grey.Darken2))
                     .BorderBottom(0.8f).BorderColor(Colors.Grey.Lighten1).PaddingVertical(6).PaddingHorizontal(3);

                static IContainer BodyCell(IContainer c) =>
                    c.BorderBottom(0.4f).BorderColor(Colors.Grey.Lighten3).PaddingVertical(6).PaddingHorizontal(3);
            });
        });

    private static void MeasurementsSection(IContainer c, ReportData d)
    {
        var first = d.PreviousMeasurement ?? d.Measurements.FirstOrDefault();
        var last = d.Measurements.LastOrDefault() ?? first;
        if (first is null || last is null) return;

        c.Column(col =>
        {
            col.Item().Element(c => SectionTitle(c, "Pomiary", d.Theme));

            // Subtitle: "Porównanie 03.04.2026 → 28.04.2026"
            var firstLabel = first.MeasurementDate.ToString("d MMMM yyyy", Pl);
            var lastLabel = last.MeasurementDate.ToString("d MMMM yyyy", Pl);
            col.Item().PaddingBottom(8).Text(t =>
            {
                t.Span("Porównanie pomiarów: ").FontSize(10).FontColor(Colors.Grey.Darken1);
                t.Span(firstLabel).FontSize(10).SemiBold().FontColor(Colors.Grey.Darken3);
                t.Span("  →  ").FontSize(10).FontColor(Colors.Grey.Darken1);
                t.Span(lastLabel).FontSize(10).SemiBold().FontColor(Colors.Grey.Darken3);
            });

            if (d.PreviousMeasurement is not null)
            {
                col.Item().PaddingBottom(6).Text("Pierwszy pomiar pochodzi sprzed bieżącego okresu — porównujemy z najnowszym dostępnym.")
                    .FontSize(8.5f).FontColor(Colors.Grey.Darken1).Italic();
            }

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
                    c.DefaultTextStyle(x => x.SemiBold().FontSize(8.5f).FontColor(Colors.Grey.Darken2))
                     .BorderBottom(0.8f).BorderColor(Colors.Grey.Lighten1).PaddingVertical(6).PaddingHorizontal(3);
            });
        });
    }

    private static void MeasRow(QuestPDF.Fluent.TableDescriptor t, string label, decimal? a, decimal? b, string unit, bool upIsGood)
    {
        IContainer Cell(IContainer c) => c.BorderBottom(0.4f).BorderColor(Colors.Grey.Lighten3).PaddingVertical(6).PaddingHorizontal(3);

        t.Cell().Element(Cell).Text(label).FontSize(10);
        t.Cell().Element(Cell).AlignRight().Text(a is null ? "—" : $"{a:0.##} {unit}").FontSize(10);
        t.Cell().Element(Cell).AlignRight().Text(b is null ? "—" : $"{b:0.##} {unit}").FontSize(10);

        if (a is null || b is null || a == b)
        {
            t.Cell().Element(Cell).AlignRight().Text("—").FontSize(10).FontColor(Colors.Grey.Darken1);
        }
        else
        {
            var delta = b.Value - a.Value;
            var positive = delta > 0;
            var isGood = upIsGood ? positive : !positive;
            var color = isGood ? Colors.Green.Darken2 : Colors.Red.Darken2;
            var arrow = positive ? "↑" : "↓";
            var sign = positive ? "+" : "";
            t.Cell().Element(Cell).AlignRight().Text($"{arrow} {sign}{delta:0.##} {unit}").FontSize(10).FontColor(color).SemiBold();
        }
    }

    private static void NotesSection(IContainer c, ReportData d) =>
        c.Column(col =>
        {
            col.Item().Element(c => SectionTitle(c, "Notatki trenera", d.Theme));

            foreach (var n in d.Notes)
            {
                col.Item().PaddingBottom(8).Background(Colors.Grey.Lighten5).Padding(12).Column(inner =>
                {
                    inner.Item().Text(Capitalize(n.CreatedAt.ToString("d MMMM yyyy", Pl)))
                        .FontSize(9).FontColor(Colors.Grey.Darken2).SemiBold();
                    inner.Item().PaddingTop(3).Text(n.Content).FontSize(10).FontColor(Colors.Grey.Darken4);
                });
            }
        });

    private static void Footer(IContainer c, ReportData d) =>
        c.BorderTop(0.4f).BorderColor(Colors.Grey.Lighten2).PaddingTop(8).Row(row =>
        {
            row.RelativeItem().Text(t =>
            {
                t.Span("Wygenerowano: ").FontSize(8.5f).FontColor(Colors.Grey.Darken1);
                t.Span(Capitalize(d.GeneratedAt.ToString("d MMMM yyyy 'o' HH:mm", Pl)))
                    .FontSize(8.5f).SemiBold().FontColor(Colors.Grey.Darken3);
                t.Span("   ·   ").FontSize(8.5f).FontColor(Colors.Grey.Lighten1);
                t.Span(d.CompanyName).FontSize(8.5f).FontColor(Colors.Grey.Darken1);
            });
            row.AutoItem().Text(t =>
            {
                t.Span("Strona ").FontSize(8.5f).FontColor(Colors.Grey.Darken1);
                t.CurrentPageNumber().FontSize(8.5f).SemiBold().FontColor(Colors.Grey.Darken3);
                t.Span(" z ").FontSize(8.5f).FontColor(Colors.Grey.Darken1);
                t.TotalPages().FontSize(8.5f).SemiBold().FontColor(Colors.Grey.Darken3);
            });
        });

    // ---- helpers ----

    private static string MonthYearLabel(int year, int month) =>
        Capitalize(new DateTime(year, month, 1).ToString("MMMM yyyy", Pl));

    /// <summary>
    /// Polish month/day names from <c>MMMM</c>/<c>dddd</c> are lowercased — capitalize
    /// the first character so they read like a proper title.
    /// </summary>
    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0], Pl) + s[1..];

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
        SessionStatus.Cancelled       => Colors.Red.Darken2,
        SessionStatus.NoShow          => Colors.Orange.Darken3,
        SessionStatus.AwaitingPackage => Colors.Amber.Darken4,
        _                             => Colors.Blue.Darken2
    };

    /// <summary>Soft pastel background to pair with <see cref="StatusColor"/> for pill-style labels.</summary>
    private static string StatusBg(SessionStatus s) => s switch
    {
        SessionStatus.Completed       => Colors.Green.Lighten5,
        SessionStatus.Cancelled       => Colors.Red.Lighten5,
        SessionStatus.NoShow          => Colors.Orange.Lighten5,
        SessionStatus.AwaitingPackage => Colors.Amber.Lighten5,
        _                             => Colors.Blue.Lighten5
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
