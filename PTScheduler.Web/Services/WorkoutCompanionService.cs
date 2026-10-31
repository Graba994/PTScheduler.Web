using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Surveys;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Application.Training;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

/// <summary>
/// Towarzysz treningu klienta: „ostatnio” przy ćwiczeniach w /train (ciężary i powtórzenia z poprzedniego razu)
/// i podsumowanie po treningu (rekordy, poprawa, seria tygodni) z jednym zdaniem od asystenta pod marką trenera.
/// </summary>
public class WorkoutCompanionService(IDbContextFactory<ApplicationDbContext> dbFactory, PlatformAssistantService platform, IAppClock clock)
{
    public sealed record LastSets(DateOnly Date, List<SetRecord> Sets, decimal MaxKg);

    /// <summary>Ostatni trening z każdym z ćwiczeń przed <paramref name="before"/>.</summary>
    public async Task<Dictionary<int, LastSets>> LastAsync(int clientId, IReadOnlyCollection<int> exerciseIds, DateOnly before)
    {
        if (exerciseIds.Count == 0) return [];
        await using var db = await dbFactory.CreateDbContextAsync();
        var logs = await db.WorkoutLogs.AsNoTracking()
            .Where(l => l.ClientId == clientId && exerciseIds.Contains(l.ExerciseId) && l.WorkoutDate < before)
            .OrderByDescending(l => l.WorkoutDate)
            .Select(l => new { l.ExerciseId, l.WorkoutDate, Sets = l.Sets.OrderBy(s => s.SetNumber).Select(s => new SetRecord(s.Reps, s.WeightKg)).ToList() })
            .Take(400)
            .ToListAsync();
        return logs.GroupBy(l => l.ExerciseId).ToDictionary(g => g.Key, g =>
        {
            var lastDate = g.Max(l => l.WorkoutDate);
            var sets = g.Where(l => l.WorkoutDate == lastDate).SelectMany(l => l.Sets).ToList();
            return new LastSets(lastDate, sets, sets.Count == 0 ? 0 : sets.Max(s => s.WeightKg));
        });
    }

    /// <summary>
    /// Szkic „po co ten plan” dla klienta: liczba dni, główne partie mięśni z ćwiczeń i cele z ankiety zdrowotnej.
    /// AI (pod marką trenera) przeredaguje szablon; liczby i cele tylko z danych.
    /// </summary>
    public async Task<string> PurposeDraftAsync(PlanEditDto plan)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var ids = plan.Days.SelectMany(d => d.Exercises).Select(e => e.ExerciseId).Distinct().ToList();
        var muscles = (await db.Exercises.AsNoTracking().Where(e => ids.Contains(e.Id)).Select(e => e.PrimaryMuscles).ToListAsync())
            .SelectMany(m => Muscles.Parse(m)).GroupBy(m => m).OrderByDescending(g => g.Count()).Take(3)
            .Select(g => System.Text.RegularExpressions.Regex.Replace(Muscles.Label(g.Key), @"\s*\(.*?\)", "").ToLowerInvariant()).ToList();
        var goals = new List<string>();
        if (plan.ClientId is { } clientId)
        {
            var health = await db.SurveyResponses.AsNoTracking()
                .Where(r => r.ClientId == clientId && r.Kind == SurveyKind.HealthIntake)
                .OrderByDescending(r => r.SubmittedAt).Select(r => r.AnswersJson).FirstOrDefaultAsync();
            if (health is not null)
                goals = SurveyJson.Deserialize(health, new List<SurveyAnswer>()).FirstOrDefault(a => a.Key == "goals")?.Choices ?? [];
        }
        var days = plan.Days.Count;
        var dayWord = days == 1 ? "dzień treningowy" : days % 10 is >= 2 and <= 4 && days % 100 is < 12 or > 14 ? "dni treningowe" : "dni treningowych";
        var goalText = goals.Count > 0 ? $" i jest ułożony pod Twój cel: {string.Join(", ", goals).ToLowerInvariant()}" : "";
        var focus = muscles.Count > 0 ? $" Najwięcej pracy dostaną: {string.Join(", ", muscles)}." : "";
        var fallback = $"Ten plan ma {days} {dayWord}{goalText}.{focus} Rób dni po kolei i zapisuj ciężary — dzięki temu widzę postęp i mogę dobrze dobierać obciążenie.";
        var facts = new Dictionary<string, string> { ["dni_treningowe"] = days.ToString() };
        if (goals.Count > 0) facts["cele_klienta"] = string.Join(", ", goals);
        if (muscles.Count > 0) facts["glowne_partie"] = string.Join(", ", muscles);
        var cfg = await platform.GetConfigAsync();
        return await platform.PhraseAsync($"po-co-plan-{plan.Id}-{Guid.NewGuid():N}", "po_co_plan", facts, fallback, cfg?.Ai == true, audience: "klient");
    }

    public sealed record Summary(List<Highlight> Items, string Text);

    public async Task<Summary> SummaryAsync(int clientId, DateOnly date)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var today = await db.WorkoutLogs.AsNoTracking()
            .Where(l => l.ClientId == clientId && l.WorkoutDate == date)
            .Select(l => new { l.ExerciseId, Name = l.Exercise!.NamePl, Sets = l.Sets.Select(s => new SetRecord(s.Reps, s.WeightKg)).ToList() })
            .ToListAsync();
        if (today.Count == 0) return new([], "");

        var ids = today.Select(t => t.ExerciseId).Distinct().ToList();
        var previousMax = await db.WorkoutSetLogs.AsNoTracking()
            .Where(s => s.WorkoutLog!.ClientId == clientId && s.WorkoutLog.WorkoutDate < date && ids.Contains(s.WorkoutLog.ExerciseId) && s.Reps > 0)
            .GroupBy(s => s.WorkoutLog!.ExerciseId)
            .Select(g => new { ExerciseId = g.Key, Max = g.Max(s => s.WeightKg) })
            .ToDictionaryAsync(x => x.ExerciseId, x => x.Max);
        var last = await LastAsync(clientId, ids, date);

        var exercises = today.GroupBy(t => t.ExerciseId).Select(g => new ExerciseToday(g.Key, g.First().Name,
            g.SelectMany(x => x.Sets).ToList(),
            previousMax.TryGetValue(g.Key, out var m) ? m : null,
            last.TryGetValue(g.Key, out var l) ? l.Sets : null)).ToList();

        var since = date.AddDays(-120);
        var days = await db.WorkoutLogs.AsNoTracking().Where(l => l.ClientId == clientId && l.WorkoutDate >= since && l.WorkoutDate <= date)
            .Select(l => l.WorkoutDate).Distinct().ToListAsync();
        var monthStart = new DateOnly(date.Year, date.Month, 1);
        var result = WorkoutHighlights.Compute(exercises, WorkoutHighlights.WeekStreak(days, date), days.Count(d => d >= monthStart));

        var cfg = await platform.GetConfigAsync();
        var text = await platform.PhraseAsync($"po-treningu-{clientId}-{date:yyyyMMdd}", "po_treningu", result.Facts, result.Fallback,
            cfg?.Ai == true, audience: "klient");
        return new(result.Items, text);
    }
}
