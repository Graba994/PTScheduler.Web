namespace PTScheduler.Application.DTOs;

public class WebPushSettingsDto
{
    public string PublicKey { get; set; } = "";
    public string PrivateKey { get; set; } = "";
    public string Subject { get; set; } = "mailto:admin@ptscheduler.app";
    public bool IsConfigured => !string.IsNullOrEmpty(PublicKey) && !string.IsNullOrEmpty(PrivateKey);
}

public class PushSubscriptionDto
{
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
}

public class PushMessageDto
{
    public string Title { get; set; } = "PTScheduler";
    public string Body { get; set; } = "";
    public string? Url { get; set; }
    public string? Icon { get; set; }
}
