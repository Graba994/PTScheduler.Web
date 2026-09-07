namespace PTScheduler.Portal.Entities;

public class LoginLog
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
