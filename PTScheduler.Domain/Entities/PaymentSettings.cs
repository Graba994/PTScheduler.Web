namespace PTScheduler.Domain.Entities;

/// <summary>Single-row PayU configuration.</summary>
public class PaymentSettings
{
    public int Id { get; set; } = 1;

    // Master switch — online payments available to clients.
    public bool Enabled { get; set; }

    // true = PayU sandbox, false = production.
    public bool Sandbox { get; set; } = true;

    // PayU credentials (from the PayU merchant panel).
    public string? PosId { get; set; }        // POS ID / merchant POS
    public string? SecondKey { get; set; }    // second key (MD5) for signature
    public string? ClientId { get; set; }     // OAuth client_id (often equals PosId)
    public string? ClientSecret { get; set; } // OAuth client_secret

    public string Currency { get; set; } = "PLN";
}
