using AwesomeAssertions;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

public class ColituTransportChoiceTests
{
    [Theory]
    [InlineData("vless-reality", "vless-reality")]
    [InlineData(" VLESS-Reality ", "vless-reality")]
    [InlineData("hysteria2", "hysteria2")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("auto", "")]
    [InlineData("wireguard", "")]
    public void Normalize_KeepsOnlyKnownMethods(string? input, string expected)
    {
        ColituTransportChoice.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void PutFirst_MovesThePickedMethodToTheFront_AndKeepsTheRestAsFallback()
    {
        var order = new List<string> { "hysteria2", "vless-reality", "vless-xhttp", "trojan" };

        ColituTransportChoice.PutFirst(order, item => item, "vless-reality")
            .Should().Equal("vless-reality", "hysteria2", "vless-xhttp", "trojan");
    }

    [Theory]
    [InlineData("")]
    [InlineData("shadowsocks")]
    public void PutFirst_KeepsTheAutomaticOrder_WhenNothingOrAnUnofferedMethodIsPicked(string picked)
    {
        var order = new List<string> { "hysteria2", "vless-reality" };

        ColituTransportChoice.PutFirst(order, item => item, picked).Should().Equal("hysteria2", "vless-reality");
    }

    [Fact]
    public void Preferences_DropAnUnknownPickedMethod()
    {
        new ColituVpnPreferences(PreferredTransport: "bogus").Normalize().PreferredTransport.Should().BeEmpty();
        new ColituVpnPreferences(PreferredTransport: "vless-xhttp").Normalize().PreferredTransport.Should().Be("vless-xhttp");
    }
}
