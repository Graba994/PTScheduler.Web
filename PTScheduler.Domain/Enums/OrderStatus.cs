namespace PTScheduler.Domain.Enums;

public enum OrderStatus
{
    Pending,   // created, awaiting payment
    Paid,      // payment completed
    Canceled,  // canceled by buyer / PayU
    Failed     // rejected / error
}
