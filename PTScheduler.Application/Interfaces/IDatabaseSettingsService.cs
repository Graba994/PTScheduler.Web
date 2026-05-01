namespace PTScheduler.Application.Interfaces;

public interface IDatabaseSettingsService
{
    string GetConnectionString();
    void SaveConnectionString(string connectionString);
    Task<bool> TestConnectionAsync(string connectionString);
}
