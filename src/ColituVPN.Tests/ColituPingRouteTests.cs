using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using ServiceLib;
using ServiceLib.Common;
using ServiceLib.Enums;
using ServiceLib.Handler.Fmt;
using ServiceLib.Models;
using ServiceLib.Services.CoreConfig;
using ServiceLib.Tests.CoreConfig;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

/// <summary>
/// Location pings in TUN mode: the sing-box config that runs the TUN adapter (plain sing-box and
/// the TUN front of an Xray tunnel) sends this app's TCP probes to the probe addresses direct,
/// before any ip/domain/rule-set rule. The rule names the exe by full path (not by file name) and
/// only the resolved probe addresses, so another program, or a copy of this one, stays in the tunnel.
/// </summary>
public class ColituPingRouteTests
{
    private const string App = @"C:\Program Files\Colitu VPN\ColituVPN.exe";
    private static readonly IPAddress[] Targets = [IPAddress.Parse("203.0.113.7"), IPAddress.Parse("198.51.100.20")];

    private static CoreConfigContext TunContext(ProfileItem node)
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.sing_box);
        config.RoutingBasicItem.DomainStrategy = Global.AsIs;
        config.TunModeItem = new TunModeItem { EnableTun = true, AutoRoute = true, Stack = "gvisor", Mtu = 9000, IcmpRouting = "rule" };
        config.HysteriaItem = new HysteriaItem { UpMbps = 0, DownMbps = 0 };
        CoreConfigTestFactory.BindAppManagerConfig(config);
        return CoreConfigTestFactory.CreateContext(config, node, ECoreType.sing_box) with
        {
            IsTunEnabled = true,
            RoutingItem = new RoutingItem
            {
                Id = "colitu",
                Remarks = "Colitu",
                RuleSet = JsonUtils.Serialize(new List<RulesItem>
                {
                    new() { Enabled = true, RuleType = ERuleType.Routing, OutboundTag = Global.DirectTag, Domain = ["geosite:category-ru"] },
                    new() { Enabled = true, RuleType = ERuleType.Routing, OutboundTag = Global.DirectTag, Ip = ["geoip:ru"] },
                }),
                DomainStrategy = Global.AsIs,
                DomainStrategy4Singbox = string.Empty,
            }
        };
    }

    /// <summary>sing-box alone runs the tunnel (Hysteria2 to a documentation address).</summary>
    private static string SingboxTunConfig()
    {
        var node = Hysteria2Fmt.Resolve("hysteria2://secret@203.0.113.7:8443?sni=vpn.example.test&insecure=0#Colitu", out _)!;
        node.IndexId = "n-hy2";
        node.CoreType = ECoreType.sing_box;
        var result = new CoreConfigSingboxService(TunContext(node)).GenerateClientConfigContent();
        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        return result.Data!.ToString()!;
    }

    /// <summary>The sing-box TUN front of an Xray tunnel: TUN in, Xray's local SOCKS port out (ConfigHandler.GetPreSocksItem).</summary>
    private static string XrayTunFrontConfig()
    {
        var front = new ProfileItem
        {
            IndexId = "pre-socks",
            CoreType = ECoreType.sing_box,
            ConfigType = EConfigType.SOCKS,
            Address = "127.0.0.1",
            Port = 10808,
            Network = nameof(ETransport.raw),
            StreamSecurity = string.Empty,
            Subid = string.Empty,
        };
        var result = new CoreConfigSingboxService(TunContext(front)).GenerateClientConfigContent();
        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        return result.Data!.ToString()!;
    }

    private static void AssertPingRuleFirst(string config)
    {
        var routed = ColituPingRoute.ApplySingbox(config, App, Targets)!;
        routed.Should().NotBeNull();
        var root = JsonNode.Parse(routed)!.AsObject();

        root["inbounds"]!.AsArray().Should().Contain(inbound => inbound!["type"]!.GetValue<string>() == "tun");
        root["outbounds"]!.AsArray().Should().Contain(outbound => outbound!["tag"]!.GetValue<string>() == "direct" && outbound["type"]!.GetValue<string>() == "direct");

        var rules = root["route"]!["rules"]!.AsArray().Select(rule => rule!.AsObject()).ToList();
        var first = rules[0];
        ColituPingRoute.IsPingRule(first, App).Should().BeTrue("the ping rule is the first route rule");
        first["process_name"].Should().BeNull("a file name alone matches any program called that");
        first["process_path_regex"]!.AsArray().Select(name => name!.GetValue<string>()).Should().Equal(ColituPingRoute.PathRegex(App));
        first["ip_cidr"]!.AsArray().Select(cidr => cidr!.GetValue<string>()).Should().BeEquivalentTo(["203.0.113.7/32", "198.51.100.20/32"]);
        first["network"]!.AsArray().Select(name => name!.GetValue<string>()).Should().Equal("tcp");
        first["port"]!.AsArray().Select(port => port!.GetValue<int>()).Should()
            .BeEquivalentTo([443, 2053, 2083, 2087, 2096, 3443, 4433, 7443, 8388, 8443, 9443]);
        first["outbound"]!.GetValue<string>().Should().Be("direct");

        // Before every other ip / domain / rule-set rule and before sniffing.
        var firstOther = rules.FindIndex(rule => !ColituPingRoute.IsPingRule(rule, App) && (rule["ip_cidr"] != null || rule["domain"] != null
            || rule["domain_suffix"] != null || rule["rule_set"] != null || rule["action"]?.GetValue<string>() == "sniff"));
        firstOther.Should().BeGreaterThan(0);
        rules.Count(rule => ColituPingRoute.IsPingRule(rule, App)).Should().Be(1);

        // A second pass changes nothing.
        ColituPingRoute.ApplySingbox(routed, App, Targets).Should().Be(routed);
    }

    [Fact]
    public void SingboxTun_PingRuleFirst_DirectExists()
    {
        var config = SingboxTunConfig();
        // The generated config has the ip/domain rules the ping rule must come before.
        config.Should().Contain("geoip-ru");
        AssertPingRuleFirst(config);
    }

    [Fact]
    public void XrayTunFront_PingRuleFirst_DirectExists()
    {
        AssertPingRuleFirst(XrayTunFrontConfig());
    }

    [Fact]
    public void Rule_CoversExactlyTheProbePorts()
    {
        var rule = ColituPingRoute.NewRule(App, Targets)!;
        rule["port"]!.AsArray().Select(port => port!.GetValue<int>()).Should().BeEquivalentTo(ColituLatency.AllowedPorts);
        rule["port"]!.AsArray().Select(port => port!.GetValue<int>()).Should().NotContain(53, "DNS keeps its hijack rule");
    }

    [Fact]
    public void NoTrustedPathOrTarget_NoRule()
    {
        var config = SingboxTunConfig();
        ColituPingRoute.ApplySingbox(config, null, Targets).Should().Be(config, "without an admin-only exe path the rule could match another program");
        ColituPingRoute.ApplySingbox(config, App, []).Should().Be(config, "without a target the rule would cover every destination");
        ColituPingRoute.NewRule("ColituVPN.exe", Targets).Should().BeNull("a bare file name is not a path");
    }

    [Fact]
    public void Targets_OnlyPublicIPv4_Distinct()
    {
        ColituPingRoute.Targets([
            IPAddress.Parse("203.0.113.7"), IPAddress.Parse("203.0.113.7"), IPAddress.Parse("10.0.0.1"),
            IPAddress.Parse("127.0.0.1"), IPAddress.Parse("100.64.1.1"), IPAddress.Parse("2001:db8::1"),
            IPAddress.Parse("::ffff:198.51.100.20")
        ]).Select(address => address.ToString()).Should().Equal("203.0.113.7", "198.51.100.20");
    }

    [Theory]
    [InlineData(@"C:\Program Files\Colitu VPN\ColituVPN.exe")]
    [InlineData(@"D:\Uygulamalar (x86)\Çolitu+[1]\ColituVPN.exe")]
    public void PathRegex_MatchesOnlyThatPath_IgnoringCase(string path)
    {
        // .NET reads the escapes used here (\xHH, \<punctuation>, (?i)) the same way as RE2. Culture-
        // invariant like RE2's Unicode case folding (a Turkish test machine folds I to dotless ı).
        var pattern = ColituPingRoute.PathRegex(path);
        static bool Matches(string input, string re) => Regex.IsMatch(input, re, RegexOptions.CultureInvariant);
        Matches(path, pattern).Should().BeTrue();
        Matches(path.ToUpperInvariant(), pattern).Should().BeTrue();
        Matches(path + ".bak", pattern).Should().BeFalse();
        Matches("X" + path, pattern).Should().BeFalse();
        Matches(@"C:\Users\x\AppData\Local\Temp\ColituVPN.exe", pattern).Should().BeFalse("a copy elsewhere must not match");
    }

    [Fact]
    public void TrustedAppPath_UserWritableFolder_IsNotTrusted()
    {
        // The test runner lives in a folder its user can write: no rule is written for it.
        ColituPingRoute.TrustedAppPath().Should().BeNull();
    }

    [Fact]
    public void NoTunInbound_ConfigUnchanged()
    {
        const string proxyMode = """
        {
          "inbounds": [ { "type": "mixed", "tag": "socks", "listen": "127.0.0.1", "listen_port": 10808 } ],
          "outbounds": [ { "type": "socks", "tag": "proxy", "server": "127.0.0.1", "server_port": 1080 }, { "type": "direct", "tag": "direct" } ],
          "route": { "rules": [ { "ip_cidr": [ "198.51.100.0/24" ], "outbound": "direct" } ], "final": "proxy" }
        }
        """;
        ColituPingRoute.ApplySingbox(proxyMode, App, Targets).Should().Be(proxyMode);
    }

    [Fact]
    public void MissingDirectOutbound_IsAdded()
    {
        const string tunOnly = """
        {
          "inbounds": [ { "type": "tun", "tag": "tun-in" } ],
          "outbounds": [ { "type": "socks", "tag": "proxy", "server": "127.0.0.1", "server_port": 10808 } ],
          "route": { "rules": [ { "ip_cidr": [ "198.51.100.0/24" ], "outbound": "proxy" } ], "final": "proxy" }
        }
        """;
        var root = JsonNode.Parse(ColituPingRoute.ApplySingbox(tunOnly, App, Targets)!)!;
        root["outbounds"]!.AsArray().Should().Contain(outbound => outbound!["tag"]!.GetValue<string>() == "direct" && outbound["type"]!.GetValue<string>() == "direct");
        ColituPingRoute.IsPingRule(root["route"]!["rules"]![0]!.AsObject(), App).Should().BeTrue();
    }

    [Fact]
    public void Unreadable_ReturnsNull()
    {
        ColituPingRoute.ApplySingbox("{ not json", App, Targets).Should().BeNull();
    }

    [Fact]
    public async Task RequiredPin_WithoutAdapter_IsNotDialled()
    {
        var (ms, unpinned) = await ColituLatency.MeasureAsync("node.example.test", 443, interfaceIndex: null, requirePin: true);
        ms.Should().BeNull();
        unpinned.Should().BeTrue("a probe that cannot be pinned while connected would measure the tunnel");
    }
}
