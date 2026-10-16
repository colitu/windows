using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
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

/// <summary>CSL alpha (colitu-panel docs/csl-alpha.md §3): settings, core configs, fallback, credential file.</summary>
[Collection("ColituCsl sidecar")]
public class ColituCslTests
{
    private const string Credential = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    // ── /config envelope ───────────────────────────────────────────────────

    [Fact]
    public void Envelope_WithCsl_ParsesVersionPortAndCredential()
    {
        var envelope = JsonSerializer.Deserialize<ColituConfigEnvelopeDto>($$"""
        { "revision": 3, "profile": { "format": "x", "payload": {} },
          "csl": { "version": 1, "port": 9440, "credential": "{{Credential}}" } }
        """)!;
        envelope.Csl.Should().NotBeNull();
        envelope.Csl!.Version.Should().Be(1);
        envelope.Csl.Port.Should().Be(9440);
        envelope.Csl.Credential.Should().Be(Credential);
        envelope.Csl.IsUsable.Should().BeTrue();
    }

    [Fact]
    public void Envelope_WithoutCsl_HasNone()
    {
        var envelope = JsonSerializer.Deserialize<ColituConfigEnvelopeDto>("""{ "revision": 3, "profile": { "format": "x", "payload": {} } }""")!;
        envelope.Csl.Should().BeNull();
        ColituCslRules.OffReason(true, envelope.Csl, false, true, null).Should().Be("the server offers no CSL");
    }

    [Theory]
    [InlineData(2, 9440, Credential)]
    [InlineData(1, 0, Credential)]
    [InlineData(1, 9440, "0123")]
    [InlineData(1, 9440, "0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData(1, 9440, null)]
    public void Csl_WithAnotherVersionOrABadCredential_IsNotUsed(int version, int port, string? credential)
    {
        var csl = new ColituCslConfig { Version = version, Port = port, Credential = credential };
        csl.IsUsable.Should().BeFalse();
        ColituCslRules.OffReason(true, csl, false, true, null).Should().Be("unsupported CSL settings");
    }

    [Fact]
    public void Credential_NeverAppearsInToString_ButSurvivesTheConfigCache()
    {
        var csl = new ColituCslConfig { Version = 1, Port = 9440, Credential = Credential };
        csl.ToString().Should().NotContain(Credential).And.Contain("redacted");
        var response = new ColituVpnConfigResponse { ServerId = "n1", Csl = csl };
        response.ToString().Should().NotContain(Credential);
        // The DPAPI config cache serializes the response like the server credentials it already holds.
        var back = JsonSerializer.Deserialize<ColituVpnConfigResponse>(JsonSerializer.Serialize(response))!;
        back.Csl!.Credential.Should().Be(Credential);
        JsonSerializer.Serialize(response).Should().NotContain("IsUsable");
    }

    [Fact]
    public void Preference_IsOffByDefault_AlsoForNewInstalls()
    {
        new ColituVpnPreferences().CslAlphaEnabled.Should().BeFalse();
        ColituVpnPreferences.ForNewInstall().CslAlphaEnabled.Should().BeFalse();
    }

    [Fact]
    public void Strings_ExistInEveryLanguage()
    {
        foreach (var key in new[] { "settings.cslAlpha", "settings.cslAlphaHint" })
        {
            Loc.ValuesOf(key).Should().HaveCount(Loc.Languages.Length).And.OnlyContain(value => value.Length > 0);
        }
        Loc.ValuesOf("settings.cslAlpha")[Array.IndexOf(Loc.Languages, "tr")].Should().Be("Kesintisiz oturum (alpha)");
    }

    // ── Core configs: unchanged without CSL ────────────────────────────────

    private static CoreConfigContext Context(ECoreType core, ProfileItem node, bool tun)
    {
        var config = CoreConfigTestFactory.CreateConfig(core);
        config.RoutingBasicItem.DomainStrategy = Global.AsIs;
        config.TunModeItem = new TunModeItem { EnableTun = tun, EnableLegacyProtect = true, AutoRoute = true, Stack = "gvisor", Mtu = 9000, IcmpRouting = "rule" };
        config.HysteriaItem = new HysteriaItem { UpMbps = 0, DownMbps = 0 };
        CoreConfigTestFactory.BindAppManagerConfig(config);
        return CoreConfigTestFactory.CreateContext(config, node, core) with
        {
            IsTunEnabled = tun,
            RoutingItem = new RoutingItem
            {
                Id = "colitu",
                Remarks = "Colitu",
                RuleSet = JsonUtils.Serialize(new List<RulesItem>
                {
                    new() { Enabled = true, RuleType = ERuleType.Routing, OutboundTag = Global.DirectTag, Domain = ["geosite:category-ru"] },
                    new() { Enabled = true, RuleType = ERuleType.Routing, OutboundTag = Global.DirectTag, Ip = ["geoip:ru"] },
                    new() { Enabled = true, RuleType = ERuleType.Routing, OutboundTag = Global.ProxyTag, Domain = ["domain:example.org"] },
                }),
                DomainStrategy = Global.AsIs,
                DomainStrategy4Singbox = string.Empty,
            }
        };
    }

    /// <summary>The main Xray core (behind the sing-box TUN front, which runs with TUN off in its own context).</summary>
    private static string XrayConfig()
    {
        var node = CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray, "n-x", "x");
        var result = new CoreConfigV2rayService(Context(ECoreType.Xray, node, tun: false)).GenerateClientConfigContent();
        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        return result.Data!.ToString()!;
    }

    /// <summary>sing-box alone runs the tunnel (Hysteria2, TUN).</summary>
    private static string SingboxTunConfig()
    {
        var node = Hysteria2Fmt.Resolve("hysteria2://secret@203.0.113.7:8443?sni=vpn.example.test&insecure=0#Colitu", out _)!;
        node.IndexId = "n-hy2";
        node.CoreType = ECoreType.sing_box;
        node.StreamSecurity = Global.StreamSecurity;
        var result = new CoreConfigSingboxService(Context(ECoreType.sing_box, node, tun: true)).GenerateClientConfigContent();
        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        return result.Data!.ToString()!;
    }

    private static JsonObject XraySpare() => JsonNode.Parse("""{ "tag": "proxy", "protocol": "trojan", "settings": { "servers": [ { "address": "203.0.113.8", "port": 443 } ] } }""")!.AsObject();

    private static JsonObject SingboxSpare() => JsonNode.Parse("""{ "type": "trojan", "tag": "proxy", "server": "203.0.113.8", "server_port": 443, "password": "p" }""")!.AsObject();

    public static TheoryData<string> Configs() => new() { "xray", "xray+spare", "sing-box", "sing-box+spare" };

    private static (string Config, bool SingBox) Generated(string kind) => kind switch
    {
        "xray" => (XrayConfig(), false),
        "xray+spare" => (ColituWarmSpare.ApplyXray(XrayConfig(), XraySpare(), verify: ColituWarmSpare.NewVerifyInbound(), spareVerify: ColituWarmSpare.NewVerifyInbound())!, false),
        "sing-box" => (SingboxTunConfig(), true),
        "sing-box+spare" => (ColituWarmSpare.ApplySingbox(SingboxTunConfig(), SingboxSpare(), verify: ColituWarmSpare.NewVerifyInbound(), spareVerify: ColituWarmSpare.NewVerifyInbound())!, true),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    [Theory]
    [MemberData(nameof(Configs))]
    public void WithoutCsl_TheConfigIsUnchanged_ByteForByte(string kind)
    {
        var (config, singBox) = Generated(kind);
        // The stage without a plan hands back the very same string.
        var applied = ColituCslRules.Apply(config, singBox, null)!;
        applied.Config.Should().BeSameAs(config);
        applied.SpareAttached.Should().BeFalse();
        // And the stages around it (check inbound, rule tags) give the same output as before.
        var check = ColituWarmSpare.NewVerifyInbound();
        ColituWarmSpare.AddCheckInbound(applied.Config, singBox, check).Should().Be(ColituWarmSpare.AddCheckInbound(config, singBox, check));
        ColituXrayRuleTags.Unique(applied.Config).Should().Be(ColituXrayRuleTags.Unique(config));
    }

    [Theory]
    [InlineData(false, "the server offers no CSL")]
    [InlineData(true, null)]
    public void NoPlan_UnlessTheServerOffersCsl(bool offered, string? reason)
    {
        var csl = offered ? new ColituCslConfig { Version = 1, Port = 9440, Credential = Credential } : null;
        ColituCslRules.OffReason(true, csl, multihop: false, binaryPresent: true, earlierFailure: null).Should().Be(reason);
        ColituCslRules.OffReason(false, csl, false, true, null).Should().Be("turned off");
    }

    // ── Core configs: with CSL ─────────────────────────────────────────────

    private static ColituCslPlan Plan(bool spare) => new("n", 41000, 9440,
        new ColituVerifyInbound(41001, "pu", "pp"), "tcp",
        spare ? new ColituVerifyInbound(41002, "su", "sp") : null, spare ? "udp" : null, "cu", "cp-0123456789abcdef");

    private static List<JsonObject> Rules(JsonObject root, bool singBox) =>
        (singBox ? root["route"]!["rules"] : root["routing"]!["rules"])!.AsArray().Select(rule => rule!.AsObject()).ToList();

    private static string? S(JsonNode? node) => node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;

    [Fact]
    public void Xray_GetsTheCarrierInbounds_AndEveryTunnelRuleSendsTcpToCslFirst()
    {
        var before = XrayConfig();
        var applied = ColituCslRules.ApplyXray(before, Plan(spare: false))!;
        applied.Should().NotBeNull();
        applied.SpareAttached.Should().BeFalse();
        var root = JsonNode.Parse(applied.Config)!.AsObject();

        var outbound = root["outbounds"]!.AsArray().Select(item => item!.AsObject()).Single(item => S(item["tag"]) == ColituCslRules.OutboundTag);
        S(outbound["protocol"]).Should().Be("socks");
        outbound["settings"]!["servers"]![0]!["address"]!.GetValue<string>().Should().Be("127.0.0.1");
        outbound["settings"]!["servers"]![0]!["port"]!.GetValue<int>().Should().Be(41000);
        S(outbound["settings"]!["servers"]![0]!["users"]![0]!["user"]).Should().Be("cu", "csl-client's SOCKS port takes only the per-run account");
        S(outbound["settings"]!["servers"]![0]!["users"]![0]!["pass"]).Should().Be("cp-0123456789abcdef");
        S(root["outbounds"]![0]!["tag"]).Should().Be("proxy", "the first outbound stays the primary");

        var inbound = root["inbounds"]!.AsArray().Select(item => item!.AsObject()).Single(item => S(item["tag"]) == ColituCslRules.PrimaryInboundTag);
        S(inbound["listen"]).Should().Be("127.0.0.1");
        inbound["port"]!.GetValue<int>().Should().Be(41001);
        S(inbound["settings"]!["auth"]).Should().Be("password");
        root["inbounds"]!.AsArray().Should().NotContain(item => S(item!["tag"]) == ColituCslRules.SpareInboundTag);

        var rules = Rules(root, singBox: false);
        S(rules[0]["inboundTag"]![0]).Should().Be(ColituCslRules.PrimaryInboundTag);
        S(rules[0]["outboundTag"]).Should().Be("proxy");
        var beforeRules = Rules(JsonNode.Parse(before)!.AsObject(), singBox: false);
        var tunnelRules = beforeRules.Where(rule => S(rule["outboundTag"]) == "proxy" && rule["inboundTag"] == null).ToList();
        tunnelRules.Should().NotBeEmpty();
        foreach (var original in tunnelRules)
        {
            var index = rules.FindIndex(rule => JsonNode.DeepEquals(rule, original));
            index.Should().BeGreaterThan(0);
            var copy = rules[index - 1];
            S(copy["outboundTag"]).Should().Be(ColituCslRules.OutboundTag);
            S(copy["network"]).Should().Be("tcp");
        }
        // Direct and block rules untouched; unmatched TCP (Xray's first outbound) goes to CSL too.
        rules.Where(rule => S(rule["outboundTag"]) == "direct").Should().HaveCount(beforeRules.Count(rule => S(rule["outboundTag"]) == "direct"));
        var last = rules[^1];
        S(last["outboundTag"]).Should().Be(ColituCslRules.OutboundTag);
        S(last["network"]).Should().Be("tcp");
        last.Count.Should().Be(3);
        ColituWarmSpare.XrayProblems(applied.Config).Where(problem => !problem.Contains("among the first rules")).Should().BeEmpty();
    }

    [Fact]
    public void Xray_WithASpare_ThePrimaryCarrierGoesToProxy_TheSpareToWarmSpare_AndTheBalancerRulesGetTcpCopies()
    {
        var withSpare = ColituWarmSpare.ApplyXray(XrayConfig(), XraySpare(), verify: ColituWarmSpare.NewVerifyInbound(), spareVerify: ColituWarmSpare.NewVerifyInbound())!;
        var applied = ColituCslRules.ApplyXray(withSpare, Plan(spare: true))!;
        applied.SpareAttached.Should().BeTrue();
        var root = JsonNode.Parse(applied.Config)!.AsObject();
        var rules = Rules(root, singBox: false);
        rules.Should().Contain(rule => S(rule["inboundTag"]![0]) == ColituCslRules.PrimaryInboundTag && S(rule["outboundTag"]) == "proxy");
        rules.Should().Contain(rule => rule["inboundTag"] != null && S(rule["inboundTag"]![0]) == ColituCslRules.SpareInboundTag && S(rule["outboundTag"]) == ColituWarmSpare.XraySpareTag);
        // The balancer catch-all keeps UDP; its TCP copy goes to CSL just before it.
        var catchAll = rules.FindIndex(rule => S(rule["balancerTag"]) == ColituWarmSpare.XrayBalancerTag && S(rule["network"]) == "tcp,udp");
        S(rules[catchAll - 1]["outboundTag"]).Should().Be(ColituCslRules.OutboundTag);
        rules[catchAll - 1]["balancerTag"].Should().BeNull();
        // The DNS module's own queries and the check inbounds stay outside CSL.
        rules.Where(rule => S(rule["outboundTag"]) == ColituCslRules.OutboundTag)
            .Should().OnlyContain(rule => rule["inboundTag"] == null && S(rule["network"]) == "tcp");
        ColituWarmSpare.XrayProblems(applied.Config).Where(problem => !problem.Contains("among the first rules")).Should().BeEmpty();
    }

    [Fact]
    public void Xray_UdpOnlyRules_StayAsTheyAre()
    {
        const string config = """
        { "outbounds": [ { "tag": "proxy", "protocol": "vless" }, { "tag": "direct", "protocol": "freedom" } ],
          "routing": { "rules": [ { "type": "field", "network": "udp", "port": "443", "outboundTag": "proxy" },
                                  { "type": "field", "inboundTag": [ "colitu-check" ], "outboundTag": "proxy" } ] } }
        """;
        var rules = Rules(JsonNode.Parse(ColituCslRules.ApplyXray(config, Plan(false))!.Config)!.AsObject(), false);
        rules.Should().NotContain(rule => S(rule["outboundTag"]) == ColituCslRules.OutboundTag && rule["port"] != null);
        rules.Should().NotContain(rule => S(rule["outboundTag"]) == ColituCslRules.OutboundTag && rule["inboundTag"] != null);
    }

    [Fact]
    public void Singbox_Tun_TcpGoesToCsl_UdpStays_TheCarrierGoesToTheRealOutbound()
    {
        var before = SingboxTunConfig();
        var applied = ColituCslRules.ApplySingbox(before, Plan(spare: false))!;
        var root = JsonNode.Parse(applied.Config)!.AsObject();
        root["inbounds"]!.AsArray().Should().Contain(item => S(item!["type"]) == "tun");

        var outbound = root["outbounds"]!.AsArray().Select(item => item!.AsObject()).Single(item => S(item["tag"]) == ColituCslRules.OutboundTag);
        S(outbound["type"]).Should().Be("socks");
        S(outbound["server"]).Should().Be("127.0.0.1");
        outbound["server_port"]!.GetValue<int>().Should().Be(41000);
        S(outbound["username"]).Should().Be("cu");
        S(outbound["password"]).Should().Be("cp-0123456789abcdef");

        var rules = Rules(root, singBox: true);
        S(rules[0]["inbound"]![0]).Should().Be(ColituCslRules.PrimaryInboundTag);
        S(rules[0]["outbound"]).Should().Be("proxy");
        rules[0].Count.Should().Be(2);
        var proxyRules = Rules(JsonNode.Parse(before)!.AsObject(), true).Where(rule => S(rule["outbound"]) == "proxy").ToList();
        foreach (var original in proxyRules)
        {
            var index = rules.FindIndex(rule => JsonNode.DeepEquals(rule, original));
            S(rules[index - 1]["outbound"]).Should().Be(ColituCslRules.OutboundTag);
            rules[index - 1]["network"]!.AsArray().Select(item => item!.GetValue<string>()).Should().Equal("tcp");
        }
        // route.final stays proxy (UDP); a TCP rule to CSL goes last.
        S(root["route"]!["final"]).Should().Be("proxy");
        S(rules[^1]["outbound"]).Should().Be(ColituCslRules.OutboundTag);
        rules[^1]["network"]!.AsArray().Select(item => item!.GetValue<string>()).Should().Equal("tcp");
        ColituWarmSpare.SingboxProblems(applied.Config).Where(problem => !problem.Contains("among the first rules")).Should().BeEmpty();
    }

    [Fact]
    public void Singbox_WithASpare_ThePrimaryCarrierSkipsTheUrltestGroup()
    {
        var withSpare = ColituWarmSpare.ApplySingbox(SingboxTunConfig(), SingboxSpare(), verify: ColituWarmSpare.NewVerifyInbound(), spareVerify: ColituWarmSpare.NewVerifyInbound())!;
        var applied = ColituCslRules.ApplySingbox(withSpare, Plan(spare: true))!;
        applied.SpareAttached.Should().BeTrue();
        var rules = Rules(JsonNode.Parse(applied.Config)!.AsObject(), true);
        rules.Should().Contain(rule => rule["inbound"] != null && S(rule["inbound"]![0]) == ColituCslRules.PrimaryInboundTag && S(rule["outbound"]) == ColituWarmSpare.SingboxPrimaryTag);
        rules.Should().Contain(rule => rule["inbound"] != null && S(rule["inbound"]![0]) == ColituCslRules.SpareInboundTag && S(rule["outbound"]) == ColituWarmSpare.SingboxSpareTag);
        ColituWarmSpare.SingboxProblems(applied.Config).Where(problem => !problem.Contains("among the first rules")).Should().BeEmpty();
    }

    [Fact]
    public void Singbox_InvertedLogicalAndUdpRules_AreNotCopied()
    {
        const string config = """
        { "outbounds": [ { "type": "vless", "tag": "proxy" }, { "type": "direct", "tag": "direct" } ],
          "route": { "final": "direct", "rules": [
            { "network": ["udp"], "outbound": "proxy" },
            { "domain": ["a.test"], "invert": true, "outbound": "proxy" },
            { "type": "logical", "mode": "or", "rules": [ { "domain": ["b.test"] } ], "outbound": "proxy" },
            { "inbound": ["colitu-verify"], "outbound": "proxy" } ] } }
        """;
        var rules = Rules(JsonNode.Parse(ColituCslRules.ApplySingbox(config, Plan(false))!.Config)!.AsObject(), true);
        rules.Count(rule => S(rule["outbound"]) == ColituCslRules.OutboundTag).Should().Be(0, "final is direct and no rule may be copied");
    }

    [Fact]
    public void ACarrierSpareOnlyWhenAttached_AndTheCommandLineNeverHoldsTheCredential()
    {
        ColituCslRules.Carriers(Plan(true), spareAttached: false).Select(item => item.Label).Should().Equal("primary");
        var carriers = ColituCslRules.Carriers(Plan(true), spareAttached: true);
        carriers.Select(item => item.Label).Should().Equal("primary", "spare");
        var launch = new ColituCslLaunch(41000, 9440, carriers, "cu", "cp-0123456789abcdef");
        var args = ColituCslRules.Arguments(launch);
        args.Should().Equal("-socks", "127.0.0.1:41000", "-secrets-stdin",
            "-carrier", "socks5://pu:pp@127.0.0.1:41001/127.0.0.1:9440?family=tcp#primary",
            "-carrier", "socks5://su:sp@127.0.0.1:41002/127.0.0.1:9440?family=udp#spare", "-v");
        string.Join(" ", args).Should().NotContain(Credential).And.NotContain("cp-0123456789abcdef");
        var secrets = JsonNode.Parse(ColituCslRules.Secrets(launch, Credential))!.AsObject();
        S(secrets["credential"]).Should().Be(Credential);
        S(secrets["socks_user"]).Should().Be("cu");
        S(secrets["socks_password"]).Should().Be("cp-0123456789abcdef");
        ColituCslRules.Secrets(launch, Credential).Should().EndWith("\n", "csl-client reads one line");
        ColituCslRules.FamilyOf(EConfigType.Hysteria2).Should().Be("udp");
        ColituCslRules.FamilyOf(EConfigType.VLESS).Should().Be("tcp");
    }

    // ── Fallback ───────────────────────────────────────────────────────────

    [Fact]
    public void Verdict_WaitsForTheReadyLine_UpToTenSeconds_AndAnExitIsAlwaysOff()
    {
        ColituCslRules.Verdict(false, false, null, TimeSpan.FromSeconds(3)).Should().BeNull();
        ColituCslRules.Verdict(true, false, null, TimeSpan.FromSeconds(3)).Should().Be("");
        ColituCslRules.Verdict(false, false, null, TimeSpan.FromSeconds(10)).Should().Be("no carrier within 10 s");
        ColituCslRules.Verdict(false, true, 1, TimeSpan.FromSeconds(1)).Should().Be("csl-client exited before a carrier came up (code 1)");
        ColituCslRules.Verdict(true, true, 1, TimeSpan.FromMinutes(5)).Should().Be("csl-client exited (code 1)");
        ColituCslRules.IsReadyLine("2026/10/16 10:00:00 csl-client: SOCKS5 on 127.0.0.1:41000").Should().BeTrue();
        ColituCslRules.IsReadyLine("2026/10/16 10:00:00 carrier primary: connected after 30ms").Should().BeFalse();
        ColituCslRules.IsReadyLine(null).Should().BeFalse();
    }

    [Fact]
    public void OffReason_NamesTheFirstMissingThing()
    {
        var csl = new ColituCslConfig { Version = 1, Port = 9440, Credential = Credential };
        ColituCslRules.OffReason(true, csl, multihop: true, binaryPresent: true, null).Should().Be("multihop route");
        ColituCslRules.OffReason(true, csl, false, binaryPresent: false, null).Should().Be("csl-client missing");
        ColituCslRules.OffReason(true, csl, false, true, "no carrier within 10 s").Should().Be("failed earlier in this connect: no carrier within 10 s");
        ColituCslRules.OffReason(true, csl, false, true, null).Should().BeNull();
    }

    // ── Credential: stdin only; an older version's file is removed ──────

    [Fact]
    public void LegacyCredentialFile_IsDeleted()
    {
        var dir = Path.Combine(Path.GetTempPath(), "colitu-csl-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "colitu-csl-credential.txt");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, Credential);
            ColituCslRules.DeleteCredentialFile(path).Should().BeTrue();
            File.Exists(path).Should().BeFalse();
            ColituCslRules.DeleteCredentialFile(path).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Sidecar_MissingBinary_Throws_AndLeavesNoCredentialFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "colitu-csl-test-" + Guid.NewGuid().ToString("N"));
        ColituCsl.PathOverride = Path.Combine(dir, "missing", "csl-client.exe");
        ColituCsl.CredentialPathOverride = Path.Combine(dir, "cred.txt");
        try
        {
            var start = () => ColituCsl.Instance.Start(new ColituCslLaunch(41000, 9440, ColituCslRules.Carriers(Plan(false), false), "cu", "cp-0123456789abcdef"), Credential, _ => { });
            start.Should().Throw<Exception>();
            File.Exists(ColituCsl.CredentialPathOverride).Should().BeFalse();
        }
        finally
        {
            ColituCsl.PathOverride = null;
            ColituCsl.CredentialPathOverride = null;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
