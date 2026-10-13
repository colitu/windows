using System.Text.Json.Nodes;
using AwesomeAssertions;
using ServiceLib.Common;
using ServiceLib.Enums;
using ServiceLib.Models;
using ServiceLib.Services.CoreConfig;
using Xunit;

namespace ServiceLib.Tests.CoreConfig;

/// <summary>
/// The panel adds mport=20000-40000 to hysteria2:// links of nodes that support port hopping
/// (Russian mobile networks throttle a long-lived UDP flow on one port). Both cores must hop:
/// sing-box with server_ports + hop_interval, Xray 26.9.8+ with the "udphop" UDP mask in
/// finalmask.udp (the older finalmask.quicParams.udpHop and hysteriaSettings.udphop shapes are
/// silently ignored). Without mport nothing changes.
/// </summary>
public class ColituHysteria2HopConfigTests
{
    private const string HopLink = "hysteria2://secret@203.0.113.7:8443?sni=vpn.example.com&insecure=0&mport=20000-40000#Colitu";
    private const string PlainLink = "hysteria2://secret@203.0.113.7:8443?sni=vpn.example.com&insecure=0#Colitu";

    private static CoreConfigContext Context(ECoreType core, string link)
    {
        var config = CoreConfigTestFactory.CreateConfig(core);
        // As ColituVpnService sets them: no declared bandwidth (BBR), 30 s between hops.
        config.HysteriaItem = new HysteriaItem { UpMbps = 0, DownMbps = 0, HopInterval = 30 };
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = ServiceLib.Handler.Fmt.Hysteria2Fmt.Resolve(link, out _)!;
        node.IndexId = "n-hy2";
        node.CoreType = core;
        return CoreConfigTestFactory.CreateContext(config, node, core);
    }

    [Fact]
    public void ShareLink_WithMport_KeepsThePortRange()
    {
        var node = ServiceLib.Handler.Fmt.Hysteria2Fmt.Resolve(HopLink, out _)!;

        node.Port.Should().Be(8443);
        node.GetProtocolExtra().Ports.Should().Be("20000-40000");
    }

    [Fact]
    public void SingBox_WithMport_HopsOverTheRangeEvery30Seconds()
    {
        var result = new CoreConfigSingboxService(Context(ECoreType.sing_box, HopLink)).GenerateClientConfigContent();

        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        var cfg = JsonUtils.Deserialize<SingboxConfig>(result.Data!.ToString()!)!;
        var hysteria = cfg.outbounds.Single(o => o.type == "hysteria2");
        hysteria.server_ports.Should().Equal("20000:40000");
        hysteria.hop_interval.Should().Be("30s");
        // server_port and server_ports conflict in sing-box.
        hysteria.server_port.Should().BeNull();
    }

    [Fact]
    public void SingBox_WithoutMport_KeepsTheSinglePort()
    {
        var result = new CoreConfigSingboxService(Context(ECoreType.sing_box, PlainLink)).GenerateClientConfigContent();

        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        var cfg = JsonUtils.Deserialize<SingboxConfig>(result.Data!.ToString()!)!;
        var hysteria = cfg.outbounds.Single(o => o.type == "hysteria2");
        hysteria.server_port.Should().Be(8443);
        hysteria.server_ports.Should().BeNull();
        hysteria.hop_interval.Should().BeNull();
    }

    private static JsonNode HysteriaStream(string link)
    {
        var result = new CoreConfigV2rayService(Context(ECoreType.Xray, link)).GenerateClientConfigContent();
        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        var root = JsonNode.Parse(result.Data!.ToString()!)!;
        var outbound = root["outbounds"]!.AsArray().Single(o => o?["protocol"]?.GetValue<string>() == "hysteria")!;
        return outbound["streamSettings"]!;
    }

    [Fact]
    public void Xray_WithMport_HopsThroughTheUdphopMask()
    {
        var stream = HysteriaStream(HopLink);

        var udp = stream["finalmask"]!["udp"]!.AsArray();
        udp.Should().HaveCount(1);
        udp[0]!["type"]!.GetValue<string>().Should().Be("udphop");
        var settings = udp[0]!["settings"]!;
        settings["mode"]!.GetValue<string>().Should().Be("intervalLocal,intervalRemote");
        settings["interval"]!.GetValue<int>().Should().Be(30);
        settings["remotePorts"]!.GetValue<string>().Should().Be("20000-40000");
        // The shapes Xray 26 ignores are not written.
        stream["finalmask"]!["quicParams"]?["udpHop"].Should().BeNull();
        stream["hysteriaSettings"]?["udphop"].Should().BeNull();
    }

    [Fact]
    public void Xray_WithMportAndSalamander_UdphopFirstThenSalamander()
    {
        var stream = HysteriaStream("hysteria2://secret@203.0.113.7:8443?sni=vpn.example.com&insecure=0&obfs=salamander&obfs-password=pw&mport=20000-40000#Colitu");

        // Xray only accepts a mask that dials (udphop) at index 0 of the list.
        stream["finalmask"]!["udp"]!.AsArray().Select(mask => mask!["type"]!.GetValue<string>()).Should().Equal("udphop", "salamander");
    }

    [Fact]
    public void Xray_WithoutMport_HasNoHop()
    {
        var stream = HysteriaStream(PlainLink);

        stream["finalmask"]?["udp"].Should().BeNull();
        stream["finalmask"]?["quicParams"]?["udpHop"].Should().BeNull();
    }

    [Theory]
    [InlineData("30", 30)]
    [InlineData("3", 5)]
    [InlineData("20-40", 20)]
    [InlineData("", 30)]
    [InlineData("x", 30)]
    public void HopSeconds_IsANumberOfAtLeastFive(string interval, int expected)
    {
        CoreConfigV2rayService.HopSeconds(interval).Should().Be(expected);
    }
}
