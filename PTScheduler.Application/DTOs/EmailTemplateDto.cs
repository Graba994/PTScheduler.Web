namespace PTScheduler.Application.DTOs;

public class EmailTemplateDto
{
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Icon { get; set; } = "bi-envelope";
    public string Subject { get; set; } = "";
    public string HeaderTitle { get; set; } = "";
    public string HtmlBody { get; set; } = "";
    public string AccentColor { get; set; } = "#0284C7";
    public string FooterText { get; set; } = "Wiadomość automatyczna — nie odpowiadaj na ten email.";
    public List<string> AvailableVariables { get; set; } = [];
    public bool IsCustomized { get; set; }
}
