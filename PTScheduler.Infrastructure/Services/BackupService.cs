using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Npgsql;
using PTScheduler.Application.Interfaces;

namespace PTScheduler.Infrastructure.Services;

public class BackupService(IConfiguration configuration) : IBackupService
{
    public async Task<byte[]> ExportAsync()
    {
        var connStr = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string not configured.");

        var b = new NpgsqlConnectionStringBuilder(connStr);

        var psi = new ProcessStartInfo
        {
            FileName = "pg_dump",
            Arguments = $"-h {b.Host} -p {b.Port} -U {b.Username} -d {b.Database} -F p",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.Environment["PGPASSWORD"] = b.Password ?? string.Empty;

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start pg_dump. Make sure PostgreSQL client tools are installed.");

        using var ms = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(ms);
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var err = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"pg_dump failed (exit {process.ExitCode}): {err}");
        }

        return ms.ToArray();
    }

    public async Task ImportAsync(Stream backupStream)
    {
        var connStr = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string not configured.");

        var b = new NpgsqlConnectionStringBuilder(connStr);
        var tempFile = Path.GetTempFileName();

        try
        {
            await using (var fs = File.Create(tempFile))
                await backupStream.CopyToAsync(fs);

            var psi = new ProcessStartInfo
            {
                FileName = "psql",
                Arguments = $"-h {b.Host} -p {b.Port} -U {b.Username} -d {b.Database} -f \"{tempFile}\"",
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.Environment["PGPASSWORD"] = b.Password ?? string.Empty;

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Could not start psql. Make sure PostgreSQL client tools are installed.");

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var err = await process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"psql restore failed (exit {process.ExitCode}): {err}");
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
