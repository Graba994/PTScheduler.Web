using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using R = PTScheduler.Portal.Services.Studio.StudioRules;

namespace PTScheduler.Portal.Services.Studio;

/// <summary>
/// Filmy reklamowe 9:16 — potok z dwiema akceptacjami właściciela:
/// write (AI) → ScriptReview → [akceptacja tekstu] → voice → render → qa → VideoReview → [akceptacja wideo] → ReadyToPublish.
/// Każdy etap można odrzucić z uwagą (AI poprawia tylko to). Publikacja zawsze ręczna: mp4 + opis.txt do pobrania.
/// Pliki filmu: {Studio:DataDir}/episodes/{Slug}/ (ten sam katalog renderer widzi jako /data/episodes/{Slug}).
/// </summary>
public class StudioVideos(
    IDbContextFactory<PortalDbContext> dbFactory,
    StudioJobs jobs,
    StudioMedia media,
    StudioScreens screens,
    AiService ai,
    IConfiguration config)
{
    private string DataDir => config["Studio:DataDir"] is { Length: > 0 } d ? d : "/opt/ptscheduler/studio";
    public string Dir(StudioVideo v) => Path.Combine(DataDir, "episodes", v.Slug);
    public string VideoFile(StudioVideo v) => Path.Combine(Dir(v), "video.mp4");

    // ── Odczyt ───────────────────────────────────────────────────────

    public async Task<StudioVideo?> GetAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.StudioVideos.AsNoTracking().Include(v => v.Scenes.OrderBy(s => s.Order)).FirstOrDefaultAsync(v => v.Id == id);
    }

    /// <summary>Filmy po promocjach (do listy w Studiu).</summary>
    public async Task<Dictionary<int, StudioVideo>> ByPromotionAsync(IEnumerable<int> promotionIds)
    {
        var ids = promotionIds.ToList();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.StudioVideos.AsNoTracking().Where(v => v.PromotionId != null && ids.Contains(v.PromotionId.Value))
            .ToDictionaryAsync(v => v.PromotionId!.Value);
    }

    public bool HasVideo(StudioVideo v) => File.Exists(VideoFile(v));

    // ── Nowy film ────────────────────────────────────────────────────

    /// <summary>Film z AI (zadanie „write”) albo ręczny (gotowy szablon scen do wypełnienia).</summary>
    public async Task<(int VideoId, string? Error)> CreateAsync(int promotionId, string brief, bool garmin, bool manual)
    {
        if (!manual && await ai.UnavailableReasonAsync() is { } reason) return (0, $"{reason} Możesz napisać scenariusz sam.");
        await using var db = await dbFactory.CreateDbContextAsync();
        var v = new StudioVideo
        {
            PromotionId = promotionId, Slug = AdRules.NewSlug(DateTime.UtcNow), Brief = brief.Trim(), Garmin = garmin, Manual = manual,
            Status = manual ? StudioVideoStatus.ScriptReview : StudioVideoStatus.Writing,
            Title = manual ? "Reklama Hantly 💪" : "",
        };
        if (manual) v.Scenes = ManualTemplate();
        db.StudioVideos.Add(v);
        await db.SaveChangesAsync();
        if (!manual) await jobs.EnqueueAsync("write", v.Id);
        return (v.Id, null);
    }

    /// <summary>Szablon ręcznego scenariusza: hak, Hantelek, ekran, asystent, korzyści, CTA.</summary>
    public static List<StudioScene> ManualTemplate() =>
    [
        Scene(0, "hook", "Hak", "😩", "Znowu wieczór na odpisywaniu klientom?",
            new JsonObject { ["template"] = "ad-hook", ["messages"] = new JsonArray("Masz jutro coś wolnego?", "Można przełożyć na 19?"), ["big"] = "23:07", ["line"] = "Grafik w *wiadomościach*", ["mood"] = "sleep" }),
        Scene(1, "fact", "Hantelek", "👋", "Spokojnie, mam to.",
            new JsonObject { ["template"] = "ad-mascot", ["bubble"] = "Cześć! Jestem Twoim asystentem w Hantly.", ["mood"] = "hello", ["name"] = "Hantelek · asystent AI" }),
        Scene(2, "fact", "Zapisy", "📱", "Klienci zapisują się sami, kiedy im pasuje.",
            new JsonObject { ["template"] = "ad-screen", ["screen"] = "", ["caption"] = "Zapisy *bez telefonu*", ["callouts"] = new JsonArray(), ["scroll"] = 0, ["side"] = "right", ["mood"] = "happy" }),
        Scene(3, "fact", "Asystent", "🤖", "A ja podpowiadam, do kogo dziś napisać.",
            new JsonObject { ["template"] = "ad-assistant", ["title"] = "Asystent pisze *za Ciebie*", ["agent"] = "Hantelek", ["messages"] = new JsonArray(
                new JsonObject { ["from"] = "ai", ["text"] = "Ania nie była od 3 tygodni. Napisać do niej?" }, new JsonObject { ["from"] = "me", ["text"] = "Tak, krótko i ciepło" }), ["mood"] = "hello" }),
        Scene(4, "fact", "Korzyści", "✅", "Grafik, płatności i plany w jednym miejscu.",
            new JsonObject { ["template"] = "ad-benefits", ["title"] = "Wszystko w *jednym miejscu*", ["items"] = new JsonArray("Grafik i zapisy 24/7", "Płatności i pakiety", "Plany treningowe"), ["mood"] = "happy" }),
        Scene(5, "outro", "Start", "🚀", "Zacznij za darmo na hantly.pl.",
            new JsonObject { ["template"] = "ad-cta", ["headline"] = "Ty trenujesz. *Hantly* prowadzi resztę.", ["price"] = "0 zł", ["button"] = "Zacznij za darmo", ["note"] = "plan startowy · bez karty" }),
    ];

    private static StudioScene Scene(int order, string kind, string headline, string icon, string text, JsonObject story) => new()
    {
        Order = order, Kind = kind, Number = kind == "fact" ? order : null, Headline = headline, Icon = icon, Text = text,
        StoryJson = story.ToJsonString(R.Json), NeedsVoice = true,
    };

    // ── Akceptacje i poprawki ────────────────────────────────────────

    /// <summary>Akceptacja tekstu → głos (tylko sceny ze zmienionym tekstem) → render.</summary>
    public async Task<string?> ApproveScriptAsync(int id)
    {
        if (await media.VoiceUnavailableAsync() is { } reason) return reason;
        if (await SetStatusAsync(id, StudioVideoStatus.Voicing, [StudioVideoStatus.ScriptReview, StudioVideoStatus.VideoReview, StudioVideoStatus.ReadyToPublish, StudioVideoStatus.Failed]) is { } err) return err;
        await jobs.EnqueueAsync("voice", id);
        return null;
    }

    /// <summary>„Przepisz” z uwagą — AI zmienia tylko to, czego dotyczy uwaga (także po obejrzeniu wideo).</summary>
    public async Task<string?> RewriteAsync(int id, string comment)
    {
        if (string.IsNullOrWhiteSpace(comment)) return "Napisz, co poprawić.";
        await using var db = await dbFactory.CreateDbContextAsync();
        var v = await db.StudioVideos.FirstOrDefaultAsync(x => x.Id == id);
        if (v is null) return "Nie ma takiego filmu.";
        if (v.Status is StudioVideoStatus.Writing or StudioVideoStatus.Voicing or StudioVideoStatus.Rendering) return "Poczekaj, aż skończy się bieżący etap.";
        if (await ai.UnavailableReasonAsync() is { } reason) return reason;
        v.ReviewComment = comment.Trim();
        v.Status = StudioVideoStatus.Writing;
        v.LastError = null;
        v.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await jobs.EnqueueAsync("write", id);
        return null;
    }

    /// <summary>Ponowny render po zmianie samej animacji — głos zostaje (chyba że zmienił się tekst sceny).</summary>
    public async Task<string?> RerenderAsync(int id)
    {
        var v = await GetAsync(id);
        if (v is null) return "Nie ma takiego filmu.";
        if (v.Scenes.Any(s => s.NeedsVoice) || !File.Exists(Path.Combine(Dir(v), "timeline.json"))) return await ApproveScriptAsync(id);
        if (await SetStatusAsync(id, StudioVideoStatus.Rendering, [StudioVideoStatus.ScriptReview, StudioVideoStatus.VideoReview, StudioVideoStatus.ReadyToPublish, StudioVideoStatus.Failed]) is { } err) return err;
        await jobs.EnqueueAsync("render", id);
        return null;
    }

    public Task<string?> ApproveVideoAsync(int id) =>
        SetStatusAsync(id, StudioVideoStatus.ReadyToPublish, [StudioVideoStatus.VideoReview]);

    public async Task<string?> MarkPublishedAsync(int id)
    {
        var err = await SetStatusAsync(id, StudioVideoStatus.Published, [StudioVideoStatus.ReadyToPublish]);
        if (err is null)
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await db.StudioVideos.Where(v => v.Id == id).ExecuteUpdateAsync(u => u.SetProperty(v => v.PublishedAt, DateTime.UtcNow));
        }
        return err;
    }

    /// <summary>Ponów etap, który się nie udał (status Failed) — od miejsca, w którym stanął.</summary>
    public async Task<string?> RetryAsync(int id)
    {
        var v = await GetAsync(id);
        if (v is null || v.Status != StudioVideoStatus.Failed) return "Ten film nie czeka na ponowienie.";
        if (v.Scenes.Count == 0)
        {
            await SetStatusAsync(id, StudioVideoStatus.Writing, [StudioVideoStatus.Failed]);
            await jobs.EnqueueAsync("write", id);
            return null;
        }
        return v.Scenes.Any(s => s.NeedsVoice) || !File.Exists(Path.Combine(Dir(v), "timeline.json")) ? await ApproveScriptAsync(id) : await RerenderAsync(id);
    }

    private async Task<string?> SetStatusAsync(int id, StudioVideoStatus to, StudioVideoStatus[] from)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var v = await db.StudioVideos.FirstOrDefaultAsync(x => x.Id == id);
        if (v is null) return "Nie ma takiego filmu.";
        if (!from.Contains(v.Status)) return "Ten krok nie jest teraz dostępny — odśwież stronę.";
        v.Status = to;
        v.LastError = null;
        v.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return null;
    }

    // ── Ręczna edycja ────────────────────────────────────────────────

    public sealed record SceneEdit(int Id, string Headline, string Icon, string Text, string StoryJson);

    /// <summary>
    /// Zapis scenariusza z panelu. Zmiana tekstu lektora = nowy głos tej sceny; zmiana samego ujęcia = tylko ponowny render.
    /// Zwraca błąd, gdy ujęcie nie jest poprawnym JSON-em albo ma nieznany szablon.
    /// </summary>
    public async Task<string?> SaveScenesAsync(int id, string title, string description, string pinned, IReadOnlyList<SceneEdit> edits)
    {
        foreach (var (e, i) in edits.Select((e, i) => (e, i)))
        {
            JsonObject? story;
            try { story = JsonNode.Parse(e.StoryJson) as JsonObject; } catch (JsonException) { return $"Scena {i + 1}: ujęcie nie jest poprawnym JSON-em."; }
            if (story is null || !AdRules.Templates.Contains(AdRules.Template(story))) return $"Scena {i + 1}: nieznane ujęcie — dozwolone: {string.Join(", ", AdRules.Templates)}.";
            if (string.IsNullOrWhiteSpace(e.Text)) return $"Scena {i + 1}: pusty tekst lektora.";
        }
        await using var db = await dbFactory.CreateDbContextAsync();
        var v = await db.StudioVideos.Include(x => x.Scenes).FirstOrDefaultAsync(x => x.Id == id);
        if (v is null) return "Nie ma takiego filmu.";
        if (v.Status is StudioVideoStatus.Writing or StudioVideoStatus.Voicing or StudioVideoStatus.Rendering) return "Poczekaj, aż skończy się bieżący etap.";
        v.Title = title.Trim();
        v.Description = description.Trim();
        v.PinnedComment = pinned.Trim();
        foreach (var e in edits)
        {
            if (v.Scenes.FirstOrDefault(s => s.Id == e.Id) is not { } s) continue;
            if (s.Text.Trim() != e.Text.Trim()) { s.Text = e.Text.Trim(); s.SpeechText = ""; s.NeedsVoice = true; }
            s.Headline = AdRules.Cut(e.Headline.Trim(), 60);
            s.Icon = AdRules.Cut(e.Icon.Trim(), 8);
            s.StoryJson = JsonNode.Parse(e.StoryJson)!.ToJsonString(R.Json);
        }
        v.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return null;
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var v = await db.StudioVideos.FirstOrDefaultAsync(x => x.Id == id);
        if (v is null) return;
        var dir = Dir(v);
        await db.StudioJobs.Where(j => j.VideoId == id && j.Status == StudioJobStatus.Pending).ExecuteDeleteAsync();
        if (v.PromotionId is { } pid) await db.StudioPromotions.Where(p => p.Id == pid).ExecuteDeleteAsync();
        db.StudioVideos.Remove(v);
        await db.SaveChangesAsync();
        if (Directory.Exists(dir) && Path.GetFileName(dir).StartsWith("reklama-", StringComparison.Ordinal)) Directory.Delete(dir, recursive: true);
    }

    // ── Pliki ────────────────────────────────────────────────────────

    public static List<string> List(string json) => JsonSerializer.Deserialize<List<string>>(string.IsNullOrWhiteSpace(json) ? "[]" : json) ?? [];

    public async Task<string> DescriptionTxtAsync(StudioVideo v, string website) =>
        AdRules.DescriptionTxt(v, List(v.TagsJson), List(v.HooksJson), website, v.Garmin);

    /// <summary>Zapisuje episode.json dla renderera (zrzuty z wymiarami, dźwięk wg ustawień).</summary>
    public async Task<string> WriteEpisodeAsync(StudioVideo v, string website, CancellationToken ct)
    {
        var state = await screens.StateAsync();
        var sizes = (state?.Screens ?? []).Where(s => File.Exists(Path.Combine(screens.ScreensDir, s.File)))
            .ToDictionary(s => s.File, s => (s.W, s.H));
        // RODO / Garmin: zrzut spoza dozwolonych nie trafi do filmu (telefon pokaże planszę)
        var allowed = StudioScreens.Usable(state, v.Garmin).Select(s => s.File).ToHashSet();
        foreach (var key in sizes.Keys.Where(k => !allowed.Contains(k)).ToList()) sizes.Remove(key);
        var sound = StudioMedia.SoundSection(await media.SoundOptionsAsync(), media.AvailableSounds(), v.Id);
        var json = AdRules.EpisodeJson(v, v.Scenes.OrderBy(s => s.Order).ToList(), website, StudioScreens.Dir, sizes, sound);
        Directory.CreateDirectory(Dir(v));
        await File.WriteAllTextAsync(Path.Combine(Dir(v), "episode.json"), json, ct);
        return json;
    }
}

// ── Zadania kolejki ──────────────────────────────────────────────────

/// <summary>„write”: scenariusz od AI (albo poprawka z uwagą) → ScriptReview.</summary>
public class WriteVideoJobHandler(IDbContextFactory<PortalDbContext> dbFactory, AdWriter writer, StudioNotifier notifier) : IStudioJobHandler
{
    public string Type => "write";

    public async Task HandleAsync(StudioJob job, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var v = await db.StudioVideos.Include(x => x.Scenes).FirstOrDefaultAsync(x => x.Id == job.VideoId, ct)
            ?? throw new StudioJobFatalException("Film został usunięty.");
        var previous = v.Scenes.Count > 0 && v.ReviewComment is not null ? ToDraft(v) : null;
        var draft = await writer.WriteAsync(v.Brief, v.Garmin, previous, v.ReviewComment, ct);

        var old = v.Scenes.OrderBy(s => s.Order).ToList();
        db.StudioScenes.RemoveRange(v.Scenes);
        v.Scenes = draft.Scenes.Select((s, i) =>
        {
            // ten sam tekst w tym samym miejscu = głos zostaje
            var same = i < old.Count && old[i].Text.Trim() == s.Text.Trim() && !old[i].NeedsVoice;
            return new StudioScene
            {
                Order = i, Kind = s.Kind, Number = s.Number, Headline = s.Headline, Icon = s.Icon, Text = s.Text,
                StoryJson = s.Story.ToJsonString(R.Json), NeedsVoice = !same, SpeechText = same ? old[i].SpeechText : "",
            };
        }).ToList();
        v.Title = draft.Title;
        v.Description = draft.Description;
        v.TagsJson = JsonSerializer.Serialize(draft.Tags);
        v.HooksJson = JsonSerializer.Serialize(draft.Hooks);
        v.SourcesJson = JsonSerializer.Serialize(new[] { await writer.WebsiteAsync() });
        v.PinnedComment = draft.PinnedComment;
        v.ReviewComment = null;
        v.LastError = null;
        v.Status = StudioVideoStatus.ScriptReview;
        v.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync("Studio reklam: scenariusz czeka na akceptację", $"„{v.Title}” — przeczytaj i zaakceptuj albo poproś o poprawkę.", v.Id);
    }

    public static AdRules.Draft ToDraft(StudioVideo v) => new()
    {
        Title = v.Title, Description = v.Description, PinnedComment = v.PinnedComment,
        Tags = StudioVideos.List(v.TagsJson), Hooks = StudioVideos.List(v.HooksJson),
        Scenes = v.Scenes.OrderBy(s => s.Order).Select(s => new AdRules.DraftScene
        {
            Kind = s.Kind, Number = s.Number, Headline = s.Headline, Icon = s.Icon, Text = s.Text, Story = AdRules.StoryOf(JsonNode.Parse(s.StoryJson)),
        }).ToList(),
    };
}

/// <summary>„voice”: tekst dla lektora (liczby słownie) + nagranie scen ze zmienionym tekstem → render.</summary>
public class VoiceJobHandler(IDbContextFactory<PortalDbContext> dbFactory, AdWriter writer, StudioMedia media, RendererClient renderer,
    StudioVideos videos, StudioJobs jobs, ILogger<VoiceJobHandler> logger) : IStudioJobHandler
{
    public string Type => "voice";

    public async Task HandleAsync(StudioJob job, CancellationToken ct)
    {
        if (await media.VoiceUnavailableAsync() is { } reason) throw new StudioJobFatalException(reason);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var v = await db.StudioVideos.Include(x => x.Scenes).FirstOrDefaultAsync(x => x.Id == job.VideoId, ct)
            ?? throw new StudioJobFatalException("Film został usunięty.");
        var scenes = v.Scenes.OrderBy(s => s.Order).ToList();
        if (scenes.Count == 0) throw new StudioJobFatalException("Film nie ma scen.");

        var missing = scenes.Where(s => s.NeedsVoice || string.IsNullOrWhiteSpace(s.SpeechText)).ToList();
        if (missing.Count > 0)
        {
            Dictionary<int, string> speech = [];
            try { speech = await writer.SpeechAsync(missing.Select(s => (s.Order, s.Text)).ToList(), ct); }
            catch (InvalidOperationException ex) { logger.LogWarning(ex, "Studio: tekst dla lektora bez AI — lektor przeczyta oryginał."); }
            foreach (var s in missing) s.SpeechText = speech.GetValueOrDefault(s.Order) ?? s.Text;
            await db.SaveChangesAsync(ct);
        }

        // Po zmianie liczby scen stare nagrania nie pasują do pozycji — nagrywamy wszystko.
        var all = !File.Exists(Path.Combine(DirOf(v), "timeline.json")) || TimelineCount(DirOf(v)) != scenes.Count;
        var voice = StudioMedia.RendererVoice(await media.VoiceAsync());
        await renderer.TtsAsync(v.Slug, voice, await media.CredentialsAsync(),
            scenes.Select((s, i) => new RendererClient.TtsScene(i, s.SpeechText, all || s.NeedsVoice)), ct);

        foreach (var s in scenes) s.NeedsVoice = false;
        v.Status = StudioVideoStatus.Rendering;
        v.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await jobs.EnqueueAsync("render", v.Id);
    }

    private string DirOf(StudioVideo v) => videos.Dir(v);

    internal static int TimelineCount(string dir)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "timeline.json")));
            return doc.RootElement.TryGetProperty("audio", out var a) ? a.GetArrayLength() : -1;
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return -1; }
    }
}

/// <summary>„render”: episode.json → video.mp4; potem kontrola klatek (jeśli włączona) albo od razu do akceptacji.</summary>
public class RenderJobHandler(IDbContextFactory<PortalDbContext> dbFactory, StudioVideos videos, AdWriter writer, RendererClient renderer,
    StudioMedia media, AiService ai, StudioJobs jobs, StudioNotifier notifier) : IStudioJobHandler
{
    public string Type => "render";

    public async Task HandleAsync(StudioJob job, CancellationToken ct)
    {
        var v = await videos.GetAsync(job.VideoId ?? 0) ?? throw new StudioJobFatalException("Film został usunięty.");
        await videos.WriteEpisodeAsync(v, await writer.WebsiteAsync(), ct);
        var r = await renderer.RenderAsync(v.Slug, ct);

        var qa = (await media.SoundOptionsAsync()).Qa && await ai.UnavailableReasonAsync() is null;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var e = await db.StudioVideos.FirstAsync(x => x.Id == v.Id, ct);
        e.VideoSeconds = Math.Round(r.DurationSeconds, 1);
        e.RenderedAt = DateTime.UtcNow;
        e.QaJson = null;
        e.Status = qa ? StudioVideoStatus.Rendering : StudioVideoStatus.VideoReview;
        e.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        if (qa) await jobs.EnqueueAsync("qa", v.Id);
        else await notifier.NotifyAsync("Studio reklam: film czeka na akceptację", $"„{v.Title}” ({r.DurationSeconds:0} s) — obejrzyj i zaakceptuj.", v.Id);
    }
}

/// <summary>„qa”: AI ogląda klatki kontrolne i zgłasza usterki. Błąd kontroli nie blokuje filmu.</summary>
public class QaJobHandler(IDbContextFactory<PortalDbContext> dbFactory, StudioVideos videos, AdWriter writer, RendererClient renderer,
    StudioNotifier notifier, ILogger<QaJobHandler> logger) : IStudioJobHandler
{
    public string Type => "qa";

    public async Task HandleAsync(StudioJob job, CancellationToken ct)
    {
        var v = await videos.GetAsync(job.VideoId ?? 0) ?? throw new StudioJobFatalException("Film został usunięty.");
        string qa;
        try
        {
            var stills = await renderer.StillsAsync(v.Slug, ct);
            var episode = await File.ReadAllTextAsync(Path.Combine(videos.Dir(v), "episode.json"), ct);
            qa = await writer.ReviewFramesAsync(episode, stills.Stills, ct);
        }
        catch (Exception ex) when (ex is RendererException or InvalidOperationException or JsonException or IOException)
        {
            logger.LogWarning(ex, "Studio: kontrola klatek pominięta.");
            qa = JsonSerializer.Serialize(new { ok = true, summary = $"Kontrola klatek się nie udała ({ex.Message}) — obejrzyj film sam.", issues = Array.Empty<object>() });
        }
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var e = await db.StudioVideos.FirstAsync(x => x.Id == v.Id, ct);
        e.QaJson = qa;
        e.Status = StudioVideoStatus.VideoReview;
        e.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync("Studio reklam: film czeka na akceptację", $"„{v.Title}” ({v.VideoSeconds:0} s) — obejrzyj i zaakceptuj.", v.Id);
    }
}
