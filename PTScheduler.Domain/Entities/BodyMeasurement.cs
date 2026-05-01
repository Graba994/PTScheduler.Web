namespace PTScheduler.Domain.Entities;

public class BodyMeasurement
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    public DateOnly MeasurementDate { get; set; }

    public decimal? WeightKg { get; set; }
    public decimal? BodyFatPercent { get; set; }
    public decimal? ChestCm { get; set; }
    public decimal? WaistCm { get; set; }
    public decimal? HipsCm { get; set; }
    public decimal? ThighCm { get; set; }
    public decimal? ArmCm { get; set; }
    public string? Notes { get; set; }
}
