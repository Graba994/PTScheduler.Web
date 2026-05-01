namespace PTScheduler.Application.Interfaces;

public interface IBackupService
{
    Task<byte[]> ExportAsync();
    Task ImportAsync(Stream backupStream);
}
