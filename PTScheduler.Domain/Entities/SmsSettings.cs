namespace PTScheduler.Domain.Entities;

public class SmsSettings
{
    public int Id { get; set; } = 1;
    public bool IsEnabled { get; set; }
    public string ApiToken { get; set; } = "";
    public string SenderName { get; set; } = "";

    // Monthly quota tracking (yyyyMM as int, e.g. 202607). Reset when the
    // month rolls over so the counter doesn't need a separate cron job.
    public int QuotaMonthKey { get; set; }
    public int QuotaSentCount { get; set; }
}
