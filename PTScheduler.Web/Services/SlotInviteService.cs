using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Messaging;
using PTScheduler.Application.Scheduling;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

/// <summary>
/// „Wolne okienka”: wolne godziny w grafiku trenera na dziś i najbliższe dni oraz klienci, którzy zwykle
/// trenują o tej porze. Trener sam wybiera, do kogo napisać — wiadomością w aplikacji (z powiadomieniem)
/// albo SMS-em z krótkim linkiem do rezerwacji z gotowym terminem. Nic nie wychodzi samo.
/// Hamulce: tę samą osobę zapraszamy najwyżej raz na kilka dni, SMS tylko przy zgodzie klienta i w limicie planu.
/// </summary>
public class SlotInviteService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ITrainerAvailabilityService availability,
    IAppClock clock,
    IChatService chat,
    ISmsService sms,
    IWebPushService push,
    INotificationPreferencesService prefs,
    EntitlementService entitlements,
    MessageDraftService drafts,
    ILogger<SlotInviteService> logger)
{
    public const int CooldownDays = 5;

    public sealed record Person(int ClientId, string FirstName, string LastName, string? Phone, int Hits, int SameWeekday,
        DateTime SuggestedStart, int SessionTypeId, bool SelfBooking, bool SmsAllowed, DateTime? LastInvitedUtc, bool HasApp = false);

    public sealed record Opportunity(FreeSlots.Window Window, string Label, int SessionMinutes, List<Person> People);

    /// <summary>Okienka na dziś (od za 3 godzin) i dwa kolejne dni — tylko te, na które jest kogo zaprosić.</summary>
    public async Task<List<Opportunity>> GetAsync(string trainerUserId, int days = 3, int maxPeople = 5)
    {
        var now = clock.LocalNow;
        var utcNow = clock.UtcNow;
        await using var db = await dbFactory.CreateDbContextAsync();

        // Typowa długość wizyty trenera (najczęstszy typ z ostatnich tygodni), domyślnie godzina.
        var since = now.AddDays(-70);
        var minutes = await db.Sessions.AsNoTracking()
            .Where(s => s.TrainerUserId == trainerUserId && s.StartTime >= since && s.Status == SessionStatus.Completed)
            .GroupBy(s => s.SessionType.DurationMinutes).OrderByDescending(g => g.Count()).Select(g => (int?)g.Key).FirstOrDefaultAsync() ?? 60;
        minutes = Math.Clamp(minutes, 30, 180);

        var clients = await db.Clients.AsNoTracking()
            .Where(c => c.TrainerUserId == trainerUserId && c.Status == ClientStatus.Active)
            .Select(c => new { c.Id, c.FirstName, c.LastName, c.Phone, c.AllowSelfBooking, c.ApplicationUserId })
            .ToListAsync();
        if (clients.Count == 0) return [];
        var ids = clients.Select(c => c.Id).ToList();
        var history = (await db.Sessions.AsNoTracking()
                .Where(s => ids.Contains(s.ClientId) && s.TrainerUserId == trainerUserId && s.StartTime >= since && s.StartTime < now
                    && s.Status == SessionStatus.Completed)
                .Select(s => new { s.ClientId, s.StartTime, s.SessionTypeId }).ToListAsync())
            .GroupBy(s => s.ClientId).ToDictionary(g => g.Key, g => g.Select(x => (x.StartTime, x.SessionTypeId)).ToList());
        var next = await db.Sessions.AsNoTracking()
            .Where(s => ids.Contains(s.ClientId) && s.StartTime >= now && (s.Status == SessionStatus.Scheduled || s.Status == SessionStatus.AwaitingPackage))
            .GroupBy(s => s.ClientId).Select(g => new { g.Key, Next = g.Min(s => s.StartTime) }).ToDictionaryAsync(x => x.Key, x => x.Next);
        var invited = await db.SlotInvitations.AsNoTracking().Where(i => ids.Contains(i.ClientId))
            .GroupBy(i => i.ClientId).Select(g => new { g.Key, Last = g.Max(i => i.SentAt) }).ToDictionaryAsync(x => x.Key, x => x.Last);

        var histories = clients.Where(c => history.ContainsKey(c.Id))
            .Select(c => new FreeSlots.ClientHistory(c.Id, history[c.Id], next.TryGetValue(c.Id, out var n) ? n : null,
                invited.TryGetValue(c.Id, out var i) ? i : null))
            .ToList();
        if (histories.Count == 0) return [];

        var smsOn = await SmsAvailableAsync();
        var pushOn = smsOn && (await push.GetSettingsAsync()).IsConfigured;
        var byId = clients.ToDictionary(c => c.Id);
        var result = new List<Opportunity>();
        var taken = new HashSet<int>();
        for (var d = 0; d < days; d++)
        {
            var date = DateOnly.FromDateTime(now).AddDays(d);
            List<FreeSlots.Window> windows;
            try { windows = FreeSlots.Merge(await availability.GetAvailableSlotsAsync(trainerUserId, date, minutes), now.AddHours(3), Math.Max(60, minutes)); }
            catch (Exception ex) { logger.LogDebug(ex, "Nie udało się policzyć wolnych terminów na {Date}.", date); continue; }
            foreach (var w in windows)
            {
                // Ta sama osoba nie trafia do kilku okienek naraz — dostaje propozycję tego najlepiej pasującego.
                var ranked = FreeSlots.Rank(w, histories.Where(h => !taken.Contains(h.ClientId)), now, utcNow, minutes, maxPeople, cooldownDays: CooldownDays);
                if (ranked.Count == 0) continue;
                var people = new List<Person>();
                foreach (var r in ranked)
                {
                    var c = byId[r.ClientId];
                    // Klient z aplikacją i powiadomieniami dostaje wiadomość w aplikacji — SMS tylko, gdy sam o to prosił.
                    var hasApp = await SmsPolicy.PushReplacesSmsAsync(push, prefs, pushOn, c.ApplicationUserId, NotificationTypes.PushMessages);
                    var smsAllowed = smsOn && !hasApp && !string.IsNullOrWhiteSpace(c.Phone)
                        && await prefs.IsEnabledAsync(c.ApplicationUserId, NotificationTypes.SmsTrainerMessages);
                    people.Add(new(c.Id, c.FirstName, c.LastName, c.Phone, r.Hits, r.SameWeekday, r.SuggestedStart, r.SessionTypeId,
                        c.AllowSelfBooking, smsAllowed, invited.TryGetValue(c.Id, out var li) ? li : null, hasApp));
                    taken.Add(c.Id);
                }
                result.Add(new(w, FreeSlots.Describe(w, now), minutes, people));
            }
        }
        return result;
    }

    public async Task<bool> SmsAvailableAsync()
    {
        try { return entitlements.IsAllowed("SmsReminders") && await sms.IsEnabledAsync(); }
        catch { return false; }
    }

    /// <summary>Wiadomość w stylu trenera z [klient] i [link] (osobno dla zapisów samodzielnych i przez trenera).</summary>
    public async Task<(string Text, bool FromAi)> TemplateAsync(string trainerUserId, Opportunity o, bool selfBooking, bool useAi)
    {
        var facts = new Dictionary<string, string>
        {
            ["termin"] = o.Label,
            ["rezerwacja"] = selfBooking ? "samodzielna" : "przez_trenera",
        };
        return await drafts.TemplateAsync(trainerUserId, DraftPurpose.SlotInvite, facts, useAi);
    }

    public sealed record SendResult(int ClientId, string Name, bool App, bool Sms, string? Error);

    /// <param name="baseUrl">Adres aplikacji do linku, gdy nie ustawiono domeny (np. adres z przeglądarki trenera).</param>
    public async Task<List<SendResult>> SendAsync(string trainerUserId, bool isAdmin, Opportunity o, IReadOnlyList<(int ClientId, bool App, bool Sms)> picks,
        string template, string? baseUrl)
    {
        var results = new List<SendResult>();
        var people = o.People.ToDictionary(p => p.ClientId);
        var smsOn = await SmsAvailableAsync();
        var maxSms = entitlements.Limit("MaxSmsPerMonth");
        var root = (AppUrls.PublicBase() ?? baseUrl ?? "").TrimEnd('/');
        await using var db = await dbFactory.CreateDbContextAsync();
        foreach (var (clientId, app, wantSms) in picks)
        {
            if (!people.TryGetValue(clientId, out var p)) continue;
            // Tylko własni klienci (właściciel studia — wszyscy).
            if (!isAdmin && !await db.Clients.AnyAsync(c => c.Id == clientId && c.TrainerUserId == trainerUserId)) continue;
            var name = $"{p.FirstName} {p.LastName}".Trim();
            var target = p.SelfBooking ? $"/my?termin={p.SuggestedStart:yyyy-MM-ddTHH:mm}&typ={p.SessionTypeId}" : "/chat";
            var link = new ShortLink { Code = ShortLinks.NewCode(), TargetPath = target, ExpiresAt = clock.UtcNow.AddDays(Math.Max(1, (o.Window.End - clock.LocalNow).TotalDays) + 2) };
            db.ShortLinks.Add(link);
            await db.SaveChangesAsync();
            var url = $"{root}/l/{link.Code}";
            var text = template.Replace(MessageTemplates.ClientTag, string.IsNullOrWhiteSpace(p.FirstName) ? "" : p.FirstName.Trim())
                .Replace(MessageTemplates.LinkTag, url).Replace(" !", "!");

            bool sentApp = false, sentSms = false;
            string? error = null;
            if (app)
            {
                var (ok, err) = await chat.SendAsync(clientId, trainerUserId, true, text);
                sentApp = ok;
                if (!ok) error = err ?? "Nie udało się wysłać wiadomości.";
            }
            if (wantSms && smsOn && p.SmsAllowed && !string.IsNullOrWhiteSpace(p.Phone) && root.StartsWith("http"))
            {
                var r = await sms.SendReminderAsync(p.Phone!, MessageTemplates.ForSms(text), maxSms);
                sentSms = r.Success;
                if (!r.Success) error = r.QuotaExceeded ? "Limit SMS w tym miesiącu się skończył." : r.Error ?? "SMS nie wyszedł.";
            }
            else if (wantSms) error ??= "SMS niedostępny dla tej osoby.";

            if (sentApp) db.SlotInvitations.Add(new SlotInvitation { TrainerUserId = trainerUserId, ClientId = clientId, SlotStart = o.Window.Start, SlotEnd = o.Window.End, Channel = "app", ShortLinkId = link.Id });
            if (sentSms) db.SlotInvitations.Add(new SlotInvitation { TrainerUserId = trainerUserId, ClientId = clientId, SlotStart = o.Window.Start, SlotEnd = o.Window.End, Channel = "sms", ShortLinkId = link.Id });
            await db.SaveChangesAsync();
            results.Add(new(clientId, name, sentApp, sentSms, error));
        }
        return results;
    }

    /// <summary>Ostatnie zaproszenia trenera z liczbą kliknięć w link (czy zaproszenie zadziałało).</summary>
    public async Task<List<(string Name, DateTime SlotStart, string Channel, int Clicks, bool Booked)>> RecentAsync(string trainerUserId, int take = 8)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var since = clock.UtcNow.AddDays(-14);
        var rows = await db.SlotInvitations.AsNoTracking().Where(i => i.TrainerUserId == trainerUserId && i.SentAt >= since)
            .OrderByDescending(i => i.SentAt).Take(take)
            .Select(i => new { i.Client.FirstName, i.Client.LastName, i.ClientId, i.SlotStart, i.SlotEnd, i.Channel, Clicks = i.ShortLink != null ? i.ShortLink.Clicks : 0 })
            .ToListAsync();
        var result = new List<(string, DateTime, string, int, bool)>();
        foreach (var r in rows)
        {
            var booked = await db.Sessions.AnyAsync(s => s.ClientId == r.ClientId && s.StartTime >= r.SlotStart && s.StartTime < r.SlotEnd && s.Status != SessionStatus.Cancelled);
            result.Add(($"{r.FirstName} {r.LastName}".Trim(), r.SlotStart, r.Channel, r.Clicks, booked));
        }
        return result;
    }
}
