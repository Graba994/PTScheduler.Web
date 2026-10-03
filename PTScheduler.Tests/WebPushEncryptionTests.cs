using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using PTScheduler.Infrastructure.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>
/// Szyfrowanie treści Web Push musi dać się odszyfrować tak, jak robi to przeglądarka
/// (RFC 8291 + RFC 8188, aes128gcm). Deszyfrowanie poniżej jest napisane niezależnie,
/// wprost z RFC — gdyby parametry HKDF znów się pomyliły, test nie przejdzie.
/// </summary>
public class WebPushEncryptionTests
{
    [Fact]
    public void Payload_Decrypts_Like_A_Browser_Would()
    {
        // „Przeglądarka”: para kluczy P-256 i sekret auth z subskrypcji.
        using var receiver = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var receiverPublic = Uncompressed(receiver.ExportParameters(false).Q);
        var auth = RandomNumberGenerator.GetBytes(16);
        var plaintext = Encoding.UTF8.GetBytes("{\"title\":\"Zostały Ci 2 treningi\",\"body\":\"Zażółć gęślą jaźń 💪\"}");

        var record = WebPushService.EncryptPayload(B64Url(receiverPublic), B64Url(auth), plaintext);

        Decrypt(record, receiver, receiverPublic, auth).Should().Equal(plaintext);
    }

    private static byte[] Decrypt(byte[] record, ECDiffieHellman receiver, byte[] receiverPublic, byte[] auth)
    {
        // Nagłówek aes128gcm: salt(16) | rs(4) | idlen(1) | keyid(idlen = klucz nadawcy).
        var salt = record[..16];
        var idLen = record[20];
        var senderPublic = record[21..(21 + idLen)];
        var body = record[(21 + idLen)..];

        using var sender = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = senderPublic[1..33], Y = senderPublic[33..65] }
        });
        var ecdhSecret = receiver.DeriveRawSecretAgreement(sender.PublicKey);

        // RFC 8291 §3.4
        var keyInfo = Encoding.ASCII.GetBytes("WebPush: info\0").Concat(receiverPublic).Concat(senderPublic).ToArray();
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm: ecdhSecret, outputLength: 32, salt: auth, info: keyInfo);

        // RFC 8188 §2.2–2.3
        var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm: ikm, outputLength: 16, salt: salt,
            info: Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm: ikm, outputLength: 12, salt: salt,
            info: Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        var ciphertext = body[..^16];
        var tag = body[^16..];
        var padded = new byte[ciphertext.Length];
        using var aes = new AesGcm(cek, 16);
        aes.Decrypt(nonce, ciphertext, tag, padded);

        // Ostatni rekord kończy się ogranicznikiem 0x02 (ew. z zerami za nim).
        var end = padded.Length - 1;
        while (end >= 0 && padded[end] == 0) end--;
        padded[end].Should().Be(0x02);
        return padded[..end];
    }

    private static byte[] Uncompressed(ECPoint q) => new byte[] { 0x04 }.Concat(q.X!).Concat(q.Y!).ToArray();

    private static string B64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
