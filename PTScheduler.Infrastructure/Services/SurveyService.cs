using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Surveys;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class SurveyService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IWebPushService push,
    IAppClock clock,
    ILogger<SurveyService> logger) : ISurveyService
{
    public async Task<SurveyTemplateDto> GetTemplateAsync(SurveyKind kind)
    {
        await using var db = dbFactory.CreateDbContext();
        var t = await db.SurveyTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Kind == kind);
        if (t is null) return SurveyDefaults.For(kind);
        return new SurveyTemplateDto
        {
            Id = t.Id,
            Kind = t.Kind,
            Name = t.Name,
            Intro = t.Intro,
            IsEnabled = t.IsEnabled,
            IsCustomized = true,
            Questions = SurveyJson.Deserialize(t.QuestionsJson, SurveyDefaults.For(kind).Questions)
        };
    }

    public async Task<List<string>> SaveTemplateAsync(SurveyTemplateDto dto)
    {
        foreach (var q in dto.Questions)
        {
            q.Label = q.Label.Trim();
            q.Options = q.Options.Select(o => o.Trim()).Where(o => o.Length > 0).Distinct().ToList();
            q.FlagOptions = q.FlagOptions.Where(q.Options.Contains).ToList();
        }
        var errors = SurveyEvaluator.ValidateTemplate(dto);
        if (errors.Count > 0) return errors;

        await using var db = dbFactory.CreateDbContext();
        var t = await db.SurveyTemplates.FirstOrDefaultAsync(x => x.Kind == dto.Kind);
        if (t is null)
        {
            t = new SurveyTemplate { Kind = dto.Kind };
            db.SurveyTemplates.Add(t);
        }
        t.Name = dto.Name.Trim();
        t.Intro = string.IsNullOrWhiteSpace(dto.Intro) ? null : dto.Intro.Trim();
        t.IsEnabled = dto.IsEnabled;
        t.QuestionsJson = SurveyJson.Serialize(dto.Questions);
        t.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync();
        return [];
    }

    public async Task ResetTemplateAsync(SurveyKind kind)
    {
        await using var db = dbFactory.CreateDbContext();
        var t = await db.SurveyTemplates.FirstOrDefaultAsync(x => x.Kind == kind);
        if (t is null) return;
        // Zachowujemy tylko włączenie/wyłączenie — treść wraca do domyślnej.
        var def = SurveyDefaults.For(kind);
        t.Name = def.Name;
        t.Intro = def.Intro;
        t.QuestionsJson = SurveyJson.Serialize(def.Questions);
        t.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<(List<string> Errors, SurveyResponseDto? Response)> SubmitAsync(
        int clientId, SurveyKind kind, IReadOnlyDictionary<string, SurveyAnswerInput> answers, DateOnly? workoutDate = null)
    {
        var template = await GetTemplateAsync(kind);
        var (errors, evaluated) = SurveyEvaluator.Evaluate(template.Questions, answers);
        if (errors.Count > 0) return (errors, null);

        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId);
        if (client is null) return (["Nie znaleziono klienta."], null);

        var date = kind == SurveyKind.PostWorkout ? workoutDate ?? clock.Today : (DateOnly?)null;
        // Jedna ankieta po treningu na dzień — ponowne wypełnienie nadpisuje poprzednią.
        var entity = kind == SurveyKind.PostWorkout
            ? await db.SurveyResponses.FirstOrDefaultAsync(r => r.ClientId == clientId && r.Kind == kind && r.WorkoutDate == date)
            : null;
        if (entity is null)
        {
            entity = new SurveyResponse { ClientId = clientId, Kind = kind, WorkoutDate = date };
            db.SurveyResponses.Add(entity);
        }
        entity.AnswersJson = SurveyJson.Serialize(evaluated);
        entity.FlagCount = evaluated.Count(a => a.Flagged);
        entity.SubmittedAt = clock.UtcNow;
        entity.ReviewedAt = null;
        entity.ReviewedByUserId = null;
        await db.SaveChangesAsync();

        var dto = Map(entity);
        await NotifyTrainerAsync(client, dto);
        return ([], dto);
    }

    public async Task<List<SurveyResponseDto>> GetResponsesAsync(int clientId, SurveyKind kind, int take = 60)
    {
        await using var db = dbFactory.CreateDbContext();
        var rows = await db.SurveyResponses.AsNoTracking()
            .Where(r => r.ClientId == clientId && r.Kind == kind)
            .OrderByDescending(r => r.SubmittedAt)
            .Take(take)
            .ToListAsync();
        return rows.Select(Map).ToList();
    }

    public async Task<SurveyResponseDto?> GetLatestAsync(int clientId, SurveyKind kind) =>
        (await GetResponsesAsync(clientId, kind, 1)).FirstOrDefault();

    public async Task<bool> IsHealthSurveyPendingAsync(int clientId)
    {
        var template = await GetTemplateAsync(SurveyKind.HealthIntake);
        if (!template.IsEnabled) return false;
        await using var db = dbFactory.CreateDbContext();
        return !await db.SurveyResponses.AnyAsync(r => r.ClientId == clientId && r.Kind == SurveyKind.HealthIntake);
    }

    public async Task<Dictionary<DateOnly, SurveyResponseDto>> GetPostWorkoutByDateAsync(int clientId, int days = 90)
    {
        var from = clock.Today.AddDays(-days);
        await using var db = dbFactory.CreateDbContext();
        var rows = await db.SurveyResponses.AsNoTracking()
            .Where(r => r.ClientId == clientId && r.Kind == SurveyKind.PostWorkout && r.WorkoutDate >= from)
            .ToListAsync();
        return rows.Select(Map)
            .GroupBy(r => r.WorkoutDate!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.SubmittedAtUtc).First());
    }

    public async Task MarkReviewedAsync(int responseId, string reviewerUserId)
    {
        await using var db = dbFactory.CreateDbContext();
        var r = await db.SurveyResponses.FirstOrDefaultAsync(x => x.Id == responseId);
        if (r is null) return;
        r.ReviewedAt = clock.UtcNow;
        r.ReviewedByUserId = reviewerUserId;
        await db.SaveChangesAsync();
    }

    public async Task<PostWorkoutStats> GetPostWorkoutStatsAsync(int clientId)
    {
        var template = await GetTemplateAsync(SurveyKind.PostWorkout);
        var responses = await GetResponsesAsync(clientId, SurveyKind.PostWorkout, 120);
        return SurveyInsights.Compute(responses, template.Questions, clock.Today);
    }

    private async Task NotifyTrainerAsync(Client client, SurveyResponseDto response)
    {
        if (string.IsNullOrEmpty(client.TrainerUserId)) return;
        var name = $"{client.FirstName} {client.LastName}".Trim();
        PushMessageDto? message = response.Kind switch
        {
            SurveyKind.HealthIntake => new PushMessageDto
            {
                Title = $"📋 Ankieta zdrowotna: {name}",
                Body = response.FlagCount > 0
                    ? $"Odpowiedzi wymagające uwagi: {response.FlagCount}. Przejrzyj przed pierwszym treningiem."
                    : "Bez zgłoszonych przeciwwskazań. Przejrzyj przed pierwszym treningiem.",
                Url = $"/clients/{client.Id}?tab=surveys"
            },
            // Po treningu powiadamiamy tylko, gdy coś wymaga uwagi (np. ból) — bez spamu.
            SurveyKind.PostWorkout when response.FlagCount > 0 => new PushMessageDto
            {
                Title = $"⚠️ {name}: uwaga po treningu",
                Body = response[SurveyKeys.Pain]?.Bool == true
                    ? "Zgłoszony ból" + (response[SurveyKeys.PainLocation]?.Text is { Length: > 0 } where ? $": {where}" : ".")
                    : "Niska energia, słaby sen lub duże zmęczenie — sprawdź ankietę.",
                Url = $"/clients/{client.Id}?tab=surveys"
            },
            _ => null
        };
        if (message is null) return;
        try { await push.SendAsync(client.TrainerUserId, message); }
        catch (Exception ex) { logger.LogWarning(ex, "Push for survey {Id} failed.", response.Id); }
    }

    private static SurveyResponseDto Map(SurveyResponse r) => new()
    {
        Id = r.Id,
        Kind = r.Kind,
        ClientId = r.ClientId,
        WorkoutDate = r.WorkoutDate,
        SubmittedAtUtc = r.SubmittedAt,
        Answers = SurveyJson.Deserialize(r.AnswersJson, new List<SurveyAnswer>()),
        FlagCount = r.FlagCount,
        ReviewedAtUtc = r.ReviewedAt
    };
}
