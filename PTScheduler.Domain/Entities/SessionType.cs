namespace PTScheduler.Domain.Entities;

public class SessionType
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public bool IsGroup { get; set; }
    public int? MaxParticipants { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Session> Sessions { get; set; } = [];
}
