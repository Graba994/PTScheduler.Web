using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services.Social;

/// <summary>
/// Promowanie postów w Meta Ads z panelu. Zasady bezpieczeństwa pieniędzy:
/// promocja powstaje WSTRZYMANA, uruchamia ją człowiek po potwierdzeniu pełnej kwoty; budżet ma twarde limity;
/// przed uruchomieniem sprawdzamy stan konta reklamowego. Tryb „ruch na stronę” idzie przez link śledzący,
/// więc rejestracje z reklamy liczą się w „Linki i wyniki”.
/// </summary>
public class AdsService(
    IDbContextFactory<PortalDbContext> dbFactory,
    AdsClient client,
    SocialService social,
    InstagramService ig,
    LinkService links,
    SiteSettingsService settings,
    ILogger<AdsService> logger)
{
    public const decimal MinDaily = 5m, MaxDaily = 500m, MaxTotal = 5000m;
    public const int MaxDays = 30;

    public sealed record Form(int PostId, string Mode, decimal DailyBudget, int Days, int AgeMin, int AgeMax,
        List<(string Id, string Name)> Interests, bool OnInstagram, bool OnFacebook, DateTime? StartUtc = null);

    public sealed record Row(AdPromotion Promotion, string? LinkCode, int Signups, int Paid);

    /// <summary>Walidacja budżetu i terminu (bez wywołań API).</summary>
    public static string? Validate(Form f)
    {
        if (f.Mode is not ("traffic" or "boost")) return "Wybierz rodzaj promocji.";
        if (f.DailyBudget < MinDaily || f.DailyBudget > MaxDaily) return $"Budżet dzienny: od {MinDaily:0} do {MaxDaily:0} zł.";
        if (f.Days is < 1 or > MaxDays) return $"Czas trwania: od 1 do {MaxDays} dni.";
        if (f.DailyBudget * f.Days > MaxTotal) return $"Łączny budżet najwyżej {MaxTotal:0} zł — skróć promocję albo zmniejsz budżet dzienny.";
        if (f.AgeMin < 18 || f.AgeMax > 65 || f.AgeMin >= f.AgeMax) return "Wiek odbiorców: od 18 do 65 lat (od < do).";
        if (!f.OnInstagram && !f.OnFacebook) return "Wybierz, gdzie wyświetlać reklamę (Instagram, Facebook).";
        if (f.Interests.Count > 10) return "Najwyżej 10 zainteresowań.";
        return null;
    }

    public async Task<string> AccountIdAsync() => AdsClient.ActId(await settings.GetAsync(K.MetaAdAccount));

    public async Task SaveAccountAsync(string raw) => await settings.SetAsync(K.MetaAdAccount, AdsClient.ActId(raw));

    /// <summary>Stan: token, konto i jego status (do karty w panelu).</summary>
    public async Task<(AdsClient.Account? Account, string? Error)> CheckAsync(CancellationToken ct = default)
    {
        var (token, exp) = await social.MetaUserTokenAsync();
        if (token is null)
            return (null, exp is null
                ? "Połącz Facebooka w zakładce Kanały (token z uprawnieniami ads_management i ads_read)."
                : "Token Meta wygasł — wygeneruj nowy w zakładce Kanały (z App ID i App secret wytrzyma ok. 60 dni).");
        var act = await AccountIdAsync();
        if (act.Length == 0) return (null, "Wybierz konto reklamowe.");
        try { return (await client.AccountAsync(act, token, ct), null); }
        catch (InstagramApiException ex) { return (null, ex.Message); }
    }

    public async Task<(List<(string Id, string Name, string Currency)> Accounts, string? Error)> AccountsAsync()
    {
        var (token, _) = await social.MetaUserTokenAsync();
        if (token is null) return ([], "Brak ważnego tokenu Meta — połącz Facebooka w zakładce Kanały.");
        try { return (await client.AccountsAsync(token), null); }
        catch (InstagramApiException ex) { return ([], ex.Message); }
    }

    public async Task<(List<AdsClient.Interest> Items, string? Error)> InterestsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2) return ([], null);
        var (token, _) = await social.MetaUserTokenAsync();
        if (token is null) return ([], "Brak ważnego tokenu Meta.");
        try { return (await client.InterestsAsync(query.Trim(), token), null); }
        catch (InstagramApiException ex) { return ([], ex.Message); }
    }

    public async Task<List<Row>> ListAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var list = await db.AdPromotions.AsNoTracking().OrderByDescending(p => p.Id).Take(100).ToListAsync();
        var linkIds = list.Where(p => p.LinkId != null).Select(p => p.LinkId!.Value).ToList();
        var codes = await db.TrackedLinks.AsNoTracking().Where(l => linkIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Code);
        var codeList = codes.Values.ToList();
        var tenants = await db.Tenants.AsNoTracking().Where(t => t.SignupSource != null && codeList.Contains(t.SignupSource))
            .Select(t => new { t.SignupSource, Paid = t.FirstPaidAt != null }).ToListAsync();
        return list.Select(p =>
        {
            var code = p.LinkId is int lid ? codes.GetValueOrDefault(lid) : null;
            var mine = tenants.Where(t => t.SignupSource == code).ToList();
            return new Row(p, code, code is null ? 0 : mine.Count, code is null ? 0 : mine.Count(t => t.Paid));
        }).ToList();
    }

    /// <summary>Tworzy kampanię, zestaw, kreację i reklamę — wszystko WSTRZYMANE.</summary>
    public async Task<(bool Ok, string Message, int? Id)> CreateAsync(Form f, CancellationToken ct = default)
    {
        if (Validate(f) is { } invalid) return (false, invalid, null);
        var (account, accountError) = await CheckAsync(ct);
        if (account is null) return (false, accountError ?? "Brak konta reklamowego.", null);
        var (token, _) = await social.MetaUserTokenAsync();
        var post = await ig.PostAsync(f.PostId);
        if (post is null) return (false, "Nie ma takiego posta.", null);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var page = await db.SocialChannels.AsNoTracking().FirstOrDefaultAsync(c => c.Platform == SocialPlatform.Facebook && c.Enabled && c.ExternalId != null, ct);
        if (page is null) return (false, "Reklamy Meta potrzebują strony na Facebooku — połącz ją w zakładce Kanały.", null);
        var igAccount = await db.InstagramAccounts.AsNoTracking().FirstOrDefaultAsync(ct);
        var igUserId = f.OnInstagram ? igAccount?.IgUserId : null;
        if (f.OnInstagram && igUserId is null) return (false, "Do reklam na Instagramie połącz Instagram (najlepiej przez stronę na Facebooku).", null);

        string? fbPostId = null, igMediaId = null;
        if (f.Mode == "boost")
        {
            var fbTarget = await db.SocialPostTargets.AsNoTracking()
                .FirstOrDefaultAsync(t => t.PostId == post.Id && t.ChannelId == page.Id && t.Status == SocialTargetStatus.Published, ct);
            if (fbTarget?.ExternalId is { } ext) fbPostId = ext.Contains('_') ? ext : $"{page.ExternalId}_{ext}";
            igMediaId = post.ToInstagram && post.Status == InstagramPostStatus.Published ? post.IgMediaId : null;
            if (fbPostId is null && igMediaId is null) return (false, "Podbić można tylko post opublikowany na Facebooku albo Instagramie.", null);
        }
        else if (post.Assets.FirstOrDefault(a => a.Kind == "image") is null)
            return (false, "Reklama „ruch na stronę” potrzebuje grafiki — dodaj ją do posta (filmy: użyj „podbicia” opublikowanej rolki).", null);

        var title = string.IsNullOrWhiteSpace(post.Title) ? $"Post #{post.Id}" : post.Title;
        var start = f.StartUtc is { } s && s > DateTime.UtcNow ? s : DateTime.UtcNow.AddMinutes(15);
        var promo = new AdPromotion
        {
            PostId = post.Id, Title = title.Length > 200 ? title[..200] : title, Mode = f.Mode, Account = account.Id, Currency = account.Currency,
            DailyBudget = f.DailyBudget, Days = f.Days, StartAt = start, EndAt = start.AddDays(f.Days), AgeMin = f.AgeMin, AgeMax = f.AgeMax,
            InterestsJson = JsonSerializer.Serialize(f.Interests.Select(i => new { id = i.Id, name = i.Name })), OnInstagram = f.OnInstagram, OnFacebook = f.OnFacebook,
            Status = AdPromotionStatus.Paused
        };
        db.AdPromotions.Add(promo);
        await db.SaveChangesAsync(ct);

        var name = $"Hantly · {title} · {DateTime.UtcNow:yyyy-MM-dd}";
        var traffic = f.Mode == "traffic";
        try
        {
            promo.CampaignId = await client.CampaignAsync(account.Id, token!, name, traffic, ct);
            promo.AdSetId = await client.AdSetAsync(account.Id, token!, promo.CampaignId, name, traffic, f.DailyBudget, promo.StartAt, promo.EndAt,
                AdsClient.Targeting(f.AgeMin, f.AgeMax, f.Interests, f.OnInstagram, f.OnFacebook), ct);
            if (traffic)
            {
                var (link, _) = await links.CreateAsync($"Reklama: {title}", "reklama", "/register");
                promo.LinkId = link?.Id;
                var image = await ig.AssetBytesAsync(post.Assets.First(a => a.Kind == "image").Token)
                            ?? throw new InstagramApiException("Nie znaleziono grafiki posta.");
                var hash = await client.ImageHashAsync(account.Id, token!, image, ct);
                var message = LinkService.ForInstagram(post.Caption).Replace(LinkService.InstagramText, "link poniżej");
                promo.CreativeId = await client.LinkCreativeAsync(account.Id, token!, name, page.ExternalId!, igUserId,
                    message.Length > 1500 ? message[..1500] : message, title.Length > 40 ? title[..40] : title, links.Url(link!.Code), hash, ct);
            }
            else promo.CreativeId = await client.PostCreativeAsync(account.Id, token!, name, f.OnFacebook ? fbPostId : null, igMediaId, igUserId, ct);
            promo.AdId = await client.AdAsync(account.Id, token!, promo.AdSetId, promo.CreativeId, name, ct);
            await db.SaveChangesAsync(ct);
            var total = f.DailyBudget * f.Days;
            return (true, $"Promocja gotowa i WSTRZYMANA — sprawdź i kliknij „Uruchom” (najwyżej {total:0.##} {account.Currency}).", promo.Id);
        }
        catch (InstagramApiException ex)
        {
            promo.Status = AdPromotionStatus.Failed;
            promo.Error = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Meta Ads: nie udało się utworzyć promocji: {Message}", ex.Message);
            return (false, $"Meta odrzuciła promocję: {ex.Message} (nic nie zostało uruchomione).", promo.Id);
        }
    }

    /// <summary>Uruchomienie (po potwierdzeniu w panelu): reklama, zestaw i kampania → ACTIVE. Najpierw sprawdzamy konto.</summary>
    public async Task<(bool Ok, string Message)> ActivateAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var p = await db.AdPromotions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return (false, "Nie ma takiej promocji.");
        if (p.Status is AdPromotionStatus.Active) return (true, "Promocja już działa.");
        if (p.CampaignId is null || p.AdSetId is null || p.AdId is null) return (false, "Promocja nie została utworzona w całości — utwórz ją od nowa.");
        if (p.EndAt <= DateTime.UtcNow) return (false, "Termin promocji minął — utwórz nową.");
        var (account, error) = await CheckAsync(ct);
        if (account is null) return (false, error ?? "Brak konta reklamowego.");
        if (account.Status != 1) return (false, $"Konto reklamowe: {AdsClient.StatusText(account.Status)}. Popraw to w Menedżerze reklam Meta.");
        var (token, _) = await social.MetaUserTokenAsync();
        try
        {
            await client.SetStatusAsync(p.AdId, token!, true, ct);
            await client.SetStatusAsync(p.AdSetId, token!, true, ct);
            await client.SetStatusAsync(p.CampaignId, token!, true, ct);
            p.Status = AdPromotionStatus.Active;
            p.ActivatedAt ??= DateTime.UtcNow;
            p.Error = null;
            await db.SaveChangesAsync(ct);
            return (true, "Promocja uruchomiona. Meta sprawdza reklamę (zwykle do 24 h), potem zacznie się wyświetlać.");
        }
        catch (InstagramApiException ex) { return (false, ex.Message); }
    }

    public async Task<(bool Ok, string Message)> PauseAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var p = await db.AdPromotions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p?.CampaignId is null) return (false, "Nie ma takiej promocji.");
        var (token, _) = await social.MetaUserTokenAsync();
        if (token is null) return (false, "Brak ważnego tokenu Meta — wstrzymaj kampanię w Menedżerze reklam albo połącz Facebooka ponownie.");
        try
        {
            await client.SetStatusAsync(p.CampaignId, token, false, ct);
            p.Status = AdPromotionStatus.Paused;
            await db.SaveChangesAsync(ct);
            return (true, "Promocja wstrzymana.");
        }
        catch (InstagramApiException ex) { return (false, ex.Message); }
    }

    /// <summary>Wyniki z Meta (wydatki, zasięg, kliknięcia) — dla aktywnych i niedawno zakończonych; koniec terminu → „zakończona”.</summary>
    public async Task<int> RefreshAsync(int? onlyId = null, CancellationToken ct = default)
    {
        var (token, _) = await social.MetaUserTokenAsync();
        if (token is null) return 0;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var cutoff = DateTime.UtcNow.AddDays(-3);
        var q = db.AdPromotions.Where(p => p.CampaignId != null && p.Status != AdPromotionStatus.Failed);
        q = onlyId is int id ? q.Where(p => p.Id == id) : q.Where(p => p.Status == AdPromotionStatus.Active || p.EndAt > cutoff);
        var list = await q.ToListAsync(ct);
        foreach (var p in list)
        {
            try
            {
                var i = await client.InsightsAsync(p.CampaignId!, token, ct);
                (p.Spend, p.Impressions, p.Reach, p.Clicks, p.Engagements) = (i.Spend, i.Impressions, i.Reach, p.Mode == "traffic" ? i.LinkClicks : i.Clicks, i.Engagements);
                p.InsightsAt = DateTime.UtcNow;
                if (p.Status == AdPromotionStatus.Active && p.EndAt <= DateTime.UtcNow) p.Status = AdPromotionStatus.Ended;
            }
            catch (InstagramApiException ex) { logger.LogInformation("Meta Ads: wyniki promocji {Id}: {Message}", p.Id, ex.Message); }
        }
        await db.SaveChangesAsync(ct);
        return list.Count;
    }
}
