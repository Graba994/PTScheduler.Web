namespace PTScheduler.Application.Marketing;

public class ClientReviewDto
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string ClientName { get; set; } = "";
    public int Rating { get; set; }
    public string? Text { get; set; }
    public string DisplayName { get; set; } = "";
    public bool PublishConsent { get; set; }
    public bool IsPublished { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool ClickedGoogle { get; set; }
}

public class PublicReviewDto
{
    public int Rating { get; set; }
    public string Text { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class PublicReviewsDto
{
    public List<PublicReviewDto> Items { get; set; } = [];
    public double Average { get; set; }
    public int Count { get; set; }
}

/// <summary>Co pokazać klientowi: prośbę o opinię, jego obecną opinię i link do Google.</summary>
public class ReviewPromptDto
{
    public bool Enabled { get; set; }
    public bool ShouldAsk { get; set; }
    public ClientReviewDto? Existing { get; set; }
    public string? GoogleReviewUrl { get; set; }
}

/// <summary>Ocena aplikacji wysyłana przez trenera do właściciela platformy.</summary>
public class AppFeedbackDto
{
    public int Rating { get; set; }
    public string? Text { get; set; }
    public string? ContactEmail { get; set; }
}
