using Microsoft.AspNetCore.Identity;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Scheduling;
using PTScheduler.Domain.Constants;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

/// <summary>
/// Dane do regulaminu, polityki prywatności i cookies, które studio pokazuje swoim klientom.
/// Jedno źródło: dane sprzedawcy z ustawień finansów (te same co na rachunkach i fakturach), a gdy ich brak —
/// imię i nazwisko właściciela. Do tego zasady odwołań trenera i włączone bramki płatności, żeby dokumenty
/// opisywały to, co w tej aplikacji naprawdę działa.
/// </summary>
public sealed class StudioLegalService(
    IBrandingService branding,
    IFinanceService finance,
    IEmailSettingsService emailSettings,
    IPaymentSettingsService paymentSettings,
    ITrainerConfigService trainerConfig,
    UserManager<ApplicationUser> users)
{
    public sealed record Info(
        string Brand, string Name, string Address, string? Nip, string? Email, bool NameFromAccount,
        string Cancellation, List<string> Gateways)
    {
        /// <summary>Dane wystarczą, żeby klient wiedział, z kim zawiera umowę i jak się skontaktować.</summary>
        public bool Complete => Name.Length > 0 && Address.Length > 0 && !string.IsNullOrWhiteSpace(Email);

        /// <summary>„Imię Nazwisko, ul. …, 00-000 Miasto, NIP …” — kto świadczy usługi i administruje danymi.</summary>
        public string Who
        {
            get
            {
                var parts = new List<string>();
                if (Name.Length > 0) parts.Add(Name);
                if (Address.Length > 0) parts.Add(Address);
                if (Nip is not null) parts.Add($"NIP {Nip}");
                return parts.Count > 0 ? string.Join(", ", parts) : $"właściciel aplikacji {Brand}";
            }
        }

        public List<string> Missing()
        {
            var list = new List<string>();
            if (Name.Length == 0) list.Add("imię i nazwisko albo nazwa firmy");
            if (Address.Length == 0) list.Add("adres");
            if (string.IsNullOrWhiteSpace(Email)) list.Add("e-mail kontaktowy");
            return list;
        }
    }

    public async Task<Info> GetAsync()
    {
        var brand = "Hantly";
        try { brand = (await branding.GetAsync()).CompanyName is { Length: > 0 } b ? b : brand; } catch { }

        FinanceTaxConfigDto? tax = null;
        try { tax = await finance.GetTaxConfigAsync("standard"); } catch { }

        var owner = await OwnerAsync();
        var ownerName = owner is null ? "" : $"{owner.FirstName} {owner.LastName}".Trim();
        var name = (tax?.SellerName ?? "").Trim();
        var fromAccount = name.Length == 0 && ownerName.Length > 0;
        if (name.Length == 0) name = ownerName;

        var cityLine = string.Join(" ", new[] { tax?.SellerPostalCode, tax?.SellerCity }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
        var address = string.Join(", ", new[] { tax?.SellerAddress?.Trim(), cityLine }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var nip = new string((tax?.SellerNip ?? "").Where(char.IsDigit).ToArray());

        string? email = null;
        try
        {
            var mail = await emailSettings.GetAsync();
            email = !string.IsNullOrWhiteSpace(mail?.ReplyTo) ? mail.ReplyTo : null;
        }
        catch { }
        email ??= owner?.Email;

        var cancellation = "";
        if (owner is not null)
        {
            try { cancellation = CancellationRules.Describe(await trainerConfig.GetAsync(owner.Id)); } catch { }
        }

        var gateways = new List<string>();
        try
        {
            var pay = await paymentSettings.GetAsync();
            if (pay.Enabled)
                gateways = pay.EnabledProviders.Where(p => p.Key != "sim")
                    .Select(p => PaymentProviderCatalog.All.FirstOrDefault(m => m.Key == p.Key)?.Name ?? p.Key).Distinct().ToList();
        }
        catch { }

        return new Info(brand, name, address, nip.Length == 10 ? nip : null, email, fromAccount, cancellation, gateways);
    }

    /// <summary>Właściciel studia: administrator (nie konto techniczne), najlepiej ten, który sam prowadzi treningi.</summary>
    private async Task<ApplicationUser?> OwnerAsync()
    {
        try
        {
            var admins = await users.GetUsersInRoleAsync(Roles.Admin);
            var candidates = new List<(ApplicationUser User, bool Trainer)>();
            foreach (var u in admins)
            {
                if (await users.IsInRoleAsync(u, Roles.Root)) continue;
                candidates.Add((u, await users.IsInRoleAsync(u, Roles.Trainer)));
            }
            return candidates.OrderByDescending(c => c.Trainer).Select(c => c.User).FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>Zapisane pola (bez podpowiedzi z konta) — do formularza.</summary>
    public async Task<(string? Name, string? Address, string? PostalCode, string? City, string? Nip)> RawAsync()
    {
        var tax = await finance.GetTaxConfigAsync("standard");
        return (tax.SellerName, tax.SellerAddress, tax.SellerPostalCode, tax.SellerCity, tax.SellerNip);
    }

    /// <summary>Zapis danych do dokumentów (te same pola co sprzedawca na rachunkach).</summary>
    public async Task<string?> SaveAsync(string? name, string? address, string? postalCode, string? city, string? nip)
    {
        var digits = new string((nip ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length is > 0 and not 10) return "NIP ma 10 cyfr — zostaw puste, jeśli nie masz firmy (np. działalność nierejestrowana).";
        if (!string.IsNullOrWhiteSpace(postalCode) && !System.Text.RegularExpressions.Regex.IsMatch(postalCode.Trim(), @"^\d{2}-\d{3}$"))
            return "Kod pocztowy w formacie 00-000.";
        var tax = await finance.GetTaxConfigAsync("standard");
        tax.SellerName = Clean(name, 200);
        tax.SellerAddress = Clean(address, 200);
        tax.SellerPostalCode = Clean(postalCode, 10);
        tax.SellerCity = Clean(city, 100);
        tax.SellerNip = digits.Length == 10 ? digits : null;
        await finance.SaveTaxConfigAsync(tax);
        return null;
    }

    private static string? Clean(string? v, int max) =>
        string.IsNullOrWhiteSpace(v) ? null : v.Trim().Length > max ? v.Trim()[..max] : v.Trim();
}
