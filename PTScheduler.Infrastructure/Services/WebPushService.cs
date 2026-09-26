using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class WebPushService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ILogger<WebPushService> logger) : IWebPushService
{
    public async Task<WebPushSettingsDto> GetSettingsAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var s = await db.WebPushSettings.FirstOrDefaultAsync();
        if (s is null) return new WebPushSettingsDto();
        return new WebPushSettingsDto
        {
            PublicKey = s.PublicKey,
            PrivateKey = s.PrivateKey,
            Subject = s.Subject
        };
    }

    public async Task SaveSettingsAsync(WebPushSettingsDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var s = await db.WebPushSettings.FirstOrDefaultAsync();
        if (s is null)
        {
            s = new WebPushSettings();
            db.WebPushSettings.Add(s);
        }
        s.PublicKey = dto.PublicKey;
        s.PrivateKey = dto.PrivateKey;
        s.Subject = dto.Subject;
        await db.SaveChangesAsync();
    }

    public async Task<string> GetPublicKeyAsync()
    {
        var settings = await GetSettingsAsync();
        return settings.PublicKey;
    }

    public async Task SubscribeAsync(string userId, PushSubscriptionDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var existing = await db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Endpoint == dto.Endpoint);
        if (existing is not null)
        {
            existing.P256dh = dto.P256dh;
            existing.Auth = dto.Auth;
        }
        else
        {
            db.PushSubscriptions.Add(new PushSubscription
            {
                UserId = userId,
                Endpoint = dto.Endpoint,
                P256dh = dto.P256dh,
                Auth = dto.Auth
            });
        }
        await db.SaveChangesAsync();
    }

    public async Task UnsubscribeAsync(string userId, string endpoint)
    {
        await using var db = dbFactory.CreateDbContext();
        var sub = await db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Endpoint == endpoint);
        if (sub is not null)
        {
            db.PushSubscriptions.Remove(sub);
            await db.SaveChangesAsync();
        }
    }

    public async Task<int> GetSubscriptionCountAsync(string userId)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.PushSubscriptions.CountAsync(s => s.UserId == userId);
    }

    public async Task SendAsync(string userId, PushMessageDto message) => await SendWithReportAsync(userId, message);

    public async Task<PushSendReport> SendWithReportAsync(string userId, PushMessageDto message)
    {
        var settings = await GetSettingsAsync();
        if (!settings.IsConfigured) return new PushSendReport(0, 0, "Powiadomienia push nie są skonfigurowane.");

        await using var db = dbFactory.CreateDbContext();
        var subs = await db.PushSubscriptions.Where(s => s.UserId == userId).ToListAsync();
        var sent = 0;
        string? lastError = null;
        foreach (var sub in subs)
        {
            var error = await SendToSubscriptionAsync(settings, sub, message);
            if (error is null) sent++;
            else lastError = error;
        }
        return new PushSendReport(sent, subs.Count - sent, lastError);
    }

    public async Task SendToAllAsync(PushMessageDto message)
    {
        var settings = await GetSettingsAsync();
        if (!settings.IsConfigured) return;

        await using var db = dbFactory.CreateDbContext();
        var subs = await db.PushSubscriptions.ToListAsync();
        foreach (var sub in subs)
        {
            await SendToSubscriptionAsync(settings, sub, message);
        }
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <returns>null = wysłano; inaczej opis błędu.</returns>
    private async Task<string?> SendToSubscriptionAsync(WebPushSettingsDto settings, PushSubscription sub, PushMessageDto message)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                title = message.Title,
                body = message.Body,
                url = message.Url,
                icon = message.Icon
            });

            var endpoint = new Uri(sub.Endpoint);
            var audience = $"{endpoint.Scheme}://{endpoint.Host}";

            var vapidHeaders = GenerateVapidHeaders(audience, settings.Subject, settings.PublicKey, settings.PrivateKey);
            var encryptedPayload = EncryptPayload(sub.P256dh, sub.Auth, Encoding.UTF8.GetBytes(payload));

            using var request = new HttpRequestMessage(HttpMethod.Post, sub.Endpoint);
            request.Headers.Add("TTL", "86400");
            // Bez „high” Android w trybie Doze potrafi opóźnić powiadomienie o wiele minut.
            request.Headers.Add("Urgency", "high");
            request.Headers.Authorization = new AuthenticationHeaderValue("vapid", $"t={vapidHeaders.Token},k={vapidHeaders.PublicKey}");
            request.Content = new ByteArrayContent(encryptedPayload);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            request.Content.Headers.ContentEncoding.Add("aes128gcm");

            using var response = await Http.SendAsync(request);

            if (response.StatusCode is System.Net.HttpStatusCode.Gone or System.Net.HttpStatusCode.NotFound)
            {
                await using var dbCleanup = dbFactory.CreateDbContext();
                var stale = await dbCleanup.PushSubscriptions.FirstOrDefaultAsync(s => s.Id == sub.Id);
                if (stale is not null)
                {
                    dbCleanup.PushSubscriptions.Remove(stale);
                    await dbCleanup.SaveChangesAsync();
                }
                return "Subskrypcja wygasła na urządzeniu — usunięta. Włącz powiadomienia ponownie.";
            }
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                logger.LogWarning("Push notification failed for {Endpoint}: {Status} {Body}", sub.Endpoint, response.StatusCode, body);
                return $"Serwer powiadomień odrzucił wiadomość ({(int)response.StatusCode}).";
            }
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send push notification to {Endpoint}", sub.Endpoint);
            return "Błąd wysyłki powiadomienia.";
        }
    }

    private static (string Token, string PublicKey) GenerateVapidHeaders(
        string audience, string subject, string publicKey, string privateKey)
    {
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes("{\"typ\":\"JWT\",\"alg\":\"ES256\"}"));
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // Apple odrzuca tokeny ważne zbyt długo; token i tak generujemy przy każdej wysyłce.
        var exp = now + 3600;
        // „sub” musi być mailto: albo https: — inaczej Apple/Mozilla zwracają 403 (BadJwtToken).
        if (string.IsNullOrWhiteSpace(subject)
            || !(subject.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                 || subject.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            subject = "mailto:admin@ptscheduler.app";
        var claimJson = JsonSerializer.Serialize(new { aud = audience, exp, sub = subject });
        var claims = Base64UrlEncode(Encoding.UTF8.GetBytes(claimJson));
        var unsigned = $"{header}.{claims}";
        var dataToSign = Encoding.UTF8.GetBytes(unsigned);

        var privateKeyBytes = Base64UrlDecode(privateKey);
        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = privateKeyBytes,
            Q = DecodeUncompressedPoint(Base64UrlDecode(publicKey))
        });

        var sig = ecdsa.SignData(dataToSign, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var token = $"{unsigned}.{Base64UrlEncode(sig)}";
        return (token, publicKey);
    }

    /// <summary>Szyfrowanie treści powiadomienia (RFC 8291 + aes128gcm, RFC 8188).</summary>
    /// <param name="salt">Tylko do testów — w produkcji losowa.</param>
    internal static byte[] EncryptPayload(string subscriberPublicKeyBase64, string subscriberAuthBase64, byte[] payload, byte[]? salt = null)
    {
        var subscriberPublicKey = Base64UrlDecode(subscriberPublicKeyBase64);
        var subscriberAuth = Base64UrlDecode(subscriberAuthBase64);

        using var localKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var localPublicKeyParams = localKey.ExportParameters(false);
        var localPublicKeyUncompressed = EncodeUncompressedPoint(localPublicKeyParams.Q);

        var subscriberPoint = DecodeUncompressedPoint(subscriberPublicKey);
        using var subscriberEcdh = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = subscriberPoint
        });

        var sharedSecret = localKey.DeriveRawSecretAgreement(subscriberEcdh.PublicKey);

        // IKM = HKDF-Extract(auth, sharedSecret) then HKDF-Expand with "WebPush: info\0" + receiver + sender
        var infoPrefix = Encoding.UTF8.GetBytes("WebPush: info\0");
        var keyInfoBuffer = new byte[infoPrefix.Length + subscriberPublicKey.Length + localPublicKeyUncompressed.Length];
        Buffer.BlockCopy(infoPrefix, 0, keyInfoBuffer, 0, infoPrefix.Length);
        Buffer.BlockCopy(subscriberPublicKey, 0, keyInfoBuffer, infoPrefix.Length, subscriberPublicKey.Length);
        Buffer.BlockCopy(localPublicKeyUncompressed, 0, keyInfoBuffer, infoPrefix.Length + subscriberPublicKey.Length, localPublicKeyUncompressed.Length);

        // RFC 8291 §3.4: salt = auth secret, info = key_info. Argumenty nazwane, bo kolejność
        // (ikm, length, salt, info) łatwo pomylić — wcześniej były zamienione i przeglądarki nie
        // potrafiły odszyfrować żadnego powiadomienia (serwer push przyjmował je bez błędu).
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm: sharedSecret, outputLength: 32,
            salt: subscriberAuth, info: keyInfoBuffer);

        salt ??= RandomNumberGenerator.GetBytes(16);

        // RFC 8188 §2.2–2.3: salt = losowa sól rekordu, info = etykiety CEK / nonce.
        var cekInfo = Encoding.UTF8.GetBytes("Content-Encoding: aes128gcm\0");
        var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm: ikm, outputLength: 16, salt: salt, info: cekInfo);

        var nonceInfo = Encoding.UTF8.GetBytes("Content-Encoding: nonce\0");
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm: ikm, outputLength: 12, salt: salt, info: nonceInfo);

        // Pad payload with delimiter (0x02) — required by RFC 8291
        var paddedPayload = new byte[payload.Length + 1];
        Buffer.BlockCopy(payload, 0, paddedPayload, 0, payload.Length);
        paddedPayload[payload.Length] = 0x02;

        var ciphertext = new byte[paddedPayload.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(cek, 16);
        aes.Encrypt(nonce, paddedPayload, ciphertext, tag);

        // Build aes128gcm record: header(salt[16] + rs[4] + idlen[1] + keyid[65]) + ciphertext + tag
        var recordSize = (uint)(paddedPayload.Length + 16); // +16 for the GCM tag
        var header = new byte[16 + 4 + 1 + localPublicKeyUncompressed.Length];
        Buffer.BlockCopy(salt, 0, header, 0, 16);
        header[16] = (byte)(recordSize >> 24);
        header[17] = (byte)(recordSize >> 16);
        header[18] = (byte)(recordSize >> 8);
        header[19] = (byte)recordSize;
        header[20] = (byte)localPublicKeyUncompressed.Length;
        Buffer.BlockCopy(localPublicKeyUncompressed, 0, header, 21, localPublicKeyUncompressed.Length);

        var result = new byte[header.Length + ciphertext.Length + tag.Length];
        Buffer.BlockCopy(header, 0, result, 0, header.Length);
        Buffer.BlockCopy(ciphertext, 0, result, header.Length, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, header.Length + ciphertext.Length, tag.Length);
        return result;
    }

    private static ECPoint DecodeUncompressedPoint(byte[] data)
    {
        if (data.Length != 65 || data[0] != 0x04)
            throw new ArgumentException("Invalid uncompressed EC point");
        var x = new byte[32];
        var y = new byte[32];
        Buffer.BlockCopy(data, 1, x, 0, 32);
        Buffer.BlockCopy(data, 33, y, 0, 32);
        return new ECPoint { X = x, Y = y };
    }

    private static byte[] EncodeUncompressedPoint(ECPoint q)
    {
        var result = new byte[65];
        result[0] = 0x04;
        Buffer.BlockCopy(q.X!, 0, result, 1, 32);
        Buffer.BlockCopy(q.Y!, 0, result, 33, 32);
        return result;
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }

    public (string PublicKey, string PrivateKey) GenerateVapidKeys()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = ecdsa.ExportParameters(true);
        var publicKeyBytes = EncodeUncompressedPoint(parameters.Q);
        return (Base64UrlEncode(publicKeyBytes), Base64UrlEncode(parameters.D!));
    }
}
