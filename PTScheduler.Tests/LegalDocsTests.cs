using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services;
using Xunit;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Tests;

/// <summary>Dokumenty prawne: dane usługodawcy z Księgowości, działalność nierejestrowana bez NIP-u, rachunki zamiast faktur.</summary>
public class LegalDocsTests
{
    [Fact]
    public async Task Unregistered_activity_needs_no_nip_and_promises_receipts()
    {
        var (legal, settings) = Build();
        await settings.SetManyAsync(new()
        {
            [K.FinanceMode] = FinanceModes.Unregistered,
            [K.FinanceSellerName] = "Patryk Grabowski",
            [K.FinanceSellerAddress] = "ul. Przykładowa 12/4\n00-001 Warszawa",
            [K.FinanceSellerEmail] = "kontakt@hantly.pl",
            [K.LegalHosting] = "OVH", [K.LegalMail] = "Brevo",
        });

        var c = await legal.CompanyAsync();
        c.FromAccounting.Should().BeTrue();
        c.Address.Should().Be("ul. Przykładowa 12/4, 00-001 Warszawa");
        c.Complete.Should().BeTrue("przy działalności nierejestrowanej NIP nie jest potrzebny");

        var html = await legal.RenderAsync("regulamin");
        html.Should().Contain("Patryk Grabowski").And.Contain("działalność nierejestrowaną (art. 5 ust. 1");
        html.Should().Contain("wystawia rachunek").And.Contain("ceny końcowe");
        html.Should().NotContain("lg-missing").And.NotContain("Krajowym Systemie e-Faktur").And.NotContain("{{");
        (await legal.RenderAsync("prywatnosc")).Should().NotContain("lg-missing").And.NotContain("NIP {");
        (await legal.RenderAsync("powierzenie")).Should().NotContain("lg-missing");
    }

    [Fact]
    public async Task Business_shows_nip_and_invoices_from_accounting()
    {
        var (legal, settings) = Build();
        await settings.SetManyAsync(new()
        {
            [K.FinanceMode] = FinanceModes.Business,
            [K.FinanceBusinessSince] = AccountingService.Today.AddDays(-1).ToString("yyyy-MM-dd"),
            [K.FinanceSellerName] = "Hantly Patryk Grabowski",
            [K.FinanceSellerAddress] = "ul. Nowa 1\n00-001 Warszawa",
            [K.FinanceSellerTaxId] = "5260250995",
            [K.FinanceVat] = "zw",
            [K.LegalEmail] = "umowy@hantly.pl", [K.LegalHosting] = "OVH", [K.LegalMail] = "Brevo",
            [K.LegalCompany] = "Stara nazwa", [K.LegalNip] = "1111111111",
        });

        var c = await legal.CompanyAsync();
        c.Name.Should().Be("Hantly Patryk Grabowski", "dane z Księgowości mają pierwszeństwo przed starymi polami");
        c.Email.Should().Be("umowy@hantly.pl");
        var html = await legal.RenderAsync("regulamin");
        html.Should().Contain("NIP 5260250995").And.Contain("faktury elektroniczne").And.Contain("ceny końcowe");
        html.Should().NotContain("wystawia rachunek").And.NotContain("Stara nazwa");
    }

    [Fact]
    public async Task Business_before_ceidg_date_is_still_unregistered()
    {
        var (legal, settings) = Build();
        await settings.SetManyAsync(new()
        {
            [K.FinanceMode] = FinanceModes.Business,
            [K.FinanceBusinessSince] = AccountingService.Today.AddDays(10).ToString("yyyy-MM-dd"),
            [K.FinanceSellerName] = "Patryk", [K.FinanceSellerAddress] = "Warszawa", [K.FinanceSellerTaxId] = "5260250995",
        });
        var c = await legal.CompanyAsync();
        c.Unregistered.Should().BeTrue();
        c.Nip.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_accounting_old_fields_are_used_and_missing_nip_is_marked()
    {
        var (legal, settings) = Build();
        await settings.SetManyAsync(new() { [K.LegalCompany] = "Firma", [K.LegalAddress] = "Adres", [K.LegalEmail] = "a@b.pl" });
        var c = await legal.CompanyAsync();
        c.FromAccounting.Should().BeFalse();
        c.Complete.Should().BeFalse();
        var html = await legal.RenderAsync("regulamin");
        html.Should().Contain("lg-missing").And.Contain("ustaw w Księgowości");
        html.Should().Contain("W cenniku wskazano, czy cena obejmuje podatek VAT");
    }

    [Fact]
    public void Blocks_follow_vat_status()
    {
        var vat = new LegalService.Company("H", "F", "A", "5260250995", "e", "h", "m", FinanceModes.Business, true, VatPayer: true);
        LegalService.ApplyBlocks("{{?BEZVAT}}bez{{/BEZVAT}}{{?VAT}}z VAT{{/VAT}}{{?FIRMA}}F{{/FIRMA}}{{?NIEREJ}}N{{/NIEREJ}}", vat).Should().Be("z VATF");
        var unreg = vat with { Mode = FinanceModes.Unregistered, Nip = "", VatPayer = false };
        LegalService.ApplyBlocks("{{?BEZVAT}}bez{{/BEZVAT}}{{?VAT}}z VAT{{/VAT}}{{?FIRMA}}F{{/FIRMA}}{{?NIEREJ}}N{{/NIEREJ}}", unreg).Should().Be("bezN");
    }

    private static (LegalService Legal, SiteSettingsService Settings) Build()
    {
        var services = new ServiceCollection();
        var name = Guid.NewGuid().ToString();
        services.AddDbContextFactory<PortalDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<SiteSettingsService>();
        services.AddScoped<LegalService>();
        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<LegalService>(), sp.GetRequiredService<SiteSettingsService>());
    }
}
