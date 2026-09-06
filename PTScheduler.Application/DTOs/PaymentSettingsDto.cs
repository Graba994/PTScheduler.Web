namespace PTScheduler.Application.DTOs;

public class PaymentSettingsDto
{
    /// <summary>Master switch — online payments available to clients.</summary>
    public bool Enabled { get; set; }
    public string Currency { get; set; } = "PLN";

    /// <summary>Per-gateway configuration (one entry per supported provider).</summary>
    public List<PaymentProviderConfigDto> Providers { get; set; } = new();

    public PaymentProviderConfigDto? Provider(string key) => Providers.FirstOrDefault(p => p.Key == key);

    /// <summary>Gateways that are enabled.</summary>
    public IEnumerable<PaymentProviderConfigDto> EnabledProviders => Providers.Where(p => p.Enabled);
}
