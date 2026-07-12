namespace PTScheduler.Domain.Entities;

public class EmailTemplate
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Subject { get; set; } = "";
    public string HeaderTitle { get; set; } = "";
    public string HtmlBody { get; set; } = "";
    public string AccentColor { get; set; } = "#0284C7";
    public string FooterText { get; set; } = "Wiadomość automatyczna — nie odpowiadaj na ten email.";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
