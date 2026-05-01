using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PTScheduler.Application.Interfaces;

namespace PTScheduler.Infrastructure.Services;

public class DatabaseSettingsService(IConfiguration configuration, string settingsFilePath) : IDatabaseSettingsService
{
    public string GetConnectionString()
        => configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

    public void SaveConnectionString(string connectionString)
    {
        var data = new { ConnectionStrings = new { DefaultConnection = connectionString } };
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(settingsFilePath, json);
    }

    public async Task<bool> TestConnectionAsync(string connectionString)
    {
        try
        {
            await using var conn = new Npgsql.NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
