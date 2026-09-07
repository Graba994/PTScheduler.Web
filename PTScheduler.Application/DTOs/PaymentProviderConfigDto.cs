namespace PTScheduler.Application.DTOs;

/// <summary>Stored configuration for a single payment gateway.</summary>
public class PaymentProviderConfigDto
{
    public string Key { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool Sandbox { get; set; } = true;
    /// <summary>Gateway-specific credentials keyed by field key.</summary>
    public Dictionary<string, string> Fields { get; set; } = new();

    public string Get(string fieldKey) => Fields.TryGetValue(fieldKey, out var v) ? v ?? string.Empty : string.Empty;
}

/// <summary>UI/description metadata for one gateway (not stored — static catalog).</summary>
public record PaymentProviderField(string Key, string Label, bool Secret = false, string? Placeholder = null);

public record PaymentProviderMeta(
    string Key,
    string Name,
    string Icon,
    string Description,
    PaymentProviderField[] Fields,
    bool AlwaysConfigured = false);

/// <summary>Static catalog describing each supported gateway and its config fields.</summary>
public static class PaymentProviderCatalog
{
    public static readonly PaymentProviderMeta[] All =
    [
        new("sim", "Symulator (test)", "bi-flask",
            "Wbudowana bramka testowa. Działa zawsze w trybie sandbox — pozwala przejść pełny proces zakupu bez prawdziwej płatności. Idealna do testów.",
            [], AlwaysConfigured: true),

        new("payu", "PayU", "bi-credit-card-2-front",
            "Popularna polska bramka. Dane z panelu PayU → Ustawienia → Klucze konfiguracji (osobne dla sandbox i produkcji).",
            [
                new("PosId", "POS ID"),
                new("SecondKey", "Drugi klucz (MD5)", Secret: true),
                new("ClientId", "OAuth client_id"),
                new("ClientSecret", "OAuth client_secret", Secret: true),
            ]),

        new("p24", "Przelewy24", "bi-bank",
            "Bramka Przelewy24. Dane z panelu P24 → Konfiguracja → Dane sprzedawcy / Klucze API.",
            [
                new("MerchantId", "Merchant ID"),
                new("PosId", "POS ID (zwykle = Merchant ID)"),
                new("ApiKey", "Klucz do raportów (API key)", Secret: true),
                new("CrcKey", "Klucz CRC", Secret: true),
            ]),

        new("klarna", "Klarna", "bi-basket",
            "Klarna Payments. Dane z Klarna Merchant Portal (API credentials). Region dopasuj do konta (eu / na / oc).",
            [
                new("Username", "Username (API)"),
                new("Password", "Password (API)", Secret: true),
                new("Region", "Region (eu / na / oc)", Placeholder: "eu"),
            ]),

        new("autopay", "Autopay", "bi-cash-coin",
            "Autopay (dawniej Blue Media / BM). Dane z panelu Autopay → Konfiguracja → Ustawienia techniczne.",
            [
                new("ServiceId", "ServiceID"),
                new("SharedKey", "Klucz współdzielony (hash)", Secret: true),
            ]),
    ];

    public static PaymentProviderMeta? Find(string key) => All.FirstOrDefault(m => m.Key == key);
}
