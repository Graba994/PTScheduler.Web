namespace PTScheduler.Application.Interfaces;

public interface IDemoDataService
{
    Task<DemoSeedResult> SeedAsync();
    Task ResetToAdminAsync();
    Task<DemoSeedResult> ResetAndSeedAsync();
}

public class DemoSeedResult
{
    public bool AlreadySeeded { get; set; }
    public List<DemoUserInfo> Users { get; set; } = [];
}

public class DemoUserInfo
{
    public string Role { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}
