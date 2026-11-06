namespace PTScheduler.Domain.Constants;

public static class Roles
{
    public const string Admin = "Admin";
    /// <summary>
    /// Konto techniczne operatora (root@admin.local) — zawsze razem z rolą Admin.
    /// Ma wszystko, co administrator studia, plus funkcje techniczne (baza, kopie, dane demo).
    /// </summary>
    public const string Root = "Root";
    public const string Trainer = "Trainer";
    public const string Subordinate = "Subordinate";
    public const string Client = "Client";
}
