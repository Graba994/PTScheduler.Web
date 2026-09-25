using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class WorkoutCommentService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserManager<ApplicationUser> userManager,
    IWebPushService push,
    IAppClock clock,
    ILogger<WorkoutCommentService> logger) : IWorkoutCommentService
{
    public const int MaxLength = 1000;

    public async Task<List<WorkoutCommentDto>> GetForClientAsync(int clientId)
    {
        await using var db = dbFactory.CreateDbContext();
        var comments = await db.WorkoutComments.AsNoTracking()
            .Where(c => c.ClientId == clientId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();

        var names = new Dictionary<string, string>();
        foreach (var authorId in comments.Select(c => c.AuthorUserId).Distinct())
            names[authorId] = await ResolveNameAsync(authorId);

        return comments.Select(c => Map(c, names.GetValueOrDefault(c.AuthorUserId, ""))).ToList();
    }

    public async Task<WorkoutCommentDto> AddAsync(int clientId, DateOnly workoutDate, string authorUserId, bool byTrainer, string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) throw new InvalidOperationException("Komentarz jest pusty.");
        if (text.Length > MaxLength) text = text[..MaxLength];

        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId)
            ?? throw new InvalidOperationException("Nie znaleziono klienta.");

        var comment = new WorkoutComment
        {
            ClientId = clientId,
            WorkoutDate = workoutDate,
            AuthorUserId = authorUserId,
            ByTrainer = byTrainer,
            Text = text,
            CreatedAt = clock.UtcNow
        };
        db.WorkoutComments.Add(comment);
        await db.SaveChangesAsync();

        var authorName = await ResolveNameAsync(authorUserId);
        await NotifyAsync(client, comment, authorName);
        return Map(comment, authorName);
    }

    public async Task<bool> DeleteAsync(int commentId, string requesterUserId)
    {
        await using var db = dbFactory.CreateDbContext();
        var comment = await db.WorkoutComments.FirstOrDefaultAsync(c => c.Id == commentId);
        if (comment is null || comment.AuthorUserId != requesterUserId) return false;
        db.WorkoutComments.Remove(comment);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task MarkReadAsync(int clientId, bool readerIsTrainer)
    {
        await using var db = dbFactory.CreateDbContext();
        // Czytelnik oznacza komentarze drugiej strony.
        var unread = await db.WorkoutComments
            .Where(c => c.ClientId == clientId && c.ReadAt == null && c.ByTrainer != readerIsTrainer)
            .ToListAsync();
        if (unread.Count == 0) return;
        var now = clock.UtcNow;
        foreach (var c in unread) c.ReadAt = now;
        await db.SaveChangesAsync();
    }

    private async Task NotifyAsync(Client client, WorkoutComment comment, string authorName)
    {
        try
        {
            var preview = comment.Text.Length > 120 ? comment.Text[..117] + "…" : comment.Text;
            var day = comment.WorkoutDate.ToString("dd.MM");
            if (comment.ByTrainer)
            {
                await push.SendAsync(client.ApplicationUserId, new PushMessageDto
                {
                    Title = $"💬 {authorName} o Twoim treningu ({day})",
                    Body = preview,
                    Url = "/my/workouts"
                });
            }
            else if (!string.IsNullOrEmpty(client.TrainerUserId))
            {
                await push.SendAsync(client.TrainerUserId, new PushMessageDto
                {
                    Title = $"💬 {client.FirstName} {client.LastName} — trening {day}".Trim(),
                    Body = preview,
                    Url = $"/trainer/activity/{client.Id}"
                });
            }
        }
        catch (Exception ex)
        {
            // Powiadomienie jest dodatkiem — komentarz już zapisany.
            logger.LogWarning(ex, "Push for workout comment {Id} failed.", comment.Id);
        }
    }

    private async Task<string> ResolveNameAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return "Użytkownik";
        var name = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrEmpty(name) ? user.Email ?? "Użytkownik" : name;
    }

    private static WorkoutCommentDto Map(WorkoutComment c, string authorName) => new()
    {
        Id = c.Id,
        WorkoutDate = c.WorkoutDate,
        AuthorUserId = c.AuthorUserId,
        AuthorName = authorName,
        ByTrainer = c.ByTrainer,
        Text = c.Text,
        CreatedAtUtc = c.CreatedAt,
        IsRead = c.ReadAt is not null
    };
}
