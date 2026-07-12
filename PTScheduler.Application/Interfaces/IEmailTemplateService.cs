using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IEmailTemplateService
{
    Task<List<EmailTemplateDto>> GetAllAsync();
    Task<EmailTemplateDto> GetAsync(string key);
    Task SaveAsync(string key, EmailTemplateDto dto);
    Task ResetAsync(string key);
    Task<(string Subject, string Html)> RenderAsync(string key, Dictionary<string, string> variables);
}
