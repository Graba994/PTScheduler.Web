using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Palette = PTScheduler.Infrastructure.Services.ClientReportService.ThemePalette;

namespace PTScheduler.Infrastructure.Services;

public class TrainingPlanPdfService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IBrandingService brandingService,
    IAppClock clock,
    IWebRootPathProvider webRootPathProvider) : ITrainingPlanPdfService
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    /// <summary>Ile pustych kolumn na wyniki (tydzień 1–4) — klient wpisuje ciężar lub powtórzenia długopisem.</summary>
    private const int LogWeeks = 4;

    public async Task<(byte[] Bytes, string FileName)?> GenerateAsync(int planId)
    {
        await using var db = dbFactory.CreateDbContext();
        var plan = await db.TrainingPlans.AsNoTracking()
            .Include(p => p.Client)
            .Include(p => p.Days).ThenInclude(d => d.Exercises).ThenInclude(e => e.Exercise)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == planId);
        if (plan is null) return null;

        var trainer = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == plan.TrainerUserId);
        var branding = await brandingService.GetAsync();
        var data = new PlanData(
            Plan: plan,
            CompanyName: string.IsNullOrWhiteSpace(branding.CompanyName) ? "PTScheduler" : branding.CompanyName,
            LogoBytes: await ClientReportService.LoadLogoAsync(branding.LogoPath, webRootPathProvider.WebRootPath),
            TrainerName: trainer is null ? null : ClientReportService.ResolveName(trainer),
            ClientName: plan.Client is { } c ? $"{c.FirstName} {c.LastName}".Trim() : null,
            GeneratedAt: clock.LocalNow,
            Theme: Palette.For(branding.ThemeName));

        var bytes = Document.Create(container => Compose(container, data)).GeneratePdf();
        return (bytes, $"Plan_{ClientReportService.Slug(plan.Name)}.pdf");
    }

    private sealed record PlanData(TrainingPlan Plan, string CompanyName, byte[]? LogoBytes, string? TrainerName,
        string? ClientName, DateTime GeneratedAt, Palette Theme);

    private static void Compose(IDocumentContainer container, PlanData d)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.4f, Unit.Centimetre);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(t => t.FontSize(9.5f).FontFamily("Lato"));
            page.Header().Element(c => Header(c, d));
            page.Content().PaddingVertical(0.5f, Unit.Centimetre).Element(c => Body(c, d));
            page.Footer().Element(c => Footer(c, d));
        });
    }

    private static void Header(IContainer c, PlanData d)
    {
        c.BorderBottom(1.5f).BorderColor(d.Theme.Primary).PaddingBottom(10).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                if (d.LogoBytes is not null)
                {
                    col.Item().Height(34).AlignLeft().Image(d.LogoBytes).FitArea();
                    col.Item().PaddingTop(3).Text(d.CompanyName).FontSize(9.5f).FontColor(Colors.Grey.Darken2);
                }
                else
                {
                    col.Item().Text(d.CompanyName).FontSize(14).SemiBold().FontColor(d.Theme.PrimaryDark);
                }
            });
            row.ConstantItem(280).AlignRight().Column(col =>
            {
                col.Item().AlignRight().Text("Plan treningowy").FontSize(9).FontColor(Colors.Grey.Darken1);
                col.Item().PaddingTop(2).AlignRight().Text(d.Plan.Name).FontSize(18).Bold().FontColor(d.Theme.Primary);
                if (d.ClientName is { Length: > 0 })
                    col.Item().PaddingTop(2).AlignRight().Text(d.ClientName).FontSize(10.5f).FontColor(Colors.Grey.Darken2);
            });
        });
    }

    private static void Body(IContainer c, PlanData d)
    {
        c.Column(col =>
        {
            col.Spacing(16);
            if (!string.IsNullOrWhiteSpace(d.Plan.Notes))
                col.Item().Background(d.Theme.PrimaryLight).Padding(10).Text(d.Plan.Notes!).FontSize(9.5f).FontColor(Colors.Grey.Darken4);

            var days = d.Plan.Days.OrderBy(x => x.Order).ToList();
            if (days.Count == 0)
            {
                col.Item().Background(Colors.Grey.Lighten5).Padding(20).AlignCenter().Text("Plan nie ma jeszcze dni treningowych.").FontColor(Colors.Grey.Darken1);
                return;
            }
            foreach (var day in days)
                col.Item().Element(x => DaySection(x, day, d.Theme));
        });
    }

    private static void DaySection(IContainer c, PlanDay day, Palette theme)
    {
        // Dzień nie rozrywa się między stronami, chyba że sam nie mieści się na jednej.
        c.ShowEntire().Column(col =>
        {
            col.Item().PaddingBottom(6).Row(row =>
            {
                row.AutoItem().Width(3).Background(theme.Primary);
                row.AutoItem().PaddingLeft(8).Text(string.IsNullOrWhiteSpace(day.Label) ? $"Dzień {day.Order + 1}" : day.Label)
                    .FontSize(12.5f).SemiBold().FontColor(Colors.Grey.Darken4);
            });

            var exercises = day.Exercises.OrderBy(e => e.Order).ToList();
            if (exercises.Count == 0)
            {
                col.Item().Text("Brak ćwiczeń.").FontSize(9).FontColor(Colors.Grey.Darken1);
                return;
            }

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    cd.ConstantColumn(18);
                    cd.RelativeColumn(3.2f);
                    cd.ConstantColumn(66);
                    cd.ConstantColumn(46);
                    cd.ConstantColumn(42);
                    for (var w = 0; w < LogWeeks; w++) cd.ConstantColumn(34);
                });

                table.Header(h =>
                {
                    h.Cell().Element(Head).Text("#");
                    h.Cell().Element(Head).Text("Ćwiczenie");
                    h.Cell().Element(Head).Text("Serie × powt.");
                    h.Cell().Element(Head).Text("Ciężar");
                    h.Cell().Element(Head).Text("Przerwa");
                    for (var w = 1; w <= LogWeeks; w++) h.Cell().Element(Head).AlignCenter().Text($"Tydz. {w}");
                });

                var i = 0;
                foreach (var e in exercises)
                {
                    i++;
                    table.Cell().Element(Cell).Text(i.ToString()).FontColor(Colors.Grey.Darken1);
                    table.Cell().Element(Cell).Column(x =>
                    {
                        x.Item().Text(e.Exercise?.NamePl is { Length: > 0 } n ? n : e.Exercise?.NameEn ?? "Ćwiczenie").SemiBold();
                        var extra = string.Join(" · ", new[]
                        {
                            string.IsNullOrWhiteSpace(e.Tempo) ? null : $"tempo {e.Tempo}",
                            string.IsNullOrWhiteSpace(e.Notes) ? null : e.Notes!.Trim()
                        }.Where(s => s is not null));
                        if (extra.Length > 0) x.Item().PaddingTop(1).Text(extra).FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                    table.Cell().Element(Cell).Text(Volume(e));
                    table.Cell().Element(Cell).Text(e.TargetWeightKg is { } kg ? $"{kg.ToString("0.##", Pl)} kg" : "—");
                    table.Cell().Element(Cell).Text(e.RestSeconds is { } r ? Duration(r) : "—");
                    for (var w = 0; w < LogWeeks; w++)
                        table.Cell().Element(Cell).BorderLeft(0.4f).BorderColor(Colors.Grey.Lighten2).Text("");
                }

                static IContainer Head(IContainer x) =>
                    x.DefaultTextStyle(t => t.SemiBold().FontSize(8).FontColor(Colors.Grey.Darken2))
                     .BorderBottom(0.8f).BorderColor(Colors.Grey.Lighten1).PaddingVertical(5).PaddingHorizontal(3);

                static IContainer Cell(IContainer x) =>
                    x.BorderBottom(0.4f).BorderColor(Colors.Grey.Lighten3).PaddingVertical(6).PaddingHorizontal(3).MinHeight(24);
            });
        });
    }

    /// <summary>„4 × 8-12”, „3 × 45 s”, „1 × 5 km” — powtórzenia, czas albo dystans serii.</summary>
    private static string Volume(PlanExercise e)
    {
        var per = !string.IsNullOrWhiteSpace(e.Reps) ? e.Reps!.Trim()
            : e.TargetDurationSeconds is { } s ? Duration(s)
            : e.TargetDistanceMeters is { } m ? (m >= 1000 ? $"{(m / 1000m).ToString("0.##", Pl)} km" : $"{m.ToString("0", Pl)} m")
            : null;
        return e.Sets > 0 ? (per is null ? $"{e.Sets} serie" : $"{e.Sets} × {per}") : per ?? "—";
    }

    private static string Duration(int seconds) =>
        seconds >= 60 && seconds % 60 == 0 ? $"{seconds / 60} min"
        : seconds > 60 ? $"{seconds / 60}:{seconds % 60:D2} min"
        : $"{seconds} s";

    private static void Footer(IContainer c, PlanData d) =>
        c.BorderTop(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken1));
                t.Span(d.TrainerName is { Length: > 0 } tn ? $"Przygotowane przez: {tn} · " : "");
                t.Span(d.GeneratedAt.ToString("d MMMM yyyy", Pl));
            });
            row.AutoItem().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken1));
                t.Span("Strona ");
                t.CurrentPageNumber();
                t.Span(" z ");
                t.TotalPages();
            });
        });
}
