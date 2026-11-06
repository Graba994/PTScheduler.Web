namespace PTScheduler.Application.Interfaces;

public interface IInvoiceService
{
    Task<(byte[] Bytes, string FileName)> GenerateInvoiceAsync(int orderId);
}
