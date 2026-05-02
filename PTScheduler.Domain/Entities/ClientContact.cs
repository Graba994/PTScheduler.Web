namespace PTScheduler.Domain.Entities;

/// <summary>
/// A "pair" relationship between two clients under the same trainer.
/// Client1Id is always less than Client2Id (enforced in app layer).
/// </summary>
public class ClientContact
{
    public int Id { get; set; }
    public string TrainerUserId { get; set; } = string.Empty;

    public int Client1Id { get; set; }
    public Client Client1 { get; set; } = null!;

    public int Client2Id { get; set; }
    public Client Client2 { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
