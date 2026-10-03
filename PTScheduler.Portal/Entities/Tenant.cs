namespace PTScheduler.Portal.Entities;

public class Tenant
{
    public int Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public int Port { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string DbPassword { get; set; } = string.Empty;

    /// <summary>Sekret wywołań Portal ↔ ta instancja (null = wspólny sekret, instancje sprzed tej zmiany).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? InternalSecret { get; set; }
    public TenantStatus Status { get; set; } = TenantStatus.Pending;
    public string PlanId { get; set; } = "start";
    public string? SetupMode { get; set; }
    /// <summary>
    /// Dane z kreatora rejestracji (JSON: kolor, szablon strony, oferta, skrót hasła) — czekają na
    /// uruchomienie instancji, trafiają do niej przez /internal/setup/bootstrap i są wtedy czyszczone.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? SetupPayload { get; set; }
    public string? Notes { get; set; }
    /// <summary>Rozliczenie abonamentu: „monthly” albo „yearly” (wybrane w kreatorze).</summary>
    public string BillingInterval { get; set; } = "monthly";
    /// <summary>Dlaczego zgłoszenie czeka w kolejce zamiast uruchomić się samo (dla admina; null = nie czeka).</summary>
    public string? QueuedReason { get; set; }
    /// <summary>Losowy klucz kreatora: powrót z płatności kartą wraca do właściwego zgłoszenia (w adresie, nie Id).</summary>
    public string? RegistrationKey { get; set; }
    /// <summary>Płatność weryfikacyjna przy rejestracji przez Autopay / PayU / Przelewy24 (bez Stripe).</summary>
    public string? RegistrationPaymentId { get; set; }
    public string? RegistrationPaymentGateway { get; set; }
    public DateTime? RegistrationPaidAt { get; set; }
    /// <summary>Trener zrezygnował z subskrypcji (rozliczanej rachunkami) — przy następnym rozliczeniu zawieszamy zamiast wystawiać rachunek.</summary>
    public DateTime? CancelRequestedAt { get; set; }
    /// <summary>Kod zaproszenia użyty przy rejestracji.</summary>
    public string? InviteCode { get; set; }
    /// <summary>Własna domena trenera (np. annafit.pl) — działa obok adresu w domenie platformy.</summary>
    public string? CustomDomain { get; set; }
    /// <summary>„waiting” (czekamy na DNS), „active”, „failed” (DNS nie wskazał serwera w 7 dni).</summary>
    public string? CustomDomainStatus { get; set; }
    public DateTime? CustomDomainSince { get; set; }
    public string? WebContainerName { get; set; }
    public string? DbContainerName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProvisionedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? TrialEndsAt { get; set; }

    // Lifecycle management
    public DateTime? LastActivityAt { get; set; }
    /// <summary>Kiedy ostatnio udało się odczytać aktywność — bez świeżego odczytu nie zawieszamy za bezczynność.</summary>
    public DateTime? LastActivityCheckedAt { get; set; }

    // ── Wideo: osobna biblioteka Bunny Stream tej instancji (zakładana przez Portal) ──
    public string? BunnyLibraryId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? BunnyLibraryApiKey { get; set; }
    public string? BunnyCdnHostname { get; set; }
    public DateTime? GraceUntil { get; set; }

    // Stripe subscription lifecycle
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public string? StripeCheckoutSessionId { get; set; }
    public string BillingStatus { get; set; } = "none"; // none | trialing | active | past_due | canceled

    // Health monitoring
    public bool? IsHealthy { get; set; }
    public DateTime? LastHealthCheckAt { get; set; }
    public string? LastHealthError { get; set; }
    public int? LastHealthResponseMs { get; set; }
    public DateTime? UnhealthySinceUtc { get; set; }
    public bool DownAlertSent { get; set; }

    public Plan? Plan { get; set; }
    public ICollection<Subscription> Subscriptions { get; set; } = [];
}

public enum TenantStatus
{
    Pending,
    Provisioning,
    Active,
    Suspended,
    Destroyed
}
