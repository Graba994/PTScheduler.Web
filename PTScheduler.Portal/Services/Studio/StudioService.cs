using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;
using R = PTScheduler.Portal.Services.Studio.StudioRules;

namespace PTScheduler.Portal.Services.Studio;

/// <summary>
/// Studio reklam: „biblia produktu” (z repozytorium na GitHubie, strony hantly.pl i katalogu funkcji),
/// teksty reklam Meta i Google, posty z grafiką do planu Instagrama, ręczne tworzenie i edycja,
/// poprawki z uwagą i ocena serwera pod przyszłe renderowanie filmów. AI przez OpenRouter (<see cref="AiService"/>).
/// Ciężkie wywołania idą przez kolejkę w tle (<see cref="StudioBackgroundService"/>).
/// </summary>
public class StudioService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    AiService ai,
    InstagramService ig,
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<StudioService> logger)
{
    public const string Feature = "studio";
    public const int MaxAttempts = 3;

    // ── Ustawienia ────────────────────────────────────────────────────

    public sealed record Options(string Model, string PublishMode, bool GarminDefault);

    public async Task<Options> OptionsAsync()
    {
        var s = await settings.GetAllAsync(K.StudioModel, K.StudioPublishMode, K.StudioGarminDefault);
        return new(s[K.StudioModel].Trim(), s[K.StudioPublishMode] == "auto" ? "auto" : "manual", s[K.StudioGarminDefault] == "true");
    }

    public Task SaveOptionsAsync(Options o) => settings.SetManyAsync(new Dictionary<string, string>
    {
        [K.StudioModel] = o.Model.Trim(),
        [K.StudioPublishMode] = o.PublishMode == "auto" ? "auto" : "manual",
        [K.StudioGarminDefault] = o.GarminDefault ? "true" : "false",
    });

    // ── Wiedza o produkcie ────────────────────────────────────────────

    public async Task<R.Knowledge?> KnowledgeAsync()
    {
        var raw = await settings.GetAsync(K.StudioKnowledge);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return JsonSerializer.Deserialize<R.Knowledge>(raw, R.Json); } catch (JsonException) { return null; }
    }

    public async Task SaveKnowledgeAsync(R.Knowledge k)
    {
        k.UpdatedAt ??= DateTime.UtcNow;
        await settings.SetAsync(K.StudioKnowledge, JsonSerializer.Serialize(k, R.Json));
    }

    public sealed record KnowledgeState(R.Knowledge? Knowledge, bool Building, string? Error);

    public async Task<KnowledgeState> KnowledgeStateAsync()
    {
        var s = await settings.GetAllAsync(K.StudioKnowledgeRequested, K.StudioKnowledgeError);
        return new(await KnowledgeAsync(), !string.IsNullOrEmpty(s[K.StudioKnowledgeRequested]), NullIfEmpty(s[K.StudioKnowledgeError]));
    }

    /// <summary>Zleca (prze)budowę wiedzy — zrobi ją kolejka w tle (1–3 min).</summary>
    public async Task RequestKnowledgeAsync()
    {
        await settings.SetManyAsync(new Dictionary<string, string> { [K.StudioKnowledgeRequested] = DateTime.UtcNow.ToString("O"), [K.StudioKnowledgeError] = "" });
    }

    /// <summary>Materiały: fakty z katalogu i cennika (pewne), strona główna, README, CHANGELOG, dokumenty, historia zmian.</summary>
    public async Task<string> GatherAsync(CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## FAKTY PEWNE (katalog funkcji i cennik z bazy Portalu)");
        foreach (var g in PlanFeatureCatalog.All.GroupBy(f => f.Group))
        {
            sb.AppendLine($"### {g.Key}");
            foreach (var f in g) sb.AppendLine($"- {f.Name}: {f.Benefit}");
        }
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var plans = await db.Plans.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync(ct);
            if (plans.Count > 0)
                sb.AppendLine("Pakiety: " + string.Join("; ", plans.Select(p => p.MonthlyPrice <= 0 ? $"{p.Name} — 0 zł" : $"{p.Name} — {p.MonthlyPrice:0} zł/mies.")));
        }
        sb.AppendLine("Program testerów: 3 miesiące za darmo w zamian za krótką ankietę raz w tygodniu.");
        sb.AppendLine($"Adres: {(await settings.GetAsync(K.MainDomain)).Trim()}");

        // Strona główna — najpierw na żywo, inaczej z kodu.
        var site = await SiteTextAsync(ct);
        var gh = await GitHubAsync();
        if (site is null && gh is not null) site = await gh.FileAsync("PTScheduler.Portal/Components/Home/HomeLanding.razor", ct) is { } landing ? R.RazorToText(landing) : null;
        if (site is not null) sb.AppendLine().AppendLine("## STRONA hantly.pl").AppendLine(site);

        if (gh is not null)
        {
            if (await gh.FileAsync("README.md", ct) is { } readme) sb.AppendLine().AppendLine("## README").AppendLine(R.Cut(readme, 12000));
            if (await gh.FileAsync("CHANGELOG.md", ct) is { } changelog) sb.AppendLine().AppendLine("## CHANGELOG").AppendLine(R.Cut(changelog, 10000));
            var tree = await gh.TreeAsync(ct);
            foreach (var doc in tree.Where(p => p.StartsWith("docs/", StringComparison.Ordinal) && p.EndsWith(".md", StringComparison.OrdinalIgnoreCase)).Order().Take(5))
                if (await gh.FileAsync(doc, ct) is { } text) sb.AppendLine().AppendLine($"## {doc}").AppendLine(R.Cut(text, 6000));
            var pages = tree.Where(p => p.StartsWith("PTScheduler.Web/Components/Pages/", StringComparison.Ordinal) && p.EndsWith(".razor", StringComparison.Ordinal))
                .Select(p => p["PTScheduler.Web/Components/Pages/".Length..^".razor".Length]).ToList();
            if (pages.Count > 0) sb.AppendLine().AppendLine("## EKRANY APLIKACJI TRENERA (pliki)").AppendLine(string.Join(", ", pages));
            var commits = await gh.CommitsAsync(200, ct);
            if (commits.Count > 0) sb.AppendLine().AppendLine("## HISTORIA ZMIAN (data | opis)").AppendLine(string.Join('\n', commits));
        }
        return sb.ToString();
    }

    private async Task<string?> SiteTextAsync(CancellationToken ct)
    {
        try
        {
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(20);
            var html = await http.GetStringAsync(PortalUrls.Public(config) + "/", ct);
            var text = R.HtmlToText(html);
            return text.Length > 500 ? text : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return null; }
    }

    private async Task<GitHub?> GitHubAsync()
    {
        var s = await settings.GetAllAsync(K.GithubOwner, K.GithubRepo, K.GithubBranch, K.GithubToken);
        if (string.IsNullOrWhiteSpace(s[K.GithubOwner]) || string.IsNullOrWhiteSpace(s[K.GithubRepo])) return null;
        return new GitHub(httpFactory, s[K.GithubOwner].Trim(), s[K.GithubRepo].Trim(),
            string.IsNullOrWhiteSpace(s[K.GithubBranch]) ? "master" : s[K.GithubBranch].Trim(), NullIfEmpty(s[K.GithubToken]?.Trim()));
    }

    /// <summary>Kolejka: buduje wiedzę, jeśli ktoś o to poprosił. Zwraca true, gdy coś zrobiła.</summary>
    public async Task<bool> BuildKnowledgeIfRequestedAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(await settings.GetAsync(K.StudioKnowledgeRequested))) return false;
        string? error = null;
        try
        {
            var materials = await GatherAsync(ct);
            var system = """
                Jesteś strategiem marketingu produktu Hantly — polskiej aplikacji dla trenerów personalnych.
                Z materiałów wyciągnij „biblię produktu” dla copywriterów. Tylko fakty z materiałów; nic nie dopowiadaj.
                Pisz po polsku, naturalnie, słowami trenera. Formy neutralne płciowo.
                Odpowiadasz wyłącznie poprawnym JSON-em, bez komentarzy i bez ```.
                """;
            var user = $$"""
                Zbuduj wiedzę o produkcie w JSON:
                {"oneLiner":"jedno zdanie, co to jest i dla kogo","audience":"kto kupuje",
                 "painPoints":["8–12 bolączek trenera jego słowami"],
                 "features":[{"name":"nazwa jak w aplikacji","benefit":"1 zdanie korzyści","detail":"co pokazać na ekranie","screen":"trasa ekranu albo pusty"}],
                 "novelties":[{"title":"nowość z historii zmian","when":"RRRR-MM-DD","benefit":"co daje trenerowi"}],
                 "proofPoints":["twarde konkrety: ceny, darmowy start, integracje — tylko z materiałów"],
                 "tone":"jak mówi marka","cta":"wezwanie do działania z adresem i ceną startu",
                 "doNotSay":["czego nie obiecywać"]}
                Funkcji 15–30 (najważniejsze dla trenera najpierw). Nowości 5–12, najświeższe najpierw, tylko zmiany widoczne dla trenera lub klienta (bez poprawek technicznych).
                Nie używaj nazwy „PTScheduler”.

                MATERIAŁY:
                {{materials}}
                """;
            var o = await OptionsAsync();
            var r = await ai.CompleteAsync(Feature, null, system, user, maxTokens: 6000, ct: ct, modelOverride: o.Model, timeoutSeconds: 240);
            if (r.Text is null) error = r.Error ?? "AI nie odpowiedziało.";
            else if (R.Parse<R.Knowledge>(r.Text) is not { Features.Count: > 0 } k) error = "AI zwróciło wiedzę w złym formacie — spróbuj jeszcze raz.";
            else
            {
                k.DoNotSay = k.DoNotSay.Concat(R.AlwaysDoNotSay).Distinct().ToList();
                k.UpdatedAt = DateTime.UtcNow;
                await SaveKnowledgeAsync(k);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Studio: budowa wiedzy nie powiodła się.");
            error = "Nie udało się zebrać materiałów — sprawdź dostęp do GitHuba w Konfiguracji.";
        }
        await settings.SetManyAsync(new Dictionary<string, string> { [K.StudioKnowledgeRequested] = "", [K.StudioKnowledgeError] = error ?? "" });
        return true;
    }

    // ── Promocje ──────────────────────────────────────────────────────

    public async Task<List<StudioPromotion>> ListAsync(int take = 50)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.StudioPromotions.AsNoTracking().OrderByDescending(p => p.CreatedAt).Take(take).ToListAsync();
    }

    public async Task<StudioPromotion?> GetAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.StudioPromotions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
    }

    /// <summary>Nowy materiał: z AI (trafia do kolejki) albo ręczny (pusty szablon do wypełnienia).</summary>
    public async Task<(int Id, string? Error)> CreateAsync(StudioKind kind, string brief, bool garmin, bool manual)
    {
        if (kind == StudioKind.Video) return (0, "Filmy będą dostępne po uruchomieniu renderera.");
        if (!manual && await ai.UnavailableReasonAsync() is { } reason) return (0, $"{reason} Możesz utworzyć materiał ręcznie.");
        await using var db = await dbFactory.CreateDbContextAsync();
        var p = new StudioPromotion
        {
            Kind = kind, Brief = brief.Trim(), Garmin = garmin, Manual = manual,
            Status = manual ? StudioStatus.Ready : StudioStatus.Writing,
            ResultJson = manual ? Empty(kind) : null,
        };
        db.StudioPromotions.Add(p);
        await db.SaveChangesAsync();
        return (p.Id, null);
    }

    public static string Empty(StudioKind kind) => kind == StudioKind.Ads
        ? JsonSerializer.Serialize(new R.Ads { Meta = [new(), new(), new()], Google = new() { Headlines = [.. Enumerable.Repeat("", R.Limits.GoogleHeadlinesMin)], Descriptions = ["", "", "", ""] } }, R.Json)
        : JsonSerializer.Serialize(new R.Post { Bullets = ["", "", ""] }, R.Json);

    public static R.Ads? AdsOf(StudioPromotion p) => p.ResultJson is null ? null : R.Parse<R.Ads>(p.ResultJson);
    public static R.Post? PostOf(StudioPromotion p) => p.ResultJson is null ? null : R.Parse<R.Post>(p.ResultJson);

    /// <summary>Ręczna edycja wyniku.</summary>
    public async Task SaveResultAsync(int id, object result)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.StudioPromotions.Where(p => p.Id == id).ExecuteUpdateAsync(u => u
            .SetProperty(p => p.ResultJson, JsonSerializer.Serialize(result, R.Json)).SetProperty(p => p.UpdatedAt, DateTime.UtcNow));
    }

    /// <summary>Poprawka z uwagą — AI zmienia tylko to, czego dotyczy uwaga.</summary>
    public async Task<string?> ReviseAsync(int id, string comment)
    {
        if (string.IsNullOrWhiteSpace(comment)) return "Napisz, co poprawić.";
        if (await ai.UnavailableReasonAsync() is { } reason) return reason;
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.StudioPromotions.Where(p => p.Id == id).ExecuteUpdateAsync(u => u
            .SetProperty(p => p.Comment, comment.Trim()).SetProperty(p => p.Status, StudioStatus.Writing)
            .SetProperty(p => p.Attempts, 0).SetProperty(p => p.RunAfter, (DateTime?)null).SetProperty(p => p.Error, (string?)null)
            .SetProperty(p => p.UpdatedAt, DateTime.UtcNow));
        return null;
    }

    public async Task RetryAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.StudioPromotions.Where(p => p.Id == id && p.Status == StudioStatus.Failed).ExecuteUpdateAsync(u => u
            .SetProperty(p => p.Status, StudioStatus.Writing).SetProperty(p => p.Attempts, 0).SetProperty(p => p.RunAfter, (DateTime?)null).SetProperty(p => p.Error, (string?)null));
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.StudioPromotions.Where(p => p.Id == id).ExecuteDeleteAsync();
    }

    /// <summary>Kolejka: jeden materiał na przebieg. 3 próby co 2 i 4 minuty, potem „Błąd”.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var p = await db.StudioPromotions.Where(x => x.Status == StudioStatus.Writing && (x.RunAfter == null || x.RunAfter <= now))
            .OrderBy(x => x.UpdatedAt).FirstOrDefaultAsync(ct);
        if (p is null) return false;
        p.Attempts++;
        var (json, error) = await WriteAsync(p, ct);
        if (json is not null)
        {
            p.ResultJson = json;
            p.Status = StudioStatus.Ready;
            p.Comment = null;
            p.Error = null;
        }
        else if (p.Attempts >= MaxAttempts)
        {
            p.Status = StudioStatus.Failed;
            p.Error = Trim(error, 1000);
        }
        else
        {
            p.RunAfter = now.AddMinutes(2 * p.Attempts);
            p.Error = Trim(error, 1000);
        }
        p.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        // Tryb automatyczny: gotowy post sam trafia do planu Instagrama.
        if (p.Status == StudioStatus.Ready && p.Kind == StudioKind.Post && p.InstagramPostId is null && (await OptionsAsync()).PublishMode == "auto")
            await ToInstagramAsync(p.Id, null, schedule: true);
        return true;
    }

    // ── Pisanie (AI) ─────────────────────────────────────────────────

    /// <summary>Wiedza z odfiltrowanym Garminem, gdy nie wolno go pokazywać.</summary>
    public static R.Knowledge ForPrompt(R.Knowledge k, bool garmin)
    {
        if (garmin) return k;
        static bool G(string? s) => s?.Contains("garmin", StringComparison.OrdinalIgnoreCase) == true;
        return new R.Knowledge
        {
            OneLiner = k.OneLiner, Audience = k.Audience, Tone = k.Tone, Cta = k.Cta,
            PainPoints = k.PainPoints.Where(x => !G(x)).ToList(),
            Features = k.Features.Where(f => !G(f.Name) && !G(f.Benefit) && !G(f.Detail)).ToList(),
            Novelties = k.Novelties.Where(n => !G(n.Title) && !G(n.Benefit)).ToList(),
            ProofPoints = k.ProofPoints.Where(x => !G(x)).ToList(),
            DoNotSay = k.DoNotSay.ToList(),
            UpdatedAt = k.UpdatedAt,
        };
    }

    public const string PolishRules = """
        POLSZCZYZNA: pisz jak rodowity Polak, nie tłumacz z angielskiego. Utarte połączenia („ma sens”, nie „robi sens”).
        Krótkie zdania mówione (do ok. 20 słów, jedna myśl). Bez serii „Więc/I/A” na początku zdań, bez powtórzeń w sąsiednich zdaniach.
        Poprawna odmiana i interpunkcja (przecinek przed „że”, „który”, „bo”, „ale”). Bez anglicyzmów.
        GŁOS MARKI: do trenera na „ty”, jak kolega z branży; humor z codzienności trenera, nigdy z klientów.
        Bez wykrzykników co zdanie i bez tonu telezakupów. Mieszaj bardzo krótkie zdania (2–4 słowa) z dłuższymi.
        Formy neutralne płciowo: zamiast „zrobiłeś/aś” pisz np. „warto”, „Twoi klienci”, „masz to w telefonie”.
        """;

    public const string GarminOn = """
        GARMIN (zgoda właściciela): możesz pokazać integrację. Nazwa aplikacji zawsze „Garmin Connect™” (ze znakiem ™ przy pierwszym użyciu).
        Nie sugeruj partnerstwa, sponsoringu, certyfikatu ani rekomendacji ze strony Garmin. Nie przypisuj Garminowi funkcji Hantly.
        Bez nazw modeli zegarków i bez opisów logo. Tylko dane i funkcje, które są w faktach.
        """;

    public const string GarminOff = "GARMIN: nie wspominaj o Garminie, zegarkach ani urządzeniach noszonych.";

    public const string GarminAttribution = "Garmin i Garmin Connect są znakami towarowymi Garmin Ltd. lub jej spółek zależnych.";

    private async Task<string> SystemPromptAsync(bool garmin)
    {
        var k = await KnowledgeAsync() ?? new R.Knowledge { DoNotSay = [.. R.AlwaysDoNotSay] };
        var voice = (await settings.GetAsync(K.InstagramVoice)).Trim();
        return $"""
            Jesteś copywriterem reklam Hantly — polskiej aplikacji dla trenerów personalnych.
            Profesjonalna reklama: konkretna korzyść, emocja z codzienności trenera, jasne wezwanie do działania.
            Tylko prawdziwe funkcje, liczby i ceny z WIEDZY poniżej. Żadnych wymyślonych opinii ani statystyk.
            {PolishRules}
            {(garmin ? GarminOn : GarminOff)}
            {(voice.Length > 0 ? "Styl marki od właściciela: " + voice : "")}
            Czego nie mówić: {string.Join(" ", k.DoNotSay.Concat(R.AlwaysDoNotSay).Distinct())}
            Odpowiadasz wyłącznie poprawnym JSON-em, bez komentarzy i bez ```.

            WIEDZA O PRODUKCIE (JSON):
            {JsonSerializer.Serialize(ForPrompt(k, garmin), R.Json)}
            """;
    }

    private const string AdsSchema = """
        {"meta":[{"angle":"2–4 słowa","primaryText":"tekst główny; pierwsze 125 znaków muszą działać same","headline":"do 40 znaków","description":"do 30 znaków"}],
         "google":{"headlines":["10–15 nagłówków, każdy do 30 znaków, różne kąty"],"descriptions":["dokładnie 4, każdy do 90 znaków"],"keywords":["10–20 fraz, jak wpisuje trener"]},
         "audiences":["grupy odbiorców do kierowania"],"notes":"plan testu A/B i orientacyjny budżet startowy, bez obietnic wyników"}
        Meta: dokładnie 3 warianty o różnych kątach (np. czas, pieniądze, klienci).
        """;

    private const string PostSchema = """
        {"headline":"do 40 znaków","sub":"do 70 znaków","bullets":["dokładnie 3 korzyści, każda do 32 znaków"],
         "caption":"hak w 1. linii, 3–6 krótkich akapitów, na końcu wezwanie z adresem; bez hashtagów","hashtags":["8–12, bez #"],"altText":"opis grafiki dla niewidomych"}
        """;

    private async Task<(string? Json, string? Error)> WriteAsync(StudioPromotion p, CancellationToken ct)
    {
        var o = await OptionsAsync();
        var system = await SystemPromptAsync(p.Garmin);
        var schema = p.Kind == StudioKind.Ads ? AdsSchema : PostSchema;
        var what = p.Kind == StudioKind.Ads ? "teksty reklam płatnych (Meta i Google Ads)" : "post na Instagram/Facebook z grafiką 4:5";
        var user = p.Comment is not null && p.ResultJson is not null
            ? $"Popraw zgodnie z uwagami właściciela. Zachowaj to, co dobre; zmień tylko to, czego dotyczą uwagi.\nUwagi: {p.Comment}\n\nObecna wersja:\n{p.ResultJson}\n\nSchemat:\n{schema}"
            : $"Przygotuj {what}.\nBrief: {(string.IsNullOrWhiteSpace(p.Brief) ? "wybierz najmocniejszą korzyść z wiedzy" : p.Brief)}\n\nSchemat JSON:\n{schema}";
        var r = await ai.CompleteAsync(Feature, null, system, user, maxTokens: 3000, ct: ct, modelOverride: o.Model, timeoutSeconds: 180);
        if (r.Text is null) return (null, r.Error ?? "AI nie odpowiedziało.");

        if (p.Kind == StudioKind.Ads)
        {
            if (R.Parse<R.Ads>(r.Text) is not { } ads || ads.Meta.Count == 0) return (null, "AI zwróciło odpowiedź w złym formacie.");
            ads = R.Clean(ads);
            // Jedna szybka poprawka długości — tylko pola z przekroczeniem.
            if (R.Check(ads).Any(i => i.Problem.Contains("znaków") || i.Problem.Contains("za długie")))
            {
                var fix = await ai.CompleteAsync(Feature, null, system,
                    $"Skróć tylko pola przekraczające limity (Meta: nagłówek 40, opis 30; Google: nagłówek 30, tekst 90). Resztę zostaw bez zmian.\nProblemy: {string.Join("; ", R.Check(ads).Select(i => $"{i.Field}: {i.Problem}"))}\n\n{JsonSerializer.Serialize(ads, R.Json)}",
                    maxTokens: 3000, ct: ct, modelOverride: o.Model, timeoutSeconds: 180);
                if (R.Parse<R.Ads>(fix.Text) is { Meta.Count: > 0 } fixedAds) ads = R.Clean(fixedAds);
            }
            return (JsonSerializer.Serialize(ads, R.Json), null);
        }

        if (R.Parse<R.Post>(r.Text) is not { } post || string.IsNullOrWhiteSpace(post.Headline)) return (null, "AI zwróciło odpowiedź w złym formacie.");
        post = R.Clean(post);
        if (p.Garmin && (post.Caption + post.Headline + string.Join(" ", post.Bullets)).Contains("Garmin", StringComparison.OrdinalIgnoreCase)
            && !post.Caption.Contains(GarminAttribution, StringComparison.Ordinal))
            post.Caption = post.Caption.TrimEnd() + "\n\n" + GarminAttribution;
        return (JsonSerializer.Serialize(post, R.Json), null);
    }

    // ── Post → grafika i plan Instagrama ─────────────────────────────

    public async Task<InstagramGraphics.Spec> SpecAsync(R.Post post, string? theme)
    {
        var acc = await ig.AccountAsync();
        return new InstagramGraphics.Spec
        {
            Template = post.Bullets.Count(b => !string.IsNullOrWhiteSpace(b)) >= 2 ? "list" : "tip",
            Theme = theme ?? "fiolet", Size = "post", Eyebrow = "Hantly",
            Headline = post.Headline, Body = post.Sub, Points = post.Bullets.Where(b => !string.IsNullOrWhiteSpace(b)).ToList(),
            Mood = "hello", Handle = acc?.Username, Domain = (await settings.GetAsync(K.MainDomain)).Trim()
        };
    }

    public async Task<byte[]> PreviewAsync(R.Post post, string? theme) => InstagramGraphics.Render(await SpecAsync(post, theme));

    /// <summary>
    /// Post ze Studia do modułu Instagrama: grafika + opis. Ręcznie — jako szkic (planujesz sam),
    /// automatycznie — od razu w najbliższy wolny termin.
    /// </summary>
    public async Task<(bool Ok, string Message, int? PostId)> ToInstagramAsync(int id, string? theme, bool schedule)
    {
        var p = await GetAsync(id);
        if (p is null || PostOf(p) is not { } post) return (false, "Nie ma takiego posta.", null);
        if (string.IsNullOrWhiteSpace(post.Headline) || string.IsNullOrWhiteSpace(post.Caption)) return (false, "Uzupełnij nagłówek i opis.", null);
        var igId = await ig.CreatePostAsync(InstagramPostKind.Image, post.Headline, "funkcja", p.Manual ? "manual" : "ai");
        await ig.SavePostAsync(igId, post.Headline, R.CaptionWithTags(post), InstagramPostKind.Image, "funkcja", true);
        var g = await ig.AddGraphicAsync(igId, await SpecAsync(post, theme));
        if (!g.Ok) return (false, g.Message, null);
        await using (var db = await dbFactory.CreateDbContextAsync())
            await db.StudioPromotions.Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.InstagramPostId, igId));
        if (!schedule) return (true, "Post jest w planie Instagrama jako szkic — ustaw termin albo opublikuj.", igId);
        var s = await ig.ScheduleAsync(igId, null);
        return (s.Ok, s.Ok ? $"Post w planie Instagrama. {s.Message}" : $"Szkic utworzony, ale nie zaplanowany: {s.Message}", igId);
    }

    // ── Serwer pod renderowanie filmów ───────────────────────────────

    public sealed record ServerCheck(string Tone, string Verdict, List<string> Notes, double? Cores, double? MemFreeGb, double? MemTotalGb,
        double? DiskFreeGb, double? Load, double? RenderMinutes, DateTime? At);

    /// <summary>
    /// Ocena serwera z ostatniego pomiaru (co 5 min, Panel → Serwer i zasoby): czy udźwignie renderer
    /// (Node + Chromium, ok. 2–3 GB RAM w czasie renderu, ok. 2 GB obrazu) i ile potrwa film 30–40 s.
    /// </summary>
    public async Task<ServerCheck> ServerAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var s = await db.ResourceSamples.AsNoTracking().Where(x => x.TenantId == null).OrderByDescending(x => x.At).FirstOrDefaultAsync();
        if (s is null) return new("muted", "Brak pomiarów serwera — pierwszy pojawi się kilka minut po starcie Portalu.", [], null, null, null, null, null, null, null);
        return Evaluate(s.CpuLimitCores, s.MemoryLimitBytes, s.MemoryBytes, s.DiskTotalBytes, s.DiskUsedBytes, s.CpuCores, s.At);
    }

    public static ServerCheck Evaluate(double? cores, long? memTotal, long? memUsed, long? diskTotal, long? diskUsed, double? load, DateTime? at)
    {
        const double Gb = 1024d * 1024 * 1024;
        var pl = System.Globalization.CultureInfo.GetCultureInfo("pl-PL");
        string F(double? v, string fmt) => v?.ToString(fmt, pl) ?? "?";
        double? memFree = memTotal is > 0 && memUsed is not null ? (memTotal.Value - memUsed.Value) / Gb : null;
        double? diskFree = diskTotal is > 0 && diskUsed is not null ? (diskTotal.Value - diskUsed.Value) / Gb : null;
        var notes = new List<string>();
        var tone = "ok";
        void Warn(string text, bool danger = false) { notes.Add(text); if (danger) tone = "danger"; else if (tone == "ok") tone = "warn"; }

        if (cores is < 2) Warn($"Tylko {F(cores, "0")} rdzeń — render będzie bardzo wolny.", true);
        if (memFree is < 2) Warn($"Wolnej pamięci {F(memFree, "0.0")} GB — renderer potrzebuje ok. 2–3 GB. Renderuj nocą albo dołóż RAM.", memFree < 1.2);
        else if (memFree is < 3) Warn($"Wolnej pamięci {F(memFree, "0.0")} GB — wystarczy na jeden render naraz.");
        if (diskFree is < 5) Warn($"Wolnego dysku {F(diskFree, "0")} GB — obraz renderera i filmy zajmą 3–5 GB.", diskFree < 3);
        var busy = cores is > 0 && load is not null ? load.Value / cores.Value : (double?)null;
        if (busy is > 0.7) Warn($"Serwer jest zajęty (obciążenie {F(busy * 100, "0")}% rdzeni) — render ustaw na noc, żeby nie spowolnić aplikacji trenerów.");

        // Wzorzec z Zakrętów: film 37 s ≈ 5,5 min przy 2 rdzeniach. Renderujemy na połowie wolnych rdzeni (max 4), resztę zostawiamy trenerom.
        double? minutes = cores is > 0 ? Math.Round(5.5 * 2 / Math.Clamp(Math.Floor(cores.Value / 2), 1, 4) * (busy is > 0.5 ? 1.5 : 1), 1) : null;
        var verdict = tone switch
        {
            "danger" => "Serwer w obecnym stanie nie udźwignie renderowania filmów bez szkody dla aplikacji trenerów.",
            "warn" => "Serwer da radę, ale z ograniczeniami — szczegóły niżej.",
            _ => "Serwer spokojnie udźwignie renderer filmów."
        };
        if (minutes is not null) notes.Insert(0, $"Film 30–40 s wyrenderuje się w około {F(minutes, "0.#")} min.");
        return new(tone, verdict, notes, cores, memFree is null ? null : Math.Round(memFree.Value, 1), memTotal is > 0 ? Math.Round(memTotal.Value / Gb, 1) : null,
            diskFree is null ? null : Math.Round(diskFree.Value, 0), load, minutes, at);
    }

    // ── Miejsce na filmy (duży dysk serwera) ─────────────────────────

    /// <summary>Katalog Studia w kontenerze — na serwerze podpięty z dużego dysku (deploy/update-portal.sh, STUDIO_DIR).</summary>
    public string DataDir => config["Studio:DataDir"] is { Length: > 0 } d ? d : "/opt/ptscheduler/studio";

    public sealed record Storage(string Tone, string Message, string Path, double? FreeGb, double? TotalGb);

    /// <summary>Czy katalog na filmy jest podpięty z osobnego dysku, zapisywalny i ile ma miejsca.</summary>
    public Storage StorageCheck()
    {
        const double Gb = 1024d * 1024 * 1024;
        var path = DataDir;
        if (!Directory.Exists(path))
            return new("warn", "Miejsce na filmy nie jest jeszcze podpięte — filmy nie będą miały gdzie się zapisać.", path, null, null);
        try
        {
            var probe = System.IO.Path.Combine(path, ".zapis-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new("danger", "Katalog na filmy jest podpięty, ale Portal nie może w nim zapisywać (uprawnienia).", path, null, null);
        }
        try
        {
            var drive = new DriveInfo(path);
            var free = drive.AvailableFreeSpace / Gb;
            var total = drive.TotalSize / Gb;
            if (drive.RootDirectory.FullName == "/")
                return new("warn", "Katalog na filmy leży wewnątrz kontenera Portalu — przy aktualizacji zniknie. Podepnij duży dysk.", path, Math.Round(free), Math.Round(total));
            return new(free < 20 ? "warn" : "ok", free < 20 ? "Podpięte, ale zostało mało miejsca." : "Podpięte — filmy, głos i zrzuty zapisują się na dużym dysku.", path, Math.Round(free), Math.Round(total));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return new("ok", "Podpięte i zapisywalne.", path, null, null);
        }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    private static string? Trim(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];

    // ── GitHub (czytanie repozytorium tokenem z Konfiguracji) ─────────

    private sealed class GitHub(IHttpClientFactory httpFactory, string owner, string repo, string branch, string? token)
    {
        private string Base => $"https://api.github.com/repos/{owner}/{repo}";

        private HttpRequestMessage Req(string url, string accept)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("Hantly-Portal");
            req.Headers.Accept.ParseAdd(accept);
            if (token is not null) req.Headers.Authorization = new("Bearer", token);
            return req;
        }

        private HttpClient Http()
        {
            var h = httpFactory.CreateClient();
            h.Timeout = TimeSpan.FromSeconds(30);
            return h;
        }

        public async Task<string?> FileAsync(string path, CancellationToken ct)
        {
            try
            {
                using var req = Req($"{Base}/contents/{Uri.EscapeDataString(path).Replace("%2F", "/")}?ref={Uri.EscapeDataString(branch)}", "application/vnd.github.raw");
                using var resp = await Http().SendAsync(req, ct);
                return resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync(ct) : null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return null; }
        }

        public async Task<List<string>> TreeAsync(CancellationToken ct)
        {
            try
            {
                using var req = Req($"{Base}/git/trees/{Uri.EscapeDataString(branch)}?recursive=1", "application/vnd.github+json");
                using var resp = await Http().SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode) return [];
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                return doc.RootElement.TryGetProperty("tree", out var tree)
                    ? tree.EnumerateArray().Where(e => e.GetProperty("type").GetString() == "blob").Select(e => e.GetProperty("path").GetString() ?? "").ToList()
                    : [];
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { return []; }
        }

        public async Task<List<string>> CommitsAsync(int max, CancellationToken ct)
        {
            var list = new List<string>();
            for (var page = 1; list.Count < max && page <= 3; page++)
            {
                try
                {
                    using var req = Req($"{Base}/commits?sha={Uri.EscapeDataString(branch)}&per_page=100&page={page}", "application/vnd.github+json");
                    using var resp = await Http().SendAsync(req, ct);
                    if (!resp.IsSuccessStatusCode) break;
                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                    var n = 0;
                    foreach (var c in doc.RootElement.EnumerateArray())
                    {
                        n++;
                        var commit = c.GetProperty("commit");
                        var date = commit.GetProperty("author").GetProperty("date").GetDateTime();
                        if (R.CommitLine(date, commit.GetProperty("message").GetString() ?? "") is { } line) list.Add(line);
                    }
                    if (n < 100) break;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException) { break; }
            }
            return list.Take(max).ToList();
        }
    }
}

/// <summary>Kolejka Studia: co 15 s buduje wiedzę (jeśli zlecono) albo pisze jeden materiał.</summary>
public class StudioBackgroundService(IServiceScopeFactory scopes, ILogger<StudioBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            var worked = false;
            try
            {
                using var scope = scopes.CreateScope();
                var studio = scope.ServiceProvider.GetRequiredService<StudioService>();
                worked = await studio.BuildKnowledgeIfRequestedAsync(ct) | await studio.ProcessNextAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Studio: przebieg kolejki nie powiódł się."); }
            try { await Task.Delay(worked ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(15), ct); } catch (OperationCanceledException) { return; }
        }
    }
}
