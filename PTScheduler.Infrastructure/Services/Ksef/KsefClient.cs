using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PTScheduler.Application.Ksef;

namespace PTScheduler.Infrastructure.Services.Ksef;

/// <summary>Wynik wysłania faktury do KSeF.</summary>
public sealed record KsefSendResult(string SessionReference, string InvoiceReference);

/// <summary>Stan przetworzenia faktury w KSeF.</summary>
public sealed record KsefInvoiceState(bool Processed, bool Accepted, string? KsefNumber, string? Error);

/// <summary>
/// Klient API KSeF 2.0: uwierzytelnienie tokenem KSeF, sesja interaktywna (online),
/// wysłanie zaszyfrowanej faktury i odczyt jej statusu.
///
/// Przepływ zgodny z dokumentacją MF dla KSeF 2.0:
///  1. POST /auth/challenge — wyzwanie,
///  2. POST /auth/ksef-token — token zaszyfrowany kluczem publicznym MF (RSA-OAEP SHA-256)
///     w postaci „token|znacznik czasu wyzwania w ms”,
///  3. GET /auth/{ref} — oczekiwanie na zakończenie uwierzytelnienia,
///  4. POST /auth/token/redeem — token dostępowy,
///  5. POST /sessions/online — sesja z kluczem AES-256 zaszyfrowanym kluczem publicznym MF,
///  6. POST /sessions/online/{ref}/invoices — faktura zaszyfrowana AES-256-CBC,
///  7. POST /sessions/online/{ref}/close, a status: GET /sessions/{ref}/invoices/{invRef}.
/// </summary>
public sealed class KsefClient(HttpClient http)
{
    public static string DefaultBaseUrl(string environment) => environment switch
    {
        "production" => "https://api.ksef.mf.gov.pl/v2",
        "demo" => "https://api-demo.ksef.mf.gov.pl/v2",
        _ => "https://api-test.ksef.mf.gov.pl/v2",
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<KsefSendResult> SendInvoiceAsync(string baseUrl, string nip, string ksefToken, string invoiceXml, CancellationToken ct = default)
    {
        baseUrl = baseUrl.TrimEnd('/');
        var certs = await GetPublicKeysAsync(baseUrl, ct);
        var accessToken = await AuthenticateAsync(baseUrl, nip, ksefToken, certs.TokenKey, ct);

        // Klucz symetryczny sesji: AES-256 + IV, przekazany do MF zaszyfrowany RSA-OAEP.
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.GenerateKey();
        aes.GenerateIV();

        var open = await PostAsync(baseUrl + "/sessions/online", accessToken, new
        {
            formCode = new { systemCode = Fa3XmlBuilder.SystemCode, schemaVersion = Fa3XmlBuilder.SchemaVersion, value = Fa3XmlBuilder.FormValue },
            encryption = new
            {
                encryptedSymmetricKey = Convert.ToBase64String(RsaOaep(certs.SymmetricKey, aes.Key)),
                initializationVector = Convert.ToBase64String(aes.IV)
            }
        }, ct);
        var sessionRef = Str(open, "referenceNumber") ?? throw new KsefException("KSeF nie zwrócił numeru sesji.");

        var plain = Encoding.UTF8.GetBytes(invoiceXml);
        var encrypted = aes.EncryptCbc(plain, aes.IV, PaddingMode.PKCS7);
        var sent = await PostAsync($"{baseUrl}/sessions/online/{sessionRef}/invoices", accessToken, new
        {
            invoiceHash = Convert.ToBase64String(SHA256.HashData(plain)),
            invoiceSize = plain.Length,
            encryptedInvoiceHash = Convert.ToBase64String(SHA256.HashData(encrypted)),
            encryptedInvoiceSize = encrypted.Length,
            encryptedInvoiceContent = Convert.ToBase64String(encrypted),
            offlineMode = false
        }, ct);
        var invoiceRef = Str(sent, "referenceNumber") ?? throw new KsefException("KSeF nie zwrócił numeru faktury.");

        // Zamknięcie sesji uruchamia generowanie UPO; błąd tu nie unieważnia wysyłki.
        try { await PostAsync($"{baseUrl}/sessions/online/{sessionRef}/close", accessToken, new { }, ct); }
        catch (KsefException) { }

        return new KsefSendResult(sessionRef, invoiceRef);
    }

    public async Task<KsefInvoiceState> GetInvoiceStateAsync(string baseUrl, string nip, string ksefToken,
        string sessionRef, string invoiceRef, CancellationToken ct = default)
    {
        baseUrl = baseUrl.TrimEnd('/');
        var certs = await GetPublicKeysAsync(baseUrl, ct);
        var accessToken = await AuthenticateAsync(baseUrl, nip, ksefToken, certs.TokenKey, ct);
        var node = await GetAsync($"{baseUrl}/sessions/{sessionRef}/invoices/{invoiceRef}", accessToken, ct);

        var code = node?["status"]?["code"]?.GetValue<int>() ?? 0;
        var ksefNumber = Str(node, "ksefNumber");
        if (code == 200 || !string.IsNullOrEmpty(ksefNumber))
            return new KsefInvoiceState(true, true, ksefNumber, null);
        if (code >= 400)
        {
            var desc = node?["status"]?["description"]?.ToString();
            var details = node?["status"]?["details"] is JsonArray arr ? string.Join("; ", arr.Select(d => d?.ToString())) : null;
            return new KsefInvoiceState(true, false, null, string.Join(" — ", new[] { desc, details }.Where(x => !string.IsNullOrWhiteSpace(x))));
        }
        return new KsefInvoiceState(false, false, null, null);
    }

    /// <summary>Sprawdza konfigurację: pobiera klucze MF i uwierzytelnia się tokenem.</summary>
    public async Task TestAsync(string baseUrl, string nip, string ksefToken, CancellationToken ct = default)
    {
        baseUrl = baseUrl.TrimEnd('/');
        var certs = await GetPublicKeysAsync(baseUrl, ct);
        await AuthenticateAsync(baseUrl, nip, ksefToken, certs.TokenKey, ct);
    }

    // ── uwierzytelnienie ──────────────────────────────────────────────────

    private async Task<string> AuthenticateAsync(string baseUrl, string nip, string ksefToken, X509Certificate2 tokenKey, CancellationToken ct)
    {
        var challenge = await PostAsync(baseUrl + "/auth/challenge", null, new { }, ct);
        var challengeValue = Str(challenge, "challenge") ?? throw new KsefException("Brak wyzwania uwierzytelnienia.");
        var timestampMs = challenge?["timestampMs"]?.GetValue<long>()
            ?? DateTimeOffset.Parse(Str(challenge, "timestamp") ?? throw new KsefException("Brak znacznika czasu wyzwania."))
                .ToUnixTimeMilliseconds();

        var payload = Encoding.UTF8.GetBytes($"{ksefToken}|{timestampMs}");
        var init = await PostAsync(baseUrl + "/auth/ksef-token", null, new
        {
            challenge = challengeValue,
            contextIdentifier = new { type = "Nip", value = Nip.Normalize(nip) },
            encryptedToken = Convert.ToBase64String(RsaOaep(tokenKey, payload))
        }, ct);
        var authRef = Str(init, "referenceNumber") ?? throw new KsefException("Brak numeru operacji uwierzytelnienia.");
        var authToken = init?["authenticationToken"]?["token"]?.ToString() ?? throw new KsefException("Brak tokenu operacji uwierzytelnienia.");

        // Uwierzytelnienie jest asynchroniczne — czekamy na status 200.
        for (var i = 0; i < 20; i++)
        {
            var st = await GetAsync($"{baseUrl}/auth/{authRef}", authToken, ct);
            var code = st?["status"]?["code"]?.GetValue<int>() ?? 0;
            if (code == 200) break;
            if (code >= 400)
                throw new KsefException($"Uwierzytelnienie odrzucone: {st?["status"]?["description"]}");
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        var redeem = await PostAsync(baseUrl + "/auth/token/redeem", authToken, new { }, ct);
        return redeem?["accessToken"]?["token"]?.ToString() ?? throw new KsefException("Brak tokenu dostępowego KSeF.");
    }

    private sealed record PublicKeys(X509Certificate2 TokenKey, X509Certificate2 SymmetricKey);

    private async Task<PublicKeys> GetPublicKeysAsync(string baseUrl, CancellationToken ct)
    {
        var node = await GetAsync(baseUrl + "/security/public-key-certificates", null, ct) as JsonArray
                   ?? throw new KsefException("Nie udało się pobrać kluczy publicznych KSeF.");
        X509Certificate2? token = null, symmetric = null;
        foreach (var c in node)
        {
            var der = c?["certificate"]?.ToString();
            if (string.IsNullOrEmpty(der)) continue;
            var usage = c?["usage"] is JsonArray u ? u.Select(x => x?.ToString()).ToList() : [];
            var cert = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(der));
            if (usage.Contains("KsefTokenEncryption")) token ??= cert;
            if (usage.Contains("SymmetricKeyEncryption")) symmetric ??= cert;
        }
        return new PublicKeys(
            token ?? throw new KsefException("Brak klucza MF do szyfrowania tokenu."),
            symmetric ?? throw new KsefException("Brak klucza MF do szyfrowania klucza sesji."));
    }

    private static byte[] RsaOaep(X509Certificate2 cert, byte[] data)
    {
        using var rsa = cert.GetRSAPublicKey() ?? throw new KsefException("Klucz publiczny MF nie jest kluczem RSA.");
        return rsa.Encrypt(data, RSAEncryptionPadding.OaepSHA256);
    }

    // ── HTTP ──────────────────────────────────────────────────────────────

    private async Task<JsonNode?> PostAsync(string url, string? bearer, object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, options: Json) };
        return await SendAsync(req, bearer, ct);
    }

    private async Task<JsonNode?> GetAsync(string url, string? bearer, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        return await SendAsync(req, bearer, ct);
    }

    private async Task<JsonNode?> SendAsync(HttpRequestMessage req, string? bearer, CancellationToken ct)
    {
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (bearer is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var resp = await http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new KsefException($"KSeF {(int)resp.StatusCode}: {Describe(text)}");
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    private static string Describe(string body)
    {
        try
        {
            var n = JsonNode.Parse(body);
            var ex = n?["exception"]?["exceptionDetailList"] as JsonArray;
            if (ex is { Count: > 0 })
                return string.Join("; ", ex.Select(e => $"{e?["exceptionDescription"]} {string.Join(", ", (e?["details"] as JsonArray)?.Select(d => d?.ToString()) ?? [])}".Trim()));
            return n?["title"]?.ToString() ?? n?["message"]?.ToString() ?? body[..Math.Min(body.Length, 300)];
        }
        catch { return body.Length > 300 ? body[..300] : body; }
    }

    private static string? Str(JsonNode? n, string prop) => n?[prop]?.ToString();
}

public sealed class KsefException(string message) : Exception(message);
