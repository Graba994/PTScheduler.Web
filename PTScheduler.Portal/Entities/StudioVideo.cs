namespace PTScheduler.Portal.Entities;

/// <summary>
/// Etapy filmu reklamowego 9:16. Dwie akceptacje właściciela: najpierw tekst (ScriptReview), potem gotowe wideo (VideoReview).
/// Publikacja zawsze ręczna — ReadyToPublish znaczy „pliki gotowe do pobrania”, Published zaznacza właściciel.
/// </summary>
public enum StudioVideoStatus
{
    /// <summary>AI pisze scenariusz (zadanie „write”).</summary>
    Writing = 0,
    /// <summary>Czeka na akceptację tekstu.</summary>
    ScriptReview = 1,
    /// <summary>Tekst dla lektora i nagrywanie głosu (zadanie „voice”).</summary>
    Voicing = 2,
    /// <summary>Renderer składa film (zadanie „render”, potem opcjonalnie „qa”).</summary>
    Rendering = 3,
    /// <summary>Czeka na akceptację gotowego wideo.</summary>
    VideoReview = 4,
    /// <summary>Zaakceptowany — mp4 i opis.txt do pobrania, publikacja ręczna.</summary>
    ReadyToPublish = 5,
    /// <summary>Właściciel zaznaczył, że opublikował.</summary>
    Published = 6,
    /// <summary>Odrzucony bez poprawki.</summary>
    Rejected = 7,
    /// <summary>Zadanie nie powiodło się po wszystkich próbach — szczegóły w LastError.</summary>
    Failed = 8,
}

/// <summary>Film reklamowy 9:16 ze Studia reklam (w Zakrętach: „Episode”). Pliki w {Studio:DataDir}/episodes/{Slug}/.</summary>
public class StudioVideo
{
    public int Id { get; set; }
    /// <summary>Promocja, z której powstał (lista w Studiu).</summary>
    public int? PromotionId { get; set; }
    /// <summary>Nazwa katalogu z plikami — tylko [a-z0-9-] (renderer odrzuca inne).</summary>
    public string Slug { get; set; } = "";
    public StudioVideoStatus Status { get; set; } = StudioVideoStatus.Writing;
    public string Brief { get; set; } = "";
    /// <summary>Czy wolno pokazać integrację z Garminem (ekran /my/garmin, zasady marki).</summary>
    public bool Garmin { get; set; }
    /// <summary>Powstał ręcznie (bez AI).</summary>
    public bool Manual { get; set; }

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>JSON: lista tagów/hashtagów.</summary>
    public string TagsJson { get; set; } = "[]";
    /// <summary>JSON: źródła (tylko strona produktu).</summary>
    public string SourcesJson { get; set; } = "[]";
    /// <summary>JSON: 3 alternatywne otwarcia (haki).</summary>
    public string HooksJson { get; set; } = "[]";
    public string PinnedComment { get; set; } = "";

    /// <summary>Uwaga właściciela przy „Przepisz” albo odrzuceniu wideo — AI poprawia tylko to.</summary>
    public string? ReviewComment { get; set; }
    public string? LastError { get; set; }
    /// <summary>Wynik kontroli klatek przez AI: {ok, summary, issues[]}.</summary>
    public string? QaJson { get; set; }

    public double? VideoSeconds { get; set; }
    public DateTime? RenderedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<StudioScene> Scenes { get; set; } = [];
}

/// <summary>Scena filmu: tekst lektora, nagłówek do podglądu i scenorys (ujęcie Remotion z parametrami).</summary>
public class StudioScene
{
    public int Id { get; set; }
    public int VideoId { get; set; }
    public StudioVideo? Video { get; set; }
    public int Order { get; set; }
    /// <summary>hook | fact | outro</summary>
    public string Kind { get; set; } = "fact";
    public int? Number { get; set; }
    /// <summary>Etykieta do podglądu (≤ 22 znaki).</summary>
    public string Headline { get; set; } = "";
    public string Icon { get; set; } = "";
    /// <summary>Tekst lektora z cyframi — tak widać go w napisach.</summary>
    public string Text { get; set; } = "";
    /// <summary>Tekst dla syntezatora (liczby i skróty słownie). Pusty = jeszcze nie przygotowany.</summary>
    public string SpeechText { get; set; } = "";
    /// <summary>Ujęcie: {"template":"ad-screen", ...} — katalog w studio/src/ad/AdShort.tsx.</summary>
    public string StoryJson { get; set; } = "{}";
    /// <summary>Tekst zmienił się od ostatniego nagrania — przy „voice” nagrać ponownie.</summary>
    public bool NeedsVoice { get; set; } = true;
}

public enum StudioJobStatus { Pending = 0, Running = 1, Done = 2, Failed = 3 }

/// <summary>
/// Zadanie w kolejce Studia (write, voice, render, qa, screens…). Kolejka bierze po jednym,
/// 3 próby z odstępem 2 i 4 min; po ostatniej porażce film dostaje status Failed i idzie powiadomienie.
/// </summary>
public class StudioJob
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public int? VideoId { get; set; }
    public string? PayloadJson { get; set; }
    public StudioJobStatus Status { get; set; } = StudioJobStatus.Pending;
    public int Attempts { get; set; }
    public DateTime? RunAfter { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
