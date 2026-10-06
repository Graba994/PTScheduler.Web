namespace PTScheduler.Application.Interfaces;

/// <summary>Dane nabywcy na fakturze (firma z NIP albo osoba prywatna).</summary>
public class InvoiceBuyerDto
{
    public string? Nip { get; set; }
    public string? Name { get; set; }
    public string? Address { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
}

public interface IKsefService
{
    /// <summary>Zapisuje token KSeF (szyfrowany). Pusty = usunięcie tokenu.</summary>
    Task SaveTokenAsync(string? token);

    /// <summary>Sprawdza połączenie i token na wybranym środowisku.</summary>
    Task<(bool Ok, string Message)> TestConnectionAsync();

    /// <summary>Zapisuje dane nabywcy zamówienia (faktura na firmę).</summary>
    Task<(bool Ok, string? Error)> SetBuyerAsync(int orderId, InvoiceBuyerDto buyer);

    /// <summary>Wysyła fakturę zamówienia do KSeF (nadaje numer faktury, jeśli go brak).</summary>
    Task<(bool Ok, string Message)> SendOrderInvoiceAsync(int orderId);

    /// <summary>Odświeża stan faktur oczekujących w KSeF; zwraca liczbę zmienionych.</summary>
    Task<int> RefreshPendingAsync(CancellationToken ct = default);
}
