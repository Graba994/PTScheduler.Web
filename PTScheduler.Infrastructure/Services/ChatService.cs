using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.Chat;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public sealed class ChatNotifier : IChatNotifier
{
    public event Action<int>? ConversationChanged;
    public void Notify(int clientId) => ConversationChanged?.Invoke(clientId);
}

public class ChatService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IWebPushService push,
    IChatNotifier notifier,
    IAppClock clock,
    ILogger<ChatService> logger) : IChatService
{
    public const int MaxLength = 2000;
    /// <summary>Limit przeciw zalewaniu rozmowy (i powiadomień push) — wiadomości na minutę od jednej osoby.</summary>
    public const int MaxPerMinute = 20;

    public async Task<bool> CanAccessAsync(int clientId, string userId, bool isAdmin)
    {
        if (isAdmin) return true;
        await using var db = dbFactory.CreateDbContext();
        return await db.Clients.AnyAsync(c => c.Id == clientId
            && (c.ApplicationUserId == userId || c.TrainerUserId == userId));
    }

    public async Task<List<ChatConversationDto>> GetConversationsAsync(string? trainerUserId)
    {
        await using var db = dbFactory.CreateDbContext();
        var clients = await db.Clients.AsNoTracking()
            .Where(c => trainerUserId == null || c.TrainerUserId == trainerUserId)
            .Select(c => new { c.Id, c.FirstName, c.LastName })
            .ToListAsync();
        var ids = clients.Select(c => c.Id).ToList();

        var last = await db.ChatMessages.AsNoTracking()
            .Where(m => ids.Contains(m.ClientId))
            .GroupBy(m => m.ClientId)
            .Select(g => g.OrderByDescending(m => m.SentAt).First())
            .ToListAsync();
        var unread = await db.ChatMessages.AsNoTracking()
            .Where(m => ids.Contains(m.ClientId) && !m.FromStaff && m.ReadAt == null)
            .GroupBy(m => m.ClientId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var lastBy = last.ToDictionary(m => m.ClientId);
        return clients
            .Select(c =>
            {
                lastBy.TryGetValue(c.Id, out var l);
                return new ChatConversationDto
                {
                    ClientId = c.Id,
                    ClientName = $"{c.FirstName} {c.LastName}".Trim(),
                    LastMessage = l?.Body,
                    LastMessageAt = l?.SentAt,
                    LastFromStaff = l?.FromStaff ?? false,
                    Unread = unread.GetValueOrDefault(c.Id)
                };
            })
            .OrderByDescending(c => c.LastMessageAt ?? DateTime.MinValue)
            .ThenBy(c => c.ClientName)
            .ToList();
    }

    public async Task<List<ChatMessageDto>> GetMessagesAsync(int clientId, int take = 100)
    {
        await using var db = dbFactory.CreateDbContext();
        var rows = await db.ChatMessages.AsNoTracking()
            .Where(m => m.ClientId == clientId)
            .OrderByDescending(m => m.SentAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync();
        var senderIds = rows.Select(r => r.SenderUserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => senderIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } n ? n : u.Email ?? "");
        return rows.OrderBy(r => r.SentAt).Select(r => new ChatMessageDto
        {
            Id = r.Id, ClientId = r.ClientId, SenderUserId = r.SenderUserId,
            SenderName = names.GetValueOrDefault(r.SenderUserId, ""), FromStaff = r.FromStaff,
            Body = r.Body, SentAt = r.SentAt, ReadAt = r.ReadAt
        }).ToList();
    }

    public async Task<(bool Ok, string? Error)> SendAsync(int clientId, string senderUserId, bool fromStaff, string body)
    {
        body = (body ?? string.Empty).Trim();
        if (body.Length == 0) return (false, "Wiadomość jest pusta.");
        if (body.Length > MaxLength) return (false, $"Wiadomość może mieć najwyżej {MaxLength} znaków.");

        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId);
        if (client is null) return (false, "Nie znaleziono rozmowy.");

        // Autoryzacja także tutaj, nie tylko na stronie: klient pisze wyłącznie we własnej rozmowie,
        // a po stronie studia — trener tego klienta albo administrator.
        var allowed = fromStaff
            ? client.TrainerUserId == senderUserId || await IsAdminAsync(db, senderUserId)
            : client.ApplicationUserId == senderUserId;
        if (!allowed)
        {
            logger.LogWarning("Chat: odrzucono wiadomość użytkownika {User} do rozmowy {ClientId}.", senderUserId, clientId);
            return (false, "Nie masz dostępu do tej rozmowy.");
        }

        var since = clock.UtcNow.AddMinutes(-1);
        if (await db.ChatMessages.CountAsync(m => m.SenderUserId == senderUserId && m.SentAt >= since) >= MaxPerMinute)
            return (false, "Za dużo wiadomości naraz — odczekaj chwilę.");

        db.ChatMessages.Add(new ChatMessage
        {
            ClientId = clientId, SenderUserId = senderUserId, FromStaff = fromStaff,
            Body = body, SentAt = clock.UtcNow
        });
        await db.SaveChangesAsync();
        notifier.Notify(clientId);

        // Push do drugiej strony: klient ↔ jego trener.
        var recipient = fromStaff ? client.ApplicationUserId : client.TrainerUserId;
        if (!string.IsNullOrEmpty(recipient))
        {
            var sender = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == senderUserId);
            var senderName = sender is null ? "Wiadomość" : $"{sender.FirstName} {sender.LastName}".Trim();
            try
            {
                await push.SendAsync(recipient, new PushMessageDto
                {
                    Title = $"💬 {(string.IsNullOrWhiteSpace(senderName) ? "Nowa wiadomość" : senderName)}",
                    Body = body.Length > 120 ? body[..117] + "…" : body,
                    Url = fromStaff ? "/chat" : $"/chat?client={clientId}"
                });
            }
            catch (Exception ex) { logger.LogWarning(ex, "Chat push failed for client {ClientId}.", clientId); }
        }
        return (true, null);
    }

    private static Task<bool> IsAdminAsync(ApplicationDbContext db, string userId) =>
        (from ur in db.UserRoles
         join r in db.Roles on ur.RoleId equals r.Id
         where ur.UserId == userId && r.Name == PTScheduler.Domain.Constants.Roles.Admin
         select ur).AnyAsync();

    public async Task MarkReadAsync(int clientId, bool readerIsStaff)
    {
        await using var db = dbFactory.CreateDbContext();
        var unread = await db.ChatMessages
            .Where(m => m.ClientId == clientId && m.FromStaff != readerIsStaff && m.ReadAt == null)
            .ToListAsync();
        if (unread.Count == 0) return;
        var now = clock.UtcNow;
        foreach (var m in unread) m.ReadAt = now;
        await db.SaveChangesAsync();
        notifier.Notify(clientId);
    }

    public async Task<int> GetUnreadCountAsync(string userId, bool isStaff, bool isAdmin)
    {
        await using var db = dbFactory.CreateDbContext();
        if (isStaff)
        {
            return await db.ChatMessages.CountAsync(m => !m.FromStaff && m.ReadAt == null
                && (isAdmin || m.Client.TrainerUserId == userId));
        }
        return await db.ChatMessages.CountAsync(m => m.FromStaff && m.ReadAt == null && m.Client.ApplicationUserId == userId);
    }
}
