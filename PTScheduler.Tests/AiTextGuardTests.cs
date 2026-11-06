using PTScheduler.Domain.Rules;
using Xunit;

namespace PTScheduler.Tests;

public class AiTextGuardTests
{
    [Theory]
    [InlineData("Świetnie, że [klient] chudnie, nie podałeś imienia :)", "masa ciała −2 kg")]
    [InlineData("Brawo, [imię]! Kolejny trening za Tobą.", "[klient] ma 10 treningów")]
    [InlineData("Super postęp — szkoda, że nie podano wagi.", "przysiad +10 kg")]
    public void Rejects_invented_markers_and_meta_comments(string text, string source) =>
        Assert.False(AiTextGuard.IsClean(text, source));

    [Theory]
    [InlineData("[klient] ma za sobą 10 treningów — brawo! 🎉", "[klient] 10")]
    [InlineData("Masa ciała w dół o 2 kg — świetna robota.", "masa ciała −2 kg")]
    public void Accepts_normal_texts(string text, string source) =>
        Assert.True(AiTextGuard.IsClean(text, source));
}
