using FluentAssertions;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Nowsze NPM (2.12+) odrzucają stare pola „meta” — Portal rozpoznaje to i ponawia z nowym formatem.</summary>
public class NpmApiTests
{
    [Theory]
    [InlineData("400: {\"error\":{\"code\":400,\"message\":\"data/meta must NOT have additional properties, data/meta must NOT have additional properties\"}}", true)]
    [InlineData("400: {\"error\":{\"message\":\"Some challenges have failed.\"}}", false)]
    [InlineData("500: Internal Error", false)]
    public void Recognizes_schema_rejection(string body, bool expected) =>
        NpmService.IsSchemaRejection(body).Should().Be(expected);
}
