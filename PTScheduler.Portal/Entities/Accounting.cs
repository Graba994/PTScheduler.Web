namespace PTScheduler.Portal.Entities;

/// <summary>
/// Dokument sprzedaży platformy: rachunek, faktura bez VAT, faktura VAT albo korekta.
/// Po wystawieniu się nie zmienia — dane sprzedawcy i nabywcy są zapisane w chwili wystawienia,
/// więc stare dokumenty zostają prawdziwe także po zmianie danych albo po założeniu firmy.
/// </summary>
public class SalesDocument
{
    public int Id { get; set; }
    /// <summary><see cref="SalesDocumentKinds"/>.</summary>
    public string Kind { get; set; } = SalesDocumentKinds.Receipt;
    /// <summary>Pełny numer, np. RACH/2026/0007 (z Fakturowni — jej numer). Pusty, dopóki Fakturownia nie nada numeru.</summary>
    public string Number { get; set; } = "";
    /// <summary>Seria numeracji: „rachunek”, „faktura” (bez VAT i VAT razem) albo „korekta”.</summary>
    public string Series { get; set; } = "";
    /// <summary>Rok i kolejny numer w serii (tylko numeracja Hantly; przy Fakturowni 0).</summary>
    public int Year { get; set; }
    public int Seq { get; set; }

    public DateOnly IssueDate { get; set; }
    public DateOnly SaleDate { get; set; }
    public DateOnly? PaidDate { get; set; }
    /// <summary>Np. „przelew”, „karta”, „PayU”, „Przelewy24”, „Autopay”, „gotówka”.</summary>
    public string? PaymentMethod { get; set; }

    /// <summary>„unregistered” (działalność nierejestrowana) albo „business” — tryb w chwili wystawienia.</summary>
    public string Mode { get; set; } = FinanceModes.Unregistered;
    public SalesDocumentStatus Status { get; set; } = SalesDocumentStatus.Issued;

    public int? TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public int? PaymentRecordId { get; set; }
    public int? BillId { get; set; }
    /// <summary>Dokument korygowany (tylko dla korekty).</summary>
    public int? CorrectsId { get; set; }
    public SalesDocument? Corrects { get; set; }
    public string? CorrectionReason { get; set; }

    /// <summary>Dane sprzedawcy (wiele linii) w chwili wystawienia.</summary>
    public string Seller { get; set; } = "";
    public string BuyerName { get; set; } = "";
    public string? BuyerAddress { get; set; }
    public string? BuyerTaxId { get; set; }
    public string? BuyerEmail { get; set; }

    public decimal Net { get; set; }
    public decimal Vat { get; set; }
    public decimal Gross { get; set; }
    public string Currency { get; set; } = "PLN";
    /// <summary>Podstawa zwolnienia z VAT albo inna adnotacja drukowana na dokumencie.</summary>
    public string? VatNote { get; set; }
    public string? Note { get; set; }

    /// <summary>„local” (numer i PDF z Hantly) albo „fakturownia”.</summary>
    public string Provider { get; set; } = "local";
    public string? ExternalId { get; set; }
    public string? ExternalUrl { get; set; }
    public string? ExternalError { get; set; }
    public int ExternalAttempts { get; set; }
    /// <summary>Czeka na zatwierdzenie przed wysłaniem do Fakturowni (tryb „po moim zatwierdzeniu”).</summary>
    public bool AwaitingApproval { get; set; }
    public DateTime? ExternalSyncedAt { get; set; }

    public DateTime? EmailedAt { get; set; }
    public string? CancelReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }

    public List<SalesDocumentLine> Lines { get; set; } = [];

    /// <summary>Dokument jest „w obrocie” (wysłany albo w Fakturowni) — zamiast anulowania tylko korekta.</summary>
    public bool InCirculation => EmailedAt is not null || ExternalId is not null;
}

public class SalesDocumentLine
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public SalesDocument? Document { get; set; }
    public string Name { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public string Unit { get; set; } = "szt.";
    /// <summary>„zw” (zwolnione / bez VAT), „23”, „8”, „5”, „0”, „np”.</summary>
    public string VatRate { get; set; } = "zw";
    public decimal UnitGross { get; set; }
    public decimal Net { get; set; }
    public decimal Vat { get; set; }
    public decimal Gross { get; set; }
}

public enum SalesDocumentStatus
{
    Issued = 0,
    /// <summary>Czeka na numer z Fakturowni (błąd połączenia — ponawiamy).</summary>
    Pending = 1,
    Cancelled = 2
}

public static class SalesDocumentKinds
{
    public const string Receipt = "rachunek";
    public const string InvoiceExempt = "faktura_zw";
    public const string InvoiceVat = "faktura_vat";
    public const string Correction = "korekta";

    public static readonly string[] All = [Receipt, InvoiceExempt, InvoiceVat, Correction];

    public static string SeriesOf(string kind) => kind switch
    {
        InvoiceExempt or InvoiceVat => "faktura",
        Correction => "korekta",
        _ => "rachunek"
    };

    public static string Label(string kind) => kind switch
    {
        Receipt => "Rachunek",
        InvoiceExempt => "Faktura bez VAT",
        InvoiceVat => "Faktura VAT",
        Correction => "Korekta",
        _ => kind
    };
}

public static class FinanceModes
{
    public const string Unregistered = "unregistered";
    public const string Business = "business";
}

/// <summary>
/// Wpis w ewidencji sprzedaży: jedna sprzedaż (wpłata, ręczny wpis albo korekta in minus).
/// Ewidencja dzienna z narastającą sumą miesiąca powstaje z tych wpisów przy wyświetlaniu i eksporcie.
/// Wpisów się nie usuwa — błędny wpis prostuje się wpisem korygującym.
/// </summary>
public class SalesLedgerEntry
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = "";
    /// <summary>„payment” (wpłata z platformy), „manual”, „correction”, „import” (dokument pobrany z Fakturowni).</summary>
    public string Source { get; set; } = "payment";
    public int? PaymentRecordId { get; set; }
    public int? DocumentId { get; set; }
    public int? TenantId { get; set; }
    /// <summary>Wpis prostujący (in minus) — dotyczy tego wpisu.</summary>
    public int? ReversesId { get; set; }
    /// <summary>Wyłączony z ewidencji (np. testowa wpłata po usunięciu dokumentu) — nie liczy się do sum ani limitu, można go przywrócić.</summary>
    public bool Excluded { get; set; }
    public string? ExcludedReason { get; set; }
    /// <summary>Bez automatycznego dokumentu (dokument usunięto, a wpłata zostaje) — dokument można wystawić ręcznie.</summary>
    public bool SkipDocument { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
}

/// <summary>Koszt (wydatek) z dowodem: serwer, domena, księgowa, sprzęt… Usunięcie jest miękkie i zapisane w historii.</summary>
public class Expense
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public string Vendor { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "inne";
    public string? DocumentNumber { get; set; }
    public byte[]? Attachment { get; set; }
    public string? AttachmentName { get; set; }
    public string? AttachmentType { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedReason { get; set; }
}

/// <summary>Historia zmian w księgowości (tylko dopisywana): kto, kiedy i co zrobił.</summary>
public class FinanceEvent
{
    public long Id { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string Actor { get; set; } = "system";
    public string Action { get; set; } = "";
    public string? Entity { get; set; }
    public int? EntityId { get; set; }
    public string? Detail { get; set; }
}
