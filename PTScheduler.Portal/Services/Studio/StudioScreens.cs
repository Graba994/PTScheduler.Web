using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;
using R = PTScheduler.Portal.Services.Studio.StudioRules;

namespace PTScheduler.Portal.Services.Studio;

/// <summary>
/// Zrzuty prawdziwych ekranów aplikacji do reklam. Robot (renderer) wchodzi na konto demo (konto testowe wybrane w Panelu
/// → Publiczne demo, z wczytanym scenariuszem — fikcyjne osoby) jednorazowymi linkami, które Portal podpisuje sekretem
/// instancji tuż przed użyciem. Przycisk „Wypróbuj demo” na hantly.pl zostaje wyłączony — tryb demo dotyczy tylko instancji.
/// Potem AI ogląda miniatury i opisuje, co widać, którą funkcję ilustrują i czy nadają się do reklamy.
/// Wynik: {Studio:DataDir}/sources/hantly/screens.json + miniatury w thumbs/.
/// </summary>
public class StudioScreens(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    PublicDemoService publicDemo,
    TenantDemoService demo,
    TenantService tenants,
    RendererClient renderer,
    AiService ai,
    IConfiguration config,
    ILogger<StudioScreens> logger)
{
    public const string Dir = "hantly";
    public const int DescribeBatch = 8;

    public sealed record Route(string Path, string As);

    /// <summary>
    /// Trasy do zrzutów (najlepsze ujęcia z doświadczeń Zakrętów). {Id:int} — z linków na wcześniej odwiedzonych stronach.
    /// Test RouteCatalog pilnuje, żeby każda trasa istniała w aplikacji trenera.
    /// </summary>
    public static readonly Route[] Routes =
    [
        new("/dzis", "trainer"), new("/clients", "trainer"), new("/clients/{Id:int}", "trainer"), new("/zaleglosci", "trainer"),
        new("/trainer/retencja", "trainer"), new("/trainer/asystent", "trainer"), new("/trainer/styl", "trainer"),
        new("/stats/business", "trainer"), new("/finance", "trainer"), new("/finance/ksiegowa", "trainer"),
        new("/pakiety-koncza", "trainer"), new("/packages", "trainer"), new("/admin/package-offers", "trainer"),
        // /admin/site (strona studia) — tylko dla właściciela, konto demo trenera dostaje „brak dostępu”
        new("/lab/miesnie", "trainer"), new("/chat", "trainer"),
        new("/trainer/availability", "trainer"), new("/okienka", "trainer"),
        new("/app", "client"), new("/book", "client"), new("/my/workouts", "client"), new("/my/stats", "client"), new("/my/garmin", "client"),
    ];

    // ── Stan (screens.json) ───────────────────────────────────────────

    public sealed class Highlight
    {
        public string Label { get; set; } = "";
        public double Y { get; set; }
    }

    public sealed class Screen
    {
        public string File { get; set; } = "";
        public string Path { get; set; } = "";
        public string Pattern { get; set; } = "";
        /// <summary>trainer | client</summary>
        public string As { get; set; } = "trainer";
        public int W { get; set; }
        public int H { get; set; }
        public string Title { get; set; } = "";
        /// <summary>Co widać — konkretnie (liczby, sekcje). Pisze AI; bez AI = tytuł strony.</summary>
        public string Shows { get; set; } = "";
        /// <summary>Którą funkcję ilustruje (nazwa z wiedzy o produkcie).</summary>
        public string Feature { get; set; } = "";
        /// <summary>false = pusty stan, błąd, logowanie, nudny ekran albo testowe śmieci.</summary>
        public bool Good { get; set; } = true;
        public List<Highlight> Highlights { get; set; } = [];
        /// <summary>Opisany przez AI (false = tylko dane z robota).</summary>
        public bool Described { get; set; }
    }

    public sealed class Skip
    {
        public string Path { get; set; } = "";
        public string Reason { get; set; } = "";
    }

    public sealed class State
    {
        public DateTime? UpdatedAt { get; set; }
        public string Tenant { get; set; } = "";
        public string Scenario { get; set; } = "";
        public List<Screen> Screens { get; set; } = [];
        public List<Skip> Skipped { get; set; } = [];
        /// <summary>Uwaga z ostatniego przebiegu (np. „widok klienta się nie udał”, „AI nie opisało”).</summary>
        public string? Warning { get; set; }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private string DataDir => config["Studio:DataDir"] is { Length: > 0 } d ? d : "/opt/ptscheduler/studio";
    public string SourceDir => Path.Combine(DataDir, "sources", Dir);
    public string ScreensDir => Path.Combine(SourceDir, "screens");
    public string ThumbsDir => Path.Combine(SourceDir, "thumbs");
    private string StateFile => Path.Combine(SourceDir, "screens.json");

    public async Task<State?> StateAsync()
    {
        if (!File.Exists(StateFile)) return null;
        try { return JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(StateFile), Json); }
        catch (Exception ex) when (ex is JsonException or IOException) { logger.LogWarning(ex, "Studio: nie da się odczytać screens.json."); return null; }
    }

    private async Task SaveStateAsync(State s, CancellationToken ct)
    {
        Directory.CreateDirectory(SourceDir);
        await File.WriteAllTextAsync(StateFile + ".tmp", JsonSerializer.Serialize(s, Json), ct);
        File.Move(StateFile + ".tmp", StateFile, overwrite: true);
    }

    /// <summary>Miniatura zrzutu (JPEG) do galerii w panelu; null = brak.</summary>
    public string? ThumbPath(string file) =>
        SafeName(file) && File.Exists(Path.Combine(ThumbsDir, ThumbOf(file))) ? Path.Combine(ThumbsDir, ThumbOf(file)) : null;

    public static string ThumbOf(string file) => System.IO.Path.ChangeExtension(file, ".jpg");

    public static bool SafeName(string file) => file.Length is > 0 and <= 120 && file.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-') && !file.StartsWith('.');

    // ── Konto demo ────────────────────────────────────────────────────

    /// <summary>Konto demo: wybrane w Panelu → Publiczne demo, musi być testowe i aktywne. Null + powód, gdy się nie da.</summary>
    public async Task<(Tenant? Tenant, string? Problem)> DemoTenantAsync()
    {
        var cfg = await publicDemo.GetConfigAsync();
        if (cfg.TenantId is not { } id) return (null, "Nie wybrano konta demo — Panel → Strona główna → Publiczne demo → konto testowe (przycisku na stronie nie trzeba włączać).");
        await using var db = await dbFactory.CreateDbContextAsync();
        var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return (null, "Konto demo nie istnieje — wybierz inne w Panelu → Publiczne demo.");
        if (!t.IsTestAccount) return (null, $"Konto {t.Slug} nie jest oznaczone jako testowe — zrzuty robimy tylko na danych demo (RODO).");
        if (t.Status != TenantStatus.Active) return (null, $"Konto demo {t.Slug} nie jest aktywne.");
        return (t, null);
    }

    // ── Przebieg (zadanie „screens”) ──────────────────────────────────

    public async Task RunAsync(CancellationToken ct)
    {
        var health = await renderer.HealthAsync(ct) ?? throw new RendererException("Renderer nie odpowiada — sprawdź Panel → Aktualizacje → Renderer filmów.");
        if (health.Busy is { Length: > 0 } || health.Queued > 0)
            throw new InvalidOperationException($"Renderer pracuje ({health.Busy ?? "kolejka"}) — linki wejścia wygasłyby w kolejce, spróbuję za chwilę.");

        var (tenant, problem) = await DemoTenantAsync();
        if (tenant is null) throw new StudioJobFatalException(problem!);
        var st = await demo.GetAsync(tenant);
        if (!st.Reachable) throw new InvalidOperationException($"Konto demo {tenant.Slug} nie odpowiada: {st.Error}");
        if (!st.Loaded)
            throw new StudioJobFatalException($"Na koncie {tenant.Slug} nie ma danych demo — wczytaj scenariusz (np. „Pełny grafik”) w Panelu → Trenerzy → {tenant.CompanyName} → Dane demo.");
        if (!st.DemoMode)
        {
            var (ok, msg) = await publicDemo.EnableDemoModeForStudioAsync(tenant);
            if (!ok) throw new StudioJobFatalException($"Nie udało się włączyć trybu demo na {tenant.Slug}: {msg}");
            logger.LogInformation("Studio: włączono tryb demo na {Slug} (bez przycisku na stronie).", tenant.Slug);
        }
        var secret = TenantSecrets.For(tenant, config);
        if (string.IsNullOrWhiteSpace(secret)) throw new StudioJobFatalException($"Instancja {tenant.Slug} nie ma sekretu — nie da się wydać linku wejścia.");
        var publicBase = await tenants.PublicUrlAsync(tenant);
        var hide = new[] { tenant.OwnerEmail, (await settings.GetAsync(K.AdminNotificationEmail)).Trim() }
            .Where(e => !string.IsNullOrWhiteSpace(e) && e.Contains('@')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var shots = new List<RendererClient.Shot>();
        var skipped = new List<Skip>();
        string? warning = null;
        foreach (var role in new[] { "trainer", "client" })
        {
            // Link ważny 2 minuty — wydany tuż przed wejściem danego widoku.
            var token = TenantSso.CreateToken(tenant.Slug, "studio", role == "client" ? "demo-client" : "demo-trainer", secret, DateTimeOffset.UtcNow);
            var pages = Routes.Where(r => r.As == role).Select(r => new RendererClient.ScreenPage(r.Path, role == "client" ? "client" : null));
            try
            {
                var r = await renderer.ScreensAsync(Dir, role, TenantSso.DemoUrl(publicBase, token), hide, pages, ct);
                shots.AddRange(r.Shots);
                skipped.AddRange(r.Skipped.Select(s => new Skip { Path = s.Path, Reason = s.Reason }));
            }
            catch (RendererException ex) when (role == "client" && shots.Count > 0)
            {
                warning = $"Widok klienta się nie udał: {ex.Message}";
                skipped.Add(new Skip { Path = "widok klienta", Reason = ex.Message });
            }
        }

        // Stare opisy zostają dla plików drugiego widoku, jeśli ten przebieg go nie zrobił.
        var previous = await StateAsync();
        var screens = shots.Select(s => new Screen
        {
            File = s.File, Path = s.Path, Pattern = s.Pattern, As = s.As, W = s.W, H = s.H, Title = s.Title, Shows = s.Title,
        }).ToList();
        if (previous is not null)
        {
            var doneRoles = shots.Select(s => s.As).ToHashSet();
            screens.AddRange(previous.Screens.Where(p => !doneRoles.Contains(p.As) && File.Exists(Path.Combine(ScreensDir, p.File))));
        }

        Directory.CreateDirectory(ThumbsDir);
        foreach (var s in shots.Where(s => SafeName(s.File)))
            await File.WriteAllBytesAsync(Path.Combine(ThumbsDir, ThumbOf(s.File)), Convert.FromBase64String(s.Thumb), ct);

        var described = await DescribeAsync(screens.Where(s => shots.Any(x => x.File == s.File)).ToList(), shots, ct);
        if (described is not null) warning = warning is null ? described : $"{warning} {described}";

        await SaveStateAsync(new State
        {
            UpdatedAt = DateTime.UtcNow, Tenant = tenant.Slug, Scenario = st.ScenarioName ?? st.Scenario ?? "",
            Screens = screens.OrderBy(s => s.As == "client").ThenBy(s => Array.FindIndex(Routes, r => r.Path == s.Pattern)).ToList(),
            Skipped = skipped, Warning = warning,
        }, ct);
        // miniatury zrzutów, których już nie ma
        var keep = screens.Select(s => ThumbOf(s.File)).ToHashSet(StringComparer.Ordinal);
        foreach (var f in Directory.EnumerateFiles(ThumbsDir, "*.jpg").Where(f => !keep.Contains(Path.GetFileName(f))))
            File.Delete(f);
        logger.LogInformation("Studio: {Count} zrzutów aplikacji ({Good} dobrych).", screens.Count, screens.Count(s => s.Good));
    }

    // ── Opis przez AI (obrazy) ────────────────────────────────────────

    private sealed class AiScreens
    {
        [JsonPropertyName("screens")] public List<AiScreen> Screens { get; set; } = [];
    }

    private sealed class AiScreen
    {
        [JsonPropertyName("file")] public string File { get; set; } = "";
        [JsonPropertyName("shows")] public string Shows { get; set; } = "";
        [JsonPropertyName("feature")] public string Feature { get; set; } = "";
        [JsonPropertyName("good")] public bool Good { get; set; } = true;
        [JsonPropertyName("highlights")] public List<Highlight> Highlights { get; set; } = [];
    }

    /// <summary>Paczki po 8 miniatur. Zwraca uwagę dla panelu, gdy AI nie opisało (zrzuty zostają, tylko bez opisu).</summary>
    private async Task<string?> DescribeAsync(List<Screen> screens, List<RendererClient.Shot> shots, CancellationToken ct)
    {
        if (screens.Count == 0) return null;
        if (await ai.UnavailableReasonAsync() is { } reason) return $"AI nie opisało zrzutów ({reason}) — opisy uzupełnią się przy następnym odświeżeniu.";
        var model = (await settings.GetAsync(K.StudioModel)).Trim();
        var features = await FeatureNamesAsync();
        var system = $"""
            Oglądasz zrzuty ekranu polskiej aplikacji Hantly (dla trenerów personalnych i ich klientów) zrobione na telefonie, na koncie demo z fikcyjnymi danymi.
            Do każdego zrzutu napisz po polsku: co konkretnie widać (sekcje, liczby, przyciski — najwyżej 160 znaków), którą funkcję produktu
            ilustruje (użyj nazwy z listy funkcji, jeśli pasuje), czy nadaje się do reklamy i 1–3 miejsca warte pokazania dymkiem.
            good = false, gdy główna część ekranu to pusty stan albo komunikat o braku czegoś („Dziś bez wizyt”, „brak danych”,
            „nic nie zaplanowano”, „rezerwacja niedostępna”), błąd, logowanie, brak dostępu, ekran nudny albo testowe śmieci (np. „test (kopia)”).
            highlights: label do 30 znaków (krótko, po ludzku), y = położenie od góry zrzutu 0–1.
            Funkcje produktu: {string.Join("; ", features)}
            Odpowiadasz wyłącznie poprawnym JSON-em, bez komentarzy i bez ```.
            """;
        // Paczka, której odpowiedź się nie odczytała (np. urwana na limicie), idzie jeszcze raz w dwóch połowach.
        var pending = new Queue<(Screen[] Batch, bool Retry)>(screens.Chunk(DescribeBatch).Select(b => (b, false)));
        var failed = 0;
        while (pending.TryDequeue(out var item))
        {
            var ok = await DescribeBatchAsync(item.Batch, shots, system, model, ct);
            if (ok) continue;
            if (!item.Retry && item.Batch.Length > 1)
                foreach (var half in item.Batch.Chunk((item.Batch.Length + 1) / 2)) pending.Enqueue((half, true));
            else failed += item.Batch.Length;
        }
        return failed > 0 ? $"AI nie opisało {failed} z {screens.Count} zrzutów — mają tylko tytuł strony." : null;
    }

    private async Task<bool> DescribeBatchAsync(Screen[] batch, List<RendererClient.Shot> shots, string system, string model, CancellationToken ct)
    {
        var thumbs = batch.Select(s => shots.First(x => x.File == s.File).Thumb).ToList();
        var sb = new StringBuilder("Zrzuty (w tej samej kolejności co obrazy):\n");
        foreach (var (s, i) in batch.Select((s, i) => (s, i)))
        {
            var shot = shots.First(x => x.File == s.File);
            sb.AppendLine($"{i + 1}. plik: {s.File}; trasa: {s.Path}; widok: {(s.As == "client" ? "klienta" : "trenera")}; tytuł: {shot.Title}; tekst strony: {R.Cut(shot.Text, 500)}");
        }
        sb.AppendLine("""Schemat: {"screens":[{"file":"nazwa pliku","shows":"co widać","feature":"nazwa funkcji","good":true,"highlights":[{"label":"…","y":0.3}]}]}""");
        var r = await ai.CompleteAsync(StudioService.Feature, null, system, sb.ToString(), maxTokens: 6000, ct: ct,
            modelOverride: model, timeoutSeconds: 180, imagesJpegBase64: thumbs);
        if (r.Text is null || R.Parse<AiScreens>(r.Text) is not { Screens.Count: > 0 } parsed)
        {
            logger.LogWarning("Studio: opis {Count} zrzutów nieudany: {Error}", batch.Length, r.Error ?? "odpowiedź nie jest poprawnym JSON-em");
            return false;
        }
        foreach (var d in parsed.Screens)
            if (batch.FirstOrDefault(s => s.File == d.File) is { } s) Apply(s, d);
        return batch.All(s => s.Described);
    }

    private static void Apply(Screen s, AiScreen d)
    {
        s.Shows = R.Cut(d.Shows.Trim(), 400) is { Length: > 0 } shows ? shows : s.Shows;
        s.Feature = R.Cut(d.Feature.Trim(), 80);
        s.Good = d.Good;
        s.Highlights = d.Highlights.Where(h => !string.IsNullOrWhiteSpace(h.Label)).Take(3)
            .Select(h => new Highlight { Label = R.Cut(h.Label.Trim(), 30), Y = Math.Clamp(h.Y, 0, 1) }).ToList();
        s.Described = true;
    }

    private async Task<List<string>> FeatureNamesAsync()
    {
        var raw = await settings.GetAsync(K.StudioKnowledge);
        if (string.IsNullOrWhiteSpace(raw)) return PlanFeatureCatalog.All.Select(f => f.Name).Distinct().ToList();
        try { return (JsonSerializer.Deserialize<R.Knowledge>(raw, R.Json)?.Features ?? []).Select(f => f.Name).ToList(); }
        catch (JsonException) { return []; }
    }

    // ── Dla scenarzysty (krok 4) ──────────────────────────────────────

    /// <summary>Czy zrzut pokazuje Garmina — bez zgody właściciela nie wolno go użyć w reklamie.</summary>
    public static bool IsGarmin(Screen s) =>
        s.Pattern.Contains("garmin", StringComparison.OrdinalIgnoreCase) || s.Feature.Contains("garmin", StringComparison.OrdinalIgnoreCase);

    /// <summary>Dobre zrzuty do wyboru w scenariuszu (bez Garmina, gdy nie ma zgody).</summary>
    public static List<Screen> Usable(State? state, bool garmin) =>
        state?.Screens.Where(s => s.Good && (garmin || !IsGarmin(s))).ToList() ?? [];

    /// <summary>Lista dla scenarzysty: „plik: co widać [funkcja; widok; wysokość N× szerokości] dymki: „…” y=…”.</summary>
    public static string ForPrompt(IEnumerable<Screen> screens) => string.Join('\n', screens.Select(s =>
    {
        var ratio = s.W > 0 ? (double)s.H / s.W : 0;
        var hl = s.Highlights.Count == 0 ? "" : " dymki: " + string.Join(", ", s.Highlights.Select(h =>
            $"„{h.Label}” y={h.Y.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}"));
        return $"{s.File}: {s.Shows} [{(s.Feature.Length > 0 ? s.Feature + "; " : "")}widok {(s.As == "client" ? "klienta" : "trenera")}; wysokość {ratio.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}× szerokości]{hl}";
    }));
}

/// <summary>Zadanie „screens”: zrzuty aplikacji na koncie demo + opis przez AI.</summary>
public class ScreensJobHandler(StudioScreens screens) : IStudioJobHandler
{
    public string Type => "screens";
    public Task HandleAsync(StudioJob job, CancellationToken ct) => screens.RunAsync(ct);
}
