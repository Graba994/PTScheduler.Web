namespace PTScheduler.Domain.Enums;

/// <summary>
/// The nature of a client's access to a course. Drives default expiry and
/// how the grant is presented. Timing itself is stored on the enrollment
/// (ExpiresAt); this describes the intent behind that access.
/// </summary>
public enum CourseAccessType
{
    Lifetime,     // Dożywotni — never expires
    Timed,        // Czasowy — expires on a date / after N days
    Trial,        // Testowy — short, limited evaluation access
    Promotional   // Promocyjny — promo/discounted access
}
