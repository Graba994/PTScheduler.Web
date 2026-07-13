namespace PTScheduler.Domain.Entities;

public class WebPushSettings
{
    public int Id { get; set; } = 1;
    public string PublicKey { get; set; } = "";
    public string PrivateKey { get; set; } = "";
    public string Subject { get; set; } = "mailto:admin@ptscheduler.app";
}
