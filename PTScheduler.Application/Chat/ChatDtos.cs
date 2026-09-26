namespace PTScheduler.Application.Chat;

public class ChatMessageDto
{
    public long Id { get; set; }
    public int ClientId { get; set; }
    public string SenderUserId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public bool FromStaff { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public class ChatConversationDto
{
    public int ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string? LastMessage { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public bool LastFromStaff { get; set; }
    /// <summary>Nieprzeczytane wiadomości od klienta (dla trenera).</summary>
    public int Unread { get; set; }
}
