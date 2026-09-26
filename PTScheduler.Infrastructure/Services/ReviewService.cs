using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Marketing;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class ReviewService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IMarketingSettingsService settings,
    IWebPushService push,
    IAppClock clock,
    ILogger<ReviewService> logger) : IReviewService
{
    public const int MaxTextLength = 1000;

    public async Task<ReviewPromptDto> GetPromptAsync(int clientId)
    {
        var cfg = await settings.GetAsync();
        var result = new ReviewPromptDto { Enabled = cfg.ReviewsEnabled, GoogleReviewUrl = cfg.GoogleReviewUrl };
        if (!cfg.ReviewsEnabled) return result;

        await using var db = dbFactory.CreateDbContext();
        var review = await db.ClientReviews.AsNoTracking().FirstOrDefaultAsync(r => r.ClientId == clientId);
        result.Existing = review is null ? null : ToDto(review, "");
        if (review is not null) return result;

        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId);
        if (client is null) return result;
        if (client.ReviewPromptSnoozedUntil is DateTime until && until > clock.UtcNow) return result;

        var completed = await db.Sessions.CountAsync(s => s.ClientId == clientId && s.Status == SessionStatus.Completed);
        result.ShouldAsk = completed >= cfg.ReviewAskAfterSessions;
        return result;
    }

    public async Task<(bool Ok, string? Error)> SubmitAsync(int clientId, int rating, string? text, bool publishConsent)
    {
        if (rating is < 1 or > 5) return (false, "Wybierz ocenę od 1 do 5 gwiazdek.");
        text = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (text?.Length > MaxTextLength) return (false, $"Opinia może mieć najwyżej {MaxTextLength} znaków.");

        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId);
        if (client is null) return (false, "Nie znaleziono profilu klienta.");

        var now = clock.UtcNow;
        var review = await db.ClientReviews.FirstOrDefaultAsync(r => r.ClientId == clientId);
        var isNew = review is null;
        if (review is null)
        {
            review = new ClientReview { ClientId = clientId, CreatedAt = now };
            db.ClientReviews.Add(review);
        }
        else if (review.Text != text || review.Rating != rating)
        {
            // Zmieniona treść wymaga ponownej akceptacji trenera.
            review.IsPublished = false;
        }

        review.Rating = rating;
        review.Text = text;
        review.PublishConsent = publishConsent;
        if (!publishConsent) review.IsPublished = false;
        review.DisplayName = ShortName(client.FirstName, client.LastName);
        review.UpdatedAt = now;
        await db.SaveChangesAsync();

        if (isNew && !string.IsNullOrEmpty(client.TrainerUserId))
        {
            try
            {
                await push.SendAsync(client.TrainerUserId, new PushMessageDto
                {
                    Title = $"Nowa opinia {new string('★', rating)}{new string('☆', 5 - rating)}",
                    Body = $"{review.DisplayName}: {(text is null ? "(bez komentarza)" : text.Length > 100 ? text[..97] + "…" : text)}",
                    Url = "/admin/reviews"
                });
            }
            catch (Exception ex) { logger.LogWarning(ex, "Review push failed."); }
        }
        return (true, null);
    }

    public async Task SnoozeAsync(int clientId, int days = 30)
    {
        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == clientId);
        if (client is null) return;
        client.ReviewPromptSnoozedUntil = clock.UtcNow.AddDays(Math.Clamp(days, 1, 365));
        await db.SaveChangesAsync();
    }

    public async Task MarkGoogleClickedAsync(int clientId)
    {
        await using var db = dbFactory.CreateDbContext();
        var review = await db.ClientReviews.FirstOrDefaultAsync(r => r.ClientId == clientId);
        if (review is null || review.GoogleClickedAt is not null) return;
        review.GoogleClickedAt = clock.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<List<ClientReviewDto>> GetAllAsync(string? trainerUserId = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var rows = await db.ClientReviews.AsNoTracking()
            .Where(r => trainerUserId == null || r.Client.TrainerUserId == trainerUserId)
            .OrderByDescending(r => r.UpdatedAt)
            .Select(r => new { Review = r, Name = r.Client.FirstName + " " + r.Client.LastName })
            .ToListAsync();
        return rows.Select(x => ToDto(x.Review, x.Name)).ToList();
    }

    public async Task<bool> SetPublishedAsync(int reviewId, bool published)
    {
        await using var db = dbFactory.CreateDbContext();
        var review = await db.ClientReviews.FirstOrDefaultAsync(r => r.Id == reviewId);
        if (review is null) return false;
        if (published && (!review.PublishConsent || string.IsNullOrWhiteSpace(review.Text))) return false;
        review.IsPublished = published;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int reviewId)
    {
        await using var db = dbFactory.CreateDbContext();
        var review = await db.ClientReviews.FirstOrDefaultAsync(r => r.Id == reviewId);
        if (review is null) return false;
        db.ClientReviews.Remove(review);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<PublicReviewsDto> GetPublicAsync(int take = 6)
    {
        var cfg = await settings.GetAsync();
        if (!cfg.ReviewsEnabled) return new PublicReviewsDto();

        await using var db = dbFactory.CreateDbContext();
        var published = db.ClientReviews.AsNoTracking()
            .Where(r => r.IsPublished && r.PublishConsent && r.Text != null);
        var count = await published.CountAsync();
        if (count == 0) return new PublicReviewsDto();

        var average = await published.AverageAsync(r => (double)r.Rating);
        var items = await published
            .OrderByDescending(r => r.Rating).ThenByDescending(r => r.UpdatedAt)
            .Take(Math.Clamp(take, 1, 24))
            .Select(r => new PublicReviewDto { Rating = r.Rating, Text = r.Text!, DisplayName = r.DisplayName, CreatedAt = r.CreatedAt })
            .ToListAsync();
        return new PublicReviewsDto { Items = items, Average = Math.Round(average, 1), Count = count };
    }

    private static string ShortName(string first, string last)
    {
        first = first.Trim();
        last = last.Trim();
        return last.Length > 0 ? $"{first} {char.ToUpperInvariant(last[0])}." : first;
    }

    private static ClientReviewDto ToDto(ClientReview r, string clientName) => new()
    {
        Id = r.Id, ClientId = r.ClientId, ClientName = clientName.Trim(), Rating = r.Rating, Text = r.Text,
        DisplayName = r.DisplayName, PublishConsent = r.PublishConsent, IsPublished = r.IsPublished,
        CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt, ClickedGoogle = r.GoogleClickedAt is not null
    };
}

/// <summary>
/// Ocena aplikacji przez trenera — wysyłana do portalu właściciela platformy
/// (PORTAL_URL + TENANT_SLUG + TENANT_INTERNAL_SECRET, jak synchronizacja planu).
/// </summary>
public class AppFeedbackService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IHttpClientFactory httpFactory,
    IAppClock clock,
    ILogger<AppFeedbackService> logger) : IAppFeedbackService
{
    private static readonly TimeSpan AskEvery = TimeSpan.FromDays(180);

    private static (string Url, string Slug, string Secret)? PortalConfig()
    {
        var url = Environment.GetEnvironmentVariable("PORTAL_URL");
        var slug = Environment.GetEnvironmentVariable("TENANT_SLUG");
        var secret = Environment.GetEnvironmentVariable("TENANT_INTERNAL_SECRET");
        return string.IsNullOrEmpty(url) || string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(secret)
            ? null : (url.TrimEnd('/'), slug, secret);
    }

    public async Task<bool> ShouldAskAsync()
    {
        if (PortalConfig() is null) return false;
        await using var db = dbFactory.CreateDbContext();
        var last = await db.MarketingSettings.AsNoTracking().Select(s => s.LastAppFeedbackAt).FirstOrDefaultAsync();
        return last is null || clock.UtcNow - last.Value > AskEvery;
    }

    public async Task<(bool Ok, string? Error)> SendAsync(AppFeedbackDto dto, string authorEmail)
    {
        if (dto.Rating is < 1 or > 5) return (false, "Wybierz ocenę od 1 do 5 gwiazdek.");
        if (dto.Text?.Length > 2000) return (false, "Wiadomość może mieć najwyżej 2000 znaków.");
        if (PortalConfig() is not { } portal) return (false, "Ta instalacja nie jest połączona z portalem — ocena nie może zostać wysłana.");

        try
        {
            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(10);
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{portal.Url}/api/internal/tenants/{Uri.EscapeDataString(portal.Slug)}/feedback")
            {
                Content = JsonContent.Create(new
                {
                    rating = dto.Rating,
                    text = string.IsNullOrWhiteSpace(dto.Text) ? null : dto.Text.Trim(),
                    contactEmail = string.IsNullOrWhiteSpace(dto.ContactEmail) ? null : dto.ContactEmail.Trim(),
                    authorEmail
                })
            };
            req.Headers.Add("X-Internal-Secret", portal.Secret);
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("App feedback rejected by portal: {Status}.", (int)resp.StatusCode);
                return (false, "Nie udało się wysłać oceny. Spróbuj później.");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "App feedback could not reach the portal.");
            return (false, "Nie udało się połączyć z portalem. Spróbuj później.");
        }

        await MarkAsync();
        return (true, null);
    }

    public Task SnoozeAsync() => MarkAsync();

    private async Task MarkAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var s = await db.MarketingSettings.FirstOrDefaultAsync();
        if (s is null) { s = new MarketingSettings(); db.MarketingSettings.Add(s); }
        s.LastAppFeedbackAt = clock.UtcNow;
        await db.SaveChangesAsync();
    }
}
