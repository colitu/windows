using System.Text.Json.Nodes;
using AwesomeAssertions;
using ServiceLib.Common;
using ServiceLib.Enums;
using ServiceLib.Models;
using ServiceLib.Services.CoreConfig;
using Xunit;

namespace ServiceLib.Tests.CoreConfig;

/// <summary>
/// Xray 26.10.10 and sing-box 1.14.3 (2026-10-12): the configs must not use what they refuse or
/// deprecate. Xray refuses "allowInsecure": true (26.6.22), dropped "echForceQuery", and checks a
/// pinned certificate against a name (26.7.11); sing-box deprecates a rule set's download_detour
/// (1.14, removed in 1.16) in favour of http_client.
/// </summary>
public class ColituCore2610ConfigTests
{
    private static JsonNode TlsSettings(Action<ProfileItem> edit, bool defaultInsecure = false)
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.Xray);
        config.CoreBasicItem.DefAllowInsecure = defaultInsecure;
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = ServiceLib.Handler.Fmt.TrojanFmt.Resolve("trojan://secret@203.0.113.7:443?security=tls&sni=vpn.example.test&type=tcp#Colitu", out _)!;
        node.IndexId = "n-trojan";
        node.CoreType = ECoreType.Xray;
        edit(node);
        var result = new CoreConfigV2rayService(CoreConfigTestFactory.CreateContext(config, node, ECoreType.Xray)).GenerateClientConfigContent();
        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        var outbound = JsonNode.Parse(result.Data!.ToString()!)!["outbounds"]!.AsArray().First(o => o?["protocol"]?.GetValue<string>() == "trojan")!;
        return outbound["streamSettings"]!["tlsSettings"]!;
    }

    [Theory]
    [InlineData("1", false)]
    [InlineData("", true)]
    [InlineData("0", true)]
    public void Xray_NeverWritesAllowInsecure(string nodeValue, bool defaultInsecure)
    {
        var tls = TlsSettings(node => node.AllowInsecure = nodeValue, defaultInsecure);
        tls["allowInsecure"].Should().BeNull("Xray 26.6.22+ refuses true and has no switch to skip checks");
        tls["serverName"]!.GetValue<string>().Should().Be("vpn.example.test");
    }

    [Fact]
    public void Xray_NoEchForceQuery()
    {
        var tls = TlsSettings(node => node.EchConfigList = "AEX+DQBB");
        tls["echConfigList"]!.GetValue<string>().Should().Be("AEX+DQBB");
        tls["echForceQuery"].Should().BeNull();
    }

    [Fact]
    public void Xray_PinnedCertificate_AlwaysHasAName()
    {
        var tls = TlsSettings(node =>
        {
            node.Sni = "";
            node.Address = "node.example.test";
            node.CertSha = "c5a0b1a2d3e4f5a6b7c8d9e0f1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0";
        });
        tls["pinnedPeerCertSha256"]!.GetValue<string>().Should().StartWith("c5a0");
        tls["serverName"]!.GetValue<string>().Should().Be("node.example.test");
    }

    [Fact]
    public void SingBox_RemoteRuleSet_UsesHttpClientNotDownloadDetour()
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.sing_box);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateSocksNode(ECoreType.sing_box);
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.sing_box) with
        {
            RoutingItem = new RoutingItem
            {
                Id = "r",
                Remarks = "r",
                // A rule set that is not shipped locally: downloaded through the proxy.
                RuleSet = JsonUtils.Serialize(new List<RulesItem>
                {
                    new() { Enabled = true, RuleType = ERuleType.Routing, OutboundTag = Global.DirectTag, Ip = ["geoip:zz-not-shipped"] },
                }),
                DomainStrategy = Global.AsIs,
                DomainStrategy4Singbox = string.Empty,
            }
        };
        var result = new CoreConfigSingboxService(context).GenerateClientConfigContent();
        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        var sets = JsonNode.Parse(result.Data!.ToString()!)!["route"]?["rule_set"]?.AsArray().OfType<JsonObject>().Where(set => set["type"]?.GetValue<string>() == "remote").ToList() ?? [];
        sets.Should().NotBeEmpty();
        sets.Should().AllSatisfy(set =>
        {
            set["download_detour"].Should().BeNull();
            set["http_client"]!["detour"]!.GetValue<string>().Should().Be(Global.ProxyTag);
        });
    }
}
