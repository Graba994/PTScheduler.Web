using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace PTScheduler.Infrastructure.Services.Payments;

/// <summary>
/// Protokół bramki Autopay (dawniej Blue Media), wersja „płatność przez przekierowanie”:
/// <list type="bullet">
/// <item>start transakcji — parametry w stałej kolejności i Hash = SHA256(wartości|…|klucz),</item>
/// <item>ITN — POST z polem <c>transactions</c> (XML w base64), hash liczony z wartości w kolejności dokumentu,</item>
/// <item>odpowiedź na ITN — XML <c>confirmationList</c> z CONFIRMED / NOTCONFIRMED i własnym hashem.</item>
/// </list>
/// Wartości puste nie wchodzą do hasha (tak jak w oficjalnym SDK Autopay).
/// </summary>
public static class AutopayProtocol
{
    public const string TestGateway = "https://testpay.autopay.eu";
    public const string ProdGateway = "https://pay.autopay.eu";
    public const string Separator = "|";

    public static string Gateway(bool sandbox) => sandbox ? TestGateway : ProdGateway;

    /// <summary>SHA256 z niepustych wartości w podanej kolejności i klucza współdzielonego.</summary>
    public static string Hash(IEnumerable<string?> values, string sharedKey)
    {
        var parts = values.Where(v => !string.IsNullOrEmpty(v)).Append(sharedKey);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(Separator, parts))));
    }

    public static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Opis transakcji: litery (także polskie), cyfry, spacja i . : - , — maks. 79 znaków.</summary>
    public static string SanitizeDescription(string? text)
    {
        var sb = new StringBuilder();
        foreach (var c in text ?? string.Empty)
        {
            if (char.IsLetterOrDigit(c) || c is ' ' or '.' or ':' or '-' or ',') sb.Append(c);
            else if (c is '–' or '—' or '/' or '_' or '×') sb.Append('-');
            else if (char.IsWhiteSpace(c)) sb.Append(' ');
        }
        var clean = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= 79 ? clean : clean[..79].TrimEnd();
    }

    /// <summary>
    /// Adres rozpoczęcia płatności (GET). Kolejność pól = kolejność w hashu:
    /// ServiceID, OrderID, Amount, Description, Currency, CustomerEmail.
    /// Adres powrotu i ITN ustawia się w panelu Autopay dla serwisu.
    /// </summary>
    public static string StartUrl(bool sandbox, string serviceId, string sharedKey, string orderId,
        decimal amount, string? description, string currency, string? customerEmail)
    {
        var fields = new List<(string Name, string Value)>
        {
            ("ServiceID", serviceId),
            ("OrderID", orderId),
            ("Amount", FormatAmount(amount)),
            ("Description", SanitizeDescription(description)),
            ("Currency", currency),
            ("CustomerEmail", customerEmail?.Trim() ?? string.Empty),
        };
        fields.RemoveAll(f => string.IsNullOrEmpty(f.Value));
        var hash = Hash(fields.Select(f => f.Value), sharedKey);

        var sb = new StringBuilder(Gateway(sandbox)).Append("/payment?");
        foreach (var (name, value) in fields)
            sb.Append(name).Append('=').Append(Uri.EscapeDataString(value)).Append('&');
        return sb.Append("Hash=").Append(hash).ToString();
    }

    public sealed record ItnTransaction(string OrderId, string? RemoteId, string? Amount, string? Currency,
        string PaymentStatus, string? PaymentStatusDetails);

    public sealed record Itn(string ServiceId, IReadOnlyList<ItnTransaction> Transactions, bool HashValid);

    /// <summary>Pole <c>transactions</c> z treści formularza (application/x-www-form-urlencoded).</summary>
    public static string? TransactionsFromForm(string formBody)
    {
        foreach (var pair in (formBody ?? string.Empty).Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = pair.IndexOf('=');
            if (i <= 0) continue;
            if (!string.Equals(Uri.UnescapeDataString(pair[..i].Replace('+', ' ')), "transactions", StringComparison.OrdinalIgnoreCase)) continue;
            return Uri.UnescapeDataString(pair[(i + 1)..].Replace('+', ' ')).Replace(' ', '+');
        }
        return null;
    }

    /// <summary>Dekoduje ITN i sprawdza hash; null, gdy to nie jest poprawny XML w base64.</summary>
    public static Itn? ParseItn(string base64, string sharedKey)
    {
        XDocument doc;
        try
        {
            var xml = Encoding.UTF8.GetString(Convert.FromBase64String(base64.Trim()));
            doc = XDocument.Parse(xml);
        }
        catch (Exception ex) when (ex is FormatException or System.Xml.XmlException) { return null; }

        var root = doc.Root;
        if (root is null) return null;

        // Hash: wszystkie wartości liści w kolejności dokumentu (bez samego hasha) + klucz.
        var leaves = root.Descendants()
            .Where(e => !e.HasElements && !string.Equals(e.Name.LocalName, "hash", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Value);
        var incoming = root.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("hash", StringComparison.OrdinalIgnoreCase))?.Value;
        var valid = !string.IsNullOrEmpty(incoming)
                    && CryptographicOperations.FixedTimeEquals(
                        Encoding.ASCII.GetBytes(Hash(leaves, sharedKey)),
                        Encoding.ASCII.GetBytes(incoming.Trim().ToLowerInvariant()));

        string? Child(XElement e, string name) =>
            e.Elements().FirstOrDefault(x => x.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

        var transactions = root.Descendants()
            .Where(e => e.Name.LocalName.Equals("transaction", StringComparison.OrdinalIgnoreCase))
            .Select(t => new ItnTransaction(
                Child(t, "orderID") ?? string.Empty,
                Child(t, "remoteID"),
                Child(t, "amount"),
                Child(t, "currency"),
                Child(t, "paymentStatus") ?? string.Empty,
                Child(t, "paymentStatusDetails")))
            .Where(t => t.OrderId.Length > 0)
            .ToList();

        return new Itn(Child(root, "serviceID") ?? string.Empty, transactions, valid);
    }

    /// <summary>Odpowiedź na ITN, której Autopay oczekuje (HTTP 200, XML).</summary>
    public static string ConfirmationXml(string serviceId, IEnumerable<(string OrderId, bool Confirmed)> items, string sharedKey)
    {
        var list = items.ToList();
        var hashValues = new List<string?> { serviceId };
        foreach (var (orderId, confirmed) in list)
        {
            hashValues.Add(orderId);
            hashValues.Add(confirmed ? "CONFIRMED" : "NOTCONFIRMED");
        }

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement("confirmationList",
                new XElement("serviceID", serviceId),
                new XElement("transactionsConfirmations",
                    list.Select(i => new XElement("transactionConfirmed",
                        new XElement("orderID", i.OrderId),
                        new XElement("confirmation", i.Confirmed ? "CONFIRMED" : "NOTCONFIRMED")))),
                new XElement("hash", Hash(hashValues, sharedKey))));
        return doc.Declaration + Environment.NewLine + doc.Root;
    }

    /// <summary>Numer zamówienia z powrotu na stronę sklepu — tylko znaki, które sami nadajemy.</summary>
    public static string? SafeOrderId(string? orderId) =>
        !string.IsNullOrEmpty(orderId) && orderId.Length <= 64 && orderId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? orderId
            : null;
}
