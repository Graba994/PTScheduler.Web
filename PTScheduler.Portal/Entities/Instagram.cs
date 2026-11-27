namespace PTScheduler.Portal.Entities;

/// <summary>Podpięte konto Instagram marki (jedno). Token zaszyfrowany (Data Protection), ważny 60 dni — odnawiany sam.</summary>
public class InstagramAccount
{
    public int Id { get; set; }
    /// <summary>Id konta profesjonalnego (user_id z /me) — do publikacji i statystyk.</summary>
    public string IgUserId { get; set; } = "";
    /// <summary>instagram (logowanie przez Instagram, graph.instagram.com) | facebook (przez stronę na Facebooku, graph.facebook.com, token strony).</summary>
    public string Provider { get; set; } = "instagram";
    /// <summary>Id strony na Facebooku (gdy Instagram połączony przez stronę) — wiadomości i prywatne odpowiedzi idą przez stronę.</summary>
    public string? PageId { get; set; }
    public string Username { get; set; } = "";
    public string? Name { get; set; }
    public string? PictureUrl { get; set; }
    public int Followers { get; set; }
    public int Follows { get; set; }
    public int MediaCount { get; set; }
    public string Token { get; set; } = "";
    public DateTime TokenExpiresAt { get; set; }
    public DateTime TokenRefreshedAt { get; set; } = DateTime.UtcNow;
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSyncAt { get; set; }
    public string? LastError { get; set; }
}

public enum InstagramPostKind { Image = 0, Carousel = 1, Reel = 2, Story = 3 }

/// <summary>Pomysł → szkic → do akceptacji → zaplanowany → publikowanie → opublikowany (albo błąd).</summary>
public enum InstagramPostStatus { Idea = 0, Draft = 1, ToApprove = 2, Scheduled = 3, Publishing = 4, Published = 5, Failed = 6 }

/// <summary>Post przygotowany w Portalu (ręcznie albo przez AI / autopilota).</summary>
public class InstagramPost
{
    public int Id { get; set; }
    public InstagramPostKind Kind { get; set; }
    public InstagramPostStatus Status { get; set; } = InstagramPostStatus.Draft;
    /// <summary>Temat roboczy (np. z pomysłu AI) — widać go w planie.</summary>
    public string Title { get; set; } = "";
    public string Caption { get; set; } = "";
    /// <summary>Filar treści (porada, funkcja, mit, pytanie, oferta, kulisy) — do analizy, co działa.</summary>
    public string Pillar { get; set; } = "";
    /// <summary>manual | ai | autopilot</summary>
    public string Source { get; set; } = "manual";
    public bool ShareToFeed { get; set; } = true;
    /// <summary>Czy publikować na Instagramie (pozostałe kanały — <see cref="SocialPostTarget"/>).</summary>
    public bool ToInstagram { get; set; } = true;
    public DateTime? ScheduledAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? ContainerId { get; set; }
    public string? IgMediaId { get; set; }
    public string? Permalink { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<InstagramAsset> Assets { get; set; } = [];
}

/// <summary>
/// Zdjęcie albo film posta. Zdjęcia (JPEG) trzymamy w bazie i wydajemy pod nieodgadnionym adresem
/// /media/ig/{Token}.jpg — Instagram pobiera je stamtąd przy publikacji. Film: publiczny adres (Bunny albo inny).
/// </summary>
public class InstagramAsset
{
    public int Id { get; set; }
    public int PostId { get; set; }
    public int Order { get; set; }
    /// <summary>image | video</summary>
    public string Kind { get; set; } = "image";
    public string Token { get; set; } = Guid.NewGuid().ToString("N");
    public byte[]? Data { get; set; }
    public string? ExternalUrl { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long SizeBytes { get; set; }
    /// <summary>Grafika z szablonu (JSON) — można ją poprawić i wygenerować ponownie.</summary>
    public string? TemplateJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public InstagramPost? Post { get; set; }
}

/// <summary>Post z Instagrama (także dodany w telefonie) ze statystykami.</summary>
public class InstagramMedia
{
    public string Id { get; set; } = "";
    /// <summary>instagram | facebook | threads</summary>
    public string Platform { get; set; } = "instagram";
    public string MediaType { get; set; } = "";
    public string? ProductType { get; set; }
    public string? Caption { get; set; }
    public string? Permalink { get; set; }
    public string? ThumbnailUrl { get; set; }
    public DateTime PostedAt { get; set; }
    public int Likes { get; set; }
    public int Comments { get; set; }
    public int? Views { get; set; }
    public int? Reach { get; set; }
    public int? Saved { get; set; }
    public int? Shares { get; set; }
    public int? Interactions { get; set; }
    public DateTime? InsightsAt { get; set; }
    public int? PostId { get; set; }
}

/// <summary>Dzień konta: obserwujący (stan) i zasięg / wyświetlenia / interakcje w tym dniu.</summary>
public class InstagramDaily
{
    public DateOnly Day { get; set; }
    public int Followers { get; set; }
    public int? Reach { get; set; }
    public int? Views { get; set; }
    public int? Interactions { get; set; }
    public int? Engaged { get; set; }
}

/// <summary>Komentarz pod postem (z odpowiedziami). Done = obsłużony (odpowiedź, ukrycie albo „bez odpowiedzi”).</summary>
public class InstagramComment
{
    public string Id { get; set; } = "";
    /// <summary>instagram | facebook | threads</summary>
    public string Platform { get; set; } = "instagram";
    public string MediaId { get; set; } = "";
    public string? ParentId { get; set; }
    public string Username { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime At { get; set; }
    public bool Hidden { get; set; }
    public bool IsOwn { get; set; }
    public bool Done { get; set; }
    public string? Suggestion { get; set; }
    public DateTime SeenAt { get; set; } = DateTime.UtcNow;
    /// <summary>Reguła „komentarz → wiadomość”, która obsłużyła komentarz, i kiedy wysłano prywatną odpowiedź.</summary>
    public int? RuleId { get; set; }
    public DateTime? PrivateReplyAt { get; set; }
}
