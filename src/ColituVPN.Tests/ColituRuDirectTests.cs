using System.IO;
using AwesomeAssertions;
using ServiceLib;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

public class ColituRuDirectTests
{
    [Fact]
    public void RussianDomainsAndIps_GoDirectWhateverTheSettings()
    {
        foreach (var preferences in new[]
                 {
                     new ColituVpnPreferences(),
                     new ColituVpnPreferences(DnsLeakProtectionEnabled: false, AdBlockEnabled: true, ConnectionMode: ColituConnectionModes.Tun),
                 })
        {
            var rules = ColituVpnService.BuildColituRoutingRules(preferences);

            var domain = rules.Single(r => r.Id == "colitu-ru-direct-domain");
            domain.Enabled.Should().BeTrue();
            domain.OutboundTag.Should().Be(Global.DirectTag);
            domain.Domain.Should().Equal(ColituVpnService.RussianDirectDomains);
            domain.Domain.Should().Contain(["geosite:category-ru", "domain:ru", "domain:su", "domain:xn--p1ai", "domain:vk.com", "domain:yandex.net", "domain:tbank.com", "domain:moscow", "domain:tatar"]);

            var ip = rules.Single(r => r.Id == "colitu-ru-direct-ip");
            ip.Enabled.Should().BeTrue();
            ip.OutboundTag.Should().Be(Global.DirectTag);
            ip.Ip.Should().Equal("geoip:ru");

            // DNS protection and ad blocking stay in front of them.
            rules.FindIndex(r => r.Id == "colitu-dns-protection").Should().BeLessThan(rules.IndexOf(domain));
            rules.FindIndex(r => r.Id == "colitu-ad-block").Should().BeLessThan(rules.IndexOf(domain));
        }
    }

    [Theory]
    [InlineData("RU")]
    [InlineData("ru")]
    [InlineData("DE")]
    [InlineData(null)]
    public void RussianSitesAreDirect_ForEveryServerCountry(string? country)
    {
        var rules = ColituVpnService.BuildColituRoutingRules(new ColituVpnPreferences(), country);

        rules.Single(r => r.Id == "colitu-ru-direct-domain").Enabled.Should().BeTrue();
        rules.Single(r => r.Id == "colitu-ru-direct-ip").Enabled.Should().BeTrue();
    }

    [Theory]
    [InlineData("DE")]
    [InlineData("RU")]
    [InlineData(null)]
    public void PrivacyMode_KeepsTheRussianDirectRule_AndSendsEverythingElseThroughTheTunnel(string? country)
    {
        var rules = ColituVpnService.BuildColituRoutingRules(new ColituVpnPreferences(PrivacyModeEnabled: true), country);

        var domain = rules.Single(r => r.Id == "colitu-ru-direct-domain");
        var ip = rules.Single(r => r.Id == "colitu-ru-direct-ip");
        domain.Enabled.Should().BeTrue();
        ip.Enabled.Should().BeTrue();
        // Nothing else may leave outside the tunnel: only the local network (which the server
        // cannot reach) and the Russian destinations.
        rules.Where(r => r.Enabled && r.OutboundTag == Global.DirectTag).Select(r => r.Id)
            .Should().BeEquivalentTo(ColituVpnService.LanDirectRuleId, "colitu-ru-direct-domain", "colitu-ru-direct-ip");
    }

    [Fact]
    public void RussianRules_SitAheadOfTheSplitTunnelRules()
    {
        var preferences = new ColituVpnPreferences(AdvancedMode: true, SplitTunnelMode: ColituSplitTunnelModes.Only,
            SplitTunnelDomains: ["example.com"]);
        var rules = ColituVpnService.BuildColituRoutingRules(preferences, "DE");
        var ru = rules.FindIndex(r => r.Id == "colitu-ru-direct-domain");
        ru.Should().BeGreaterThanOrEqualTo(0);
        rules.FindIndex(r => r.Id == "colitu-ru-direct-ip").Should().BeGreaterThan(ru);
        // Every proxy-bound rule that is not DNS/ad block comes after them.
        foreach (var other in rules.Where(r => r.Enabled && r.OutboundTag == Global.ProxyTag && r.Port != "53"))
        {
            rules.IndexOf(other).Should().BeGreaterThan(ru);
        }
    }

    [Fact]
    public void RussianSitesDirect_IsAlwaysTrue() => ColituVpnService.RussianSitesDirect().Should().BeTrue();

    [Fact]
    public void PrivacyMode_IsOnForNewInstalls_OffForSavedStatesWithoutTheProperty()
    {
        ColituVpnPreferences.ForNewInstall().PrivacyModeEnabled.Should().BeTrue();
        ColituVpnPreferences.ForNewInstall().RuDirectNoticeShown.Should().BeFalse();
    }

    [Fact]
    public void PrivacyMode_IsOffByDefault_AlsoForStatesSavedByOlderBuilds()
    {
        new ColituVpnPreferences().PrivacyModeEnabled.Should().BeFalse();
        new ColituVpnPreferences().RuDirectNoticeShown.Should().BeFalse();

        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var old = System.Text.Json.JsonSerializer.Deserialize<ColituVpnPreferences>("{\"KillSwitchEnabled\":true,\"AdBlockEnabled\":true}", options)!;
        old.PrivacyModeEnabled.Should().BeFalse();
        old.RuDirectNoticeShown.Should().BeFalse();

        var saved = System.Text.Json.JsonSerializer.Serialize(new ColituVpnPreferences(PrivacyModeEnabled: true, RuDirectNoticeShown: true), options);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<ColituVpnPreferences>(saved, options)!;
        loaded.PrivacyModeEnabled.Should().BeTrue();
        loaded.RuDirectNoticeShown.Should().BeTrue();
    }

    [Fact]
    public void RuleSetFilesShipWithTheApp()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "srss-dosyalari");
        File.Exists(Path.Combine(dir, "geosite-category-ru.srs")).Should().BeTrue();
        File.Exists(Path.Combine(dir, "geoip-ru.srs")).Should().BeTrue();
    }

    [Fact]
    public void LocalNetwork_GoesDirectAfterDnsUnlessTheKillSwitchBlocksIt()
    {
        var rules = ColituVpnService.BuildColituRoutingRules(new ColituVpnPreferences(PrivacyModeEnabled: true), "DE", tun: true);
        var lan = rules.Single(r => r.Id == ColituVpnService.LanDirectRuleId);
        lan.Enabled.Should().BeTrue();
        lan.OutboundTag.Should().Be(Global.DirectTag);
        lan.Ip.Should().Equal("geoip:private");
        rules.FindIndex(r => r.Id == "colitu-dns-protection").Should().BeLessThan(rules.IndexOf(lan));

        ColituVpnService.BuildColituRoutingRules(new ColituVpnPreferences(KillSwitchEnabled: false, KillSwitchAllowLan: false))
            .Single(r => r.Id == ColituVpnService.LanDirectRuleId).Enabled.Should().BeTrue();
        ColituVpnService.BuildColituRoutingRules(new ColituVpnPreferences(KillSwitchEnabled: true, KillSwitchAllowLan: false))
            .Single(r => r.Id == ColituVpnService.LanDirectRuleId).Enabled.Should().BeFalse();
    }

    [Fact]
    public void LocalNetwork_IsExcludedFromTheTunRoutesUnlessTheKillSwitchBlocksIt()
    {
        ColituVpnService.LocalNetworkRouteExclusions().Should().Contain(
            ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "169.254.0.0/16", "fe80::/10", "fc00::/7"]);

        ColituVpnService.LocalNetworkBypassed(new ColituVpnPreferences()).Should().BeTrue();
        ColituVpnService.LocalNetworkBypassed(new ColituVpnPreferences(KillSwitchEnabled: true)).Should().BeTrue();
        ColituVpnService.LocalNetworkBypassed(new ColituVpnPreferences(KillSwitchEnabled: false, KillSwitchAllowLan: false)).Should().BeTrue();
        ColituVpnService.LocalNetworkBypassed(new ColituVpnPreferences(KillSwitchEnabled: true, KillSwitchAllowLan: false)).Should().BeFalse();
    }
}
