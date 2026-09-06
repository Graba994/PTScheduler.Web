namespace PTScheduler.Application.Interfaces;

public interface ISetupService
{
    Task<bool> IsSetupCompletedAsync();
    Task CompleteSetupAsync(string mode, string companyName, string adminEmail, string adminPassword);
}
