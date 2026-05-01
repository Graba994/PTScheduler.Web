namespace PTScheduler.Application.Interfaces;

public class ClearDataResult
{
    public int Sessions { get; set; }
    public int Packages { get; set; }
    public int Clients { get; set; }
    public int ClientUsers { get; set; }
    public int Notes { get; set; }
    public int Measurements { get; set; }
    public int IntroConfigs { get; set; }
}

public interface IDatabaseMaintenanceService
{
    Task<ClearDataResult> ClearAllDataAsync();
}
