using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Poczta w Panelu. Brevo (Inbound Parsing) odbiera e-maile na kontakt@… i wysyła je webhookiem do Portalu;
/// tu łączymy je w wątki, powiadamiamy administratora, a odpowiedzi wysyłamy przez SMTP platformy
/// (z nagłówkami wątku, więc u odbiorcy rozmowa się nie rozpada). Szkic odpowiedzi może napisać Leo —
/// do AI idzie temat i treść bez adresów e-mail i numerów telefonów.
/// </summary>
public class InboxService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    EmailService email,
    CreditService credits,
    AssistantService assistant,
    IDataProtectionProvider dataProtection,
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<InboxService> logger)
{
    /// <summary>Od tej oceny Brevo wiadomość trafia do folderu Spam.</summary>
    public const double SpamThreshold = 5.0;
    /// <summary>Nagłówek naszych powiadomień — gdyby wróciły do skrzynki, nie powiadamiamy w kółko.</summary>
    public const string NotificationHeader = "X-Hantly-Inbox";
    private const string BrevoApi = "https://api.brevo.com/v3";
    private IDataProtector Secrets => dataProtection.CreateProtector("inbox-brevo-api-key");

    // ── Ustawienia ───────────────────────────────────────────────────────────────────────

    public async Task<string> AddressAsync() =>
        (await settings.GetAsync(K.InboxAddress)) is { Length: > 0 } a ? a.Trim().ToLowerInvariant()
        : config.GetValue<string>("Portal:ContactEmail") is { Length: > 0 } c ? c.Trim().ToLowerInvariant() : "kontakt@hantly.pl";

    public async Task<string> SecretAsync()
    {
        var s = await settings.GetAsync(K.InboxSecret);
        if (s.Length >= 32) return s;
        s = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        await settings.SetAsync(K.InboxSecret, s);
        return s;
    }

    public async Task RegenerateSecretAsync() =>
        await settings.SetAsync(K.InboxSecret, Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant());

    public async Task<bool> SecretMatchesAsync(string? candidate)
    {
        var secret = await settings.GetAsync(K.InboxSecret);
        if (string.IsNullOrEmpty(candidate) || secret.Length < 32) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate), Encoding.UTF8.GetBytes(secret));
    }

    public string PublicRoot(string? fallback) =>
        (config.GetValue<string>("Portal:PublicUrl") is { Length: > 0 } pub ? pub : fallback ?? "https://hantly.pl").TrimEnd('/');

    public async Task<string> WebhookUrlAsync(string? baseUrl) => $"{PublicRoot(baseUrl)}/api/inbound/brevo/{await SecretAsync()}";

    public sealed record Config(string Address, bool HasApiKey, bool NotifyEmail, bool NotifySms, string AdminEmail, string AdminPhone, DateTime? LastReceivedAt);

    public async Task<Config> GetConfigAsync()
    {
        var s = await settings.GetAllAsync(K.InboxBrevoApiKey, K.InboxNotifyEmail, K.InboxNotifySms, K.AdminNotificationEmail,
            K.AdminNotificationPhone, K.InboxLastReceivedAt);
        DateTime? last = DateTime.TryParse(s[K.InboxLastReceivedAt], null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;
        return new(await AddressAsync(), s[K.InboxBrevoApiKey].Length > 0, s[K.InboxNotifyEmail] != "false", s[K.InboxNotifySms] == "true",
            s[K.AdminNotificationEmail], s[K.AdminNotificationPhone], last);
    }

    public async Task SaveConfigAsync(string address, bool notifyEmail, bool notifySms)
    {
        var a = address.Trim().ToLowerInvariant();
        if (!System.Net.Mail.MailAddress.TryCreate(a, out _)) throw new ArgumentException("To nie wygląda na adres e-mail.");
        await settings.SetManyAsync(new()
        {
            [K.InboxAddress] = a,
            [K.InboxNotifyEmail] = notifyEmail ? "true" : "false",
            [K.InboxNotifySms] = notifySms ? "true" : "false"
        });
    }

    public Task SetApiKeyAsync(string? key) =>
        settings.SetAsync(K.InboxBrevoApiKey, string.IsNullOrWhiteSpace(key) ? "" : Secrets.Protect(key.Trim()));

    private async Task<string?> ApiKeyAsync()
    {
        var raw = await settings.GetAsync(K.InboxBrevoApiKey);
        if (string.IsNullOrEmpty(raw)) return null;
        try { return Secrets.Unprotect(raw); }
        catch (CryptographicException) { logger.LogWarning("Nie da się odszyfrować klucza Brevo — wpisz go ponownie w Poczcie."); return null; }
    }

    /// <summary>Zakłada w Brevo webhook odbioru poczty dla domeny (np. hantly.pl). Zwraca błąd albo null.</summary>
    public async Task<string?> CreateBrevoWebhookAsync(string domain, string? baseUrl)
    {
        var key = await ApiKeyAsync();
        if (key is null) return "Najpierw wpisz klucz API Brevo.";
        domain = domain.Trim().ToLowerInvariant();
        if (domain.Length < 4 || !domain.Contains('.') || domain.Contains('/')) return "Podaj samą domenę, np. hantly.pl.";
        var http = httpFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BrevoApi}/webhooks")
        {
            Content = JsonContent(new
            {
                type = "inbound",
                events = new[] { "inboundEmailProcessed" },
                url = await WebhookUrlAsync(baseUrl),
                domain,
                description = "Hantly — Poczta w Panelu"
            })
        };
        req.Headers.Add("api-key", key);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            using var res = await http.SendAsync(req);
            if (res.IsSuccessStatusCode) return null;
            var body = await res.Content.ReadAsStringAsync();
            return $"Brevo odpowiedziało {(int)res.StatusCode}: {(body.Length > 300 ? body[..300] : body)}";
        }
        catch (Exception ex) { return $"Nie udało się połączyć z Brevo: {ex.Message}"; }
    }

    private static StringContent JsonContent(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    // ── Odbiór ───────────────────────────────────────────────────────────────────────────

    /// <summary>Zapisuje wiadomości z webhooka Brevo. Zwraca, ile nowych dodano.</summary>
    public async Task<int> ReceiveAsync(JsonElement payload)
    {
        var items = InboxParsing.ParseBrevo(payload);
        if (items.Count == 0) return 0;
        var inbox = await AddressAsync();
        var platformFrom = (await settings.GetAsync(K.SmtpFrom)).Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;
        var added = 0;
        await using var db = await dbFactory.CreateDbContextAsync();

        foreach (var m in items)
        {
            if (m.MessageId is { } mid && await db.InboxMessages.AnyAsync(x => x.MessageId == mid)) continue; // Brevo ponowiło wysyłkę

            var refs = m.References.Append(m.InReplyTo).Where(r => !string.IsNullOrEmpty(r)).Cast<string>().Distinct().ToList();
            InboxThread? thread = null;
            if (refs.Count > 0)
            {
                var threadId = await db.InboxMessages.Where(x => x.MessageId != null && refs.Contains(x.MessageId)).Select(x => (int?)x.ThreadId).FirstOrDefaultAsync();
                if (threadId is int tid) thread = await db.InboxThreads.FirstOrDefaultAsync(t => t.Id == tid);
            }
            var key = InboxParsing.SubjectKey(m.Subject);
            var since = now.AddDays(-60);
            thread ??= await db.InboxThreads
                .Where(t => t.Counterpart == m.FromAddress && t.SubjectKey == key && t.LastAt >= since)
                .OrderByDescending(t => t.LastAt).FirstOrDefaultAsync();

            var spam = m.SpamScore >= SpamThreshold;
            if (thread is null)
            {
                var tenantId = await db.Tenants.Where(t => t.OwnerEmail.ToLower() == m.FromAddress).Select(t => (int?)t.Id).FirstOrDefaultAsync();
                thread = new InboxThread
                {
                    Subject = string.IsNullOrWhiteSpace(m.Subject) ? "(bez tematu)" : m.Subject.Trim(),
                    SubjectKey = key,
                    Counterpart = m.FromAddress,
                    CounterpartName = m.FromName,
                    TenantId = tenantId,
                    Folder = spam ? InboxFolder.Spam : InboxFolder.Inbox
                };
                db.InboxThreads.Add(thread);
            }
            else if (thread.Folder == InboxFolder.Archived && !spam) thread.Folder = InboxFolder.Inbox; // nowa odpowiedź wraca do Odebranych

            thread.Unread++;
            thread.LastAt = now;
            thread.Snippet = InboxParsing.Snippet(m.Text);
            if (string.IsNullOrEmpty(thread.CounterpartName) && !string.IsNullOrEmpty(m.FromName)) thread.CounterpartName = m.FromName;
            thread.Messages.Add(new InboxMessage
            {
                Incoming = true,
                MessageId = m.MessageId,
                InReplyTo = m.InReplyTo,
                FromAddress = m.FromAddress,
                FromName = m.FromName,
                ToAddress = string.IsNullOrEmpty(m.ToAddress) ? inbox : m.ToAddress,
                Subject = m.Subject,
                Text = m.Text,
                Html = m.Html,
                SpamScore = m.SpamScore,
                AttachmentsJson = m.Attachments.Count > 0 ? JsonSerializer.Serialize(m.Attachments) : null,
                At = now
            });
            await db.SaveChangesAsync();
            added++;

            // Własne powiadomienia i wiadomości z adresu platformy nie wywołują kolejnych powiadomień (bez pętli).
            var ours = m.FromAddress == inbox || m.FromAddress == platformFrom || m.Subject.StartsWith("Poczta:", StringComparison.Ordinal);
            if (!spam && !ours && thread.Unread == 1 && thread.Folder == InboxFolder.Inbox)
            {
                try { await NotifyAsync(thread, m, inbox); }
                catch (Exception ex) { logger.LogWarning(ex, "Inbox: powiadomienie o wiadomości {Id} nie wyszło.", thread.Id); }
            }
        }

        // Spam starszy niż 30 dni znika sam.
        var spamCutoff = now.AddDays(-30);
        var oldSpam = await db.InboxThreads.Where(t => t.Folder == InboxFolder.Spam && t.LastAt < spamCutoff).ToListAsync();
        if (oldSpam.Count > 0) { db.InboxThreads.RemoveRange(oldSpam); await db.SaveChangesAsync(); }
        await settings.SetAsync(K.InboxLastReceivedAt, now.ToString("O"));
        return added;
    }

    private async Task NotifyAsync(InboxThread thread, InboxParsing.Incoming m, string inbox)
    {
        var s = await settings.GetAllAsync(K.AdminNotificationEmail, K.AdminNotificationPhone, K.InboxNotifyEmail, K.InboxNotifySms);
        var who = string.IsNullOrWhiteSpace(m.FromName) ? m.FromAddress : m.FromName;
        var link = $"{PublicRoot(null)}/panel/poczta?w={thread.Id}";
        var admin = s[K.AdminNotificationEmail].Trim().ToLowerInvariant();
        // Powiadomienie na adres w tej samej domenie co skrzynka wróciłoby do Poczty — pomijamy.
        if (s[K.InboxNotifyEmail] != "false" && admin.Length > 0 && InboxParsing.Domain(admin) != InboxParsing.Domain(inbox))
        {
            var html = $"""
                <div style="font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;font-size:15px;line-height:1.5;color:#1f2937;max-width:560px">
                <p style="margin:0 0 8px;color:#6b7280;font-size:13px">Nowa wiadomość na {WebUtility.HtmlEncode(inbox)}</p>
                <p style="margin:0 0 4px"><strong>{WebUtility.HtmlEncode(who)}</strong> &lt;{WebUtility.HtmlEncode(m.FromAddress)}&gt;</p>
                <p style="margin:0 0 12px"><strong>{WebUtility.HtmlEncode(thread.Subject)}</strong></p>
                <p style="margin:0 0 16px;color:#374151">{WebUtility.HtmlEncode(InboxParsing.Snippet(m.Text))}</p>
                <p><a href="{link}" style="background:#0f766e;color:#fff;text-decoration:none;padding:10px 20px;border-radius:8px;font-weight:600;display:inline-block">Otwórz w Poczcie</a></p>
                <p style="margin-top:16px;color:#9ca3af;font-size:12px">Odpowiedz z Panelu — wtedy rozmowa zostaje w jednym miejscu.</p>
                </div>
                """;
            await email.SendAsync(admin, $"Poczta: {who} — {thread.Subject}", html, fromNameOverride: "Poczta · Panel",
                headers: new Dictionary<string, string> { [NotificationHeader] = "notification" });
        }
        var phone = s[K.AdminNotificationPhone];
        if (s[K.InboxNotifySms] == "true" && !string.IsNullOrWhiteSpace(phone))
        {
            var text = $"Hantly: nowa wiadomosc od {who}: {thread.Subject}";
            if (text.Length > 120) text = text[..117] + "...";
            await credits.SendPlatformSmsAsync(phone, $"{text} {link}");
        }
    }

    // ── Panel ────────────────────────────────────────────────────────────────────────────

    public async Task<int> UnreadThreadsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.InboxThreads.CountAsync(t => t.Folder == InboxFolder.Inbox && t.Unread > 0);
    }

    public async Task<Dictionary<InboxFolder, int>> CountsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var list = await db.InboxThreads.GroupBy(t => t.Folder).Select(g => new { g.Key, Count = g.Count(t => t.Unread > 0) }).ToListAsync();
        return list.ToDictionary(x => x.Key, x => x.Count);
    }

    public async Task<List<InboxThread>> ListAsync(InboxFolder folder, string? search, int take = 200)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var q = db.InboxThreads.AsNoTracking().Include(t => t.Tenant).Where(t => t.Folder == folder);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = $"%{search.Trim()}%";
            q = q.Where(t => EF.Functions.ILike(t.Subject, s) || EF.Functions.ILike(t.Counterpart, s)
                          || (t.CounterpartName != null && EF.Functions.ILike(t.CounterpartName, s))
                          || t.Messages.Any(m => m.Text != null && EF.Functions.ILike(m.Text, s)));
        }
        return await q.OrderByDescending(t => t.LastAt).Take(take).ToListAsync();
    }

    /// <summary>Wątek z wiadomościami (od najstarszej); otwarcie oznacza go jako przeczytany.</summary>
    public async Task<InboxThread?> OpenAsync(int id, bool markRead = true)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var t = await db.InboxThreads.Include(x => x.Tenant).Include(x => x.Messages.OrderBy(m => m.At)).FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return null;
        if (markRead && t.Unread > 0) { t.Unread = 0; await db.SaveChangesAsync(); }
        return t;
    }

    public async Task MarkUnreadAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.InboxThreads.Where(t => t.Id == id).ExecuteUpdateAsync(u => u.SetProperty(t => t.Unread, 1));
    }

    public async Task MoveAsync(int id, InboxFolder folder)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.InboxThreads.Where(t => t.Id == id).ExecuteUpdateAsync(u => u.SetProperty(t => t.Folder, folder));
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.InboxThreads.Where(t => t.Id == id).ExecuteDeleteAsync();
    }

    /// <summary>Odpowiedź w wątku: cytat ostatniej wiadomości, nagłówki wątku, adres skrzynki jako nadawca lub „odpowiedz do”.</summary>
    public async Task<(bool Ok, string? Error)> ReplyAsync(int threadId, string body, string adminEmail)
    {
        if (string.IsNullOrWhiteSpace(body)) return (false, "Napisz treść odpowiedzi.");
        await using var db = await dbFactory.CreateDbContextAsync();
        var t = await db.InboxThreads.Include(x => x.Messages).FirstOrDefaultAsync(x => x.Id == threadId);
        if (t is null) return (false, "Nie ma takiego wątku.");
        var last = t.Messages.OrderByDescending(m => m.At).FirstOrDefault(m => m.Incoming) ?? t.Messages.OrderByDescending(m => m.At).FirstOrDefault();
        var subject = InboxParsing.ReplySubject(t.Subject);
        var quoteHeader = last is null ? null
            : $"{ContainerLogService.ToLocal(last.At):dd.MM.yyyy, HH:mm} · {(string.IsNullOrWhiteSpace(last.FromName) ? last.FromAddress : $"{last.FromName} <{last.FromAddress}>")}:";
        var refs = t.Messages.Where(m => m.MessageId != null).OrderBy(m => m.At).Select(m => m.MessageId!).TakeLast(10).ToList();
        return await SendAndStoreAsync(db, t, t.Counterpart, subject, body, InboxParsing.ReplyHtml(body, quoteHeader, last?.Text),
            last?.MessageId, refs, adminEmail);
    }

    /// <summary>Nowa wiadomość z Panelu (np. do trenera) — zaczyna wątek, w który trafi odpowiedź.</summary>
    public async Task<(bool Ok, string? Error, int? ThreadId)> ComposeAsync(string to, string subject, string body, string adminEmail)
    {
        to = to.Trim().ToLowerInvariant();
        if (!System.Net.Mail.MailAddress.TryCreate(to, out _)) return (false, "Podaj poprawny adres odbiorcy.", null);
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body)) return (false, "Uzupełnij temat i treść.", null);
        await using var db = await dbFactory.CreateDbContextAsync();
        var tenantId = await db.Tenants.Where(x => x.OwnerEmail.ToLower() == to).Select(x => (int?)x.Id).FirstOrDefaultAsync();
        var t = new InboxThread { Subject = subject.Trim(), SubjectKey = InboxParsing.SubjectKey(subject), Counterpart = to, TenantId = tenantId };
        db.InboxThreads.Add(t);
        var (ok, error) = await SendAndStoreAsync(db, t, to, subject.Trim(), body, InboxParsing.ReplyHtml(body, null, null), null, [], adminEmail);
        return (ok, error, ok ? t.Id : null);
    }

    private async Task<(bool Ok, string? Error)> SendAndStoreAsync(PortalDbContext db, InboxThread t, string to, string subject, string body,
        string html, string? inReplyTo, List<string> refs, string adminEmail)
    {
        var inbox = await AddressAsync();
        var platformFrom = (await settings.GetAsync(K.SmtpFrom)).Trim().ToLowerInvariant();
        // Ta sama domena co nadawca platformy (zweryfikowana w Brevo) — piszemy wprost z kontakt@; inaczej tylko „odpowiedz do”.
        var fromOverride = InboxParsing.Domain(inbox) == InboxParsing.Domain(platformFrom) ? inbox : null;
        var domain = InboxParsing.Domain(inbox) is { Length: > 0 } d ? d : "hantly.pl";
        var messageId = $"<{Guid.NewGuid():N}@{domain}>";
        var headers = new Dictionary<string, string> { ["Message-ID"] = messageId };
        if (inReplyTo is not null) headers["In-Reply-To"] = inReplyTo;
        if (refs.Count > 0) headers["References"] = string.Join(' ', refs);

        var (ok, error) = await email.SendAsync(to, subject, html, replyTo: inbox, fromAddressOverride: fromOverride, headers: headers);
        if (!ok) return (false, error is null ? "Nie udało się wysłać." : $"Nie udało się wysłać: {error}");

        var now = DateTime.UtcNow;
        t.Messages.Add(new InboxMessage
        {
            Incoming = false, MessageId = messageId, InReplyTo = inReplyTo, FromAddress = fromOverride ?? platformFrom, ToAddress = to,
            Subject = subject, Text = body.Trim(), SentBy = adminEmail, At = now
        });
        t.LastAt = now;
        t.Unread = 0;
        t.Snippet = InboxParsing.Snippet("Ty: " + body);
        if (t.Folder == InboxFolder.Spam) t.Folder = InboxFolder.Inbox;
        await db.SaveChangesAsync();
        return (true, null);
    }

    /// <summary>Szkic odpowiedzi od Leo (AI albo szablon). Do AI: temat i treść bez e-maili i telefonów.</summary>
    public async Task<AssistantService.Draft> DraftAsync(int threadId)
    {
        var t = await OpenAsync(threadId, markRead: false);
        var last = t?.Messages.LastOrDefault(m => m.Incoming);
        var text = InboxParsing.MaskForAi(last?.Text);
        var facts = new Dictionary<string, string>
        {
            ["temat"] = InboxParsing.MaskForAi(t?.Subject),
            ["tresc_wiadomosci"] = text,
            ["czy_trener_platformy"] = t?.TenantId is null ? "nie wiadomo" : "tak",
        };
        if (t?.Tenant is { } tenant) facts["studio"] = tenant.CompanyName;
        const string fallback = "Dzień dobry,\n\ndziękujemy za wiadomość. Sprawdzimy temat i wrócimy z odpowiedzią najszybciej, jak to możliwe. Jeśli to pilne, napisz proszę, jak najlepiej się skontaktować.\n\nPozdrawiamy\nZespół {MARKA}";
        return await assistant.DraftReplyAsync("poczta",
            "Ktoś napisał na adres kontaktowy platformy. Odpowiedz konkretnie na treść wiadomości; jeśli czegoś nie wiadomo, poproś o szczegóły albo zaproponuj krótką rozmowę.",
            facts, InboxParsing.ReplySubject(t?.Subject ?? "Twoja wiadomość"), fallback, message: text);
    }

    /// <summary>Załącznik z Brevo (wymaga klucza API). Null — brak klucza, tokenu albo błąd.</summary>
    public async Task<(byte[] Data, string ContentType, string Name)?> AttachmentAsync(int messageId, int index)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var json = await db.InboxMessages.Where(m => m.Id == messageId).Select(m => m.AttachmentsJson).FirstOrDefaultAsync();
        if (json is null) return null;
        var list = JsonSerializer.Deserialize<List<InboxParsing.Attachment>>(json) ?? [];
        if (index < 0 || index >= list.Count || string.IsNullOrEmpty(list[index].Token)) return null;
        var key = await ApiKeyAsync();
        if (key is null) return null;
        var http = httpFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{BrevoApi}/inbound/attachments/{Uri.EscapeDataString(list[index].Token!)}");
        req.Headers.Add("api-key", key);
        try
        {
            using var res = await http.SendAsync(req);
            if (!res.IsSuccessStatusCode) { logger.LogWarning("Brevo: załącznik {Index} wiadomości {Id} — {Status}.", index, messageId, (int)res.StatusCode); return null; }
            var data = await res.Content.ReadAsByteArrayAsync();
            return (data, list[index].Type ?? "application/octet-stream", list[index].Name);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Brevo: nie udało się pobrać załącznika."); return null; }
    }
}
