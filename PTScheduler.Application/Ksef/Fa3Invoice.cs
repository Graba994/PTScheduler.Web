namespace PTScheduler.Application.Ksef;

/// <summary>Dane faktury potrzebne do zbudowania XML w schemacie FA(3).</summary>
public sealed record Fa3Party(string? Nip, string Name, string? AddressLine1, string? AddressLine2);

public sealed record Fa3Line(string Name, decimal Quantity, decimal UnitGross, string Unit = "szt.");

public sealed record Fa3Invoice(
    string Number,
    DateOnly IssueDate,
    DateOnly SaleDate,
    Fa3Party Seller,
    Fa3Party Buyer,
    IReadOnlyList<Fa3Line> Lines,
    bool VatPayer,
    decimal VatRate,
    string? VatExemptBasis,
    string Currency = "PLN",
    DateOnly? PaidDate = null);
