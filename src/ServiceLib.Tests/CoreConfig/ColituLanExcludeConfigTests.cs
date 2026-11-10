using AwesomeAssertions;
using ServiceLib.Common;
using ServiceLib.Enums;
using ServiceLib.Models;
using ServiceLib.Services.CoreConfig;
using Xunit;

namespace ServiceLib.Tests.CoreConfig;

/// <summary>
/// Windows 3.1.3: the local network was captured by the TUN adapter (a routing rule sent it
/// direct, but the OS route still pointed into the tunnel, so printers, NAS and the router page
/// hung). The ranges set in TunModeItem.RouteExcludeAddress now reach sing-box as
/// route_exclude_address, so the OS keeps them on the network adapter.
/// </summary>
public class ColituLanExcludeConfigTests
{
    private static readonly List<string> Lan = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fe80::/10"];

    private static Inbound4Sbox TunInbound(bool autoRoute, List<string>? exclude)
    {
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.sing_box);
        config.TunModeItem = new TunModeItem
        {
            EnableTun = true, AutoRoute = autoRoute, StrictRoute = true, Stack = "gvisor", Mtu = 9000,
            IcmpRouting = "rule", RouteExcludeAddress = exclude
        };
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateSocksNode(ECoreType.sing_box);
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.sing_box) with { IsTunEnabled = true };

        var result = new CoreConfigSingboxService(context).GenerateClientConfigContent();
        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        var cfg = JsonUtils.Deserialize<SingboxConfig>(result.Data!.ToString()!)!;
        return cfg.inbounds.Single(i => i.type == "tun");
    }

    [Fact]
    public void Tun_ExcludesTheLocalNetworkFromTheRoutes()
    {
        TunInbound(autoRoute: true, Lan).route_exclude_address.Should().Equal(Lan);
    }

    [Fact]
    public void Tun_WithoutExclusions_WritesNone()
    {
        TunInbound(autoRoute: true, null).route_exclude_address.Should().BeNull();
        TunInbound(autoRoute: false, Lan).route_exclude_address.Should().BeNull();
    }
}
