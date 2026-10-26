using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Księgowość platformy: ewidencja sprzedaży z wpłat, dokumenty sprzedaży (Hantly albo Fakturownia),
/// korekty, koszty, limit działalności nierejestrowanej i paczka dla księgowej.
/// Zasada: wystawionego dokumentu ani wpisu w ewidencji się nie zmienia — prostuje się je korektą,
/// a każda czynność trafia do historii zmian.
/// </summary>
public class AccountingService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    FakturowniaClient fakturownia,
    EmailService email,
    ILogger<AccountingService> logger)
{
    public static readonly TimeZoneInfo Tz = FindWarsaw();
    public static DateOnly Today => LocalDate(DateTime.UtcNow);
    public static DateOnly LocalDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Tz));
    public static DateTime UtcStart(DateOnly d) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), Tz);

    public const int MaxProviderAttempts = 12;
    public static readonly string[] ExpenseCategories = ["serwer", "domeny", "oprogramowanie", "księgowość", "marketing", "sprzęt", "szkolenia", "telefon i internet", "inne"];

    // ── Ustawienia ──

    public sealed record Settings(
        string Mode, DateOnly? UnregisteredSince, DateOnly? BusinessSince, DateOnly? Start,
        string SellerName, string SellerAddress, string SellerEmail, string SellerTaxId, string SellerRegon, string SellerBank,
        string Vat, string VatNote, bool AutoIssue, bool EmailDocs, string Provider, string FakturowniaDomain, bool HasFakturowniaToken,
        decimal? QuarterLimit, decimal? VatLimit, decimal TaxReservePct, string PrefixReceipt, string PrefixInvoice, string PrefixCorrection)
    {
        public bool Configured => Mode is FinanceModes.Unregistered or FinanceModes.Business;
        public bool UsesFakturownia => Provider == "fakturownia";
        public string ModeOn(DateOnly d) => FinanceRules.ModeOn(d, Mode, BusinessSince);
        public string PrefixFor(string kind) => SalesDocumentKinds.SeriesOf(kind) switch
        {
            "faktura" => PrefixInvoice,
            "korekta" => PrefixCorrection,
            _ => PrefixReceipt
        };
        /// <summary>Brakujące dane sprzedawcy dla trybu (puste — wszystko jest).</summary>
        public List<string> MissingSellerData()
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(SellerName)) list.Add("imię i nazwisko / nazwa");
            if (string.IsNullOrWhiteSpace(SellerAddress)) list.Add("adres");
            if (Mode == FinanceModes.Business && string.IsNullOrWhiteSpace(SellerTaxId)) list.Add("NIP");
            return list;
        }
    }

    private static readonly string[] SettingKeys =
    [
        K.FinanceMode, K.FinanceUnregisteredSince, K.FinanceBusinessSince, K.FinanceStart, K.FinanceSellerName, K.FinanceSellerAddress,
        K.FinanceSellerEmail, K.FinanceSellerTaxId, K.FinanceSellerRegon, K.FinanceSellerBank, K.FinanceVat, K.FinanceVatNote,
        K.FinanceAutoIssue, K.FinanceEmailDocs, K.FinanceProvider, K.FinanceFakturowniaDomain, K.FinanceFakturowniaToken,
        K.FinanceQuarterLimit, K.FinanceVatLimit, K.FinanceTaxReservePct, K.FinancePrefixReceipt, K.FinancePrefixInvoice, K.FinancePrefixCorrection
    ];

    public async Task<Settings> GetSettingsAsync()
    {
        var s = await settings.GetAllAsync(SettingKeys);
        static DateOnly? Date(string v) => DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        return new Settings(
            s[K.FinanceMode], Date(s[K.FinanceUnregisteredSince]), Date(s[K.FinanceBusinessSince]), Date(s[K.FinanceStart]),
            s[K.FinanceSellerName], s[K.FinanceSellerAddress], s[K.FinanceSellerEmail], s[K.FinanceSellerTaxId], s[K.FinanceSellerRegon], s[K.FinanceSellerBank],
            s[K.FinanceVat] == "vat" ? "vat" : "zw", s[K.FinanceVatNote], s[K.FinanceAutoIssue] == "true", s[K.FinanceEmailDocs] == "true",
            s[K.FinanceProvider] == "fakturownia" ? "fakturownia" : "local", s[K.FinanceFakturowniaDomain], !string.IsNullOrEmpty(s[K.FinanceFakturowniaToken]),
            FinanceRules.ParseAmount(s[K.FinanceQuarterLimit]) is > 0 and var q ? q : null,
            FinanceRules.ParseAmount(s[K.FinanceVatLimit]) is > 0 and var v ? v : null,
            FinanceRules.ParseAmount(s[K.FinanceTaxReservePct]) is >= 0 and <= 100 and var pct ? pct : 12m,
            s[K.FinancePrefixReceipt], s[K.FinancePrefixInvoice], s[K.FinancePrefixCorrection]);
    }

    public async Task<string?> SaveSettingsAsync(Settings n, string actor)
    {
        if (n.Mode is not (FinanceModes.Unregistered or FinanceModes.Business)) return "Wybierz formę działalności.";
        if (n.Start is null) return "Podaj, od kiedy prowadzić ewidencję.";
        if (n.Mode == FinanceModes.Business && n.BusinessSince is null) return "Podaj datę rozpoczęcia firmy (z CEIDG).";
        if (!FinanceRules.IsValidNip(n.SellerTaxId)) return "NIP sprzedawcy ma błędną sumę kontrolną.";
        if (n.Provider == "fakturownia" && FakturowniaClient.NormalizeDomain(n.FakturowniaDomain) is null) return "Podaj subdomenę konta Fakturowni (np. „mojafirma” z mojafirma.fakturownia.pl).";
        foreach (var prefix in new[] { n.PrefixReceipt, n.PrefixInvoice, n.PrefixCorrection })
            if (string.IsNullOrWhiteSpace(prefix) || prefix.Length > 12 || prefix.Any(c => !char.IsLetterOrDigit(c) && c != '-'))
                return "Przedrostek numeru: 1–12 liter lub cyfr (np. FV).";

        static string D(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
        var before = await GetSettingsAsync();
        var values = new Dictionary<string, string>
        {
            [K.FinanceMode] = n.Mode,
            [K.FinanceUnregisteredSince] = D(n.UnregisteredSince),
            [K.FinanceBusinessSince] = n.Mode == FinanceModes.Business ? D(n.BusinessSince) : "",
            [K.FinanceStart] = D(n.Start),
            [K.FinanceSellerName] = n.SellerName.Trim(),
            [K.FinanceSellerAddress] = n.SellerAddress.Trim(),
            [K.FinanceSellerEmail] = n.SellerEmail.Trim(),
            [K.FinanceSellerTaxId] = FinanceRules.CleanNip(n.SellerTaxId),
            [K.FinanceSellerRegon] = n.SellerRegon.Trim(),
            [K.FinanceSellerBank] = n.SellerBank.Trim(),
            [K.FinanceVat] = n.Vat,
            [K.FinanceVatNote] = n.VatNote.Trim(),
            [K.FinanceAutoIssue] = n.AutoIssue ? "true" : "false",
            [K.FinanceEmailDocs] = n.EmailDocs ? "true" : "false",
            [K.FinanceProvider] = n.Provider,
            [K.FinanceFakturowniaDomain] = FakturowniaClient.NormalizeDomain(n.FakturowniaDomain) ?? "",
            [K.FinanceQuarterLimit] = n.QuarterLimit?.ToString(CultureInfo.InvariantCulture) ?? "",
            [K.FinanceVatLimit] = n.VatLimit?.ToString(CultureInfo.InvariantCulture) ?? "",
            [K.FinanceTaxReservePct] = n.TaxReservePct.ToString(CultureInfo.InvariantCulture),
            [K.FinancePrefixReceipt] = n.PrefixReceipt.Trim().ToUpperInvariant(),
            [K.FinancePrefixInvoice] = n.PrefixInvoice.Trim().ToUpperInvariant(),
            [K.FinancePrefixCorrection] = n.PrefixCorrection.Trim().ToUpperInvariant(),
        };
        await settings.SetManyAsync(values);

        var changes = new List<string>();
        if (before.Mode != n.Mode) changes.Add($"forma: {ModeLabel(before.Mode)} → {ModeLabel(n.Mode)}");
        if (before.BusinessSince != n.BusinessSince && n.Mode == FinanceModes.Business) changes.Add($"firma od {n.BusinessSince:dd.MM.yyyy}");
        if (before.Start != n.Start) changes.Add($"ewidencja od {n.Start:dd.MM.yyyy}");
        if (before.SellerName != n.SellerName.Trim() || before.SellerAddress != n.SellerAddress.Trim() || before.SellerTaxId != FinanceRules.CleanNip(n.SellerTaxId)) changes.Add("dane sprzedawcy");
        if (before.Vat != n.Vat) changes.Add($"VAT: {n.Vat}");
        if (before.Provider != n.Provider) changes.Add($"dokumenty: {(n.Provider == "fakturownia" ? "Fakturownia" : "Hantly")}");
        if (before.AutoIssue != n.AutoIssue) changes.Add($"automatyczne dokumenty: {(n.AutoIssue ? "tak" : "nie")}");
        if (before.EmailDocs != n.EmailDocs) changes.Add($"wysyłka e-mailem: {(n.EmailDocs ? "tak" : "nie")}");
        if (before.QuarterLimit != n.QuarterLimit) changes.Add($"limit kwartalny: {(n.QuarterLimit is { } l ? FinanceRules.Money(l) : "domyślny")}");
        if (before.PrefixReceipt != values[K.FinancePrefixReceipt] || before.PrefixInvoice != values[K.FinancePrefixInvoice] || before.PrefixCorrection != values[K.FinancePrefixCorrection]) changes.Add("numeracja");
        await using var db = await dbFactory.CreateDbContextAsync();
        Log(db, actor, "settings", null, null, changes.Count == 0 ? "zapisano bez zmian" : string.Join("; ", changes));
        await db.SaveChangesAsync();
        return null;
    }

    public async Task SetFakturowniaDomainAsync(string domain, string actor)
    {
        if (FakturowniaClient.NormalizeDomain(domain) is not { } d) return;
        if (await settings.GetAsync(K.FinanceFakturowniaDomain) == d) return;
        await settings.SetAsync(K.FinanceFakturowniaDomain, d);
        await using var db = await dbFactory.CreateDbContextAsync();
        Log(db, actor, "settings", null, null, $"Fakturownia: {d}.fakturownia.pl");
        await db.SaveChangesAsync();
    }

    public async Task SetFakturowniaTokenAsync(string? token, string actor)
    {
        await fakturownia.SetTokenAsync(token);
        await using var db = await dbFactory.CreateDbContextAsync();
        Log(db, actor, "fakturownia-token", null, null, string.IsNullOrWhiteSpace(token) ? "usunięto token" : "zapisano nowy token");
        await db.SaveChangesAsync();
    }

    /// <summary>Przejście na firmę — od daty z CEIDG dokumenty wystawia firma; ewidencja i numeracja biegną dalej.</summary>
    public async Task<string?> SwitchToBusinessAsync(DateOnly since, string name, string address, string taxId, string regon, string bank, string vat, string actor)
    {
        if (!FinanceRules.IsValidNip(taxId) || string.IsNullOrWhiteSpace(taxId)) return "Podaj poprawny NIP firmy.";
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address)) return "Podaj nazwę i adres firmy.";
        var s = await GetSettingsAsync();
        var n = s with
        {
            Mode = FinanceModes.Business, BusinessSince = since, SellerName = name, SellerAddress = address, SellerTaxId = taxId,
            SellerRegon = regon ?? "", SellerBank = bank ?? "", Vat = vat == "vat" ? "vat" : "zw", VatNote = "",
            Start = s.Start ?? since, UnregisteredSince = s.UnregisteredSince
        };
        var error = await SaveSettingsAsync(n, actor);
        if (error is not null) return error;
        await using var db = await dbFactory.CreateDbContextAsync();
        var late = await db.SalesDocuments.CountAsync(d => d.Mode == FinanceModes.Unregistered && d.IssueDate >= since && d.Status != SalesDocumentStatus.Cancelled);
        Log(db, actor, "switch-business", null, null, $"Założenie firmy od {since:dd.MM.yyyy}, NIP {FinanceRules.CleanNip(taxId)}, VAT: {n.Vat}"
            + (late > 0 ? $"; {late} dok. wystawionych po tej dacie jako działalność nierejestrowana" : ""));
        await db.SaveChangesAsync();
        return null;
    }

    public static string ModeLabel(string mode) => mode switch
    {
        FinanceModes.Unregistered => "działalność nierejestrowana",
        FinanceModes.Business => "firma (CEIDG)",
        _ => "nie ustawiono"
    };

    public static string SellerBlock(Settings s, string mode)
    {
        var lines = new List<string> { s.SellerName.Trim() };
        lines.AddRange(s.SellerAddress.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (mode == FinanceModes.Business && !string.IsNullOrWhiteSpace(s.SellerTaxId)) lines.Add($"NIP: {s.SellerTaxId}");
        if (mode == FinanceModes.Business && !string.IsNullOrWhiteSpace(s.SellerRegon)) lines.Add($"REGON: {s.SellerRegon}");
        if (!string.IsNullOrWhiteSpace(s.SellerEmail)) lines.Add(s.SellerEmail.Trim());
        if (!string.IsNullOrWhiteSpace(s.SellerBank)) lines.Add($"Konto: {s.SellerBank.Trim()}");
        return string.Join("\n", lines.Where(l => l.Length > 0));
    }

    // ── Historia zmian ──

    private static void Log(PortalDbContext db, string actor, string action, string? entity, int? id, string? detail) =>
        db.FinanceEvents.Add(new FinanceEvent
        {
            Actor = string.IsNullOrWhiteSpace(actor) ? "system" : actor, Action = action, Entity = entity, EntityId = id,
            Detail = detail is { Length: > 2000 } ? detail[..2000] : detail
        });

    public async Task<List<FinanceEvent>> HistoryAsync(int take = 200)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.FinanceEvents.AsNoTracking().OrderByDescending(e => e.Id).Take(take).ToListAsync();
    }

    public static string ActionLabel(string action) => action switch
    {
        "settings" => "Ustawienia",
        "fakturownia-token" => "Token Fakturowni",
        "switch-business" => "Założenie firmy",
        "ledger-payment" => "Wpłata w ewidencji",
        "ledger-manual" => "Ręczny wpis w ewidencji",
        "ledger-reverse" => "Wpis prostujący",
        "doc-issue" => "Wystawienie dokumentu",
        "doc-provider" => "Fakturownia",
        "doc-provider-error" => "Błąd Fakturowni",
        "doc-local" => "Numer z Hantly zamiast Fakturowni",
        "doc-email" => "Wysyłka dokumentu",
        "doc-email-error" => "Błąd wysyłki",
        "doc-cancel" => "Anulowanie dokumentu",
        "doc-correct" => "Korekta",
        "expense-add" => "Nowy koszt",
        "expense-delete" => "Usunięcie kosztu",
        "export" => "Eksport",
        "limit-alert" => "Powiadomienie o limicie",
        "buyer-data" => "Dane do faktury trenera",
        _ => action
    };

    // ── Synchronizacja wpłat ──

    public sealed record SyncResult(int Ledger, int Documents, int Synced, int Emailed, List<string> Errors);

    /// <summary>
    /// Wpłaty z platformy (rachunki, sklep, Stripe, weryfikacja) → ewidencja, a przy automatycznych dokumentach
    /// także dokument sprzedaży i e-mail. Bezpieczna do wielokrotnego uruchomienia: wpłata trafia do ewidencji raz.
    /// </summary>
    public async Task<SyncResult> SyncAsync(string actor = "system", CancellationToken ct = default)
    {
        var errors = new List<string>();
        var s = await GetSettingsAsync();
        if (!s.Configured || s.Start is not { } start) return new(0, 0, 0, 0, errors);

        var ledger = 0;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var fromUtc = UtcStart(start);
            var known = db.SalesLedger.Where(l => l.Source == "payment" && l.PaymentRecordId != null).Select(l => l.PaymentRecordId!.Value);
            var fresh = await db.PaymentRecords.AsNoTracking().Include(p => p.Tenant)
                .Where(p => p.Status == PaymentRecordStatus.Paid && p.Amount > 0 && p.CreatedAt >= fromUtc && !known.Contains(p.Id))
                .OrderBy(p => p.CreatedAt).Take(500).ToListAsync(ct);
            foreach (var p in fresh)
            {
                db.SalesLedger.Add(new SalesLedgerEntry
                {
                    Date = LocalDate(p.CreatedAt), Amount = p.Amount, Source = "payment", PaymentRecordId = p.Id, TenantId = p.TenantId,
                    Description = Clip($"{p.Tenant?.CompanyName ?? $"Trener #{p.TenantId}"} — {p.Description ?? "wpłata"} ({SourceLabel(p.Source)})", 500),
                    CreatedBy = actor
                });
                Log(db, actor, "ledger-payment", "payment", p.Id, $"{FinanceRules.Money(p.Amount)} · {p.Tenant?.CompanyName}");
                try { await db.SaveChangesAsync(ct); ledger++; }
                catch (DbUpdateException) { db.ChangeTracker.Clear(); } // równoległa synchronizacja już ją dodała
            }
        }

        var documents = 0;
        if (s.AutoIssue)
        {
            List<int> waiting;
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
                waiting = await db.SalesLedger.AsNoTracking()
                    .Where(l => l.Source == "payment" && l.DocumentId == null && l.PaymentRecordId != null && l.Date >= start)
                    .OrderBy(l => l.Id).Select(l => l.PaymentRecordId!.Value).Take(200).ToListAsync(ct);
            foreach (var id in waiting)
            {
                var (doc, error) = await IssueForPaymentAsync(id, actor, emailNow: false, settingsNow: s);
                if (doc is not null) documents++;
                else if (error is not null) errors.Add(error);
            }
        }

        var synced = await RetryPendingAsync(actor, ct);
        var emailed = s.EmailDocs ? await EmailWaitingAsync(actor, ct) : 0;
        try { await CheckLimitAlertAsync(); } catch (Exception ex) { logger.LogWarning(ex, "Sprawdzenie limitu nie powiodło się."); }
        return new(ledger, documents, synced, emailed, errors);
    }

    public static string SourceLabel(string? source) => (source ?? "").ToLowerInvariant() switch
    {
        "stripe" => "karta (Stripe)",
        "payu" => "PayU",
        "przelewy24" or "p24" => "Przelewy24",
        "autopay" => "Autopay",
        "manual" => "przelew / gotówka",
        "" => "wpłata",
        var x => x
    };

    private static string PaymentMethodOf(string? source) => (source ?? "").ToLowerInvariant() switch
    {
        "stripe" => "karta",
        "manual" => "przelew",
        "payu" => "płatność online (PayU)",
        "przelewy24" or "p24" => "płatność online (Przelewy24)",
        "autopay" => "płatność online (Autopay)",
        _ => "przelew"
    };

    // ── Dokumenty ──

    /// <summary>Dokument sprzedaży do wpłaty (rachunek albo faktura — zależnie od trybu i NIP-u nabywcy).</summary>
    public async Task<(SalesDocument? Doc, string? Error)> IssueForPaymentAsync(int paymentRecordId, string actor, bool emailNow = true, Settings? settingsNow = null)
    {
        var s = settingsNow ?? await GetSettingsAsync();
        if (!s.Configured) return (null, "Najpierw ustaw księgowość (forma działalności i dane sprzedawcy).");
        if (s.MissingSellerData() is { Count: > 0 } missing) return (null, "Uzupełnij dane sprzedawcy: " + string.Join(", ", missing) + ".");

        await using var db = await dbFactory.CreateDbContextAsync();
        var p = await db.PaymentRecords.AsNoTracking().Include(x => x.Tenant).FirstOrDefaultAsync(x => x.Id == paymentRecordId);
        if (p is null || p.Status != PaymentRecordStatus.Paid) return (null, "Nie ma takiej opłaconej wpłaty.");
        if (await db.SalesDocuments.AnyAsync(d => d.PaymentRecordId == p.Id && d.Kind != SalesDocumentKinds.Correction && d.Status != SalesDocumentStatus.Cancelled))
            return (null, null); // już jest

        var t = p.Tenant;
        var saleDate = LocalDate(p.CreatedAt);
        var mode = s.ModeOn(saleDate);
        var taxId = string.IsNullOrWhiteSpace(t?.InvoiceTaxId) ? null : FinanceRules.CleanNip(t.InvoiceTaxId);
        var kind = FinanceRules.KindFor(mode, s.Vat, taxId);
        var rate = kind == SalesDocumentKinds.InvoiceVat ? "23" : "zw";

        var lines = new List<SalesDocumentLine>();
        TenantBill? bill = null;
        if (p.Description is { } desc && desc.StartsWith("Rachunek ", StringComparison.Ordinal))
        {
            var number = desc["Rachunek ".Length..].Trim();
            bill = await db.TenantBills.AsNoTracking().Include(b => b.Lines).FirstOrDefaultAsync(b => b.Number == number && b.TenantId == p.TenantId);
            if (bill is not null && bill.Lines.Count > 0 && bill.Lines.Sum(l => l.Amount) == p.Amount)
                lines.AddRange(bill.Lines.Where(l => l.Amount != 0).Select(l => Line(l.Detail is { Length: > 0 } d ? $"{l.Name} — {d}" : l.Name, l.Quantity, l.UnitPrice, l.Amount, rate)));
        }
        if (lines.Count == 0)
            lines.Add(Line(LineNameFor(p), 1, p.Amount, p.Amount, rate));

        var doc = new SalesDocument
        {
            Kind = kind, Series = SalesDocumentKinds.SeriesOf(kind), Mode = mode,
            IssueDate = Today, SaleDate = saleDate, PaidDate = saleDate, PaymentMethod = PaymentMethodOf(p.Source),
            TenantId = p.TenantId, PaymentRecordId = p.Id, BillId = bill?.Id,
            Seller = SellerBlock(s, mode),
            BuyerName = Clip(FirstNonEmpty(t?.InvoiceName, taxId is null ? t?.OwnerName : t?.CompanyName, t?.CompanyName, "Nabywca"), 300),
            BuyerAddress = t?.InvoiceAddress, BuyerTaxId = taxId,
            BuyerEmail = FirstNonEmpty(t?.InvoiceEmail, t?.OwnerEmail),
            Currency = p.Currency is { Length: 3 } c ? c : "PLN",
            VatNote = kind == SalesDocumentKinds.InvoiceVat ? null : FirstNonEmpty(s.VatNote, FinanceRules.DefaultVatNote(mode, kind)),
            Lines = lines, CreatedBy = actor
        };
        Totals(doc);
        var error = await SaveNewAsync(db, doc, s, actor, null);
        if (error is not null) return (null, error);

        var entry = await db.SalesLedger.FirstOrDefaultAsync(l => l.PaymentRecordId == p.Id && l.Source == "payment");
        if (entry is not null) { entry.DocumentId = doc.Id; await db.SaveChangesAsync(); }
        if (emailNow && s.EmailDocs && doc.Status == SalesDocumentStatus.Issued) await EmailDocumentAsync(doc.Id, actor);
        return (doc, null);
    }

    private static string LineNameFor(PaymentRecord p)
    {
        var d = p.Description ?? "";
        if (d.StartsWith("Weryfikacja", StringComparison.Ordinal)) return "Opłata aktywacyjna — weryfikacja konta Hantly";
        if (d.StartsWith("Zamówienie usług", StringComparison.Ordinal)) return "Usługi dodatkowe Hantly — " + d;
        if (p.StripeInvoiceId is not null) return "Abonament Hantly — " + (d.Length > 0 ? d : "aplikacja dla trenera");
        return d.Length > 0 ? "Hantly — " + d : "Abonament Hantly — aplikacja dla trenera";
    }

    private static SalesDocumentLine Line(string name, decimal qty, decimal unitGross, decimal gross, string rate)
    {
        var (net, vat) = FinanceRules.Split(gross, rate);
        return new SalesDocumentLine { Name = Clip(name, 300), Quantity = qty, UnitGross = unitGross, Gross = gross, Net = net, Vat = vat, VatRate = rate };
    }

    private static void Totals(SalesDocument doc)
    {
        doc.Gross = doc.Lines.Sum(l => l.Gross);
        doc.Net = doc.Lines.Sum(l => l.Net);
        doc.Vat = doc.Lines.Sum(l => l.Vat);
    }

    /// <summary>Zapis nowego dokumentu z numerem: z Hantly (kolejny w serii) albo z Fakturowni.</summary>
    private async Task<string?> SaveNewAsync(PortalDbContext db, SalesDocument doc, Settings s, string actor, SalesDocument? original)
    {
        doc.Provider = s.UsesFakturownia && (original is null || original.Provider == "fakturownia") ? "fakturownia" : "local";
        if (doc.Provider == "fakturownia")
        {
            doc.Status = SalesDocumentStatus.Pending;
            doc.Year = doc.IssueDate.Year;
            db.SalesDocuments.Add(doc);
            Log(db, actor, "doc-issue", "document", null, $"{SalesDocumentKinds.Label(doc.Kind)} dla {doc.BuyerName} na {FinanceRules.Money(doc.Gross)} — czeka na numer z Fakturowni");
            await db.SaveChangesAsync();
            await SendToProviderAsync(db, doc, original, actor);
            return null;
        }

        for (var attempt = 0; attempt < 6; attempt++)
        {
            doc.Year = doc.IssueDate.Year;
            var series = doc.Series;
            doc.Seq = (await db.SalesDocuments.Where(d => d.Series == series && d.Year == doc.Year && d.Seq > 0).MaxAsync(d => (int?)d.Seq) ?? 0) + 1;
            doc.Number = FinanceRules.FormatNumber(s.PrefixFor(doc.Kind), doc.Year, doc.Seq);
            doc.Status = SalesDocumentStatus.Issued;
            if (doc.Id == 0) db.SalesDocuments.Add(doc);
            try
            {
                await db.SaveChangesAsync();
                Log(db, actor, "doc-issue", "document", doc.Id, $"{SalesDocumentKinds.Label(doc.Kind)} {doc.Number} dla {doc.BuyerName} na {FinanceRules.Money(doc.Gross)}");
                await db.SaveChangesAsync();
                return null;
            }
            catch (DbUpdateException) when (attempt < 5)
            {
                // Ktoś w tej samej chwili zajął numer — bierzemy następny.
                db.Entry(doc).State = doc.Id == 0 ? EntityState.Detached : EntityState.Unchanged;
            }
        }
        return "Nie udało się nadać numeru dokumentu — spróbuj ponownie.";
    }

    private async Task SendToProviderAsync(PortalDbContext db, SalesDocument doc, SalesDocument? original, string actor)
    {
        if (original is null && doc.CorrectsId is { } cid)
            original = await db.SalesDocuments.Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == cid);
        var (created, error) = await fakturownia.CreateAsync(doc, original);
        doc.ExternalAttempts++;
        if (created is not null)
        {
            doc.ExternalId = created.Id;
            doc.ExternalUrl = created.Url;
            doc.Number = created.Number;
            doc.ExternalError = null;
            doc.ExternalSyncedAt = DateTime.UtcNow;
            doc.Status = SalesDocumentStatus.Issued;
            Log(db, actor, "doc-provider", "document", doc.Id, $"Fakturownia nadała numer {created.Number}");
        }
        else
        {
            doc.ExternalError = Clip(error ?? "Nieznany błąd Fakturowni.", 1000);
            Log(db, actor, "doc-provider-error", "document", doc.Id, doc.ExternalError);
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Ponawia wysyłkę do Fakturowni dokumentów bez numeru (coraz rzadziej, najwyżej 12 prób).</summary>
    public async Task<int> RetryPendingAsync(string actor = "system", CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var pending = await db.SalesDocuments.Include(d => d.Lines)
            .Where(d => d.Status == SalesDocumentStatus.Pending && d.ExternalAttempts < MaxProviderAttempts).OrderBy(d => d.Id).Take(20).ToListAsync(ct);
        var ok = 0;
        foreach (var doc in pending)
        {
            // Odstęp rośnie z każdą próbą: 15 min, 30 min, 1 h… (synchronizacja i tak chodzi co kwadrans).
            var wait = TimeSpan.FromMinutes(15 * Math.Pow(2, Math.Min(doc.ExternalAttempts - 1, 6)));
            var last = await db.FinanceEvents.Where(e => e.Entity == "document" && e.EntityId == doc.Id).MaxAsync(e => (DateTime?)e.At, ct) ?? doc.CreatedAt;
            if (actor == "system" && DateTime.UtcNow - last < wait) continue;
            await SendToProviderAsync(db, doc, null, actor);
            if (doc.Status == SalesDocumentStatus.Issued) ok++;
        }
        return ok;
    }

    public async Task<string?> RetryProviderAsync(int docId, string actor)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var doc = await db.SalesDocuments.Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == docId);
        if (doc is null || doc.Status != SalesDocumentStatus.Pending) return "Ten dokument nie czeka na Fakturownię.";
        await SendToProviderAsync(db, doc, null, actor);
        return doc.Status == SalesDocumentStatus.Issued ? null : doc.ExternalError;
    }

    /// <summary>Fakturownia nie działa, a dokument jest potrzebny — numer z serii Hantly (dokument zostaje tylko w Hantly).</summary>
    public async Task<string?> IssueLocallyAsync(int docId, string actor)
    {
        var s = await GetSettingsAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var doc = await db.SalesDocuments.Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == docId);
        if (doc is null || doc.Status != SalesDocumentStatus.Pending) return "Ten dokument nie czeka na Fakturownię.";
        doc.Provider = "local";
        doc.ExternalError = null;
        Log(db, actor, "doc-local", "document", doc.Id, "Numer z serii Hantly zamiast Fakturowni");
        return await SaveNewAsync(db, doc, s with { Provider = "local" }, actor, null);
    }

    public sealed class ManualLine
    {
        public string Name { get; set; } = "";
        public decimal Quantity { get; set; } = 1;
        public string Unit { get; set; } = "szt.";
        public decimal UnitGross { get; set; }
        public string VatRate { get; set; } = "zw";
    }

    public sealed class ManualDocument
    {
        public string Kind { get; set; } = SalesDocumentKinds.Receipt;
        public int? TenantId { get; set; }
        public string BuyerName { get; set; } = "";
        public string? BuyerAddress { get; set; }
        public string? BuyerTaxId { get; set; }
        public string? BuyerEmail { get; set; }
        public DateOnly SaleDate { get; set; } = Today;
        public bool Paid { get; set; } = true;
        public string PaymentMethod { get; set; } = "przelew";
        public string? Note { get; set; }
        public bool AddToLedger { get; set; } = true;
        public bool SendEmail { get; set; }
        public List<ManualLine> Lines { get; set; } = [new()];
    }

    /// <summary>Allowed kinds for a manual document in the given mode (korekta — tylko z dokumentu).</summary>
    public static string[] ManualKinds(Settings s, DateOnly date) =>
        s.ModeOn(date) == FinanceModes.Business && s.Vat == "vat"
            ? [SalesDocumentKinds.InvoiceVat]
            : [SalesDocumentKinds.Receipt, SalesDocumentKinds.InvoiceExempt];

    public async Task<(SalesDocument? Doc, string? Error)> IssueManualAsync(ManualDocument input, string actor)
    {
        var s = await GetSettingsAsync();
        if (!s.Configured) return (null, "Najpierw ustaw księgowość.");
        if (s.MissingSellerData() is { Count: > 0 } missing) return (null, "Uzupełnij dane sprzedawcy: " + string.Join(", ", missing) + ".");
        if (!ManualKinds(s, input.SaleDate).Contains(input.Kind)) return (null, "Ten rodzaj dokumentu nie pasuje do formy działalności w dniu sprzedaży.");
        if (string.IsNullOrWhiteSpace(input.BuyerName)) return (null, "Podaj nabywcę.");
        if (!FinanceRules.IsValidNip(input.BuyerTaxId)) return (null, "NIP nabywcy ma błędną sumę kontrolną.");
        var lines = input.Lines.Where(l => !string.IsNullOrWhiteSpace(l.Name) || l.UnitGross != 0).ToList();
        if (lines.Count == 0) return (null, "Dodaj co najmniej jedną pozycję.");
        if (lines.Any(l => string.IsNullOrWhiteSpace(l.Name) || l.Quantity <= 0 || l.UnitGross <= 0)) return (null, "Każda pozycja potrzebuje nazwy, ilości i ceny większej od zera.");
        if (input.SaleDate > Today.AddDays(31)) return (null, "Data sprzedaży jest zbyt odległa.");

        var mode = s.ModeOn(input.SaleDate);
        var defaultRate = input.Kind == SalesDocumentKinds.InvoiceVat ? "23" : "zw";
        var doc = new SalesDocument
        {
            Kind = input.Kind, Series = SalesDocumentKinds.SeriesOf(input.Kind), Mode = mode,
            IssueDate = Today, SaleDate = input.SaleDate, PaidDate = input.Paid ? input.SaleDate : null,
            PaymentMethod = Clip(input.PaymentMethod, 40), TenantId = input.TenantId,
            Seller = SellerBlock(s, mode),
            BuyerName = Clip(input.BuyerName.Trim(), 300), BuyerAddress = NullIfEmpty(input.BuyerAddress),
            BuyerTaxId = string.IsNullOrWhiteSpace(input.BuyerTaxId) ? null : FinanceRules.CleanNip(input.BuyerTaxId),
            BuyerEmail = NullIfEmpty(input.BuyerEmail),
            VatNote = input.Kind == SalesDocumentKinds.InvoiceVat ? null : FirstNonEmpty(s.VatNote, FinanceRules.DefaultVatNote(mode, input.Kind)),
            Note = NullIfEmpty(input.Note), CreatedBy = actor,
            Lines = lines.Select(l =>
            {
                var rate = input.Kind == SalesDocumentKinds.InvoiceVat ? (l.VatRate is "23" or "8" or "5" or "0" or "zw" or "np" ? l.VatRate : defaultRate) : "zw";
                var gross = Math.Round(l.UnitGross * l.Quantity, 2, MidpointRounding.AwayFromZero);
                var line = Line(l.Name.Trim(), l.Quantity, l.UnitGross, gross, rate);
                line.Unit = string.IsNullOrWhiteSpace(l.Unit) ? "szt." : Clip(l.Unit.Trim(), 16);
                return line;
            }).ToList()
        };
        Totals(doc);

        await using var db = await dbFactory.CreateDbContextAsync();
        var error = await SaveNewAsync(db, doc, s, actor, null);
        if (error is not null) return (null, error);
        if (input.AddToLedger)
        {
            db.SalesLedger.Add(new SalesLedgerEntry
            {
                Date = doc.SaleDate, Amount = doc.Gross, Source = "manual", DocumentId = doc.Id, TenantId = doc.TenantId, CreatedBy = actor,
                Description = Clip($"{doc.BuyerName} — {string.Join(", ", doc.Lines.Select(l => l.Name))}", 500)
            });
            Log(db, actor, "ledger-manual", "document", doc.Id, $"{FinanceRules.Money(doc.Gross)} z dokumentu {(doc.Number.Length > 0 ? doc.Number : "(czeka na numer)")}");
            await db.SaveChangesAsync();
        }
        if (input.SendEmail && doc.Status == SalesDocumentStatus.Issued && doc.BuyerEmail is not null) await EmailDocumentAsync(doc.Id, actor);
        return (doc, null);
    }

    /// <summary>Korekta in minus (zwrot, rabat, pomyłka w kwocie). Ewidencja dostaje wpis ujemny w dniu korekty.</summary>
    public async Task<(SalesDocument? Doc, string? Error)> CorrectAsync(int docId, decimal amount, string reason, string actor)
    {
        if (string.IsNullOrWhiteSpace(reason)) return (null, "Podaj przyczynę korekty.");
        if (amount <= 0) return (null, "Kwota korekty musi być większa od zera.");
        var s = await GetSettingsAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var original = await db.SalesDocuments.Include(d => d.Lines).FirstOrDefaultAsync(d => d.Id == docId);
        if (original is null || original.Status != SalesDocumentStatus.Issued || original.Kind == SalesDocumentKinds.Correction)
            return (null, "Korygować można tylko wystawiony dokument (nie korektę).");
        var corrected = await db.SalesDocuments.Where(d => d.CorrectsId == docId && d.Status != SalesDocumentStatus.Cancelled).SumAsync(d => (decimal?)d.Gross) ?? 0;
        var left = original.Gross + corrected;
        if (amount > left) return (null, $"Do skorygowania zostało najwyżej {FinanceRules.Money(left)}.");

        var rate = original.Lines.FirstOrDefault()?.VatRate ?? "zw";
        var doc = new SalesDocument
        {
            Kind = SalesDocumentKinds.Correction, Series = "korekta", Mode = original.Mode,
            IssueDate = Today, SaleDate = original.SaleDate, PaidDate = Today, PaymentMethod = original.PaymentMethod,
            TenantId = original.TenantId, PaymentRecordId = original.PaymentRecordId, BillId = original.BillId,
            CorrectsId = original.Id, CorrectionReason = Clip(reason.Trim(), 500),
            Seller = original.Seller, BuyerName = original.BuyerName, BuyerAddress = original.BuyerAddress,
            BuyerTaxId = original.BuyerTaxId, BuyerEmail = original.BuyerEmail, Currency = original.Currency,
            VatNote = original.VatNote, CreatedBy = actor,
            Lines = [Line($"Korekta do {original.Number}: {reason.Trim()}", 1, -amount, -amount, rate)]
        };
        Totals(doc);
        var error = await SaveNewAsync(db, doc, s, actor, original);
        if (error is not null) return (null, error);

        var inLedger = await db.SalesLedger.AnyAsync(l => (original.PaymentRecordId != null && l.PaymentRecordId == original.PaymentRecordId) || l.DocumentId == original.Id);
        if (inLedger)
        {
            db.SalesLedger.Add(new SalesLedgerEntry
            {
                Date = Today, Amount = -amount, Source = "correction", DocumentId = doc.Id, TenantId = doc.TenantId, CreatedBy = actor,
                Description = Clip($"Korekta {(doc.Number.Length > 0 ? doc.Number : "")} do {original.Number}: {reason.Trim()}", 500)
            });
        }
        Log(db, actor, "doc-correct", "document", original.Id, $"Korekta {doc.Number} do {original.Number} na −{FinanceRules.Money(amount)}: {reason.Trim()}");
        await db.SaveChangesAsync();
        if (s.EmailDocs && doc.Status == SalesDocumentStatus.Issued && doc.BuyerEmail is not null) await EmailDocumentAsync(doc.Id, actor);
        return (doc, null);
    }

    /// <summary>Anulowanie — tylko dokumentu, który nie trafił do obrotu (nie wysłany, nie w Fakturowni). Wpłata zostaje w ewidencji.</summary>
    public async Task<string?> CancelAsync(int docId, string reason, string actor)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "Podaj powód anulowania.";
        await using var db = await dbFactory.CreateDbContextAsync();
        var doc = await db.SalesDocuments.FirstOrDefaultAsync(d => d.Id == docId);
        if (doc is null || doc.Status == SalesDocumentStatus.Cancelled) return "Nie ma takiego dokumentu albo już jest anulowany.";
        if (doc.InCirculation) return "Dokument został już wysłany albo jest w Fakturowni — zamiast anulowania wystaw korektę.";
        if (await db.SalesDocuments.AnyAsync(d => d.CorrectsId == docId && d.Status != SalesDocumentStatus.Cancelled)) return "Do dokumentu jest korekta — nie da się go anulować.";
        doc.Status = SalesDocumentStatus.Cancelled;
        doc.CancelReason = Clip(reason.Trim(), 500);
        doc.CancelledAt = DateTime.UtcNow;
        // Wpis ewidencji z tej wpłaty czeka na nowy dokument (sprzedaż się odbyła, zmienia się tylko dokument).
        foreach (var entry in await db.SalesLedger.Where(l => l.DocumentId == docId && l.Source == "payment").ToListAsync())
            entry.DocumentId = null;
        Log(db, actor, "doc-cancel", "document", doc.Id, $"{doc.Number}: {reason.Trim()}");
        await db.SaveChangesAsync();
        return null;
    }

    public async Task<(bool Ok, string? Error)> EmailDocumentAsync(int docId, string actor, string? to = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var doc = await db.SalesDocuments.Include(d => d.Lines).Include(d => d.Corrects).FirstOrDefaultAsync(d => d.Id == docId);
        if (doc is null || doc.Status != SalesDocumentStatus.Issued) return (false, "Dokument nie jest wystawiony.");
        var address = FirstNonEmpty(to, doc.BuyerEmail);
        if (string.IsNullOrWhiteSpace(address) || !address.Contains('@')) return (false, "Brak adresu e-mail nabywcy.");
        var pdf = await RenderPdfAsync(doc);
        var label = SalesDocumentKinds.Label(doc.Kind);
        var body = $"""
            <p>Dzień dobry,</p>
            <p>w załączniku {label.ToLowerInvariant()} <b>{Enc(doc.Number)}</b> na kwotę <b>{Enc(FinanceRules.Money(doc.Gross))}</b>
            {(doc.PaidDate is not null ? "— opłacony, dziękujemy." : $"— do zapłaty.")}</p>
            {(doc.CorrectionReason is { } r ? $"<p>Przyczyna korekty: {Enc(r)}</p>" : "")}
            <p>Pozdrawiamy,<br>{Enc(doc.Seller.Split('\n')[0])}</p>
            """;
        var (ok, error) = await email.SendAsync(address, $"{label} {doc.Number}", body,
            attachments: [new EmailService.FileAttachment(FileName(doc), pdf, "application/pdf")]);
        if (ok) doc.EmailedAt = DateTime.UtcNow;
        Log(db, actor, ok ? "doc-email" : "doc-email-error", "document", doc.Id, ok ? $"{doc.Number} → {address}" : $"{doc.Number}: {error}");
        await db.SaveChangesAsync();
        return (ok, error);
    }

    private async Task<int> EmailWaitingAsync(string actor, CancellationToken ct)
    {
        List<int> ids;
        var since = DateTime.UtcNow.AddDays(-14);
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            // Dokumenty z ostatnich 2 tygodni, bez wysyłki i bez wcześniejszego błędu (błąd = ręcznie, żeby nie zasypać skrzynki).
            var failed = db.FinanceEvents.Where(e => e.Action == "doc-email-error" && e.Entity == "document").Select(e => e.EntityId);
            ids = await db.SalesDocuments.AsNoTracking()
                .Where(d => d.Status == SalesDocumentStatus.Issued && d.EmailedAt == null && d.BuyerEmail != null && d.CreatedAt >= since && d.CreatedBy == "system" && !failed.Contains(d.Id))
                .OrderBy(d => d.Id).Select(d => d.Id).Take(30).ToListAsync(ct);
        }
        var sent = 0;
        foreach (var id in ids)
            if ((await EmailDocumentAsync(id, actor)).Ok) sent++;
        return sent;
    }

    public async Task<SalesDocument?> GetDocumentAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.SalesDocuments.AsNoTracking().Include(d => d.Lines).Include(d => d.Corrects).FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<(byte[] Pdf, string FileName)?> PdfAsync(int docId)
    {
        var doc = await GetDocumentAsync(docId);
        if (doc is null || doc.Status == SalesDocumentStatus.Pending) return null;
        return (await RenderPdfAsync(doc), FileName(doc));
    }

    private async Task<byte[]> RenderPdfAsync(SalesDocument doc)
    {
        if (doc.Provider == "fakturownia" && doc.ExternalId is { } ext && await fakturownia.PdfAsync(ext) is { } original) return original;
        return SalesDocumentPdf.Render(doc);
    }

    public static string FileName(SalesDocument doc) =>
        $"{SalesDocumentKinds.Label(doc.Kind).ToLowerInvariant().Replace(' ', '-')}-{(doc.Number.Length > 0 ? doc.Number : $"szkic-{doc.Id}").Replace('/', '-')}.pdf";

    public sealed record DocumentFilter(int? Year = null, int? Month = null, string? Kind = null, string? Query = null, bool OnlyProblems = false);

    public async Task<List<SalesDocument>> DocumentsAsync(DocumentFilter f, int take = 300)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var q = db.SalesDocuments.AsNoTracking().AsQueryable();
        if (f.Year is { } y) q = q.Where(d => d.IssueDate.Year == y);
        if (f.Month is { } m) q = q.Where(d => d.IssueDate.Month == m);
        if (!string.IsNullOrEmpty(f.Kind)) q = q.Where(d => d.Kind == f.Kind);
        if (f.OnlyProblems) q = q.Where(d => d.Status == SalesDocumentStatus.Pending || (d.Status == SalesDocumentStatus.Issued && d.EmailedAt == null && d.BuyerEmail != null));
        if (!string.IsNullOrWhiteSpace(f.Query))
        {
            var t = f.Query.Trim().ToLower();
            q = q.Where(d => d.Number.ToLower().Contains(t) || d.BuyerName.ToLower().Contains(t) || (d.BuyerTaxId != null && d.BuyerTaxId.Contains(t)));
        }
        return await q.OrderByDescending(d => d.IssueDate).ThenByDescending(d => d.Id).Take(take).ToListAsync();
    }

    /// <summary>Wpłaty w ewidencji bez dokumentu (gdy automatyczne dokumenty są wyłączone albo czekają na dane sprzedawcy).</summary>
    public async Task<List<SalesLedgerEntry>> PaymentsWithoutDocumentAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.SalesLedger.AsNoTracking().Where(l => l.Source == "payment" && l.DocumentId == null)
            .OrderByDescending(l => l.Date).Take(100).ToListAsync();
    }

    // ── Ewidencja ──

    public sealed record LedgerDay(int No, DateOnly Date, decimal Amount, decimal MonthToDate, List<SalesLedgerEntry> Entries);

    /// <summary>Ewidencja dzienna: przychód z dnia i suma narastająco od początku miesiąca (numeracja Lp. w miesiącu).</summary>
    public async Task<List<LedgerDay>> LedgerDaysAsync(DateOnly from, DateOnly to)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var monthStart = new DateOnly(from.Year, from.Month, 1);
        var entries = await db.SalesLedger.AsNoTracking().Where(l => l.Date >= monthStart && l.Date <= to).OrderBy(l => l.Date).ThenBy(l => l.Id).ToListAsync();
        return BuildDays(entries).Where(d => d.Date >= from).ToList();
    }

    public static List<LedgerDay> BuildDays(IEnumerable<SalesLedgerEntry> entries)
    {
        var days = new List<LedgerDay>();
        var month = (0, 0);
        decimal running = 0; var no = 0;
        foreach (var g in entries.GroupBy(e => e.Date).OrderBy(g => g.Key))
        {
            if ((g.Key.Year, g.Key.Month) != month) { month = (g.Key.Year, g.Key.Month); running = 0; no = 0; }
            var amount = g.Sum(e => e.Amount);
            running += amount;
            days.Add(new LedgerDay(++no, g.Key, amount, running, g.ToList()));
        }
        return days;
    }

    public async Task<string?> AddManualEntryAsync(DateOnly date, decimal amount, string description, string actor)
    {
        if (amount == 0) return "Podaj kwotę.";
        if (string.IsNullOrWhiteSpace(description)) return "Opisz sprzedaż (co i dla kogo).";
        if (date > Today) return "Sprzedaż z przyszłości? Wpisz ją w dniu sprzedaży.";
        await using var db = await dbFactory.CreateDbContextAsync();
        var entry = new SalesLedgerEntry { Date = date, Amount = amount, Source = "manual", Description = Clip(description.Trim(), 500), CreatedBy = actor };
        db.SalesLedger.Add(entry);
        await db.SaveChangesAsync();
        Log(db, actor, "ledger-manual", "ledger", entry.Id, $"{date:dd.MM.yyyy} {FinanceRules.Money(amount)}: {entry.Description}");
        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>Wpis prostujący (in minus) błędny wpis — oryginał zostaje, oba widać w ewidencji.</summary>
    public async Task<string?> ReverseEntryAsync(int entryId, string reason, string actor)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "Podaj powód.";
        await using var db = await dbFactory.CreateDbContextAsync();
        var entry = await db.SalesLedger.FirstOrDefaultAsync(l => l.Id == entryId);
        if (entry is null || entry.ReversesId is not null) return "Tego wpisu nie da się sprostować.";
        if (await db.SalesLedger.AnyAsync(l => l.ReversesId == entryId)) return "Ten wpis jest już sprostowany.";
        if (entry.DocumentId is not null && entry.Source != "manual") return "Do wpisu jest dokument — wystaw do niego korektę.";
        var rev = new SalesLedgerEntry
        {
            Date = Today, Amount = -entry.Amount, Source = "correction", ReversesId = entry.Id, TenantId = entry.TenantId, CreatedBy = actor,
            Description = Clip($"Sprostowanie wpisu z {entry.Date:dd.MM.yyyy}: {reason.Trim()}", 500)
        };
        db.SalesLedger.Add(rev);
        await db.SaveChangesAsync();
        Log(db, actor, "ledger-reverse", "ledger", entry.Id, $"−{FinanceRules.Money(entry.Amount)}: {reason.Trim()}");
        await db.SaveChangesAsync();
        return null;
    }

    // ── Koszty ──

    public const int MaxAttachmentBytes = 10 * 1024 * 1024;

    public async Task<string?> AddExpenseAsync(Expense e, string actor)
    {
        if (e.Amount <= 0) return "Podaj kwotę kosztu.";
        if (string.IsNullOrWhiteSpace(e.Vendor) || string.IsNullOrWhiteSpace(e.Description)) return "Podaj sprzedawcę i opis.";
        if (e.Date > Today) return "Data kosztu jest w przyszłości.";
        if (e.Attachment is { Length: > MaxAttachmentBytes }) return "Załącznik może mieć najwyżej 10 MB.";
        e.Vendor = Clip(e.Vendor.Trim(), 200);
        e.Description = Clip(e.Description.Trim(), 500);
        e.Category = ExpenseCategories.Contains(e.Category) ? e.Category : "inne";
        e.CreatedBy = actor;
        await using var db = await dbFactory.CreateDbContextAsync();
        db.Expenses.Add(e);
        await db.SaveChangesAsync();
        Log(db, actor, "expense-add", "expense", e.Id, $"{e.Date:dd.MM.yyyy} {FinanceRules.Money(e.Amount)} · {e.Vendor} · {e.Description}{(e.Attachment is null ? " (bez dowodu)" : "")}");
        await db.SaveChangesAsync();
        return null;
    }

    public async Task<string?> DeleteExpenseAsync(int id, string reason, string actor)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "Podaj powód usunięcia.";
        await using var db = await dbFactory.CreateDbContextAsync();
        var e = await db.Expenses.FirstOrDefaultAsync(x => x.Id == id && x.DeletedAt == null);
        if (e is null) return "Nie ma takiego kosztu.";
        e.DeletedAt = DateTime.UtcNow;
        e.DeletedReason = Clip(reason.Trim(), 500);
        Log(db, actor, "expense-delete", "expense", e.Id, $"{FinanceRules.Money(e.Amount)} · {e.Vendor}: {reason.Trim()}");
        await db.SaveChangesAsync();
        return null;
    }

    public async Task<List<Expense>> ExpensesAsync(DateOnly from, DateOnly to, bool withDeleted = false)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var q = db.Expenses.AsNoTracking().Where(e => e.Date >= from && e.Date <= to);
        if (!withDeleted) q = q.Where(e => e.DeletedAt == null);
        // Bez treści załączników — lista ma być lekka.
        return await q.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id).Select(e => new Expense
        {
            Id = e.Id, Date = e.Date, Amount = e.Amount, Vendor = e.Vendor, Description = e.Description, Category = e.Category,
            DocumentNumber = e.DocumentNumber, AttachmentName = e.AttachmentName, AttachmentType = e.AttachmentType,
            CreatedAt = e.CreatedAt, CreatedBy = e.CreatedBy, DeletedAt = e.DeletedAt, DeletedReason = e.DeletedReason
        }).ToListAsync();
    }

    public async Task<(byte[] Data, string Name, string Type)?> ExpenseAttachmentAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var e = await db.Expenses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return e?.Attachment is { Length: > 0 } a ? (a, e.AttachmentName ?? $"koszt-{id}", e.AttachmentType ?? "application/octet-stream") : null;
    }

    // ── Przegląd ──

    public sealed record QuarterView(int Year, int Quarter, DateOnly Start, DateOnly End, decimal Limit, bool Estimated,
        decimal Received, decimal Outstanding, decimal Forecast, DateOnly? ExceededOn, string Tone)
    {
        public decimal Total => Received + Outstanding;
        public decimal Left => Math.Max(0, Limit - Total);
        public int Percent => Limit <= 0 ? 0 : (int)Math.Min(999, Math.Round(Total * 100 / Limit));
        public DateOnly? Deadline => ExceededOn is { } d ? FinanceRules.CeidgDeadline(d) : null;
    }

    public sealed record VatView(int Year, decimal Limit, decimal Revenue, string Tone)
    {
        public int Percent => Limit <= 0 ? 0 : (int)Math.Min(999, Math.Round(Revenue * 100 / Limit));
    }

    public sealed record Alert(string Tone, string Icon, string Title, string Text, string? Tab = null);
    public sealed record MonthBar(int Month, decimal Revenue, decimal Costs);

    public sealed record Overview(Settings Settings, string ModeToday, QuarterView? Quarter, List<QuarterView> YearQuarters,
        decimal MonthRevenue, decimal YearRevenue, decimal YearCosts, decimal TaxReserve, VatView? Vat,
        int PendingDocs, int UnsentDocs, int PaymentsWithoutDoc, int ExpensesWithoutProof, DateTime? LastExport,
        List<MonthBar> Months, List<Alert> Alerts)
    {
        public decimal YearIncome => YearRevenue - YearCosts;
    }

    public async Task<Overview> OverviewAsync(DateOnly? todayOverride = null)
    {
        var today = todayOverride ?? Today;
        var s = await GetSettingsAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var yearStart = new DateOnly(today.Year, 1, 1);
        var yearEnd = new DateOnly(today.Year, 12, 31);
        var raw = await db.SalesLedger.AsNoTracking().Where(l => l.Date >= yearStart && l.Date <= yearEnd)
            .Select(l => new { l.Date, l.Amount, l.DocumentId }).ToListAsync();
        // Forma wpisu: z dokumentu (korekta sprzedaży sprzed założenia firmy zostaje przy działalności nierejestrowanej), inaczej z daty.
        var docIds = raw.Where(e => e.DocumentId != null).Select(e => e.DocumentId!.Value).Distinct().ToList();
        var docModes = await db.SalesDocuments.AsNoTracking().Where(d => docIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Mode);
        var entries = raw.Select(e => new
        {
            e.Date, e.Amount,
            Mode = e.DocumentId is { } id && docModes.TryGetValue(id, out var m) ? m : s.Configured ? s.ModeOn(e.Date) : FinanceModes.Unregistered
        }).ToList();
        var costs = await db.Expenses.AsNoTracking().Where(e => e.DeletedAt == null && e.Date >= yearStart && e.Date <= yearEnd)
            .Select(e => new { e.Date, e.Amount, HasProof = e.Attachment != null }).ToListAsync();
        var openBills = await db.TenantBills.AsNoTracking().Where(b => b.Status == TenantBillStatus.Issued)
            .Select(b => new { b.IssuedAt, b.Amount }).ToListAsync();

        var modeToday = s.Configured ? s.ModeOn(today) : "";
        var quarters = new List<QuarterView>();
        for (var q = 1; q <= 4; q++)
        {
            var qs = FinanceRules.QuarterStart(today.Year, q);
            var qe = FinanceRules.QuarterEnd(today.Year, q);
            if (qs > today) break;
            var (limit, estimated) = FinanceRules.QuarterLimit(today.Year, q, s.QuarterLimit);
            var inQuarter = entries.Where(e => e.Date >= qs && e.Date <= qe && e.Mode == FinanceModes.Unregistered).ToList();
            var received = inQuarter.Sum(e => e.Amount);
            var outstanding = q == FinanceRules.QuarterOf(today)
                ? openBills.Where(b => LocalDate(b.IssuedAt) >= qs && LocalDate(b.IssuedAt) <= qe).Sum(b => b.Amount) : 0;
            var exceeded = FinanceRules.ExceededOn(inQuarter.Select(e => (e.Date, e.Amount)), limit);
            var forecast = FinanceRules.Forecast(received, qs, qe, today) + outstanding;
            quarters.Add(new QuarterView(today.Year, q, qs, qe, limit, estimated, received, outstanding, forecast, exceeded,
                FinanceRules.Tone(received + outstanding, limit)));
        }
        var current = modeToday == FinanceModes.Unregistered ? quarters.LastOrDefault() : null;

        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var yearRevenue = entries.Sum(e => e.Amount);
        var yearCosts = costs.Sum(c => c.Amount);
        var reserve = Math.Max(0, Math.Round((yearRevenue - yearCosts) * s.TaxReservePct / 100m, 2));

        VatView? vat = null;
        if (modeToday == FinanceModes.Business && s.Vat == "zw")
        {
            var limit = FinanceRules.VatLimitFor(today.Year, s.VatLimit ?? FinanceRules.DefaultVatLimit(today.Year), s.BusinessSince);
            var revenue = Math.Max(0, entries.Where(e => e.Mode == FinanceModes.Business && (s.BusinessSince is null || e.Date >= s.BusinessSince)).Sum(e => e.Amount));
            vat = new VatView(today.Year, limit, revenue, FinanceRules.Tone(revenue, limit));
        }

        var pending = await db.SalesDocuments.CountAsync(d => d.Status == SalesDocumentStatus.Pending);
        var unsent = await db.SalesDocuments.CountAsync(d => d.Status == SalesDocumentStatus.Issued && d.EmailedAt == null && d.BuyerEmail != null);
        var noDoc = await db.SalesLedger.CountAsync(l => l.Source == "payment" && l.DocumentId == null);
        var lastExportRaw = await settings.GetAsync(K.FinanceLastExportAt);
        DateTime? lastExport = DateTime.TryParse(lastExportRaw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var le) ? le : null;

        var months = Enumerable.Range(1, 12).Select(m => new MonthBar(m,
            entries.Where(e => e.Date.Month == m).Sum(e => e.Amount),
            costs.Where(c => c.Date.Month == m).Sum(c => c.Amount))).ToList();

        var alerts = new List<Alert>();
        if (!s.Configured)
            alerts.Add(new("info", "bi-signpost-split", "Ustaw księgowość", "Wybierz formę działalności i wpisz dane sprzedawcy — od tej chwili każda wpłata trafi do ewidencji.", "ustawienia"));
        else
        {
            if (s.MissingSellerData() is { Count: > 0 } miss)
                alerts.Add(new("warn", "bi-person-vcard", "Brakuje danych sprzedawcy", "Uzupełnij: " + string.Join(", ", miss) + ". Bez nich dokumenty nie powstaną.", "ustawienia"));
            if (current is { ExceededOn: { } ex } cq)
                alerts.Add(new("danger", "bi-exclamation-octagon", $"Przekroczony limit w {cq.Quarter}. kwartale",
                    $"Suma przekroczyła {FinanceRules.Money(cq.Limit)} dnia {ex:dd.MM.yyyy}. Działalność nierejestrowana staje się firmą od dnia przekroczenia, jeśli w ciągu 7 dni (do {cq.Deadline:dd.MM.yyyy}) złożysz wniosek do CEIDG. Po założeniu firmy przełącz księgowość w ustawieniach („Firma założona?”).", "ustawienia"));
            else if (current is { } q2 && q2.Total >= q2.Limit * 0.8m)
                alerts.Add(new("warn", "bi-speedometer", $"Limit kwartału wykorzystany w {q2.Percent}%",
                    $"Zostało {FinanceRules.Money(q2.Left)} do {FinanceRules.Money(q2.Limit)}. Przygotuj wniosek do CEIDG, żeby założyć firmę bez nerwów.", null));
            else if (current is { } q3 && q3.Forecast > q3.Limit)
                alerts.Add(new("warn", "bi-graph-up-arrow", "W tym tempie limit kwartału pęknie",
                    $"Prognoza na koniec kwartału: {FinanceRules.Money(q3.Forecast)} przy limicie {FinanceRules.Money(q3.Limit)}.", null));
            if (current is { Estimated: true })
                alerts.Add(new("info", "bi-calendar-check", "Sprawdź limit na ten rok", "Nie znamy płacy minimalnej na ten rok — liczymy z ostatniej znanej. Wpisz aktualny limit w ustawieniach.", "ustawienia"));
            if (vat is { } v && v.Revenue >= v.Limit * 0.8m)
                alerts.Add(new(v.Tone, "bi-percent", $"Limit zwolnienia z VAT: {v.Percent}%", $"Sprzedaż w tym roku {FinanceRules.Money(v.Revenue)} z {FinanceRules.Money(v.Limit)}. Po przekroczeniu trzeba zarejestrować się do VAT — porozmawiaj z księgową.", null));
            if (pending > 0)
                alerts.Add(new("warn", "bi-cloud-slash", $"{pending} dok. czeka na Fakturownię", "Fakturownia nie nadała numeru. Ponawiamy co jakiś czas — możesz ponowić ręcznie albo wystawić z numerem Hantly.", "dokumenty"));
            if (noDoc > 0)
                alerts.Add(new("info", "bi-receipt", $"{noDoc} {FinanceRules.Plural(noDoc, "wpłata", "wpłaty", "wpłat")} bez dokumentu", s.AutoIssue ? "Dokumenty powstaną przy najbliższej synchronizacji (co 15 minut)." : "Automatyczne dokumenty są wyłączone — wystaw je ręcznie w zakładce Dokumenty.", "dokumenty"));
            if (costs.Count(c => !c.HasProof) is var noProof and > 0)
                alerts.Add(new("info", "bi-paperclip", $"{noProof} {FinanceRules.Plural(noProof, "koszt", "koszty", "kosztów")} bez dowodu", "Dołącz fakturę albo paragon — bez dowodu koszt trudno obronić.", "koszty"));
            if (today.Day >= 5 && (lastExport is null || LocalDate(lastExport.Value) < monthStart))
                alerts.Add(new("info", "bi-file-earmark-zip", "Zrób kopię za poprzedni miesiąc", "Pobierz paczkę (ewidencja, dokumenty, koszty) i odłóż ją poza Hantly — na dysk albo do księgowej.", "eksport"));
        }

        return new Overview(s, modeToday, current, quarters,
            entries.Where(e => e.Date >= monthStart).Sum(e => e.Amount), yearRevenue, yearCosts, reserve, vat,
            pending, unsent, noDoc, costs.Count(c => !c.HasProof), lastExport, months, alerts);
    }

    /// <summary>E-mail do administratora przy 80% i 100% limitu (raz na próg w kwartale).</summary>
    private async Task CheckLimitAlertAsync()
    {
        var o = await OverviewAsync();
        if (o.Quarter is not { } q) return;
        var level = q.ExceededOn is not null ? 100 : q.Total >= q.Limit * 0.8m ? 80 : 0;
        if (level == 0) return;
        var key = $"{q.Year}-Q{q.Quarter}-{level}";
        var sent = await settings.GetAsync(K.FinanceLimitAlertSent);
        if (string.Compare(sent, key, StringComparison.Ordinal) >= 0 && sent.StartsWith($"{q.Year}-Q{q.Quarter}-", StringComparison.Ordinal)) return;
        var to = await settings.GetAsync(K.AdminNotificationEmail);
        if (string.IsNullOrWhiteSpace(to)) return;
        var body = level == 100
            ? $"<p>Przychód w {q.Quarter}. kwartale przekroczył limit działalności nierejestrowanej ({Enc(FinanceRules.Money(q.Limit))}) dnia <b>{q.ExceededOn:dd.MM.yyyy}</b>.</p><p>Działalność staje się firmą od dnia przekroczenia, jeśli w ciągu 7 dni — do <b>{q.Deadline:dd.MM.yyyy}</b> — złożysz wniosek do CEIDG. Sprawdź szczegóły z księgową.</p>"
            : $"<p>Wykorzystano {q.Percent}% limitu działalności nierejestrowanej w {q.Quarter}. kwartale: {Enc(FinanceRules.Money(q.Total))} z {Enc(FinanceRules.Money(q.Limit))}.</p><p>Zostało {Enc(FinanceRules.Money(q.Left))}. Warto przygotować wniosek do CEIDG.</p>";
        var (ok, _) = await email.SendAsync(to, level == 100 ? "Księgowość: przekroczony limit kwartalny" : $"Księgowość: {q.Percent}% limitu kwartalnego", body);
        if (!ok) return;
        await settings.SetAsync(K.FinanceLimitAlertSent, key);
        await using var db = await dbFactory.CreateDbContextAsync();
        Log(db, "system", "limit-alert", null, null, $"{key} → {to}");
        await db.SaveChangesAsync();
    }

    // ── Eksport ──

    private static readonly CultureInfo Pl = FinanceRules.Pl;
    private static string Csv(string? v) => "\"" + (v ?? "").Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
    private static string N(decimal v) => v.ToString("0.00", Pl);
    private static byte[] Utf8Bom(StringBuilder sb) => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];

    public static byte[] LedgerCsv(List<LedgerDay> days)
    {
        var sb = new StringBuilder("Lp.;Data;Przychód z dnia;Narastająco od początku miesiąca;Opis\r\n");
        foreach (var d in days)
            sb.Append($"{d.No};{d.Date:yyyy-MM-dd};{N(d.Amount)};{N(d.MonthToDate)};{Csv(string.Join(" | ", d.Entries.Select(e => $"{e.Description} ({N(e.Amount)})")))}\r\n");
        return Utf8Bom(sb);
    }

    public static byte[] DocumentsCsv(List<SalesDocument> docs)
    {
        var sb = new StringBuilder("Numer;Rodzaj;Status;Data wystawienia;Data sprzedaży;Nabywca;NIP;Netto;VAT;Brutto;Forma;Korygowany;Przyczyna\r\n");
        foreach (var d in docs.OrderBy(d => d.IssueDate).ThenBy(d => d.Id))
            sb.Append($"{Csv(d.Number)};{Csv(SalesDocumentKinds.Label(d.Kind))};{Csv(StatusLabel(d.Status))};{d.IssueDate:yyyy-MM-dd};{d.SaleDate:yyyy-MM-dd};{Csv(d.BuyerName)};{Csv(d.BuyerTaxId)};{N(d.Net)};{N(d.Vat)};{N(d.Gross)};{Csv(ModeLabel(d.Mode))};{Csv(d.Corrects?.Number)};{Csv(d.CorrectionReason ?? d.CancelReason)}\r\n");
        return Utf8Bom(sb);
    }

    public static byte[] ExpensesCsv(List<Expense> list)
    {
        var sb = new StringBuilder("Data;Kwota;Sprzedawca;Opis;Kategoria;Numer dowodu;Załącznik\r\n");
        foreach (var e in list.OrderBy(e => e.Date))
            sb.Append($"{e.Date:yyyy-MM-dd};{N(e.Amount)};{Csv(e.Vendor)};{Csv(e.Description)};{Csv(e.Category)};{Csv(e.DocumentNumber)};{Csv(e.AttachmentName is null ? "brak" : AttachmentFile(e))}\r\n");
        return Utf8Bom(sb);
    }

    private static string AttachmentFile(Expense e) => $"{e.Date:yyyy-MM-dd}-{e.Id}-{SafeName(e.AttachmentName ?? "dowod")}";
    private static string SafeName(string s) => new(s.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray());

    public static string StatusLabel(SalesDocumentStatus s) => s switch
    {
        SalesDocumentStatus.Issued => "wystawiony",
        SalesDocumentStatus.Pending => "czeka na Fakturownię",
        _ => "anulowany"
    };

    public async Task<byte[]> LedgerCsvAsync(DateOnly from, DateOnly to) => LedgerCsv(await LedgerDaysAsync(from, to));

    public async Task<byte[]> LedgerPdfAsync(DateOnly from, DateOnly to)
    {
        var s = await GetSettingsAsync();
        return LedgerPdf.Render(await LedgerDaysAsync(from, to), from, to, SellerBlock(s, s.ModeOn(to)), ModeLabel(s.ModeOn(to)));
    }

    /// <summary>Paczka dla księgowej: ewidencja (CSV i PDF), rejestr dokumentów z PDF-ami, koszty z dowodami i podsumowanie.</summary>
    public async Task<byte[]> PackageAsync(DateOnly from, DateOnly to, string actor)
    {
        var s = await GetSettingsAsync();
        var days = await LedgerDaysAsync(from, to);
        List<SalesDocument> docs;
        List<Expense> expenses;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            docs = await db.SalesDocuments.AsNoTracking().Include(d => d.Lines).Include(d => d.Corrects)
                .Where(d => d.IssueDate >= from && d.IssueDate <= to).OrderBy(d => d.IssueDate).ThenBy(d => d.Id).ToListAsync();
            expenses = await db.Expenses.AsNoTracking().Where(e => e.DeletedAt == null && e.Date >= from && e.Date <= to).ToListAsync();
        }

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, byte[] data)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var st = entry.Open();
                st.Write(data);
            }
            Add("ewidencja-sprzedazy.csv", LedgerCsv(days));
            Add("ewidencja-sprzedazy.pdf", LedgerPdf.Render(days, from, to, SellerBlock(s, s.ModeOn(to)), ModeLabel(s.ModeOn(to))));
            Add("dokumenty.csv", DocumentsCsv(docs));
            foreach (var d in docs.Where(d => d.Status != SalesDocumentStatus.Pending))
                Add($"dokumenty/{FileName(d)}", await RenderPdfAsync(d));
            Add("koszty.csv", ExpensesCsv(expenses));
            foreach (var e in expenses.Where(e => e.Attachment is { Length: > 0 }))
                Add($"koszty/{AttachmentFile(e)}", e.Attachment!);

            var revenue = days.Sum(d => d.Amount);
            var cost = expenses.Sum(e => e.Amount);
            var summary = new StringBuilder()
                .AppendLine($"Paczka księgowa {from:dd.MM.yyyy} – {to:dd.MM.yyyy}")
                .AppendLine($"Wygenerowano: {TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tz):dd.MM.yyyy HH:mm} ({actor})")
                .AppendLine()
                .AppendLine(SellerBlock(s, s.ModeOn(to)))
                .AppendLine($"Forma: {ModeLabel(s.ModeOn(from))}{(s.ModeOn(from) != s.ModeOn(to) ? $" → {ModeLabel(s.ModeOn(to))} od {s.BusinessSince:dd.MM.yyyy}" : "")}")
                .AppendLine()
                .AppendLine($"Przychód (ewidencja): {FinanceRules.Money(revenue)}")
                .AppendLine($"Koszty: {FinanceRules.Money(cost)} ({expenses.Count} poz., bez dowodu: {expenses.Count(e => e.Attachment is null)})")
                .AppendLine($"Różnica: {FinanceRules.Money(revenue - cost)}")
                .AppendLine($"Dokumenty: {docs.Count(d => d.Status == SalesDocumentStatus.Issued)} wystawionych, {docs.Count(d => d.Status == SalesDocumentStatus.Cancelled)} anulowanych, {docs.Count(d => d.Status == SalesDocumentStatus.Pending)} czeka na numer")
                .AppendLine()
                .AppendLine("Ewidencja liczy sprzedaż w dniu wpłaty. Kwoty i limity sprawdź z księgową — Hantly porządkuje dane, ale nie zastępuje doradcy podatkowego.");
            Add("PRZECZYTAJ.txt", Encoding.UTF8.GetBytes(summary.ToString()));
        }

        await settings.SetAsync(K.FinanceLastExportAt, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            Log(db, actor, "export", null, null, $"Paczka {from:dd.MM.yyyy} – {to:dd.MM.yyyy}: {docs.Count} dok., {expenses.Count} kosztów");
            await db.SaveChangesAsync();
        }
        return ms.ToArray();
    }

    // ── Dane do faktury trenera ──

    /// <summary>Komunikaty walidacji danych nabywcy — panel trenera pokazuje tylko te (nie dowolny tekst z adresu).</summary>
    public static readonly string[] BuyerErrors =
    [
        "NIP ma błędną sumę kontrolną — sprawdź cyfry.",
        "Do faktury na firmę podaj też nazwę i adres.",
        "Adres e-mail do dokumentów wygląda na niepoprawny.",
        "Za długie dane."
    ];

    public static string? ValidateBuyer(string? name, string? address, string? taxId, string? invoiceEmail)
    {
        if (!FinanceRules.IsValidNip(taxId)) return BuyerErrors[0];
        if (!string.IsNullOrWhiteSpace(taxId) && (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))) return BuyerErrors[1];
        if (!string.IsNullOrWhiteSpace(invoiceEmail) && !invoiceEmail.Contains('@')) return BuyerErrors[2];
        if ((name?.Length ?? 0) > 300 || (address?.Length ?? 0) > 500) return BuyerErrors[3];
        return null;
    }

    public async Task<string?> SaveBuyerAsync(int tenantId, string? name, string? address, string? taxId, string? invoiceEmail, string actor)
    {
        if (ValidateBuyer(name, address, taxId, invoiceEmail) is { } error) return error;
        await using var db = await dbFactory.CreateDbContextAsync();
        var t = await db.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId);
        if (t is null) return "Nie ma takiej aplikacji.";
        t.InvoiceName = NullIfEmpty(name);
        t.InvoiceAddress = NullIfEmpty(address);
        t.InvoiceTaxId = string.IsNullOrWhiteSpace(taxId) ? null : FinanceRules.CleanNip(taxId);
        t.InvoiceEmail = NullIfEmpty(invoiceEmail);
        t.InvoiceDataAt = DateTime.UtcNow;
        Log(db, actor, "buyer-data", "tenant", t.Id, $"{t.CompanyName}: {(t.InvoiceTaxId is null ? "osoba prywatna" : $"firma, NIP {t.InvoiceTaxId}")}");
        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>Dokumenty trenera (do pobrania w jego panelu) — bez anulowanych i bez czekających na numer.</summary>
    public async Task<List<SalesDocument>> DocumentsOfTenantsAsync(IReadOnlyCollection<int> tenantIds, int take = 12)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.SalesDocuments.AsNoTracking()
            .Where(d => d.TenantId != null && tenantIds.Contains(d.TenantId.Value) && d.Status == SalesDocumentStatus.Issued)
            .OrderByDescending(d => d.IssueDate).ThenByDescending(d => d.Id).Take(take).ToListAsync();
    }

    // ── Pomocnicze ──

    private static string Clip(string? s, int max) => (s ?? "").Length > max ? s![..max] : s ?? "";
    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";
    private static string Enc(string s) => System.Net.WebUtility.HtmlEncode(s);

    private static TimeZoneInfo FindWarsaw()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"); }
        catch { try { return TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time"); } catch { return TimeZoneInfo.Utc; } }
    }
}

/// <summary>Co kwadrans: nowe wpłaty → ewidencja i dokumenty, ponowienia Fakturowni, wysyłka, alarm limitu.</summary>
public class AccountingBackgroundService(IServiceScopeFactory scopes, ILogger<AccountingBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); } catch (OperationCanceledException) { return; }
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var r = await scope.ServiceProvider.GetRequiredService<AccountingService>().SyncAsync("system", stoppingToken);
                if (r.Ledger + r.Documents + r.Synced + r.Emailed > 0)
                    logger.LogInformation("Księgowość: {Ledger} wpłat w ewidencji, {Docs} dokumentów, {Synced} z Fakturowni, {Mail} wysłanych.", r.Ledger, r.Documents, r.Synced, r.Emailed);
                foreach (var e in r.Errors.Distinct().Take(3)) logger.LogWarning("Księgowość: {Error}", e);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { logger.LogError(ex, "Synchronizacja księgowości nie powiodła się."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
