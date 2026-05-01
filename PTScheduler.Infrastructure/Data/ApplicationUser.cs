using Microsoft.AspNetCore.Identity;

namespace PTScheduler.Infrastructure.Data;

public class ApplicationUser : IdentityUser
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    // For Subordinate role: points to the Trainer who manages this user
    public string? SupervisorId { get; set; }
    public ApplicationUser? Supervisor { get; set; }
    public ICollection<ApplicationUser> Subordinates { get; set; } = [];
}
