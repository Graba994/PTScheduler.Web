namespace PTScheduler.Application.DTOs;

/// <summary>
/// Which optional platform modules are enabled for clients. Stored as a JSON
/// blob in the persistent branding volume (no migration). Extend as modules grow.
/// </summary>
public class ModuleSettingsDto
{
    // Training portal (courses) visible to clients.
    public bool CoursesEnabled { get; set; } = true;
}
