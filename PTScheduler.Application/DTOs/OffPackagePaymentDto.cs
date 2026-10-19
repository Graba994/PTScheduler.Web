namespace PTScheduler.Application.DTOs;

/// <summary>Trening poza pakietem opłacony u trenera — wiersz w Finansach (kwota z ceny pojedynczego treningu).</summary>
public class OffPackagePaymentDto
{
    public int SessionId { get; set; }
    public int ClientId { get; set; }
    public string ClientName { get; set; } = "";
    public string SessionTypeName { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime PaidAt { get; set; }
    public string? PaidVia { get; set; }
    public decimal Amount { get; set; }
    public bool IsHidden { get; set; }
}
