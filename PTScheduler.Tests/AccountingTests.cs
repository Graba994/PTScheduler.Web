using System.IO.Compression;
using System.Net;
using System.Text;
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

/// <summary>Księgowość: limit działalności nierejestrowanej, ewidencja z wpłat, numeracja, korekty, przejście na firmę i Fakturownia.</summary>
public class AccountingTests
{
    private static readonly int Year = AccountingService.Today.Year;

    // ── Reguły ──

    [Theory]
    [InlineData(2026, 1, 10813.50)]
    [InlineData(2025, 3, 10498.50)]
    [InlineData(2024, 1, 9544.50)]
    [InlineData(2024, 3, 9675.00)]
    public void Quarter_limit_is_225_percent_of_minimum_wage(int year, int quarter, decimal expected)
    {
        var (limit, estimated) = FinanceRules.QuarterLimit(year, quarter);
        limit.Should().Be(expected);
        estimated.Should().BeFalse();
    }

    [Fact]
    public void Unknown_year_is_estimated_and_custom_limit_wins()
    {
        FinanceRules.QuarterLimit(2031, 1).Estimated.Should().BeTrue();
        FinanceRules.QuarterLimit(2031, 1, 12000m).Should().Be((12000m, false));
    }

    [Fact]
    public void Exceeded_day_and_ceidg_deadline()
    {
        var d = new DateOnly(2026, 2, 1);
        var entries = new[] { (d, 6000m), (d.AddDays(3), 4000m), (d.AddDays(10), 900m), (d.AddDays(12), 10m) };
        FinanceRules.ExceededOn(entries, 10813.50m).Should().Be(d.AddDays(10));
        FinanceRules.ExceededOn(entries.Take(2), 10813.50m).Should().BeNull();
        FinanceRules.CeidgDeadline(d.AddDays(10)).Should().Be(new DateOnly(2026, 2, 18));
    }

    [Fact]
    public void Forecast_extrapolates_daily_pace()
    {
        var qs = new DateOnly(2026, 1, 1);
        var qe = new DateOnly(2026, 3, 31); // 90 dni
        FinanceRules.Forecast(3000m, qs, qe, new DateOnly(2026, 1, 30)).Should().Be(9000m);
        FinanceRules.Forecast(3000m, qs, qe, qe).Should().Be(3000m);
    }

    [Fact]
    public void Vat_limit_is_proportional_in_first_year()
    {
        FinanceRules.VatLimitFor(2026, 240_000m, new DateOnly(2025, 5, 1)).Should().Be(240_000m);
        FinanceRules.VatLimitFor(2026, 240_000m, new DateOnly(2026, 7, 1)).Should().Be(Math.Round(240_000m * 184 / 365, 2));
    }

    [Theory]
    [InlineData("5260250995", true)]
    [InlineData("526-025-09-95", true)]
    [InlineData("PL5260250995", true)]
    [InlineData("5260250994", false)]
    [InlineData("123", false)]
    [InlineData("", true)]
    public void Nip_checksum(string nip, bool ok) => FinanceRules.IsValidNip(nip).Should().Be(ok);

    [Fact]
    public void Vat_split_and_words()
    {
        FinanceRules.Split(123m, "23").Should().Be((100m, 23m));
        FinanceRules.Split(99m, "zw").Should().Be((99m, 0m));
        FinanceRules.InWords(1234.56m).Should().Be("tysiąc dwieście trzydzieści cztery złote 56/100");
        FinanceRules.InWords(99m).Should().Be("dziewięćdziesiąt dziewięć złotych 00/100");
        FinanceRules.InWords(1m).Should().Be("jeden złoty 00/100");
        FinanceRules.InWords(22000m).Should().Be("dwadzieścia dwa tysiące złotych 00/100");
    }

    [Fact]
    public void Kind_depends_on_mode_vat_and_buyer()
    {
        FinanceRules.KindFor(FinanceModes.Unregistered, "zw", null).Should().Be(SalesDocumentKinds.Receipt);
        FinanceRules.KindFor(FinanceModes.Unregistered, "zw", "5260250995").Should().Be(SalesDocumentKinds.InvoiceExempt);
        FinanceRules.KindFor(FinanceModes.Business, "vat", null).Should().Be(SalesDocumentKinds.InvoiceVat);
        FinanceRules.ModeOn(new DateOnly(2026, 5, 1), FinanceModes.Business, new DateOnly(2026, 6, 1)).Should().Be(FinanceModes.Unregistered);
        FinanceRules.ModeOn(new DateOnly(2026, 6, 1), FinanceModes.Business, new DateOnly(2026, 6, 1)).Should().Be(FinanceModes.Business);
    }

    [Fact]
    public void Ledger_days_run_cumulative_per_month()
    {
        var entries = new[]
        {
            new SalesLedgerEntry { Date = new DateOnly(2026, 1, 30), Amount = 100 },
            new SalesLedgerEntry { Date = new DateOnly(2026, 1, 30), Amount = 50 },
            new SalesLedgerEntry { Date = new DateOnly(2026, 1, 31), Amount = 25 },
            new SalesLedgerEntry { Date = new DateOnly(2026, 2, 1), Amount = 10 },
        };
        var days = AccountingService.BuildDays(entries);
        days.Select(d => (d.No, d.Amount, d.MonthToDate)).Should().Equal((1, 150m, 150m), (2, 25m, 175m), (1, 10m, 10m));
    }

    [Fact]
    public void Fakturownia_payload_and_address()
    {
        FakturowniaClient.NormalizeDomain("https://Moja-Firma.fakturownia.pl/invoices").Should().Be("moja-firma");
        FakturowniaClient.NormalizeDomain("zła domena").Should().BeNull();
        FakturowniaClient.SplitAddress("ul. Długa 5/2\n00-950 Warszawa").Should().Be(("ul. Długa 5/2", "00-950", "Warszawa"));

        var bill = new SalesDocument
        {
            Kind = SalesDocumentKinds.Receipt, BuyerName = "Anna Nowak", IssueDate = new DateOnly(2026, 10, 1), SaleDate = new DateOnly(2026, 10, 1),
            PaidDate = new DateOnly(2026, 10, 1), PaymentMethod = "karta", VatNote = "art. 113",
            Lines = [new SalesDocumentLine { Name = "Abonament", Quantity = 1, Gross = 99, VatRate = "zw" }]
        };
        var p = FakturowniaClient.Payload(bill, null);
        p["kind"].Should().Be("bill");
        p["status"].Should().Be("paid");
        p["payment_type"].Should().Be("card");
        var pos = ((Dictionary<string, object?>[])p["positions"]!)[0];
        pos.Should().NotContainKey("tax");
        pos["total_price_gross"].Should().Be("99.00");

        var original = new SalesDocument { Kind = SalesDocumentKinds.InvoiceExempt, ExternalId = "77", Number = "FV 1", Gross = 99, Lines = bill.Lines };
        var corr = new SalesDocument { Kind = SalesDocumentKinds.Correction, Gross = -20, CorrectionReason = "zwrot", BuyerName = "Anna", IssueDate = bill.IssueDate, SaleDate = bill.SaleDate };
        var c = FakturowniaClient.Payload(corr, original);
        c["kind"].Should().Be("correction");
        c["invoice_id"].Should().Be("77");
        var cpos = ((Dictionary<string, object?>[])c["positions"]!)[0];
        cpos["total_price_gross"].Should().Be("-20.00");
        ((Dictionary<string, object?>)cpos["correction_after_attributes"]!)["total_price_gross"].Should().Be("79.00");
    }

    // ── Serwis ──

    [Fact]
    public async Task Sync_puts_payments_into_ledger_once_and_numbers_documents()
    {
        var (acc, db, tenant, _) = await BuildAsync();
        await AddPaymentAsync(db, tenant.Id, 99m, "Rachunek R/1");
        await AddPaymentAsync(db, tenant.Id, 49m, "Zamówienie usług (1 poz.)");
        await AddPaymentAsync(db, tenant.Id, 10m, "stara wpłata", DateTime.UtcNow.AddYears(-2));

        var first = await acc.SyncAsync();
        first.Ledger.Should().Be(2);
        first.Documents.Should().Be(2);
        (await acc.SyncAsync()).Ledger.Should().Be(0);

        await using var ctx = await db.CreateDbContextAsync();
        var docs = await ctx.SalesDocuments.Include(d => d.Lines).OrderBy(d => d.Id).ToListAsync();
        docs.Select(d => d.Number).Should().Equal($"RACH/{Year}/0001", $"RACH/{Year}/0002");
        docs[0].Kind.Should().Be(SalesDocumentKinds.Receipt);
        docs[0].Seller.Should().StartWith("Patryk Testowy").And.NotContain("NIP");
        docs[0].VatNote.Should().Contain("art. 113");
        docs[0].BuyerName.Should().Be("Jan Trener");
        (await ctx.SalesLedger.CountAsync()).Should().Be(2);
        (await ctx.SalesLedger.AllAsync(l => l.DocumentId != null)).Should().BeTrue();
        (await ctx.FinanceEvents.CountAsync(e => e.Action == "doc-issue")).Should().Be(2);
    }

    [Fact]
    public async Task Bill_lines_are_copied_and_buyer_with_nip_gets_invoice()
    {
        var (acc, db, tenant, _) = await BuildAsync();
        (await acc.SaveBuyerAsync(tenant.Id, "Siłownia Jan sp. z o.o.", "ul. Długa 5\n00-950 Warszawa", "526-025-09-95", null, "test")).Should().BeNull();
        await using (var ctx = await db.CreateDbContextAsync())
        {
            ctx.TenantBills.Add(new TenantBill
            {
                TenantId = tenant.Id, Number = "R/2026/10/0001", Amount = 129, Status = TenantBillStatus.Paid, PayToken = "x",
                Lines = [new TenantBillLine { Name = "Plan Pro", Amount = 99, UnitPrice = 99 }, new TenantBillLine { Name = "SMS 300", Amount = 30, UnitPrice = 30 }]
            });
            await ctx.SaveChangesAsync();
        }
        await AddPaymentAsync(db, tenant.Id, 129m, "Rachunek R/2026/10/0001");
        await acc.SyncAsync();

        var doc = (await acc.DocumentsAsync(new AccountingService.DocumentFilter())).Single();
        doc.Kind.Should().Be(SalesDocumentKinds.InvoiceExempt);
        doc.Number.Should().Be($"FV/{Year}/0001");
        doc.BuyerTaxId.Should().Be("5260250995");
        var full = await acc.GetDocumentAsync(doc.Id);
        full!.Lines.Select(l => l.Name).Should().Equal("Plan Pro", "SMS 300");
        full.BillId.Should().NotBeNull();
        (await acc.PdfAsync(doc.Id))!.Value.Pdf.Take(4).Should().Equal("%PDF"u8.ToArray());
    }

    [Fact]
    public async Task Correction_goes_negative_and_cancel_needs_document_out_of_circulation()
    {
        var (acc, db, tenant, _) = await BuildAsync();
        await AddPaymentAsync(db, tenant.Id, 100m, "Rachunek R/1");
        await acc.SyncAsync();
        var doc = (await acc.DocumentsAsync(new AccountingService.DocumentFilter())).Single();

        (await acc.CorrectAsync(doc.Id, 150m, "zwrot", "admin")).Error.Should().Contain("100,00");
        var (corr, error) = await acc.CorrectAsync(doc.Id, 40m, "zwrot za tydzień", "admin");
        error.Should().BeNull();
        corr!.Number.Should().Be($"KOR/{Year}/0001");
        corr.Gross.Should().Be(-40m);
        (await acc.CorrectAsync(doc.Id, 61m, "jeszcze", "admin")).Error.Should().Contain("60,00");

        await using (var ctx = await db.CreateDbContextAsync())
            (await ctx.SalesLedger.SumAsync(l => l.Amount)).Should().Be(60m);

        (await acc.CancelAsync(doc.Id, "zły nabywca", "admin")).Should().Contain("korekta");
    }

    [Fact]
    public async Task Cancelled_document_frees_payment_for_a_new_one()
    {
        var (acc, db, tenant, _) = await BuildAsync();
        await AddPaymentAsync(db, tenant.Id, 100m, "Rachunek R/1");
        await acc.SyncAsync();
        var doc = (await acc.DocumentsAsync(new AccountingService.DocumentFilter())).Single();

        (await acc.CancelAsync(doc.Id, "", "admin")).Should().NotBeNull();
        (await acc.CancelAsync(doc.Id, "zły nabywca", "admin")).Should().BeNull();
        (await acc.PaymentsWithoutDocumentAsync()).Should().HaveCount(1);

        await acc.SyncAsync();
        var docs = await acc.DocumentsAsync(new AccountingService.DocumentFilter());
        docs.Should().HaveCount(2);
        docs.Single(d => d.Status == SalesDocumentStatus.Issued).Number.Should().Be($"RACH/{Year}/0002");
    }

    [Fact]
    public async Task Ledger_entries_are_reversed_not_deleted()
    {
        var (acc, db, _, _) = await BuildAsync();
        (await acc.AddManualEntryAsync(AccountingService.Today, 300m, "Wdrożenie dla studia X", "admin")).Should().BeNull();
        (await acc.AddManualEntryAsync(AccountingService.Today.AddDays(5), 10m, "przyszłość", "admin")).Should().NotBeNull();
        int id;
        await using (var ctx = await db.CreateDbContextAsync()) id = (await ctx.SalesLedger.SingleAsync()).Id;
        (await acc.ReverseEntryAsync(id, "pomyłka", "admin")).Should().BeNull();
        (await acc.ReverseEntryAsync(id, "pomyłka", "admin")).Should().NotBeNull();
        await using (var ctx = await db.CreateDbContextAsync())
        {
            (await ctx.SalesLedger.CountAsync()).Should().Be(2);
            (await ctx.SalesLedger.SumAsync(l => l.Amount)).Should().Be(0m);
        }
    }

    [Fact]
    public async Task Overview_tracks_quarter_limit_and_alerts()
    {
        var (acc, db, tenant, _) = await BuildAsync();
        await AddPaymentAsync(db, tenant.Id, 9000m, "Duża wpłata");
        await acc.SyncAsync();
        var o = await acc.OverviewAsync();
        o.ModeToday.Should().Be(FinanceModes.Unregistered);
        o.Quarter.Should().NotBeNull();
        o.Quarter!.Received.Should().Be(9000m);
        o.Quarter.Tone.Should().Be("warn");
        o.Alerts.Should().Contain(a => a.Title.Contains("Limit kwartału"));

        await AddPaymentAsync(db, tenant.Id, 2000m, "Druga wpłata");
        await acc.SyncAsync();
        o = await acc.OverviewAsync();
        o.Quarter!.ExceededOn.Should().Be(AccountingService.Today);
        o.Quarter.Deadline.Should().Be(AccountingService.Today.AddDays(7));
        o.Alerts.Should().Contain(a => a.Tone == "danger" && a.Text.Contains("CEIDG"));
    }

    [Fact]
    public async Task Switching_to_business_keeps_history_and_adds_nip()
    {
        var (acc, db, tenant, _) = await BuildAsync();
        await AddPaymentAsync(db, tenant.Id, 50m, "Rachunek R/1");
        await acc.SyncAsync();

        (await acc.SwitchToBusinessAsync(AccountingService.Today, "Hantly Patryk Testowy", "ul. Nowa 1\n00-001 Warszawa", "123", "", "", "zw", "admin"))
            .Should().Contain("NIP");
        (await acc.SwitchToBusinessAsync(AccountingService.Today, "Hantly Patryk Testowy", "ul. Nowa 1\n00-001 Warszawa", "5260250995", "", "", "zw", "admin"))
            .Should().BeNull();

        await AddPaymentAsync(db, tenant.Id, 60m, "Rachunek R/2");
        await acc.SyncAsync();
        var docs = (await acc.DocumentsAsync(new AccountingService.DocumentFilter())).OrderBy(d => d.Id).ToList();
        docs[0].Mode.Should().Be(FinanceModes.Unregistered);
        docs[0].Seller.Should().NotContain("NIP");
        docs[1].Mode.Should().Be(FinanceModes.Business);
        docs[1].Seller.Should().Contain("NIP: 5260250995");
        docs[1].Number.Should().Be($"RACH/{Year}/0002");
        (await acc.OverviewAsync()).Quarter.Should().BeNull("limit działalności nierejestrowanej nie dotyczy już firmy");
        (await acc.CorrectAsync(docs[0].Id, 20m, "zwrot", "admin")).Error.Should().BeNull();
        (await acc.OverviewAsync()).Vat!.Revenue.Should().Be(60m, "korekta sprzedaży sprzed założenia firmy nie zmienia limitu VAT firmy");
        (await acc.HistoryAsync()).Should().Contain(e => e.Action == "switch-business");
    }

    [Fact]
    public async Task Fakturownia_number_is_used_and_failures_wait_for_retry()
    {
        var handler = new FakeHandler();
        var (acc, db, tenant, sp) = await BuildAsync(handler);
        var settings = sp.GetRequiredService<SiteSettingsService>();
        await settings.SetAsync(K.FinanceProvider, "fakturownia");
        await settings.SetAsync(K.FinanceFakturowniaDomain, "hantly");
        await sp.GetRequiredService<FakturowniaClient>().SetTokenAsync("token-testowy");
        await settings.SetAsync(K.FinanceFakturowniaApprove, "false");

        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.Created)
        { Content = new StringContent("""{"id":555,"number":"FV 3/10/2026","view_url":"https://hantly.fakturownia.pl/invoice/555"}""", Encoding.UTF8, "application/json") };
        await AddPaymentAsync(db, tenant.Id, 99m, "Rachunek R/1");
        await acc.SyncAsync();
        var ok = (await acc.DocumentsAsync(new AccountingService.DocumentFilter())).Single();
        ok.Number.Should().Be("FV 3/10/2026");
        ok.ExternalId.Should().Be("555");
        ok.Status.Should().Be(SalesDocumentStatus.Issued);
        handler.LastBody.Should().Contain("\"kind\":\"bill\"").And.Contain("token-testowy");

        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("awaria") };
        await AddPaymentAsync(db, tenant.Id, 49m, "Rachunek R/2");
        await acc.SyncAsync();
        var pending = (await acc.DocumentsAsync(new AccountingService.DocumentFilter())).Single(d => d.Status == SalesDocumentStatus.Pending);
        pending.Number.Should().BeEmpty();
        pending.ExternalError.Should().Contain("500");
        (await acc.OverviewAsync()).PendingDocs.Should().Be(1);

        (await acc.IssueLocallyAsync(pending.Id, "admin")).Should().BeNull();
        var local = await acc.GetDocumentAsync(pending.Id);
        local!.Number.Should().Be($"RACH/{Year}/0001");
        local.Provider.Should().Be("local");
    }

    private static async Task<(AccountingService Acc, IDbContextFactory<PortalDbContext> Db, Tenant Tenant, FakeHandler Handler)> BuildFakturowniaAsync()
    {
        var handler = new FakeHandler();
        var (acc, db, tenant, sp) = await BuildAsync(handler);
        var settings = sp.GetRequiredService<SiteSettingsService>();
        await settings.SetAsync(K.FinanceProvider, "fakturownia");
        await settings.SetAsync(K.FinanceFakturowniaDomain, "hantly");
        await sp.GetRequiredService<FakturowniaClient>().SetTokenAsync("token-testowy");
        var id = 700;
        handler.Respond = r => r.Method == HttpMethod.Post
            ? new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent($$"""{"id":{{++id}},"number":"FV {{id}}/2026"}""", Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        return (acc, db, tenant, handler);
    }

    [Fact]
    public async Task Fakturownia_waits_for_approval_by_default()
    {
        var (acc, db, tenant, handler) = await BuildFakturowniaAsync();
        await AddPaymentAsync(db, tenant.Id, 99m, "Rachunek R/1");
        await acc.SyncAsync();
        await acc.SyncAsync();

        handler.Calls.Should().BeEmpty("nic nie idzie do Fakturowni bez zatwierdzenia");
        var doc = (await acc.DocumentsAsync(new AccountingService.DocumentFilter())).Single();
        doc.AwaitingApproval.Should().BeTrue();
        doc.Status.Should().Be(SalesDocumentStatus.Pending);
        (await acc.OverviewAsync()).AwaitingApproval.Should().Be(1);

        (await acc.ApproveAllAsync("admin")).Should().Be((1, (string?)null));
        var sent = await acc.GetDocumentAsync(doc.Id);
        sent!.Number.Should().Be("FV 701/2026");
        sent.AwaitingApproval.Should().BeFalse();
        handler.Calls.Should().Equal("POST /invoices.json");
    }

    [Fact]
    public async Task Deleting_test_document_removes_it_in_fakturownia_and_excludes_the_sale()
    {
        var (acc, db, tenant, handler) = await BuildFakturowniaAsync();
        await AddPaymentAsync(db, tenant.Id, 99m, "Rachunek R/1");
        await acc.SyncAsync();
        await acc.ApproveAllAsync("admin");
        var doc = (await acc.DocumentsAsync(new AccountingService.DocumentFilter())).Single();
        (await acc.CorrectAsync(doc.Id, 10m, "test", "admin")).Error.Should().BeNull();
        await acc.ApproveAllAsync("admin");

        var r = await acc.DeleteDocumentsAsync([doc.Id], alsoInFakturownia: true, AccountingService.LedgerExclude, "admin");
        r.Error.Should().BeNull();
        r.Deleted.Should().Be(2, "korekta znika razem z dokumentem");
        r.DeletedInFakturownia.Should().Be(2);
        handler.Calls.Where(c => c.StartsWith("DELETE")).Should().HaveCount(2);

        (await acc.DocumentsAsync(new AccountingService.DocumentFilter())).Should().BeEmpty();
        await acc.SyncAsync();
        (await acc.DocumentsAsync(new AccountingService.DocumentFilter())).Should().BeEmpty("usunięty test nie wraca przy synchronizacji");
        (await acc.OverviewAsync()).YearRevenue.Should().Be(0m);
        (await acc.ExcludedEntriesAsync()).Should().HaveCount(2);
        (await acc.HistoryAsync()).Count(e => e.Action == "doc-delete").Should().Be(2);

        var payment = (await acc.ExcludedEntriesAsync()).Single(e => e.Source == "payment");
        (await acc.RestoreEntryAsync(payment.Id, "admin")).Should().BeNull();
        (await acc.OverviewAsync()).YearRevenue.Should().Be(99m);
    }

    [Fact]
    public async Task Deleting_keeps_real_sale_and_stops_when_fakturownia_refuses()
    {
        var (acc, db, tenant, handler) = await BuildFakturowniaAsync();
        await AddPaymentAsync(db, tenant.Id, 50m, "Rachunek R/1");
        await acc.SyncAsync();
        await acc.ApproveAllAsync("admin");
        var doc = (await acc.DocumentsAsync(new AccountingService.DocumentFilter())).Single();

        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity) { Content = new StringContent("wysłana do KSeF") };
        var refused = await acc.DeleteDocumentsAsync([doc.Id], true, AccountingService.LedgerKeep, "admin");
        refused.Error.Should().Contain("KSeF").And.Contain("nic nie zostało usunięte");
        (await acc.DocumentsAsync(new AccountingService.DocumentFilter())).Should().HaveCount(1);

        (await acc.DeleteDocumentsAsync([doc.Id], false, AccountingService.LedgerKeep, "admin")).Error.Should().BeNull();
        await acc.SyncAsync();
        (await acc.DocumentsAsync(new AccountingService.DocumentFilter())).Should().BeEmpty("wpłata bez dokumentu nie dostaje go sama ponownie");
        (await acc.OverviewAsync()).YearRevenue.Should().Be(50m);
        (await acc.PaymentsWithoutDocumentAsync()).Should().HaveCount(1);
    }

    [Fact]
    public async Task Import_from_fakturownia_links_ours_and_adds_new_ones()
    {
        var (acc, db, tenant, handler) = await BuildFakturowniaAsync();
        (await acc.SaveBuyerAsync(tenant.Id, "Siłownia Jan", "ul. Długa 5\n00-950 Warszawa", "5260250995", null, "test")).Should().BeNull();
        await AddPaymentAsync(db, tenant.Id, 99m, "Rachunek R/1");
        await acc.SyncAsync(); // czeka na zatwierdzenie
        var ours = (await acc.DocumentsAsync(new AccountingService.DocumentFilter())).Single();
        var today = AccountingService.Today.ToString("yyyy-MM-dd");
        var list = $$"""
            [
              {"id":901,"number":"FV 9/2026","kind":"vat","issue_date":"{{today}}","sell_date":"{{today}}","status":"paid","paid_date":"{{today}}",
               "buyer_name":"Siłownia Jan","buyer_tax_no":"526-025-09-95","buyer_street":"ul. Długa 5","buyer_post_code":"00-950","buyer_city":"Warszawa",
               "seller_name":"Patryk","price_net":"300.00","price_gross":"300.00","currency":"PLN","exempt_tax_kind":"art. 113",
               "positions":[{"name":"Konsultacja","quantity":"2","total_price_gross":"300.00","total_price_net":"300.00","tax":"zw"}]},
              {"id":902,"number":"FV 10/2026","kind":"vat","issue_date":"{{today}}","price_gross":"99.00","oid":"hantly-{{ours.Id}}","buyer_name":"Siłownia Jan","positions":[]},
              {"id":903,"number":"PRO 1/2026","kind":"proforma","issue_date":"{{today}}","price_gross":"10.00","buyer_name":"X"}
            ]
            """;
        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(list, Encoding.UTF8, "application/json") };

        var (preview, error) = await acc.PreviewImportAsync(AccountingService.Today.AddDays(-30), AccountingService.Today);
        error.Should().BeNull();
        preview!.Select(c => (c.Doc.Id, c.State)).Should().BeEquivalentTo(new[] { ("901", "new"), ("902", "link") }, "proformy pomijamy");
        preview.Single(c => c.Doc.Id == "901").TenantId.Should().Be(tenant.Id);

        (await acc.ImportAsync(preview, addToLedger: false, "admin")).Should().Be(2);
        var docs = await acc.DocumentsAsync(new AccountingService.DocumentFilter());
        docs.Should().HaveCount(2, "nasz dokument został powiązany, nie zdublowany");
        var linked = docs.Single(d => d.Id == ours.Id);
        linked.Number.Should().Be("FV 10/2026");
        linked.Status.Should().Be(SalesDocumentStatus.Issued);
        var imported = docs.Single(d => d.ExternalId == "901");
        imported.Kind.Should().Be(SalesDocumentKinds.InvoiceExempt);
        imported.BuyerAddress.Should().Be("ul. Długa 5\n00-950 Warszawa");
        (await acc.GetDocumentAsync(imported.Id))!.Lines.Single().UnitGross.Should().Be(150m);

        (await acc.DocumentsInLedgerAsync([imported.Id, linked.Id])).Should().BeEquivalentTo([linked.Id]);
        (await acc.OverviewAsync()).YearRevenue.Should().Be(99m, "pobrane dokumenty nie wchodzą do ewidencji bez zgody");
        (await acc.AddDocumentToLedgerAsync(imported.Id, "admin")).Should().BeNull();
        (await acc.AddDocumentToLedgerAsync(imported.Id, "admin")).Should().NotBeNull();
        (await acc.OverviewAsync()).YearRevenue.Should().Be(399m);

        (await acc.PreviewImportAsync(AccountingService.Today.AddDays(-30), AccountingService.Today)).List!.Should().OnlyContain(c => c.State == "exists");
    }

    [Fact]
    public async Task Manual_document_validates_kind_and_lines()
    {
        var (acc, _, _, _) = await BuildAsync();
        var input = new AccountingService.ManualDocument
        {
            Kind = SalesDocumentKinds.InvoiceVat, BuyerName = "Klient",
            Lines = [new AccountingService.ManualLine { Name = "Wdrożenie", Quantity = 2, UnitGross = 150 }]
        };
        (await acc.IssueManualAsync(input, "admin")).Error.Should().Contain("nie pasuje");
        input.Kind = SalesDocumentKinds.Receipt;
        input.Lines[0].UnitGross = 0;
        (await acc.IssueManualAsync(input, "admin")).Error.Should().Contain("ceny");
        input.Lines[0].UnitGross = 150;
        var (doc, error) = await acc.IssueManualAsync(input, "admin");
        error.Should().BeNull();
        doc!.Gross.Should().Be(300m);
        (await acc.LedgerDaysAsync(AccountingService.Today, AccountingService.Today)).Single().Amount.Should().Be(300m);
    }

    [Fact]
    public async Task Package_has_ledger_documents_costs_and_summary()
    {
        var (acc, db, tenant, _) = await BuildAsync();
        await AddPaymentAsync(db, tenant.Id, 99m, "Rachunek R/1");
        await acc.SyncAsync();
        (await acc.AddExpenseAsync(new Expense { Date = AccountingService.Today, Amount = 40, Vendor = "Hetzner", Description = "serwer", Category = "serwer",
            Attachment = [1, 2, 3], AttachmentName = "faktura.pdf" }, "admin")).Should().BeNull();
        (await acc.AddExpenseAsync(new Expense { Date = AccountingService.Today, Amount = 0, Vendor = "x", Description = "y" }, "admin")).Should().NotBeNull();

        var zip = await acc.PackageAsync(AccountingService.Today.AddDays(-1), AccountingService.Today, "admin");
        using var archive = new ZipArchive(new MemoryStream(zip));
        var names = archive.Entries.Select(e => e.FullName).ToList();
        names.Should().Contain(["ewidencja-sprzedazy.csv", "ewidencja-sprzedazy.pdf", "dokumenty.csv", "koszty.csv", "PRZECZYTAJ.txt"]);
        names.Should().Contain(n => n.StartsWith("dokumenty/rachunek-RACH-"));
        names.Should().Contain(n => n.StartsWith("koszty/") && n.EndsWith("faktura.pdf"));
        using var reader = new StreamReader(archive.GetEntry("PRZECZYTAJ.txt")!.Open());
        (await reader.ReadToEndAsync()).Should().Contain("99,00 zł").And.Contain("40,00 zł");
        (await acc.OverviewAsync()).LastExport.Should().NotBeNull();
    }

    [Fact]
    public void Buyer_validation()
    {
        AccountingService.ValidateBuyer("Firma", "Adres", "5260250995", "a@b.pl").Should().BeNull();
        AccountingService.ValidateBuyer(null, null, "5260250995", null).Should().Be(AccountingService.BuyerErrors[1]);
        AccountingService.ValidateBuyer("Firma", "Adres", "1234567890", null).Should().Be(AccountingService.BuyerErrors[0]);
        AccountingService.ValidateBuyer(null, null, null, "zly").Should().Be(AccountingService.BuyerErrors[2]);
    }

    [Fact]
    public async Task Nothing_happens_until_accounting_is_configured()
    {
        var (acc, db, tenant, sp) = await BuildAsync(configure: false);
        await AddPaymentAsync(db, tenant.Id, 99m, "Rachunek R/1");
        (await acc.SyncAsync()).Ledger.Should().Be(0);
        (await acc.OverviewAsync()).Alerts.Should().ContainSingle(a => a.Title == "Ustaw księgowość");
    }

    // ── Pomocnicze ──

    private static async Task AddPaymentAsync(IDbContextFactory<PortalDbContext> db, int tenantId, decimal amount, string description, DateTime? at = null)
    {
        await using var ctx = await db.CreateDbContextAsync();
        ctx.PaymentRecords.Add(new PaymentRecord
        {
            TenantId = tenantId, Amount = amount, Status = PaymentRecordStatus.Paid, Source = "payu", Description = description,
            CreatedAt = at ?? DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);
        public string? LastBody { get; private set; }
        public List<string> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Calls.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            return Respond(request);
        }
    }

    private static async Task<(AccountingService Acc, IDbContextFactory<PortalDbContext> Db, Tenant Tenant, IServiceProvider Sp)> BuildAsync(
        FakeHandler? handler = null, bool configure = true)
    {
        var services = new ServiceCollection();
        var name = Guid.NewGuid().ToString();
        services.AddDbContextFactory<PortalDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddLogging();
        services.AddSingleton<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        services.AddHttpClient();
        services.AddHttpClient(FakturowniaClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler ?? new FakeHandler());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<SiteSettingsService>();
        services.AddScoped<EmailService>();
        services.AddScoped<FakturowniaClient>();
        services.AddScoped<AccountingService>();
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<IDbContextFactory<PortalDbContext>>();
        var tenant = new Tenant { Slug = "studio", CompanyName = "Studio Siła", OwnerName = "Jan Trener", OwnerEmail = "jan@example.com" };
        await using (var ctx = await db.CreateDbContextAsync()) { ctx.Tenants.Add(tenant); await ctx.SaveChangesAsync(); }

        var acc = sp.GetRequiredService<AccountingService>();
        if (configure)
        {
            var s = await acc.GetSettingsAsync();
            (await acc.SaveSettingsAsync(s with
            {
                Mode = FinanceModes.Unregistered, UnregisteredSince = new DateOnly(Year, 1, 1), Start = new DateOnly(Year, 1, 1),
                SellerName = "Patryk Testowy", SellerAddress = "ul. Testowa 1\n00-001 Warszawa", EmailDocs = false
            }, "test")).Should().BeNull();
        }
        return (acc, db, tenant, sp);
    }
}
