using PTScheduler.Application.Marketing;

namespace PTScheduler.Application.Interfaces;

public interface IGiftVoucherService
{
    /// <summary>Tworzy bon czekający na płatność (zakup online). Zwraca id bonu.</summary>
    Task<(bool Ok, string? Error, int? VoucherId)> CreatePendingAsync(string buyerUserId, GiftVoucherRequest request);

    /// <summary>Trener wystawia bon od razu aktywny (np. sprzedany na miejscu).</summary>
    Task<(bool Ok, string? Error, GiftVoucherDto? Voucher)> IssueAsync(string issuedByUserId, GiftVoucherRequest request);

    /// <summary>Po opłaceniu zamówienia: aktywacja bonu (dla bonu kwotowego — kupon).</summary>
    Task ActivateAsync(int voucherId);

    /// <summary>Realizacja bonu na pakiet przez klienta — tworzy opłacony pakiet sesji.</summary>
    Task<(bool Ok, string Message)> RedeemAsync(string code, int clientId);

    /// <summary>Trener oznacza bon jako wykorzystany (np. klient zrealizował go na miejscu).</summary>
    Task<bool> MarkRedeemedAsync(int voucherId);
    Task<bool> CancelAsync(int voucherId);

    Task<GiftVoucherDto?> GetByCodeAsync(string code);
    Task<List<GiftVoucherDto>> GetBoughtByAsync(string buyerUserId);
    Task<List<GiftVoucherDto>> GetAllAsync();

    Task<(byte[] Bytes, string FileName)> GeneratePdfAsync(int voucherId);
}
