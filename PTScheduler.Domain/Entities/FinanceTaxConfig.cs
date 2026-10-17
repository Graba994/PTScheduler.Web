namespace PTScheduler.Domain.Entities;

public class FinanceTaxConfig
{
    public int Id { get; set; }
    public string Module { get; set; } = "standard";

    public bool VatEnabled { get; set; }
    public decimal VatRate { get; set; } = 23m;

    public string IncomeTaxType { get; set; } = "none";
    public decimal FlatTaxRate { get; set; } = 19m;
    public decimal LumpSumRate { get; set; } = 8.5m;
    public decimal ScaleTaxThreshold { get; set; } = 120000m;
    public decimal ScaleTaxRateLow { get; set; } = 12m;
    public decimal ScaleTaxRateHigh { get; set; } = 32m;

    public bool ZusEnabled { get; set; }
    public decimal ZusMonthlyAmount { get; set; } = 1600m;

    public bool HealthInsuranceEnabled { get; set; }
    public decimal HealthInsuranceMonthly { get; set; } = 380m;

    public bool CostDeductionsEnabled { get; set; }
    public decimal MonthlyFixedCosts { get; set; }

    public bool InvoiceNumberingEnabled { get; set; }
    public string InvoicePrefix { get; set; } = "FV";
    public int InvoiceNextNumber { get; set; } = 1;

    public string? SellerNip { get; set; }
    public string? SellerAddress { get; set; }
    public string? SellerCity { get; set; }
    public string? SellerPostalCode { get; set; }

    /// <summary>Pełna nazwa sprzedawcy (firma) — wymagana na fakturze i w KSeF.</summary>
    public string? SellerName { get; set; }

    /// <summary>Podstawa zwolnienia z VAT, gdy VAT wyłączony (np. „art. 113 ust. 1 ustawy o VAT”).</summary>
    public string? VatExemptBasis { get; set; }

    // ── KSeF ─────────────────────────────────────────────────────────────
    public bool KsefEnabled { get; set; }
    /// <summary>„test” | „demo” | „production”.</summary>
    public string KsefEnvironment { get; set; } = "test";
    /// <summary>Opcjonalny adres API nadpisujący domyślny dla środowiska.</summary>
    public string? KsefApiUrl { get; set; }
    /// <summary>Token KSeF zaszyfrowany Data Protection (nigdy w postaci jawnej).</summary>
    public string? KsefTokenProtected { get; set; }
}
