using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Messaging;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

/// <summary>
/// Szkice wiadomości do klienta w stylu trenera: profil stylu, przykłady (wpisane przez trenera i zapamiętane
/// z jego poprawek), tekst od AI przez Portal albo szablon. Do AI trafia tylko znacznik [klient] zamiast imienia.
/// Wysyłka czatem w aplikacji; WhatsApp i SMS otwierają się na telefonie trenera z gotowym tekstem.
/// </summary>
public class MessageDraftService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    PlatformAssistantService platform,
    IChatService chat,
    IAppClock clock)
{
    public const int MaxLearned = 12;
    public const int MaxOwn = 10;

    public sealed record Draft(string Text, bool FromAi, string Generated, DraftPurpose Purpose);

    public async Task<TrainerStyle> GetStyleAsync(string trainerUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.TrainerStyles.AsNoTracking().FirstOrDefaultAsync(s => s.TrainerUserId == trainerUserId)
            ?? new TrainerStyle { TrainerUserId = trainerUserId };
    }

    public async Task SaveStyleAsync(TrainerStyle style)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.TrainerStyles.FirstOrDefaultAsync(s => s.TrainerUserId == style.TrainerUserId);
        if (row is null) db.TrainerStyles.Add(row = new TrainerStyle { TrainerUserId = style.TrainerUserId });
        row.Address = style.Address == "pan" ? "pan" : "ty";
        row.Emoji = style.Emoji is "brak" or "często" ? style.Emoji : "czasem";
        row.Length = style.Length == "srednio" ? "srednio" : "krotko";
        row.Greeting = Cut(style.Greeting, 40);
        row.Signature = Cut(style.Signature, 80);
        row.Notes = Cut(style.Notes, 300);
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<List<TrainerStyleExample>> GetExamplesAsync(string trainerUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.TrainerStyleExamples.AsNoTracking().Where(e => e.TrainerUserId == trainerUserId)
            .OrderByDescending(e => e.CreatedAt).ToListAsync();
    }

    public async Task<string?> AddExampleAsync(string trainerUserId, string text)
    {
        text = text.Trim();
        if (text.Length < 15) return "Przykład jest za krótki — wklej całą wiadomość.";
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.TrainerStyleExamples.CountAsync(e => e.TrainerUserId == trainerUserId && e.Source == "wzór") >= MaxOwn)
            return $"Najwyżej {MaxOwn} własnych przykładów — usuń któryś, żeby dodać nowy.";
        db.TrainerStyleExamples.Add(new TrainerStyleExample
        {
            TrainerUserId = trainerUserId, Source = "wzór",
            Text = MessageTemplates.ToExample(text, "", null),
        });
        await db.SaveChangesAsync();
        return null;
    }

    public async Task DeleteExampleAsync(string trainerUserId, int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.TrainerStyleExamples.Where(e => e.Id == id && e.TrainerUserId == trainerUserId).ExecuteDeleteAsync();
    }

    /// <summary>Szkic do konkretnego klienta (null, gdy klient nie należy do trenera).</summary>
    public async Task<Draft?> DraftAsync(string trainerUserId, bool isAdmin, int clientId, DraftPurpose purpose, bool useAi)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var client = await db.Clients.AsNoTracking().Where(c => c.Id == clientId && (isAdmin || c.TrainerUserId == trainerUserId))
            .Select(c => new { c.Id, c.FirstName }).FirstOrDefaultAsync();
        if (client is null) return null;

        var facts = await FactsAsync(db, clientId, purpose);
        var style = await GetStyleAsync(trainerUserId);
        var fallback = MessageTemplates.Build(purpose, new(style.Address, style.Emoji, style.Greeting, style.Signature), facts);
        var aiFacts = new Dictionary<string, string>(facts);
        if (style.Address != "pan") aiFacts["klient"] = MessageTemplates.ClientTag;

        var examples = (await GetExamplesAsync(trainerUserId))
            .OrderByDescending(e => e.Source == "poprawka" && e.Purpose == purpose.ToString())
            .ThenByDescending(e => e.Source == "wzór")
            .ThenByDescending(e => e.CreatedAt)
            .Take(5).Select(e => e.Text).ToList();
        var (text, ai) = await platform.DraftAsync(MessageTemplates.Label(purpose), aiFacts, fallback, style, examples, useAi);
        var personal = text.Replace(MessageTemplates.ClientTag, string.IsNullOrWhiteSpace(client.FirstName) ? "" : client.FirstName.Trim());
        return new(personal, ai, personal, purpose);
    }

    private async Task<Dictionary<string, string>> FactsAsync(ApplicationDbContext db, int clientId, DraftPurpose purpose)
    {
        var facts = new Dictionary<string, string>();
        var now = clock.LocalNow;
        var last = await db.Sessions.Where(s => s.ClientId == clientId && s.StartTime < now && s.Status == SessionStatus.Completed)
            .MaxAsync(s => (DateTime?)s.StartTime);
        if (last is { } l && purpose == DraftPurpose.Comeback) facts["dni_od_treningu"] = (now.Date - l.Date).Days.ToString();
        if (purpose is DraftPurpose.Package)
        {
            var utcNow = clock.UtcNow;
            var left = await db.SessionPackages.Where(p => p.ClientId == clientId && p.Status == PackageStatus.Active && (p.ExpiresAt == null || p.ExpiresAt > utcNow))
                .SumAsync(p => (int?)(p.TotalSessions - p.UsedSessions)) ?? 0;
            facts["pozostale_wejscia"] = Math.Max(0, left).ToString();
        }
        if (purpose is DraftPurpose.Payment)
        {
            var debt = (await PTScheduler.Infrastructure.Services.DebtService.LoadAsync(db, now, null, clientId)).FirstOrDefault();
            if (debt is not null)
            {
                var sessions = debt.Items.Where(i => i.Kind == "session").ToList();
                var parts = new List<string>();
                if (sessions.Count == 1) parts.Add($"za trening {sessions[0].At:d.MM}");
                else if (sessions.Count > 1) parts.Add($"za {sessions.Count} treningi ({string.Join(", ", sessions.Take(4).Select(s => s.At.ToString("d.MM")))})");
                parts.AddRange(debt.Items.Where(i => i.Kind == "package").Select(i => "za " + i.Label.Replace("Pakiet", "pakiet")));
                var forWhat = string.Join(" i ", parts);
                facts["za_co"] = forWhat.Length > 150 ? forWhat[..147] + "…" : forWhat;
                if (debt.Total > 0 && !debt.HasUnknownAmount) facts["kwota"] = debt.Total.ToString("0.##");
            }
        }
        if (purpose is DraftPurpose.Congrats)
            facts["liczba_treningow"] = (await db.Sessions.CountAsync(s => s.ClientId == clientId && s.Status == SessionStatus.Completed)).ToString();
        return facts;
    }

    /// <summary>
    /// Wysyła wiadomość czatem i — jeśli trener poprawił szkic — zapamiętuje poprawioną wersję jako przykład stylu.
    /// </summary>
    public async Task<(bool Ok, string? Error)> SendChatAsync(string trainerUserId, bool isAdmin, int clientId, Draft draft, string final)
    {
        if (!await chat.CanAccessAsync(clientId, trainerUserId, isAdmin)) return (false, "Brak dostępu do tego klienta.");
        var result = await chat.SendAsync(clientId, trainerUserId, fromStaff: true, final.Trim());
        if (result.Ok) await LearnAsync(trainerUserId, clientId, draft, final);
        return result;
    }

    /// <summary>Zapamiętuje poprawkę (także przy wysyłce WhatsAppem, SMS-em albo po skopiowaniu).</summary>
    public async Task LearnAsync(string trainerUserId, int clientId, Draft draft, string final)
    {
        if (!MessageTemplates.IsEdited(draft.Generated, final)) return;
        await using var db = await dbFactory.CreateDbContextAsync();
        var client = await db.Clients.AsNoTracking().Where(c => c.Id == clientId).Select(c => new { c.FirstName, c.LastName }).FirstOrDefaultAsync();
        if (client is null) return;
        db.TrainerStyleExamples.Add(new TrainerStyleExample
        {
            TrainerUserId = trainerUserId,
            Source = "poprawka",
            Purpose = draft.Purpose.ToString(),
            Text = MessageTemplates.ToExample(final, client.FirstName, client.LastName),
        });
        await db.SaveChangesAsync();
        // Trzymamy najnowsze poprawki — starsze wypadają.
        var stale = await db.TrainerStyleExamples.Where(e => e.TrainerUserId == trainerUserId && e.Source == "poprawka")
            .OrderByDescending(e => e.CreatedAt).Skip(MaxLearned).Select(e => e.Id).ToListAsync();
        if (stale.Count > 0) await db.TrainerStyleExamples.Where(e => stale.Contains(e.Id)).ExecuteDeleteAsync();
    }

    private static string? Cut(string? v, int max) => string.IsNullOrWhiteSpace(v) ? null : v.Trim().Length <= max ? v.Trim() : v.Trim()[..max];
}
