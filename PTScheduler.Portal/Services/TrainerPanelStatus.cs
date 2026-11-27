using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Stan aplikacji trenera w prostych słowach — dla jego panelu na stronie Hantly (/moje-konto):
/// co się dzieje, co trener ma zrobić i na którym etapie drogi „zgłoszenie → działa” jest.
/// </summary>
public static class TrainerPanelStatus
{
    /// <param name="Key">running, updating, down, building, queued, payment, card, preparing, suspended, deleted</param>
    /// <param name="Tone">ok, info, warn, danger, muted</param>
    public sealed record State(string Key, string Label, string Tone, string Icon, string Text, int Step);

    /// <summary>Etapy osi czasu w panelu (Step w <see cref="State"/> to indeks bieżącego; -1 — oś niewidoczna).</summary>
    public static readonly string[] Steps = ["Zgłoszenie", "E-mail i płatność", "Akceptacja", "Uruchomienie", "Działa"];

    public static State From(Tenant t, bool updating = false)
    {
        switch (t.Status)
        {
            case TenantStatus.Destroyed:
                return new("deleted", "Usunięta", "muted", "bi-trash3",
                    "Ta aplikacja została usunięta. Jeśli to pomyłka, napisz do nas — sprawdzimy, czy da się ją przywrócić z kopii.", -1);

            case TenantStatus.Suspended:
                if (t.CancelRequestedAt is not null)
                    return new("suspended", "Uśpiona", "muted", "bi-moon-stars",
                        "Uśpiona po rezygnacji z subskrypcji. Dane klientów zostają — napisz do nas, a włączymy ją ponownie.", -1);
                if (t.BillingStatus is "past_due" or "canceled")
                    return new("suspended", "Zawieszona", "danger", "bi-pause-circle",
                        "Abonament nie jest opłacony. Opłać rachunek poniżej, a aplikacja wróci w kilka minut — dane są bezpieczne.", -1);
                return new("suspended", "Zawieszona", "warn", "bi-pause-circle",
                    "Aplikacja jest wstrzymana (np. po dłuższej bezczynności). Dane są bezpieczne — napisz do nas, a włączymy ją ponownie.", -1);

            case TenantStatus.Active when updating:
                return new("updating", "Aktualizacja", "info", "bi-arrow-repeat",
                    "Wgrywamy nową wersję aplikacji. Potrwa to kilka minut — klienci mogą przez chwilę zobaczyć ekran ładowania.", 4);

            case TenantStatus.Active when t.IsHealthy == false:
                return new("down", "Chwilowo niedostępna", "warn", "bi-exclamation-triangle",
                    $"Aplikacja chwilowo nie odpowiada (od {Since(t.UnhealthySinceUtc)}). Już to sprawdzamy — zwykle wraca sama w kilka minut.", 4);

            case TenantStatus.Active:
                return new("running", "Działa", "ok", "bi-check-circle-fill",
                    "Aplikacja działa. Klienci mogą się zapisywać i płacić.", 4);

            case TenantStatus.Provisioning:
                return new("building", "W budowie", "info", "bi-hammer",
                    "Serwer uruchamia Twoją aplikację — zwykle trwa to około minuty. Dostaniesz e-mail, gdy będzie gotowa.", 3);
        }

        // Pending — zgłoszenie zapisane, aplikacja jeszcze nie stoi.
        if (t.RegistrationPaymentGateway is not null && t.RegistrationPaidAt is null)
            return new("payment", "Czeka na płatność", "warn", "bi-credit-card",
                "Brakuje płatności weryfikacyjnej. Wróć do kreatora i dokończ płatność — potem od razu uruchomimy aplikację.", 1);
        if (t.StripeCheckoutSessionId is not null && t.StripeSubscriptionId is null && t.BillingStatus == "none")
            return new("card", "Czeka na kartę", "warn", "bi-credit-card-2-front",
                "Brakuje podpiętej karty. Pierwsza opłata dopiero po okresie próbnym — karta chroni nas przed zakładaniem aplikacji „na próbę”.", 1);
        if (t.QueuedReason is not null)
            return new("queued", "Czeka na akceptację", "info", "bi-hourglass-split",
                "Zgłoszenie jest zapisane i czeka na uruchomienie — zwykle w ciągu kilku godzin. Okres próbny liczymy od dnia startu.", 2);
        return new("preparing", "W przygotowaniu", "info", "bi-hourglass-split",
            "Zgłoszenie jest zapisane. Gdy aplikacja ruszy, napiszemy e-mailem.", 1);
    }

    private static string Since(DateTime? utc)
    {
        if (utc is null) return "chwili";
        var m = (int)(DateTime.UtcNow - utc.Value).TotalMinutes;
        return m < 2 ? "chwili" : m < 60 ? $"{m} min" : m < 48 * 60 ? $"{m / 60} godz." : $"{m / 1440} dni";
    }
}
