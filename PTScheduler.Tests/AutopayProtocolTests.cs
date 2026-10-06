using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PTScheduler.Infrastructure.Services.Payments;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Protokół Autopay — wektory z oficjalnego SDK Autopay (bm-sdk, fixtures ITN).</summary>
public class AutopayProtocolTests
{
    private const string Key = "QCBm3N0oFjzQAWsTIVN8mPLK12TW6HU6InSfjvnF";

    private const string ItnXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <transactionList>
            <serviceID>123456</serviceID>
            <transactions>
                <transaction>
                    <orderID>123</orderID>
                    <remoteID>102686</remoteID>
                    <amount>500.00</amount>
                    <currency>PLN</currency>
                    <gatewayID>106</gatewayID>
                    <paymentDate>20200621171238</paymentDate>
                    <paymentStatus>SUCCESS</paymentStatus>
                    <paymentStatusDetails>ACCEPTED</paymentStatusDetails>
                </transaction>
            </transactions>
            <hash>422fbd7c1bbac1eb745b647980dd63c2e8ccf261ea196739ab8043eff8dd4e17</hash>
        </transactionList>
        """;

    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    [Fact]
    public void Itn_From_Sdk_Fixture_Has_Valid_Hash()
    {
        var itn = AutopayProtocol.ParseItn(B64(ItnXml), Key);

        itn.Should().NotBeNull();
        itn!.HashValid.Should().BeTrue();
        itn.ServiceId.Should().Be("123456");
        itn.Transactions.Should().ContainSingle();
        itn.Transactions[0].OrderId.Should().Be("123");
        itn.Transactions[0].PaymentStatus.Should().Be("SUCCESS");
    }

    [Fact]
    public void Tampered_Itn_Is_Rejected()
    {
        var itn = AutopayProtocol.ParseItn(B64(ItnXml.Replace("500.00", "5.00")), Key);
        itn!.HashValid.Should().BeFalse();
        AutopayProtocol.ParseItn("to-nie-jest-base64!", Key).Should().BeNull();
    }

    [Fact]
    public void Confirmation_Matches_Sdk_Fixture_Hash()
    {
        var xml = AutopayProtocol.ConfirmationXml("123456", [("123", true)], Key);

        xml.Should().Contain("<confirmation>CONFIRMED</confirmation>")
           .And.Contain("<hash>7194a50431ce359cc4c201a0f154400f89f49f765b8eeb60299fc6fee1a01228</hash>");
    }

    [Fact]
    public void Start_Url_Hashes_Fields_In_Protocol_Order()
    {
        var url = AutopayProtocol.StartUrl(true, "1000", "klucz", "abc123", 150m, "Pakiet 10×Trening / 60 min!", "PLN", "anna@example.com");

        url.Should().StartWith("https://testpay.autopay.eu/payment?ServiceID=1000&OrderID=abc123&Amount=150.00&Description=");
        var expected = AutopayProtocol.Hash(["1000", "abc123", "150.00", "Pakiet 10-Trening - 60 min", "PLN", "anna@example.com"], "klucz");
        url.Should().EndWith("&Hash=" + expected);
        AutopayProtocol.SanitizeDescription(new string('a', 120)).Should().HaveLength(79);
    }

    [Fact]
    public async Task Provider_Confirms_Valid_Itn_And_Refuses_Wrong_Service()
    {
        var provider = new AutoPayProvider(NullLogger<AutoPayProvider>.Instance);
        var cfg = new ProviderRuntimeConfig { Fields = new Dictionary<string, string> { ["ServiceId"] = "123456", ["SharedKey"] = Key } };
        var body = "transactions=" + Uri.EscapeDataString(B64(ItnXml));

        var ok = await provider.HandleNotifyAsync(body, new Dictionary<string, string>(), cfg);
        ok.Valid.Should().BeTrue();
        ok.ExtOrderId.Should().Be("123");
        ok.Outcome.Should().Be(PaymentOutcome.Paid);
        ok.Respond!(true).Should().Contain("CONFIRMED").And.NotContain("NOTCONFIRMED");
        ok.Respond!(false).Should().Contain("NOTCONFIRMED");

        var other = new ProviderRuntimeConfig { Fields = new Dictionary<string, string> { ["ServiceId"] = "999", ["SharedKey"] = Key } };
        var refused = await provider.HandleNotifyAsync(body, new Dictionary<string, string>(), other);
        refused.Valid.Should().BeFalse();
        refused.Respond!(false).Should().Contain("NOTCONFIRMED");
    }

    [Theory]
    [InlineData("0f3a9c", "0f3a9c")]
    [InlineData("../../evil", null)]
    [InlineData("https://evil.example", null)]
    public void Return_Order_Id_Is_Sanitized(string input, string? expected) =>
        AutopayProtocol.SafeOrderId(input).Should().Be(expected);
}
