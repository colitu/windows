using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using ServiceLib;
using ServiceLib.Enums;
using ServiceLib.Handler;
using ServiceLib.Manager;
using ServiceLib.Models;
using ServiceLib.Services.CoreConfig;
using ServiceLib.Tests.CoreConfig;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

/// <summary>
/// Fixes from the 2026-10-12 security test (v2): the account details of the saved session are
/// encrypted (WIN-03), the local SOCKS/HTTP port asks for a per-run account in TUN mode (WIN-04),
/// node and mirror names and addresses are masked in the app's own log lines (WIN-05).
/// </summary>
public class ColituSecurityTestFixesTests
{
    [Fact]
    public void Session_OnDisk_HasNoPlainAccountDetails_AndRoundTrips()
    {
        var session = new ColituSession
        {
            CachedUser = new ColituUser { Id = "u-1", Email = "someone@example.test", Name = "Some One" },
            CachedSubscription = new ColituSubscription { PlanName = "Plus" },
            PendingEmail = "new@example.test",
            AccessToken = "protected-access",
            RefreshToken = "protected-refresh",
            RegisteredDeviceId = "dev-1",
        };

        var json = JsonSerializer.Serialize(ColituAuthService.ForDisk(session));
        json.Should().NotContain("example.test").And.NotContain("Some One").And.NotContain("Plus");
        json.Should().Contain("protected-refresh", "the tokens are already protected on their own").And.Contain("dev-1");

        var loaded = JsonSerializer.Deserialize<ColituSession>(json)!;
        loaded.CachedUser.Should().BeNull();
        ColituAuthService.RestoreProfile(loaded);
        loaded.CachedUser!.Email.Should().Be("someone@example.test");
        loaded.CachedUser.Name.Should().Be("Some One");
        loaded.CachedSubscription!.PlanName.Should().Be("Plus");
        loaded.PendingEmail.Should().Be("new@example.test");
        loaded.Profile.Should().BeNull("the blob is only the on-disk form");
    }

    [Fact]
    public void Session_UnreadableProfile_LeavesDetailsEmpty()
    {
        var session = new ColituSession { Profile = Convert.ToBase64String([1, 2, 3, 4]) };
        ColituAuthService.RestoreProfile(session);
        session.CachedUser.Should().BeNull();
        session.Profile.Should().BeNull();
    }

    [Theory]
    [InlineData("203.0.113.7", "203.0.x.x")]
    [InlineData("2001:db8:1:2::7", "2001:db8::x")]
    [InlineData("de1.nodes.example.test", "***.test")]
    [InlineData("mirror.example.test", "***.test")]
    [InlineData("api.colitu.com", "api.colitu.com")]
    [InlineData("colitu.com", "colitu.com")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("", "?")]
    [InlineData(null, "?")]
    public void ForLog_MasksNodeAndMirrorNames(string? value, string expected)
    {
        ColituLogPrivacy.ForLog(value).Should().Be(expected);
    }

    private static Config TunConfig(ECoreType core)
    {
        var config = CoreConfigTestFactory.CreateConfig(core);
        // Colitu always runs Xray behind a sing-box TUN front (EnableLegacyProtect, ColituVpnService).
        config.TunModeItem = new TunModeItem { EnableTun = true, EnableLegacyProtect = true, AutoRoute = true, Stack = "gvisor", Mtu = 9000, IcmpRouting = "rule" };
        config.HysteriaItem = new HysteriaItem { UpMbps = 0, DownMbps = 0 };
        CoreConfigTestFactory.BindAppManagerConfig(config);
        return config;
    }

    private static T WithLocalAuth<T>(bool on, Func<T> action)
    {
        var before = AppManager.Instance.LocalInboundAuth;
        AppManager.Instance.LocalInboundAuth = on;
        try
        {
            return action();
        }
        finally
        {
            AppManager.Instance.LocalInboundAuth = before;
        }
    }

    [Fact]
    public void Xray_BehindTunFront_LocalInboundAsksForTheRunAccount()
    {
        var config = TunConfig(ECoreType.Xray);
        var node = CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray, "n-x", "x");
        // The Xray core behind the sing-box TUN front runs with TUN off in its own context.
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.Xray) with { IsTunEnabled = false };

        var json = WithLocalAuth(true, () => new CoreConfigV2rayService(context).GenerateClientConfigContent().Data!.ToString()!);
        var socks = JsonNode.Parse(json)!["inbounds"]!.AsArray().First(item => item!["tag"]!.GetValue<string>() == "socks")!;
        socks["listen"]!.GetValue<string>().Should().Be("127.0.0.1");
        socks["settings"]!["auth"]!.GetValue<string>().Should().Be("password");
        socks["settings"]!["accounts"]![0]!["user"]!.GetValue<string>().Should().Be(AppManager.Instance.LocalInboundUser);
        socks["settings"]!["accounts"]![0]!["pass"]!.GetValue<string>().Should().Be(AppManager.Instance.LocalInboundPass);

        // The TUN front logs in with the same account.
        var front = WithLocalAuth(true, () => ConfigHandler.GetPreSocksItem(config, node, ECoreType.Xray))!;
        front.Username.Should().Be(AppManager.Instance.LocalInboundUser);
        front.Password.Should().Be(AppManager.Instance.LocalInboundPass);
    }

    [Fact]
    public void Singbox_Tun_LocalInboundAsksForTheRunAccount()
    {
        var config = TunConfig(ECoreType.sing_box);
        var node = CoreConfigTestFactory.CreateSocksNode(ECoreType.sing_box);
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.sing_box) with { IsTunEnabled = true };

        var json = WithLocalAuth(true, () => new CoreConfigSingboxService(context).GenerateClientConfigContent().Data!.ToString()!);
        var mixed = JsonNode.Parse(json)!["inbounds"]!.AsArray().First(item => item!["tag"]!.GetValue<string>() == "socks")!;
        mixed["users"]![0]!["username"]!.GetValue<string>().Should().Be(AppManager.Instance.LocalInboundUser);
        mixed["users"]![0]!["password"]!.GetValue<string>().Should().Be(AppManager.Instance.LocalInboundPass);
    }

    [Fact]
    public void ProxyMode_LocalInboundStaysOpen()
    {
        // Windows' system proxy setting cannot carry credentials.
        var config = CoreConfigTestFactory.CreateConfig(ECoreType.Xray);
        CoreConfigTestFactory.BindAppManagerConfig(config);
        var node = CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray, "n-p", "p");
        var context = CoreConfigTestFactory.CreateContext(config, node, ECoreType.Xray);

        var json = WithLocalAuth(true, () => new CoreConfigV2rayService(context).GenerateClientConfigContent().Data!.ToString()!);
        var socks = JsonNode.Parse(json)!["inbounds"]!.AsArray().First(item => item!["tag"]!.GetValue<string>() == "socks")!;
        socks["settings"]!["auth"]?.GetValue<string>().Should().NotBe("password");
        socks["settings"]!["accounts"].Should().BeNull();
    }
}
