namespace PTScheduler.Application.Interfaces;

public interface IClientReportService
{
    /// <summary>
    /// Generates a printable PDF report for the given client covering one month
    /// (sessions, body measurements, trainer notes, summary stats). Returns the
    /// raw PDF bytes — caller is responsible for persisting or streaming.
    /// </summary>
    /// <returns>(pdfBytes, suggestedFileName)</returns>
    Task<(byte[] Bytes, string FileName)> GenerateMonthlyReportAsync(int clientId, int year, int month);
}
