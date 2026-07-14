namespace PTScheduler.Domain.Entities;

public class LoginLog
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime LoginTime { get; set; } = DateTime.UtcNow;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool Success { get; set; } = true;
}
