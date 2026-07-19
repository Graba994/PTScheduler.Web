namespace PTScheduler.Application.DTOs;

public class PaymentSettingsDto
{
    public bool Enabled { get; set; }
    public bool Sandbox { get; set; } = true;
    public string? PosId { get; set; }
    public string? SecondKey { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string Currency { get; set; } = "PLN";
}
