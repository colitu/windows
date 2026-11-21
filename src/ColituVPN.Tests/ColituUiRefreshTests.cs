using AwesomeAssertions;
using v2rayN.Services;
using v2rayN.Views;
using Xunit;

namespace v2rayN.Tests;

/// <summary>Logic behind the 3.x interface refresh: leak test verdicts, the speed meter, device cleanup, strings.</summary>
public class ColituUiRefreshTests
{
    [Fact]
    public void Trace_GivesIpAndCountry()
    {
        var exit = ColituConnectionCheck.FromTrace("fl=1\nh=www.cloudflare.com\nip=203.0.113.24\nts=1.2\nloc=de\ntls=TLSv1.3\n");
        exit.Should().Be(new ColituExitInfo("203.0.113.24", "DE"));
        ColituConnectionCheck.FromTrace("ip=2001:db8::1\nloc=XX").Should().Be(new ColituExitInfo("2001:db8::1", null));
        ColituConnectionCheck.FromTrace("ip=not-an-ip\nloc=DE").Should().BeNull();
        ColituConnectionCheck.FromTrace("").Should().BeNull();
    }

    [Theory]
    [InlineData("DE", "DE", ColituLeakState.Ok)]
    [InlineData("de", "DE", ColituLeakState.Ok)]
    [InlineData("TR", "DE", ColituLeakState.Warn)]
    [InlineData("DE", null, ColituLeakState.Ok)]
    public void Ip_IsFine_InTheServersCountry(string exitCountry, string? serverCountry, ColituLeakState expected)
    {
        ColituConnectionCheck.JudgeIp(new ColituExitInfo("203.0.113.24", exitCountry.ToUpperInvariant()), serverCountry).Should().Be(expected);
        ColituConnectionCheck.JudgeIp(null, serverCountry).Should().Be(ColituLeakState.Unknown);
    }

    [Fact]
    public void Dns_IsFine_NextToTheExitOrInItsCountry()
    {
        var exit = new ColituExitInfo("203.0.113.24", "DE");
        ColituConnectionCheck.JudgeDns(exit, "203.0.200.1", null).Should().Be(ColituLeakState.Ok, "same /16 as the exit");
        ColituConnectionCheck.JudgeDns(exit, "198.51.100.7", "DE").Should().Be(ColituLeakState.Ok);
        ColituConnectionCheck.JudgeDns(exit, "198.51.100.7", "TR").Should().Be(ColituLeakState.Warn, "a resolver in another country (home ISP) sees the lookups");
        ColituConnectionCheck.JudgeDns(exit, "198.51.100.7", null).Should().Be(ColituLeakState.Unknown);
        ColituConnectionCheck.JudgeDns(exit, null, null).Should().Be(ColituLeakState.Unknown);
        ColituConnectionCheck.JudgeDns(null, "198.51.100.7", "DE").Should().Be(ColituLeakState.Unknown);
    }

    [Fact]
    public void Meter_KeepsTheLastMinute_AndRatesNeverGoNegative()
    {
        var meter = new ColituTrafficMeter();
        for (var i = 0; i < 75; i++)
        {
            meter.Add(new ColituSpeedSample(i, i * 2));
        }
        meter.Samples.Should().HaveCount(ColituTrafficMeter.Capacity);
        meter.Latest.Should().Be(new ColituSpeedSample(74, 148));
        ColituTrafficMeter.Rate(2048, 1024, 1).Should().Be(1024);
        ColituTrafficMeter.Rate(10, 5000, 1).Should().Be(0, "a counter reset is not negative traffic");
        ColituTrafficMeter.Format(0, "en", System.Globalization.CultureInfo.InvariantCulture).Should().Be("0 B/s");
        ColituTrafficMeter.Format(1536, "en", System.Globalization.CultureInfo.InvariantCulture).Should().Be("1.5 KB/s");
        ColituTrafficMeter.Format(250 * 1024 * 1024, "ru", System.Globalization.CultureInfo.InvariantCulture).Should().Be("250 МБ/с");
    }

    [Fact]
    public void Cleanup_TakesOnlyDevicesOlderThan30Days_NeverThisOne()
    {
        var now = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        var devices = new (string, bool, DateTimeOffset?)[]
        {
            ("this", true, now.AddDays(-90)),
            ("old", false, now.AddDays(-31)),
            ("recent", false, now.AddDays(-29)),
            ("never", false, null),
            ("", false, now.AddDays(-100))
        };
        ColituAccountApi.StaleDeviceIds(devices, 30, now).Should().Equal("old");
    }

    [Fact]
    public void NewStrings_ExistInEveryLanguage()
    {
        foreach (var key in Loc.Keys.Where(key => key.StartsWith("conn.", StringComparison.Ordinal) || key.StartsWith("usage.", StringComparison.Ordinal)
            || key is "locations.pingAll" or "locations.pingRow" or "account.cleanup" or "account.rename" or "support.typing" or "support.read" or "tray.favorites"))
        {
            var values = Loc.ValuesOf(key);
            values.Should().HaveCount(Loc.Languages.Length, key);
            values.Should().OnlyContain(value => value.Length > 0, key);
        }
        Loc.Has("conn.leak.dnsWarn").Should().BeTrue();
    }

    [Fact]
    public void ServerRows_HaveAPingButton_AndTheWhitelistRowShowsItsPing()
    {
        // Rows build "pack://" flag URIs: register the scheme even when this test runs first.
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        var berlin = new ColituVpnServer { Id = "de", Name = "Berlin", CountryCode = "DE", City = "Berlin", Available = true, WhitelistOk = true, Protocols = ["vless-ws"] };
        ColituServerRow.From(berlin, 42).PingButtonVisibility.Should().Be(System.Windows.Visibility.Visible);
        ColituServerRow.Auto("x").PingButtonVisibility.Should().Be(System.Windows.Visibility.Collapsed);
        ColituServerRow.Header("h").PingButtonVisibility.Should().Be(System.Windows.Visibility.Collapsed);
        var whitelist = ColituServerRow.WhitelistWithPing(berlin, 57);
        whitelist.PingText.Should().Be("57 ms");
        whitelist.BarsVisibility.Should().Be(System.Windows.Visibility.Visible);
    }
}
