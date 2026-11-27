namespace PTScheduler.Portal.Entities;

/// <summary>Kanały poza Instagramem (Instagram ma własne połączenie: <see cref="InstagramAccount"/>).</summary>
public enum SocialPlatform { Facebook = 1, Threads = 2, TikTok = 3, YouTube = 4, LinkedIn = 5, X = 6, Pinterest = 7 }

/// <summary>Połączony kanał: przez API (publikuje sam) albo ręczny (przypomina i przygotowuje paczkę do wrzucenia).</summary>
public class SocialChannel
{
    public int Id { get; set; }
    public SocialPlatform Platform { get; set; }
    /// <summary>Publikacja przez API; false = kanał ręczny.</summary>
    public bool Api { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>Domyślnie zaznaczony przy nowych postach (i u autopilota).</summary>
    public bool Default { get; set; }
    public string Name { get; set; } = "";
    public string? Handle { get; set; }
    /// <summary>Id strony na Facebooku / użytkownika Threads.</summary>
    public string? ExternalId { get; set; }
    public string? PictureUrl { get; set; }
    public string? ProfileUrl { get; set; }
    /// <summary>Token (zaszyfrowany Data Protection).</summary>
    public string? Token { get; set; }
    public DateTime? TokenExpiresAt { get; set; }
    public DateTime? TokenRefreshedAt { get; set; }
    public int? Followers { get; set; }
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSyncAt { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// Pending — czeka na termin posta; Publishing — w toku (np. kontener Threads); Published; Failed;
/// ManualDue — termin minął, kanał ręczny czeka, aż wrzucisz; Skipped — pominięty.
/// </summary>
public enum SocialTargetStatus { Pending = 0, Publishing = 1, Published = 2, Failed = 3, ManualDue = 4, Skipped = 5 }

/// <summary>Post z planu → konkretny kanał (poza Instagramem), z własnym opisem i stanem publikacji.</summary>
public class SocialPostTarget
{
    public int Id { get; set; }
    public int PostId { get; set; }
    public int ChannelId { get; set; }
    public SocialTargetStatus Status { get; set; } = SocialTargetStatus.Pending;
    /// <summary>Opis tylko dla tego kanału (pusty = opis posta).</summary>
    public string? Caption { get; set; }
    public string? ContainerId { get; set; }
    public string? ExternalId { get; set; }
    public string? Permalink { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? NotifiedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public InstagramPost? Post { get; set; }
    public SocialChannel? Channel { get; set; }
}

/// <summary>
/// Link śledzący (hantly.pl/go/{kod}) — jeden na post i kanał albo własny (bio, reklama, wizytówka).
/// Liczymy tylko kliknięcia (bez danych osób i bez cookie); źródło rejestracji trafia do <see cref="Tenant.SignupSource"/>
/// przez parametr ?src= w adresie.
/// </summary>
public class TrackedLink
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    /// <summary>instagram, facebook, threads, tiktok, youtube, linkedin, x, pinterest, bio, reklama, inne.</summary>
    public string Channel { get; set; } = "inne";
    public int? PostId { get; set; }
    public string Label { get; set; } = "";
    /// <summary>Ścieżka na stronie Hantly, np. „/register” albo „/#cennik”.</summary>
    public string Target { get; set; } = "/register";
    public int Clicks { get; set; }
    public DateTime? LastClickAt { get; set; }
    public bool Archived { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Kliknięcia linku w danym dniu (do wykresów i raportu tygodniowego).</summary>
public class TrackedLinkDay
{
    public int LinkId { get; set; }
    public DateOnly Day { get; set; }
    public int Clicks { get; set; }
}

/// <summary>Przycisk na stronie „link w bio” (hantly.pl/linki).</summary>
public class BioItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    /// <summary>Ścieżka na stronie Hantly („/register”) — wtedy przez link śledzący — albo pełny adres https:// (bez liczenia).</summary>
    public string Target { get; set; } = "/register";
    public string Icon { get; set; } = "bi-arrow-right";
    public int Order { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Highlight { get; set; }
    /// <summary>Link śledzący (dla ścieżek na stronie Hantly).</summary>
    public int? LinkId { get; set; }
}

/// <summary>Rozmowa w wiadomościach prywatnych (Instagram albo Messenger strony).</summary>
public class SocialThread
{
    public int Id { get; set; }
    /// <summary>instagram | facebook.</summary>
    public string Platform { get; set; } = "instagram";
    public string ExternalId { get; set; } = "";
    /// <summary>Id osoby w danej platformie (IGSID / PSID) — do wysyłki odpowiedzi.</summary>
    public string ParticipantId { get; set; } = "";
    public string ParticipantName { get; set; } = "";
    public string? Snippet { get; set; }
    public DateTime LastMessageAt { get; set; }
    /// <summary>Ostatnia wiadomość od osoby — odpowiadać można 24 h od niej (zasada Meta).</summary>
    public DateTime? LastInboundAt { get; set; }
    public bool Unread { get; set; }
    /// <summary>Obsłużona (odpowiedziana albo niewymagająca odpowiedzi).</summary>
    public bool Done { get; set; }
    public string? Suggestion { get; set; }
}

public class SocialMessage
{
    public string Id { get; set; } = "";
    public int ThreadId { get; set; }
    public bool Inbound { get; set; }
    public string Text { get; set; } = "";
    public bool HasAttachment { get; set; }
    public DateTime At { get; set; }
}

/// <summary>Reguła „komentarz → wiadomość”: słowo w komentarzu (np. PLAN) wysyła autorowi prywatną wiadomość z linkiem.</summary>
public class CommentRule
{
    public int Id { get; set; }
    public string Keyword { get; set; } = "";
    /// <summary>Treść wiadomości prywatnej; „{link}” → link śledzący reguły.</summary>
    public string Message { get; set; } = "";
    /// <summary>Opcjonalna publiczna odpowiedź pod komentarzem (np. „Wysłane w wiadomości 📩”).</summary>
    public string? PublicReply { get; set; }
    public string Target { get; set; } = "/register";
    public int? LinkId { get; set; }
    public bool Enabled { get; set; } = true;
    public int Uses { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
