namespace PTScheduler.Portal.Entities;

/// <summary>
/// Konto Garmin Connect klienta trenera. Hantly jest u Garmina jednym partnerem (plan Starter), więc połączenia
/// wszystkich studiów trzymamy tutaj: Garmin wysyła dane z identyfikatorem użytkownika, a Portal wie, do którego
/// studia i klienta je przekazać. Tokeny zaszyfrowane (Data Protection).
/// </summary>
public class GarminConnection
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    /// <summary>Id klienta w aplikacji studia.</summary>
    public int TenantClientId { get; set; }
    public string GarminUserId { get; set; } = "";
    public string? AccessTokenProtected { get; set; }
    public DateTime? AccessTokenExpiresAt { get; set; }
    public string? RefreshTokenProtected { get; set; }
    /// <summary>Uprawnienia przyznane przez użytkownika w Garmin Connect (np. ACTIVITY_EXPORT, HEALTH_EXPORT).</summary>
    public string? Permissions { get; set; }
    /// <summary>Połączenie testowe z panelu (bez prawdziwego konta Garmin).</summary>
    public bool IsTest { get; set; }
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DisconnectedAt { get; set; }
    public DateTime? LastDataAt { get; set; }
}

/// <summary>Logowanie do Garmina w toku (OAuth2 PKCE): stan, weryfikator i powrót do aplikacji studia. Ważne 15 minut.</summary>
public class GarminAuthRequest
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public string CodeVerifier { get; set; } = "";
    public int TenantId { get; set; }
    public int TenantClientId { get; set; }
    /// <summary>Ścieżka w aplikacji studia (np. /garmin) — adres bazowy dokładamy przy powrocie, żeby nie było otwartego przekierowania.</summary>
    public string ReturnPath { get; set; } = "/garmin";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Dane, które Garmin przysłał (push): zapisujemy od razu i odpowiadamy 200, przekazanie do studiów idzie w tle z ponawianiem.</summary>
public class GarminInboxItem
{
    public int Id { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public string Payload { get; set; } = "";
    /// <summary>Rodzaje danych w paczce (np. „dailies, sleeps”) — do podglądu w panelu.</summary>
    public string Kinds { get; set; } = "";
    public DateTime? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? Error { get; set; }
}
