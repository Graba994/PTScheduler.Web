namespace PTScheduler.Domain.Enums;

/// <summary>What an <see cref="Entities.Order"/> pays for.</summary>
public enum OrderKind
{
    Course,   // access to a course
    Package,  // a session package (bookable credits)
    Membership, // opłata za okres karnetu cyklicznego
    GiftVoucher // bon podarunkowy
}
