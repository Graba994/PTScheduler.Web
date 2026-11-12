namespace PTScheduler.Domain.Enums;

/// <summary>Jak klient korzysta z planu: na treningach z trenerem, samodzielnie albo oba.</summary>
public enum PlanUse
{
    /// <summary>Na wizytach z trenerem i samodzielnie w aplikacji (domyślnie — tak działały dotychczasowe plany).</summary>
    Both = 0,
    /// <summary>Tylko na treningach z trenerem — klient widzi go, ale nie zaczyna sam w „Trenuj”.</summary>
    WithTrainer = 1,
    /// <summary>Do samodzielnego trenowania (bez spotkania), np. plan na dni między wizytami.</summary>
    Solo = 2
}
