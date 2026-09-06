namespace PTScheduler.Application.Interfaces;

public interface IDataExportService
{
    Task<byte[]> ExportClientsCsvAsync(string? trainerUserId = null);
    Task<byte[]> ExportSessionsCsvAsync(DateTime from, DateTime to, string? trainerUserId = null);
    Task<byte[]> ExportPackagesCsvAsync(string? trainerUserId = null);
    Task<byte[]> ExportMeasurementsCsvAsync(string? trainerUserId = null);
    Task<byte[]> ExportOrdersCsvAsync(DateTime from, DateTime to);
}
