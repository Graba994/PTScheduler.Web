using PTScheduler.Domain.Constants;

namespace PTScheduler.Application.DTOs;

public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string FullName => string.IsNullOrWhiteSpace($"{FirstName} {LastName}".Trim())
        ? Email
        : $"{FirstName} {LastName}".Trim();
    public string Role { get; set; } = string.Empty;
    public string? SupervisorId { get; set; }
    public string? SupervisorEmail { get; set; }
    public bool IsLockedOut { get; set; }
}

public class CreateUserDto
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName  { get; set; } = string.Empty;
    public string Email     { get; set; } = string.Empty;
    public string Password  { get; set; } = string.Empty;
    public string Role      { get; set; } = Roles.Trainer;
    public string? SupervisorId { get; set; }
}
