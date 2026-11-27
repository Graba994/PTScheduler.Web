using System.Globalization;
using System.Text.Json;
using static PTScheduler.Portal.Services.Social.MetaApi;

namespace PTScheduler.Portal.Services.Social;

/// <summary>
/// Meta Marketing API: konto reklamowe, kampania → zestaw reklam → kreacja → reklama, wyniki i włącz/wyłącz.
/// Wszystko powstaje jako WSTRZYMANE (PAUSED) — uruchomienie to osobna, świadoma decyzja w panelu.
/// Wymaga tokenu użytkownika z uprawnieniami ads_management i ads_read.
/// </summary>
public class AdsClient(IHttpClientFactory httpFactory, IConfiguration? config = null)
{
    public const string Version = "v24.0";
    private string Graph => (config?["Social:FacebookGraphUrl"] is { Length: > 0 } g ? g : "https://graph.facebook.com").TrimEnd('/');

    public sealed record Account(string Id, string Name, string Currency, int Status, string? DisableReason, decimal Spent, bool HasFunding);
    public sealed record Interest(string Id, string Name, long? Size);
    public sealed record Insights(decimal Spend, int Impressions, int Reach, int Clicks, int LinkClicks, int Engagements);

    /// <summary>Konto reklamowe w formacie act_123 (przyjmuje też samą liczbę).</summary>
    public static string ActId(string raw)
    {
        var digits = new string((raw ?? "").Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? "" : $"act_{digits}";
    }

    /// <summary>Budżet w jednostkach waluty (zł) → najmniejsze jednostki API (grosze).</summary>
    public static long Minor(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    /// <summary>Opis stanu konta reklamowego (account_status) po polsku.</summary>
    public static string StatusText(int status) => status switch
    {
        1 => "aktywne",
        2 => "wyłączone",
        3 => "nieopłacone — uzupełnij płatność w Menedżerze reklam",
        7 => "w weryfikacji ryzyka",
        8 => "czeka na rozliczenie",
        9 => "w okresie karencji",
        100 => "do zamknięcia",
        101 => "zamknięte",
        _ => $"stan {status}"
    };

    private Task<JsonElement> Get(string path, string query, string token, CancellationToken ct) =>
        SendAsync(httpFactory, HttpMethod.Get, $"{Graph}/{Version}/{path}?{query}{(query.Length > 0 ? "&" : "")}access_token={Uri.EscapeDataString(token)}", null, ct);

    private Task<JsonElement> Post(string path, Dictionary<string, string> form, string token, CancellationToken ct)
    {
        form["access_token"] = token;
        return SendAsync(httpFactory, HttpMethod.Post, $"{Graph}/{Version}/{path}", form, ct);
    }

    public async Task<Account> AccountAsync(string act, string token, CancellationToken ct = default)
    {
        var j = await Get(act, "fields=id,name,currency,account_status,disable_reason,amount_spent,funding_source_details", token, ct);
        var spent = decimal.TryParse(Str(j, "amount_spent"), NumberStyles.Any, CultureInfo.InvariantCulture, out var s) ? s / 100m : 0m;
        return new Account(Str(j, "id") ?? act, Str(j, "name") ?? act, Str(j, "currency") ?? "PLN", Int(j, "account_status"), Str(j, "disable_reason"),
            spent, j.TryGetProperty("funding_source_details", out var f) && f.ValueKind == JsonValueKind.Object);
    }

    /// <summary>Konta reklamowe widoczne dla tokenu (do wyboru w panelu).</summary>
    public async Task<List<(string Id, string Name, string Currency)>> AccountsAsync(string token, CancellationToken ct = default)
    {
        var j = await Get("me/adaccounts", "fields=id,name,currency&limit=50", token, ct);
        var list = new List<(string, string, string)>();
        if (j.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array)
            foreach (var a in d.EnumerateArray()) list.Add((Str(a, "id") ?? "", Str(a, "name") ?? "", Str(a, "currency") ?? ""));
        return list;
    }

    public async Task<List<Interest>> InterestsAsync(string query, string token, CancellationToken ct = default)
    {
        var j = await Get("search", $"type=adinterest&q={Uri.EscapeDataString(query)}&limit=10&locale=pl_PL", token, ct);
        var list = new List<Interest>();
        if (j.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array)
            foreach (var i in d.EnumerateArray())
                list.Add(new(Str(i, "id") ?? "", Str(i, "name") ?? "", i.TryGetProperty("audience_size_upper_bound", out var u) && u.TryGetInt64(out var n) ? n : null));
        return list;
    }

    /// <summary>Kampania (wstrzymana). traffic → OUTCOME_TRAFFIC, boost → OUTCOME_ENGAGEMENT.</summary>
    public async Task<string> CampaignAsync(string act, string token, string name, bool traffic, CancellationToken ct = default) =>
        Str(await Post($"{act}/campaigns", new()
        {
            ["name"] = name,
            ["objective"] = traffic ? "OUTCOME_TRAFFIC" : "OUTCOME_ENGAGEMENT",
            ["status"] = "PAUSED",
            ["special_ad_categories"] = "[]",
            ["buying_type"] = "AUCTION",
            ["is_adset_budget_sharing_enabled"] = "false"
        }, token, ct), "id") ?? throw new InstagramApiException("Meta nie zwróciła id kampanii.");

    public static string Targeting(int ageMin, int ageMax, IEnumerable<(string Id, string Name)> interests, bool instagram, bool facebook)
    {
        var platforms = new List<string>();
        if (facebook) platforms.Add("facebook");
        if (instagram) platforms.Add("instagram");
        var t = new Dictionary<string, object>
        {
            ["geo_locations"] = new { countries = new[] { "PL" } },
            ["age_min"] = ageMin,
            ["age_max"] = ageMax,
            ["publisher_platforms"] = platforms,
            ["targeting_automation"] = new { advantage_audience = 0 }
        };
        var list = interests.Select(i => new { id = i.Id, name = i.Name }).ToList();
        if (list.Count > 0) t["flexible_spec"] = new[] { new { interests = list } };
        return JsonSerializer.Serialize(t);
    }

    /// <summary>Zestaw reklam (wstrzymany): dzienny budżet, termin, grupa odbiorców.</summary>
    public async Task<string> AdSetAsync(string act, string token, string campaignId, string name, bool traffic, decimal dailyBudget,
        DateTime startUtc, DateTime endUtc, string targetingJson, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["name"] = name,
            ["campaign_id"] = campaignId,
            ["daily_budget"] = Minor(dailyBudget).ToString(CultureInfo.InvariantCulture),
            ["billing_event"] = "IMPRESSIONS",
            ["optimization_goal"] = traffic ? "LINK_CLICKS" : "POST_ENGAGEMENT",
            ["bid_strategy"] = "LOWEST_COST_WITHOUT_CAP",
            ["start_time"] = startUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["end_time"] = endUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["targeting"] = targetingJson,
            ["status"] = "PAUSED"
        };
        if (traffic) form["destination_type"] = "WEBSITE";
        return Str(await Post($"{act}/adsets", form, token, ct), "id") ?? throw new InstagramApiException("Meta nie zwróciła id zestawu reklam.");
    }

    /// <summary>Obraz do kreacji (bajty, base64) → image_hash.</summary>
    public async Task<string> ImageHashAsync(string act, string token, byte[] jpeg, CancellationToken ct = default)
    {
        var j = await Post($"{act}/adimages", new() { ["bytes"] = Convert.ToBase64String(jpeg) }, token, ct);
        if (j.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Object)
            foreach (var img in images.EnumerateObject())
                if (Str(img.Value, "hash") is { } hash) return hash;
        throw new InstagramApiException("Meta nie przyjęła grafiki reklamy.");
    }

    /// <summary>Kreacja „ruch na stronę”: grafika, tekst, link (śledzący) i przycisk „Zarejestruj się”.</summary>
    public async Task<string> LinkCreativeAsync(string act, string token, string name, string pageId, string? instagramUserId, string message,
        string headline, string link, string imageHash, CancellationToken ct = default)
    {
        var spec = new Dictionary<string, object>
        {
            ["page_id"] = pageId,
            ["link_data"] = new
            {
                link, message, name = headline, image_hash = imageHash,
                call_to_action = new { type = "SIGN_UP", value = new { link } }
            }
        };
        if (instagramUserId is { Length: > 0 }) spec["instagram_user_id"] = instagramUserId;
        return Str(await Post($"{act}/adcreatives", new() { ["name"] = name, ["object_story_spec"] = JsonSerializer.Serialize(spec) }, token, ct), "id")
               ?? throw new InstagramApiException("Meta nie zwróciła id kreacji.");
    }

    /// <summary>Kreacja „podbicie posta”: istniejący post z Facebooka (object_story_id) albo z Instagrama (source_instagram_media_id).</summary>
    public async Task<string> PostCreativeAsync(string act, string token, string name, string? facebookPostId, string? instagramMediaId,
        string? instagramUserId, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["name"] = name };
        if (facebookPostId is { Length: > 0 }) form["object_story_id"] = facebookPostId;
        else
        {
            form["source_instagram_media_id"] = instagramMediaId ?? throw new InstagramApiException("Brak posta do podbicia.");
            if (instagramUserId is { Length: > 0 }) form["instagram_user_id"] = instagramUserId;
        }
        return Str(await Post($"{act}/adcreatives", form, token, ct), "id") ?? throw new InstagramApiException("Meta nie zwróciła id kreacji.");
    }

    public async Task<string> AdAsync(string act, string token, string adSetId, string creativeId, string name, CancellationToken ct = default) =>
        Str(await Post($"{act}/ads", new()
        {
            ["name"] = name, ["adset_id"] = adSetId, ["creative"] = JsonSerializer.Serialize(new { creative_id = creativeId }), ["status"] = "PAUSED"
        }, token, ct), "id") ?? throw new InstagramApiException("Meta nie zwróciła id reklamy.");

    /// <summary>ACTIVE / PAUSED dla kampanii, zestawu albo reklamy.</summary>
    public Task SetStatusAsync(string objectId, string token, bool active, CancellationToken ct = default) =>
        Post(objectId, new() { ["status"] = active ? "ACTIVE" : "PAUSED" }, token, ct);

    public async Task<Insights> InsightsAsync(string campaignId, string token, CancellationToken ct = default)
    {
        var j = await Get($"{campaignId}/insights", "fields=spend,impressions,reach,clicks,inline_link_clicks,inline_post_engagement&date_preset=maximum", token, ct);
        if (!j.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.Array || d.GetArrayLength() == 0) return new(0, 0, 0, 0, 0, 0);
        var r = d[0];
        return new(decimal.TryParse(Str(r, "spend"), NumberStyles.Any, CultureInfo.InvariantCulture, out var s) ? s : 0m,
            Int(r, "impressions"), Int(r, "reach"), Int(r, "clicks"), Int(r, "inline_link_clicks"), Int(r, "inline_post_engagement"));
    }
}
