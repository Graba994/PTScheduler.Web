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
