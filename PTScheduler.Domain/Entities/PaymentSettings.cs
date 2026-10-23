namespace PTScheduler.Domain.Entities;

/// <summary>Single-row payment configuration (master switch + per-gateway config).</summary>
public class PaymentSettings
{
    public int Id { get; set; } = 1;

    // Master switch — online payments available to clients.
    public bool Enabled { get; set; }

    // Legacy single-gateway PayU fields (kept for backward compatibility;
    // migrated into ProvidersJson on first save of the new config UI).
    public bool Sandbox { get; set; } = true;
    public string? PosId { get; set; }
    public string? SecondKey { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    public string Currency { get; set; } = "PLN";

    /// <summary>
    /// JSON map of gateway key → provider config
    /// ({ enabled, sandbox, fields: { ... } }). See PaymentSettingsService.
    /// </summary>
    public string? ProvidersJson { get; set; }
}
