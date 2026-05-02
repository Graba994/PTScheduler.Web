namespace PTScheduler.Application.DTOs;

public class BodyMeasurementDto
{
    public int Id { get; set; }
    public int ClientId { get; set; }
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

public class CreateBodyMeasurementDto
{
    public int ClientId { get; set; }
    public DateOnly MeasurementDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public decimal? WeightKg { get; set; }
    public decimal? BodyFatPercent { get; set; }
    public decimal? ChestCm { get; set; }
    public decimal? WaistCm { get; set; }
    public decimal? HipsCm { get; set; }
    public decimal? ThighCm { get; set; }
    public decimal? ArmCm { get; set; }
    public string? Notes { get; set; }
}
