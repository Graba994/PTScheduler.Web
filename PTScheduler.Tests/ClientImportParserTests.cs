using FluentAssertions;
using PTScheduler.Application.Import;
using Xunit;

namespace PTScheduler.Tests;

public class ClientImportParserTests
{
    [Fact]
    public void Comma_Csv_With_Full_Name_Column_Is_Mapped_And_Split()
    {
        var t = ClientImportParser.Parse("Klient,Mail,Nr telefonu\nanna nowak,Anna@x.pl,600 100 200\n\"Kowalski, Jan\",jan@x.pl,");
        var map = ClientImportParser.GuessMapping(t);
        map.Values.Should().Contain([ImportField.FullName, ImportField.Email, ImportField.Phone]);
        var c = ClientImportParser.Build(t, map);
        c[0].FirstName.Should().Be("Anna"); c[0].LastName.Should().Be("Nowak"); c[0].Email.Should().Be("anna@x.pl"); c[0].Problem.Should().BeNull();
        c[1].FirstName.Should().Be("Jan"); c[1].LastName.Should().Be("Kowalski"); c[1].Phone.Should().BeNull();
    }

    [Fact]
    public void Pasted_Excel_Without_Headers_Is_Detected_By_Content()
    {
        var t = ClientImportParser.Parse("Ola\tZielińska\tola@x.pl\t+48 501 222 333\nPiotr\tWiśniewski\tpiotr@x.pl\t502333444");
        t.Source.Should().Be("excel");
        var map = ClientImportParser.GuessMapping(t);
        map[2].Should().Be(ImportField.Email);
        map[3].Should().Be(ImportField.Phone);
        map[0].Should().Be(ImportField.FirstName);
        map[1].Should().Be(ImportField.LastName);
        ClientImportParser.Build(t, map)[1].Should().Match<ImportCandidate>(c => c.FirstName == "Piotr" && c.LastName == "Wiśniewski" && c.Problem == null);
    }

    [Fact]
    public void Vcf_Contacts_From_Phone()
    {
        var vcf = "BEGIN:VCARD\r\nVERSION:3.0\r\nN:Nowak;Marek;;;\r\nFN:Marek Nowak\r\nTEL;TYPE=CELL:+48600700800\r\nEMAIL;TYPE=INTERNET:marek@x.pl\r\nEND:VCARD\r\nBEGIN:VCARD\r\nFN:Ewa Lis\r\nEND:VCARD";
        var t = ClientImportParser.Parse(vcf);
        var c = ClientImportParser.Build(t, ClientImportParser.GuessMapping(t));
        c.Should().HaveCount(2);
        c[0].Should().Match<ImportCandidate>(x => x.FirstName == "Marek" && x.LastName == "Nowak" && x.Phone == "+48600700800" && x.Problem == null);
        c[1].FirstName.Should().Be("Ewa");
        c[1].Problem.Should().Contain("e-mail");
    }

    [Fact]
    public void Semicolon_Csv_With_Polish_Headers_And_Date()
    {
        var t = ClientImportParser.Parse("Imię;Nazwisko;Email;Data urodzenia\nJAN;KOWALSKI;jan@x.pl;12.03.1990");
        var c = ClientImportParser.Build(t, ClientImportParser.GuessMapping(t));
        c[0].FirstName.Should().Be("Jan");
        c[0].LastName.Should().Be("Kowalski");
        c[0].BirthDate.Should().Be(new DateOnly(1990, 3, 12));
    }
}
