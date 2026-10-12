using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using ServiceLib.Enums;
using ServiceLib.Handler.Fmt;
using ServiceLib.Models;
using ServiceLib.Services.CoreConfig;
using ServiceLib.Tests.CoreConfig;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

/// <summary>
/// 2.9.0 connect fixes: the local port check reads the listener table (a connect to a closed
/// loopback port waited 1.5 s on every core start), a Hysteria2 spare from the next server gets
/// TLS (sing-box failed with "TLS required"), and a wrapped connect error keeps "offline".
/// </summary>
public class ColituConnectSpeedTests
{
    // ── Local port check ───────────────────────────────────────────────────

    [Fact]
    public async Task LocalPort_ListeningAndFreed_AnsweredWithoutWaiting()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            (await ColituVpnService.IsLocalPortListeningAsync(port)).Should().BeTrue();
        }
        finally
        {
            listener.Stop();
        }

        var watch = Stopwatch.StartNew();
        (await ColituVpnService.IsLocalPortListeningAsync(port)).Should().BeFalse();
        watch.ElapsedMilliseconds.Should().BeLessThan(500, "a free port must not wait for a connect timeout");
    }

    [Fact]
    public void LocalPort_CountsLoopbackAndAnyAddressesOnly()
    {
        ColituVpnService.IsListening([new IPEndPoint(IPAddress.Loopback, 10808)], 10808).Should().BeTrue();
        ColituVpnService.IsListening([new IPEndPoint(IPAddress.Any, 10808)], 10808).Should().BeTrue();
        ColituVpnService.IsListening([new IPEndPoint(IPAddress.IPv6Any, 10808)], 10808).Should().BeTrue();
        ColituVpnService.IsListening([new IPEndPoint(IPAddress.Parse("192.0.2.10"), 10808)], 10808).Should().BeFalse();
        ColituVpnService.IsListening([new IPEndPoint(IPAddress.Loopback, 10809)], 10808).Should().BeFalse();
    }

    // ── Hysteria2 spare from the next server ──────────────────────────────

    // As the panel builds it: sni, but no security=tls.
    private const string SpareLink = "hysteria2://secret@hy.example.test:8443?sni=hy.example.test&insecure=0#Colitu";

    private const string PrimaryConfig = """
    {
      "inbounds": [ { "type": "mixed", "tag": "socks", "listen": "127.0.0.1", "listen_port": 10808 } ],
      "outbounds": [
        { "type": "vless", "tag": "proxy", "server": "primary.example.test", "server_port": 443, "uuid": "11111111-2222-3333-4444-555555555555", "tls": { "enabled": true } },
        { "type": "direct", "tag": "direct" }
      ],
      "route": { "rules": [], "final": "proxy" }
    }
    """;

    private static JsonObject? SpareOutbound(ProfileItem node)
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.sing_box);
        config.HysteriaItem = new HysteriaItem { UpMbps = 0, DownMbps = 0, HopInterval = 30 };
        CoreConfigTestFactory.BindAppManagerConfig(config);
        node.IndexId = "spare-hy2";
        node.CoreType = ECoreType.sing_box;
        var result = new CoreConfigSingboxService(CoreConfigTestFactory.CreateContext(config, node, ECoreType.sing_box)).GenerateClientConfigContent();
        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        return ColituWarmSpare.FindOutbound(result.Data!.ToString()!);
    }

    [Fact]
    public void Hysteria2SpareLink_ParsedAlone_HasNoTls_AndIsRefused()
    {
        var node = FmtHandler.ResolveConfig(SpareLink, out _)!;
        node.StreamSecurity.Should().BeNullOrEmpty("the root cause: the link has no security=tls");

        var outbound = SpareOutbound(node)!;
        outbound["tls"].Should().BeNull();
        var merged = ColituWarmSpare.ApplySingbox(PrimaryConfig, outbound)!;
        ColituWarmSpare.SingboxProblems(merged).Should().Contain(problem => problem.Contains("without tls"));
    }

    [Fact]
    public void Hysteria2SpareLink_Normalized_GetsTls_AndJoinsTheConfig()
    {
        var node = ColituWarmSpare.NormalizeSpareProfile(FmtHandler.ResolveConfig(SpareLink, out _)!);
        node.StreamSecurity.Should().Be("tls");

        var outbound = SpareOutbound(node)!;
        outbound["type"]!.GetValue<string>().Should().Be("hysteria2");
        outbound["tls"]!["enabled"]!.GetValueKind().Should().Be(JsonValueKind.True);
        outbound["tls"]!["server_name"]!.GetValue<string>().Should().Be("hy.example.test");
        var merged = ColituWarmSpare.ApplySingbox(PrimaryConfig, outbound)!;
        ColituWarmSpare.SingboxProblems(merged).Should().BeEmpty();
    }

    [Fact]
    public void TuicSpare_Normalized_GetsTlsAndH3()
    {
        var node = ColituWarmSpare.NormalizeSpareProfile(new ProfileItem { ConfigType = EConfigType.TUIC, Address = "tuic.example.test", Port = 443 });

        node.StreamSecurity.Should().Be("tls");
        node.Alpn.Should().Be("h3");
        node.CoreType.Should().Be(ECoreType.sing_box);
    }

    [Fact]
    public void SingboxProblems_QuicOutboundWithTlsDisabled_IsReported()
    {
        var spare = JsonNode.Parse("""{ "type": "tuic", "tag": "proxy", "server": "tuic.example.test", "server_port": 443, "tls": { "enabled": false } }""")!.AsObject();

        ColituWarmSpare.SingboxProblems(ColituWarmSpare.ApplySingbox(PrimaryConfig, spare)!).Should().ContainSingle(problem => problem.Contains("without tls"));
    }

    // ── Offline survives the wrap ─────────────────────────────────────────

    [Fact]
    public void WrappedOfflineError_StaysOffline()
    {
        var offline = new ColituConnectException("no internet", offline: true);

        ColituConnectException.IsOffline(new ColituConnectException("wrapped", offline)).Should().BeTrue();
        ColituConnectException.IsOffline(new InvalidOperationException("outer", new ColituConnectException("wrapped", offline))).Should().BeTrue();
        ColituConnectException.IsOffline(new ColituConnectException("unreachable")).Should().BeFalse();
        ColituConnectException.IsOffline(new InvalidOperationException("core failed")).Should().BeFalse();
        ColituConnectException.IsOffline(null).Should().BeFalse();
    }
}
