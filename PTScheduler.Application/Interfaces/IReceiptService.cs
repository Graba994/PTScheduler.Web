namespace PTScheduler.Application.Interfaces;

public interface IReceiptService
{
    Task<(byte[] Bytes, string FileName)> GenerateReceiptAsync(int orderId);
}
