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
}
