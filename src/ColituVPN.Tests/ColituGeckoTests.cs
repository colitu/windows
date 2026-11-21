using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Colitu.KillSwitch;
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

/// <summary>ColituGecko as a protocol: sidecar contract, panel answer, Gecko-only profile, Adaptive Connect steps, offer states.</summary>
[Collection("ColituGecko sidecar")]
public class ColituGeckoTests
{
    private const string SocksUser = "gu-0123";
    private const string SocksPassword = "gp-0123456789abcdef";
    private const string ConfigSecret = "s3cr3t-ticket-0123456789abcdef";
    private static readonly string Config = $$"""{ "version": 1, "anchors": [ { "id": 7, "ticket": "{{ConfigSecret}}" } ], "extra": { "x": [1, 2] } }""";
    private const string ConfigKeyHex = "e0a6c63f2313311887b9e7e7f670390af3a16e8a9249b7d97b6ad32426615faa";
    private static readonly string Signature = "01" + new string('a', 128);
    // Whitespace and key order that a parse-and-write would change: the signature covers these exact bytes.
    private static readonly byte[] ConfigBytes = System.Text.Encoding.UTF8.GetBytes(Config.Replace("{ \"version\"", "{   \"version\"") + "\n");
    private static readonly string ClientPath = @"C:\Program Files\Colitu VPN\bin\colitu-gecko\gecko-client.exe";

    private static ColituGeckoPlan Plan() => new("n", 42000, SocksUser, SocksPassword, ClientPath);

    private static string? S(JsonNode? node) => node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;

    // ── Command line and standard input ────────────────────────────────────

    [Fact]
    public void Arguments_HoldThePortAndSkinKey_NeverASecret()
    {
        var launch = new ColituGeckoLaunch(42000, SocksUser, SocksPassword);
        var args = ColituGeckoRules.Arguments(launch);
        args.Should().Equal("-socks", "127.0.0.1:42000", "-secrets-stdin",
            "-config-key", $"1={ConfigKeyHex}",
            "-skin-key", "2=05c3ca41534e35dfe1f1ec807733a1d5b77d60ac24e272e29fcc1a0fdb5eb2fc");
        string.Join(" ", args).Should().NotContain(SocksPassword).And.NotContain(SocksUser).And.NotContain(ConfigSecret);
        ColituGeckoRules.Arguments(launch with { Standby = true }).Should().EndWith(["-mode", "standby"]);
        // Only the production key is pinned; the lab key (id 1) never is.
        ColituGeckoRules.SkinKeys.Keys.Should().Equal(2);
        ColituGeckoRules.ConfigKeys.Keys.Should().Equal(1);
    }

    private static ColituGeckoSignedConfig Signed() => ColituGeckoRules.Sign(ConfigBytes, Signature)!;

    [Fact]
    public void Secrets_IsOneLine_WithTheExactBytesTheirSignatureAndTheFloor()
    {
        var floor = new ColituGeckoFloor(1_763_190_000, new Dictionary<string, long> { ["s2"] = 5, ["s1"] = 3 });
        var line = ColituGeckoRules.Secrets(new ColituGeckoLaunch(42000, SocksUser, SocksPassword), Signed(), floor);
        line.Should().EndWith("\n");
        line.TrimEnd('\n').Should().NotContain("\n").And.NotContain("\r");
        var root = JsonNode.Parse(line)!.AsObject();
        S(root["socks_user"]).Should().Be(SocksUser);
        S(root["socks_password"]).Should().Be(SocksPassword);
        root.ContainsKey("config").Should().BeFalse("the unsigned form is for the lab only");
        Convert.FromBase64String(S(root["config_b64"])!).Should().Equal(ConfigBytes, "the bytes go through untouched");
        S(root["config_sig"]).Should().Be(Signature);
        root["min_issued"]!.GetValue<long>().Should().Be(1_763_190_000);
        root["skin_floor"]!["s1"]!.GetValue<long>().Should().Be(3);
        root["skin_floor"]!["s2"]!.GetValue<long>().Should().Be(5);

        // First start of an install: nothing to give back.
        var fresh = JsonNode.Parse(ColituGeckoRules.Secrets(new ColituGeckoLaunch(42000, SocksUser, SocksPassword), Signed(), null))!.AsObject();
        fresh.Select(p => p.Key).Should().BeEquivalentTo(["socks_user", "socks_password", "config_b64", "config_sig"]);

        var later = JsonNode.Parse(ColituGeckoRules.ConfigLine(Signed()))!.AsObject();
        later.Select(p => p.Key).Should().BeEquivalentTo(["config_b64", "config_sig"]);
        Convert.FromBase64String(S(later["config_b64"])!).Should().Equal(ConfigBytes);
        ColituGeckoRules.ConfigLine(Signed()).Should().EndWith("\n");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "anchors": [] }""")]
    [InlineData("""{ "anchors": {} }""")]
    [InlineData("""{ "version": 1 }""")]
    public void UnusableConfig_IsRefused(string body)
    {
        ColituGeckoRules.IsUsableConfig(body).Should().BeFalse();
        ColituGeckoRules.Sign(System.Text.Encoding.UTF8.GetBytes(body), Signature).Should().BeNull();
        ColituApiClient.GeckoResult(System.Text.Encoding.UTF8.GetBytes(body), Signature).Offer.Should().Be(ColituGeckoOffer.Unavailable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("01aa")]
    [InlineData("zz")]
    public void NoUsableSignature_GeckoDoesNotStart_AndSaysWhy(string? header)
    {
        var result = ColituApiClient.GeckoResult(ConfigBytes, header);
        result.Offer.Should().Be(ColituGeckoOffer.Unavailable);
        result.Signed.Should().BeNull("no signature, no configuration for gecko-client");
        result.Reason.Should().StartWith("signature header").And.NotContain(ConfigSecret);
        ColituGeckoRules.Sign(ConfigBytes, header).Should().BeNull();
    }

    [Fact]
    public void PanelAnswer_KeepsTheBytesAsTheyCame_AndNeverPrintsThem()
    {
        var result = ColituApiClient.GeckoResult(ConfigBytes, Signature.ToUpperInvariant());
        result.Offer.Should().Be(ColituGeckoOffer.Offered);
        result.Signed!.Body.Should().Equal(ConfigBytes);
        result.Signed.Signature.Should().Be(Signature, "hex is compared case-insensitively; sent lower case");
        result.ToString().Should().NotContain(ConfigSecret).And.Contain("redacted");
        result.Signed.ToString().Should().NotContain(ConfigSecret).And.NotContain(Signature);
        ColituApiClient.GeckoConfigPath.Should().Be("/gecko/config");
        ColituApiClient.GeckoSignatureHeader.Should().Be("X-Gecko-Config-Signature");
    }

    // ── F-14/F-15: refused configurations and the floor kept across runs ───

    [Fact]
    public void ConfigRefused_IsOnlyLogged_AndFloorEventsAreParsed()
    {
        var refused = ColituGeckoRules.ParseEvent("gecko-client: event {\"event\":\"config_refused\",\"reason\":\"signature\"}")!;
        refused.Kind.Should().Be("config_refused");
        refused.Reason.Should().Be("signature");
        refused.Describe().Should().Contain("last valid one stays");
        ColituGeckoRules.ParseEvent("gecko-client: event {\"event\":\"config_refused\",\"reason\":\"old\"}")!.Reason.Should().Be("old");
        ColituGeckoRules.ParseEvent("gecko-client: event {\"event\":\"config_refused\",\"reason\":\"x y\"}")!.Reason.Should().Be("other");

        var accepted = ColituGeckoRules.ParseEvent("gecko-client: event {\"event\":\"config\",\"issued\":1763190000}")!;
        accepted.Issued.Should().Be(1763190000);
        var revs = ColituGeckoRules.ParseEvent("gecko-client: event {\"event\":\"skin_revs\",\"revs\":{\"s1\":3,\"s2\":5,\"bad key\":9,\"neg\":-1}}")!;
        revs.Revs.Should().Equal(new Dictionary<string, long> { ["s1"] = 3, ["s2"] = 5 });
    }

    [Fact]
    public void Floor_OnlyGoesUp_SurvivesAStateSaveAndComesBackAtTheNextStart()
    {
        var stored = new ColituGeckoFloor(0, null);
        stored = ColituGeckoRules.MergeFloor(stored, 100, new Dictionary<string, long> { ["s1"] = 3 });
        stored = ColituGeckoRules.MergeFloor(stored, 90, new Dictionary<string, long> { ["s1"] = 2, ["s2"] = 7 });
        stored.MinIssued.Should().Be(100, "an older issued never lowers it");
        stored.SkinFloor.Should().Equal(new Dictionary<string, long> { ["s1"] = 3, ["s2"] = 7 });

        // Save: the app's state file; restart: read it back.
        var prefs = new ColituVpnPreferences { GeckoMinIssued = stored.MinIssued, GeckoSkinRevs = stored.SkinFloor!.ToDictionary(r => r.Key, r => r.Value) };
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var back = JsonSerializer.Deserialize<ColituVpnPreferences>(JsonSerializer.Serialize(prefs, options), options)!.Normalize();
        back.GeckoMinIssued.Should().Be(100);
        back.GeckoSkinRevs.Should().Equal(new Dictionary<string, long> { ["s1"] = 3, ["s2"] = 7 });

        var line = JsonNode.Parse(ColituGeckoRules.Secrets(new ColituGeckoLaunch(1, "u", "p"), Signed(), new ColituGeckoFloor(back.GeckoMinIssued, back.GeckoSkinRevs)))!.AsObject();
        line["min_issued"]!.GetValue<long>().Should().Be(100);
        line["skin_floor"]!["s2"]!.GetValue<long>().Should().Be(7);
        new ColituVpnPreferences().GeckoMinIssued.Should().Be(0, "an install that never ran Gecko gives back nothing");
    }

    // ── stderr: ready line and events ──────────────────────────────────────

    [Fact]
    public void ReadyLine_IsRecognised()
    {
        ColituGeckoRules.IsReadyLine("2026/11/13 10:00:00 gecko-client: SOCKS5 on 127.0.0.1:42000").Should().BeTrue();
        ColituGeckoRules.IsReadyLine("gecko-client: SOCKS5 on 127.0.0.1:42000").Should().BeTrue();
        ColituGeckoRules.IsReadyLine("csl-client: SOCKS5 on 127.0.0.1:42000").Should().BeFalse();
        ColituGeckoRules.IsReadyLine(null).Should().BeFalse();
    }

    [Fact]
    public void Events_AreParsed_WithOnlySafeFields()
    {
        var session = ColituGeckoRules.ParseEvent("""2026/11/13 10:00:00 gecko-client: event {"event":"session","anchor":3,"bootstrap":false}""")!;
        session.Kind.Should().Be("session");
        session.Anchor.Should().Be(3);
        session.Bootstrap.Should().BeFalse();

        ColituGeckoRules.ParseEvent("""gecko-client: event {"event":"session","anchor":3,"bootstrap":true}""")!.Bootstrap.Should().BeTrue();
        ColituGeckoRules.ParseEvent("""gecko-client: event {"event":"session","anchor":3}""")!.Bootstrap.Should().BeNull();

        foreach (var reason in new[] { "half_life", "bootstrap", "expired" })
        {
            ColituGeckoRules.ParseEvent($$"""gecko-client: event {"event":"need_config","reason":"{{reason}}"}""")!.Reason.Should().Be(reason);
        }
        ColituGeckoRules.ParseEvent("""gecko-client: event {"event":"need_config","reason":"<script>"}""")!.Reason.Should().Be("other");

        var grip = ColituGeckoRules.ParseEvent("""gecko-client: event {"event":"grip","entry":"203.0.113.9:443","skin":1,"rev":"r4","class":"tls","bytes":4096,"bucket":"b2"}""")!;
        grip.Kind.Should().Be("grip");
        grip.Skin.Should().Be("1");
        grip.Rev.Should().Be("r4");
        grip.Class.Should().Be("tls");
        grip.Bytes.Should().Be(4096);
        grip.Bucket.Should().Be("b2");
        grip.Describe().Should().NotContain("203.0.113.9", "the entry may name a server address");

        var hostile = ColituGeckoRules.ParseEvent($$"""gecko-client: event {"event":"grip","class":"{{ConfigSecret}}-and-more-than-thirty-two-characters"}""")!;
        hostile.Class.Should().BeNull();

        ColituGeckoRules.ParseEvent("""gecko-client: event {"event":"lost"}""")!.Kind.Should().Be("lost");
    }

    [Theory]
    [InlineData("gecko-client: event {\"event\":\"teleport\"}")]
    [InlineData("gecko-client: event {\"event\":")]
    [InlineData("gecko-client: event [1,2]")]
    [InlineData("gecko-client: event {\"anchor\":1}")]
    [InlineData("gecko-client: event {\"event\":7}")]
    [InlineData("gecko-client: SOCKS5 on 127.0.0.1:42000")]
    [InlineData("some other line")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownOrMalformedLines_AreNotEvents(string? line)
    {
        ColituGeckoRules.ParseEvent(line).Should().BeNull();
    }

    // ── Verdict ────────────────────────────────────────────────────────────

    [Fact]
    public void Verdict_NeedsTheSocksLineAndANonBootstrapSession_Within15Seconds()
    {
        var t = TimeSpan.FromSeconds(3);
        ColituGeckoRules.Verdict(false, false, false, null, t).Should().BeNull();
        ColituGeckoRules.Verdict(true, false, false, null, t).Should().BeNull("a bootstrap session is not ready");
        ColituGeckoRules.Verdict(false, true, false, null, t).Should().BeNull("the SOCKS port is not open yet");
        ColituGeckoRules.Verdict(true, true, false, null, t).Should().Be("");
        ColituGeckoRules.Verdict(true, false, false, null, TimeSpan.FromSeconds(15)).Should().Be("no session within 15 s");
        ColituGeckoRules.Verdict(false, false, false, null, TimeSpan.FromSeconds(15)).Should().Be("no SOCKS port within 15 s");
        ColituGeckoRules.Verdict(true, true, true, 2, TimeSpan.FromMinutes(5)).Should().Be("gecko-client exited (code 2)");
        ColituGeckoRules.Verdict(true, false, true, null, t).Should().Be("gecko-client exited before its session came up (code ?)");
        ColituGeckoRules.ReadyDeadline.Should().Be(TimeSpan.FromSeconds(15));
    }

    // ── Off reasons, Adaptive Connect steps, cool-down ─────────────────────

    [Fact]
    public void OffReason_NamesTheFirstMissingThing_AndAManualPickIgnoresTheCoolDown()
    {
        ColituGeckoRules.OffReason(ColituGeckoOffer.NotOffered, false, true, true, false).Should().Be("not offered to this account");
        ColituGeckoRules.OffReason(ColituGeckoOffer.Unavailable, true, false, false, false).Should().Be("Gecko settings unavailable");
        ColituGeckoRules.OffReason(ColituGeckoOffer.Unknown, true, false, false, true).Should().Be("Gecko settings unavailable");
        ColituGeckoRules.OffReason(ColituGeckoOffer.Offered, false, false, false, false).Should().Be("gecko-client missing");
        ColituGeckoRules.OffReason(ColituGeckoOffer.Offered, true, true, false, false).Should().Be("the kill switch allows only the VPN servers' addresses");
        ColituGeckoRules.OffReason(ColituGeckoOffer.Offered, true, false, true, manual: false).Should().Be("cooling down after a failure on this network");
        ColituGeckoRules.OffReason(ColituGeckoOffer.Offered, true, false, true, manual: true).Should().BeNull("a manual pick ignores the cool-down (11-20 bug)");
        ColituGeckoRules.OffReason(ColituGeckoOffer.Offered, true, false, false, false).Should().BeNull();
    }

    [Fact]
    public void EffectiveOffer_IsOfferedOnlyWithASignedConfigurationInHand()
    {
        ColituGeckoRules.EffectiveOffer(null).Should().Be(ColituGeckoOffer.Unknown);
        ColituGeckoRules.EffectiveOffer(ColituGeckoConfigResult.NotOffered).Should().Be(ColituGeckoOffer.NotOffered);
        ColituGeckoRules.EffectiveOffer(ColituGeckoConfigResult.Unavailable).Should().Be(ColituGeckoOffer.Unavailable);
        ColituGeckoRules.EffectiveOffer(new ColituGeckoConfigResult(ColituGeckoOffer.Offered, null)).Should().Be(ColituGeckoOffer.Unavailable);
        ColituGeckoRules.EffectiveOffer(ColituApiClient.GeckoResult(ConfigBytes, Signature)).Should().Be(ColituGeckoOffer.Offered);
    }

    [Fact]
    public void AdaptiveConnect_NormalFails_ThenGecko_ThenWhitelist()
    {
        var steps = ColituConnectSteps.Plan(geckoPicked: false, automatic: true, whitelistMode: false, ColituGeckoOffer.Offered);
        steps.Should().Equal(ColituConnectSteps.Normal, ColituConnectSteps.Gecko, ColituConnectSteps.Whitelist);
        ColituConnectSteps.After(steps, ColituConnectSteps.Normal).Should().Be(ColituConnectSteps.Gecko, "Gecko comes right after the normal servers");
        ColituConnectSteps.After(steps, ColituConnectSteps.Gecko).Should().Be(ColituConnectSteps.Whitelist);
        ColituConnectSteps.After(steps, ColituConnectSteps.Whitelist).Should().BeNull();
        ColituConnectSteps.Gecko.Should().Be("gecko", "the journal's step name");
        ColituEngineJournal.Gecko.Should().Be("gecko");
        // Not asked yet: the configuration may still come within 6 s, so the step stays (and is skipped then if nothing came).
        ColituConnectSteps.Plan(false, true, false, ColituGeckoOffer.Unknown).Should().Contain(ColituConnectSteps.Gecko);
        ColituConnectSteps.Plan(false, true, false, ColituGeckoOffer.Unavailable).Should().Contain(ColituConnectSteps.Gecko);
    }

    [Fact]
    public void AdaptiveConnect_GeckoNotOffered_IsSkipped()
    {
        var steps = ColituConnectSteps.Plan(false, true, false, ColituGeckoOffer.NotOffered);
        steps.Should().Equal(ColituConnectSteps.Normal, ColituConnectSteps.Whitelist);
        ColituConnectSteps.After(steps, ColituConnectSteps.Normal).Should().Be(ColituConnectSteps.Whitelist);
        ColituConnectSteps.Plan(false, true, true, ColituGeckoOffer.NotOffered).Should().Equal(ColituConnectSteps.Whitelist);
    }

    [Fact]
    public void AdaptiveConnect_WhitelistMode_GeckoBeforeTheWhitelistRows_APickedServerIsNeverSwitched()
    {
        ColituConnectSteps.Plan(false, true, whitelistMode: true, ColituGeckoOffer.Offered).Should().Equal(ColituConnectSteps.Gecko, ColituConnectSteps.Whitelist);
        ColituConnectSteps.Plan(false, automatic: false, false, ColituGeckoOffer.Offered).Should().Equal(ColituConnectSteps.Normal);
    }

    [Fact]
    public void ManualPick_IsGeckoOnly_NoSilentSwitch()
    {
        ColituConnectSteps.Plan(geckoPicked: true, automatic: true, false, ColituGeckoOffer.Offered).Should().Equal(ColituConnectSteps.Gecko);
        ColituConnectSteps.Plan(geckoPicked: true, automatic: false, true, ColituGeckoOffer.Unavailable).Should().Equal(ColituConnectSteps.Gecko);
        ColituConnectSteps.GeckoPicked(ColituGeckoRules.Protocol, false, ColituGeckoOffer.Offered).Should().BeTrue();
        ColituConnectSteps.GeckoPicked(ColituGeckoRules.Protocol, false, ColituGeckoOffer.Unavailable).Should().BeTrue("offered but unavailable: the connect error says so");
        ColituConnectSteps.GeckoPicked(ColituGeckoRules.Protocol, false, ColituGeckoOffer.Unknown).Should().BeTrue();
        ColituConnectSteps.GeckoPicked(ColituGeckoRules.Protocol, false, ColituGeckoOffer.NotOffered).Should().BeFalse("404 hides Gecko: automatic");
        ColituConnectSteps.GeckoPicked(ColituGeckoRules.Protocol, whitelistSelected: true, ColituGeckoOffer.Offered).Should().BeFalse("a Whitelist row has no method");
        ColituConnectSteps.GeckoPicked("hysteria2", false, ColituGeckoOffer.Offered).Should().BeFalse();
    }

    [Fact]
    public void CoolDown_SkipsGeckoInAutomatic_ButNotOnAManualPick()
    {
        var cooldown = new ColituGeckoCooldown();
        var now = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        cooldown.Active("net-a", now).Should().BeFalse();
        cooldown.Failed("net-a", now);
        cooldown.Active("net-a", now.AddMinutes(9)).Should().BeTrue();
        cooldown.Active("net-b", now.AddMinutes(1)).Should().BeFalse("per network");
        cooldown.Active("net-a", now.AddMinutes(10)).Should().BeFalse();
        var active = cooldown.Active("net-a", now.AddMinutes(1));
        ColituGeckoRules.OffReason(ColituGeckoOffer.Offered, true, false, active, manual: false).Should().NotBeNull("Auto skips Gecko while it cools down");
        ColituGeckoRules.OffReason(ColituGeckoOffer.Offered, true, false, active, manual: true).Should().BeNull("a manual pick tries it anyway");
        cooldown.Succeeded("net-a");
        cooldown.Active("net-a", now.AddMinutes(1)).Should().BeFalse("a working Gecko connection ends it");
        ColituGeckoRules.CoolDown.Should().Be(TimeSpan.FromMinutes(10));
        ColituGeckoRules.ConfigWait.Should().Be(TimeSpan.FromSeconds(6));
    }

    // ── Protocol menu: offer states ────────────────────────────────────────

    [Fact]
    public void ProtocolMenu_404HidesGecko_503ShowsItDisabled_200ShowsIt()
    {
        static List<string> Methods(ColituGeckoOffer offer) => ColituTransportChoice.Menu(offer).Select(entry => entry.Method).ToList();
        Methods(ColituGeckoOffer.NotOffered).Should().Equal(new[] { "" }.Concat(ColituTransportChoice.Methods));
        Methods(ColituGeckoOffer.Unknown).Should().NotContain(ColituTransportChoice.Gecko, "not known yet: not shown");
        ColituTransportChoice.Menu(ColituGeckoOffer.Offered)[1].Should().Be(new ColituMethodEntry(ColituGeckoRules.Protocol, true));
        ColituTransportChoice.Menu(ColituGeckoOffer.Unavailable)[1].Should().Be(new ColituMethodEntry(ColituGeckoRules.Protocol, false));
        ColituTransportChoice.Menu(ColituGeckoOffer.Offered).Where(entry => entry.Method != ColituTransportChoice.Gecko).Should().OnlyContain(entry => entry.Enabled);

        ColituTransportChoice.Normalize("COLITU-GECKO").Should().Be(ColituGeckoRules.Protocol);
        ColituTransportChoice.Shown(ColituGeckoRules.Protocol, ColituGeckoOffer.NotOffered).Should().Be("", "the pick reads as automatic while Gecko is not offered");
        ColituTransportChoice.Shown(ColituGeckoRules.Protocol, ColituGeckoOffer.Offered).Should().Be(ColituGeckoRules.Protocol);
        ColituTransportChoice.PutFirst(["trojan", "hysteria2"], item => item, ColituGeckoRules.Protocol).Should().Equal("trojan", "hysteria2");
        ColituTransportNames.Of(ColituGeckoRules.Protocol, Loc.I).Should().Be("ColituGecko");
    }

    [Fact]
    public void OldToggle_IsGone_AndItsStoredKeyIsIgnored()
    {
        typeof(ColituVpnPreferences).GetProperty("GeckoAlphaEnabled").Should().BeNull("no toggle: the panel's offer decides");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var session = JsonSerializer.Deserialize<ColituVpnSession>("""{"Status":0,"Preferences":{"GeckoAlphaEnabled":false,"PreferredTransport":"colitu-gecko"},"PreferencesMigration":2}""", options)!;
        session.Preferences.Normalize().PreferredTransport.Should().Be(ColituGeckoRules.Protocol, "the pick is kept across restarts");
        JsonSerializer.Serialize(session, options).Should().NotContain("GeckoAlphaEnabled");
    }

    [Fact]
    public void Strings_ExistInEveryLanguage_WithoutAlpha()
    {
        foreach (var key in new[] { "transport.colitu-gecko", "gecko.autoExit", "gecko.unavailable", "connect.gecko", "err.gecko", "err.geckoUnavailable" })
        {
            Loc.ValuesOf(key).Should().HaveCount(Loc.Languages.Length).And.OnlyContain(value => value.Length > 0);
            Loc.ValuesOf(key).Should().OnlyContain(value => !value.Contains("alpha", StringComparison.OrdinalIgnoreCase) && !value.Contains("альфа", StringComparison.OrdinalIgnoreCase));
        }
        Loc.ValuesOf("transport.colitu-gecko").Should().OnlyContain(value => value == "ColituGecko");
        // [ru, tr, en, zh, fa]
        Loc.ValuesOf("gecko.autoExit").Take(3).Should().Equal("автовыход", "otomatik çıkış", "automatic exit");
        var keys = typeof(Loc).GetField("Strings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null) as Dictionary<string, string[]>;
        keys!.Keys.Should().NotContain(["settings.geckoAlpha", "settings.geckoAlphaHint"], "the Advanced-mode toggle is gone");
    }

    // ── Kill switch: gecko-client is permitted exactly like the cores ──────

    [Fact]
    public void KillSwitch_PermitsGeckoClientLikeTheCores_TcpAndUdp()
    {
        var layout = KsInstallLayout.FromDirectory(@"C:\Program Files\Colitu VPN");
        layout.CorePaths.Should().Contain(@"C:\Program Files\Colitu VPN\bin\colitu-gecko\gecko-client.exe");
        KsValidator.TryValidateArm(new KsArm { CoreAccess = KsProtocol.CoreAccessFull }, out var full, out _).Should().BeTrue();
        var filters = KsPlanBuilder.Build(full!, layout);
        foreach (var layer in new[] { KsLayer.ConnectV4, KsLayer.ConnectV6 })
        {
            var cores = filters.Single(filter => filter.Layer == layer && filter.Name == "Permit the VPN cores");
            cores.Conditions.OfType<KsApp>().Select(app => app.Path).Should().BeEquivalentTo(layout.CorePaths);
            cores.Conditions.OfType<KsProtocolCondition>().Should().BeEmpty("UDP too: Gecko carries UDP flows");
        }
        // Not added anywhere else (no broader permit).
        filters.Where(filter => filter.Conditions.OfType<KsApp>().Any(app => app.Path.EndsWith("gecko-client.exe")))
            .Should().OnlyContain(filter => filter.Name.StartsWith("Permit the VPN cores"));
        // The Gecko-only profile runs on sing-box with split tunnelling by app, like every other transport.
        ColituVpnService.CoreTypes(true).Should().Contain(item => item.ConfigType == EConfigType.SOCKS && item.CoreType == ECoreType.sing_box);
    }

    // ── Core configs ───────────────────────────────────────────────────────

    private static CoreConfigContext Context(ECoreType core, ProfileItem node, bool tun, List<RulesItem>? rules = null)
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
                RuleSet = JsonUtils.Serialize(rules ?? new List<RulesItem>
                {
                    new() { Enabled = true, RuleType = ERuleType.Routing, OutboundTag = Global.DirectTag, Ip = ["geoip:ru"] },
                    new() { Enabled = true, RuleType = ERuleType.Routing, OutboundTag = Global.ProxyTag, Domain = ["domain:example.org"] },
                }),
                DomainStrategy = Global.AsIs,
                DomainStrategy4Singbox = string.Empty,
            }
        };
    }

    private static string XrayConfig()
    {
        var node = CoreConfigTestFactory.CreateVmessNode(ECoreType.Xray, "n-x", "x");
        var result = new CoreConfigV2rayService(Context(ECoreType.Xray, node, tun: false)).GenerateClientConfigContent();
        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        return result.Data!.ToString()!;
    }

    private static string SingboxConfig(bool tun)
    {
        var node = Hysteria2Fmt.Resolve("hysteria2://secret@203.0.113.7:8443?sni=vpn.example.test&insecure=0#Colitu", out _)!;
        node.IndexId = "n-hy2";
        node.CoreType = ECoreType.sing_box;
        node.StreamSecurity = Global.StreamSecurity;
        var result = new CoreConfigSingboxService(Context(ECoreType.sing_box, node, tun)).GenerateClientConfigContent();
        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        return result.Data!.ToString()!;
    }

    /// <summary>The core config v2rayN generates for the Gecko SOCKS profile, with the app's own routing rules.</summary>
    private static string GeckoProfileConfig(bool singBox, bool tun)
    {
        var core = singBox ? ECoreType.sing_box : ECoreType.Xray;
        var node = ColituGeckoRules.Profile("colitu-api", 1080);
        node.CoreType = core;
        var rules = ColituVpnService.BuildColituRoutingRules(new ColituVpnPreferences(), null, tun);
        foreach (var rule in rules)
        {
            rule.RuleType ??= ERuleType.Routing;
        }
        var context = Context(core, node, tun, rules);
        var result = singBox
            ? new CoreConfigSingboxService(context).GenerateClientConfigContent()
            : new CoreConfigV2rayService(context).GenerateClientConfigContent();
        result.Success.Should().BeTrue($"ret msg: {result.Msg}");
        return result.Data!.ToString()!;
    }

    private static List<JsonObject> Rules(JsonObject root, bool singBox) =>
        (singBox ? root["route"]!["rules"] : root["routing"]!["rules"])!.AsArray().Select(rule => rule!.AsObject()).ToList();

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void GeckoOnly_TcpAndUdpGoToGecko_RuAndPrivateDirect_NoServerOutbound(bool singBox, bool tun)
    {
        var before = GeckoProfileConfig(singBox, tun);
        before.Should().NotContain(SocksPassword, "the per-run account is never in the stored profile");
        var applied = ColituGeckoRules.ApplyOnly(before, singBox, Plan())!;
        applied.Should().NotBeNull();
        ColituGeckoRules.IsGeckoOnly(applied, singBox, 42000).Should().BeTrue("gecko-client is the only way out");
        var root = JsonNode.Parse(applied)!.AsObject();
        var outbounds = root["outbounds"]!.AsArray().Select(item => item!.AsObject()).ToList();
        var proxy = outbounds.Single(item => S(item["tag"]) == "proxy");
        if (singBox)
        {
            S(proxy["type"]).Should().Be("socks");
            S(proxy["server"]).Should().Be("127.0.0.1");
            proxy["server_port"]!.GetValue<int>().Should().Be(42000);
            S(proxy["username"]).Should().Be(SocksUser);
            S(proxy["password"]).Should().Be(SocksPassword);
            proxy.ContainsKey("network").Should().BeFalse("no network field: TCP and UDP (UDP ASSOCIATE)");
        }
        else
        {
            S(proxy["protocol"]).Should().Be("socks");
            S(proxy["settings"]!["servers"]![0]!["address"]).Should().Be("127.0.0.1");
            proxy["settings"]!["servers"]![0]!["port"]!.GetValue<int>().Should().Be(42000);
            S(proxy["settings"]!["servers"]![0]!["users"]![0]!["user"]).Should().Be(SocksUser);
            S(proxy["settings"]!["servers"]![0]!["users"]![0]!["pass"]).Should().Be(SocksPassword);
            S(outbounds[0]["tag"]).Should().Be("proxy", "unmatched traffic goes to the first outbound: Gecko");
        }
        outbounds.Should().NotContain(item => S(item["tag"]) == ColituGeckoRules.OutboundTag, "no alpha chain outbound");

        var rules = Rules(root, singBox);
        var target = singBox ? "outbound" : "outboundTag";
        // Nothing routes traffic to a server, and no rule splits TCP from UDP.
        rules.Where(rule => S(rule[target]) is { } tag).Should().OnlyContain(rule => S(rule[target]) == "proxy" || S(rule[target]) == "direct" || S(rule[target]) == "block" || S(rule[target]) == "dns-out");
        rules.Where(rule => S(rule[target]) == "proxy").Should().OnlyContain(rule => rule["network"] == null || rule["network"]!.ToJsonString().Contains("udp"));
        // Russian and private destinations direct, ahead of every proxy rule but DNS.
        var text = string.Join("\n", rules.Select(rule => rule.ToJsonString()));
        var ru = rules.FindIndex(rule => S(rule[target]) == "direct" && (rule.ToJsonString().Contains("geoip:ru") || rule.ToJsonString().Contains("geoip-ru")));
        var lan = rules.FindIndex(rule => S(rule[target]) == "direct" && (rule.ToJsonString().Contains("geoip:private") || rule.ToJsonString().Contains("geoip-private") || rule.ToJsonString().Contains("ip_is_private")));
        ru.Should().BeGreaterThanOrEqualTo(0, "Russian destinations go direct: the anchors refuse them");
        lan.Should().BeGreaterThanOrEqualTo(0, "the local network stays direct");
        // DNS through the tunnel = through Gecko: port 53 goes to proxy, which is gecko-client.
        rules.Should().Contain(rule => S(rule[target]) == "proxy" && rule["port"] != null && rule["port"]!.ToJsonString().Contains("53"), "DNS leak protection sends DNS into the tunnel");
        if (singBox && tun)
        {
            // gecko-client's own connections leave directly (its DNS answered by sing-box), before every other rule.
            S(rules[0]["action"]).Should().Be("hijack-dns");
            S(rules[0]["process_path_regex"]![0]).Should().Be(ColituPingRoute.PathRegex(ClientPath));
            ColituGeckoRules.IsDirectRule(rules[1], ClientPath).Should().BeTrue();
            ColituGeckoRules.HasDirectRules(applied, ClientPath).Should().BeTrue();
        }
        else
        {
            applied.Should().NotContain("process_path_regex");
        }
        ColituGeckoRules.ApplyOnly(applied, singBox, Plan()).Should().NotBeNull("a restart rebuilds the config from the profile, never from the applied one");
    }

    [Fact]
    public void GeckoOnly_SingboxDnsServers_UseTheGeckoOutbound()
    {
        var applied = ColituGeckoRules.ApplyOnly(GeckoProfileConfig(singBox: true, tun: true), true, Plan())!;
        var root = JsonNode.Parse(applied)!.AsObject();
        var tags = root["outbounds"]!.AsArray().Select(item => S(item!["tag"])).ToHashSet();
        var servers = root["dns"]?["servers"]?.AsArray().Select(item => item!.AsObject()).ToList() ?? [];
        servers.Should().NotBeEmpty();
        // A detour names an outbound that exists: the remote resolver goes through "proxy" (= gecko-client).
        servers.Where(server => S(server["detour"]) is { }).Should().OnlyContain(server => tags.Contains(S(server["detour"])));
        servers.Should().Contain(server => S(server["detour"]) == "proxy" || S(server["detour"]) == null);
    }

    [Fact]
    public void ApplyOnly_NeverReplacesAServerOutbound_AndRefusesOddShapes()
    {
        ColituGeckoRules.ApplyOnly(XrayConfig(), false, Plan()).Should().BeNull("a VMess server is not the Gecko profile");
        ColituGeckoRules.ApplyOnly(SingboxConfig(tun: true), true, Plan()).Should().BeNull("a Hysteria2 server is not the Gecko profile");
        ColituGeckoRules.ApplyOnly("""{ "outbounds": [ { "tag": "direct", "protocol": "freedom" } ] }""", false, Plan()).Should().BeNull();
        ColituGeckoRules.ApplyOnly("{", false, Plan()).Should().BeNull();
        ColituGeckoRules.ApplyOnly(GeckoProfileConfig(true, true), true, Plan() with { ClientPath = "gecko-client.exe" }).Should().BeNull("TUN needs the sidecar's full path");
        ColituGeckoRules.AddSidecarDirectRules(SingboxConfig(tun: true), "gecko-client.exe").Should().BeNull();
    }

    [Fact]
    public void ApplyOnly_DropsEveryOtherCarrier_WithItsRulesAndBalancers()
    {
        const string xray = """
        { "outbounds": [ { "tag": "proxy", "protocol": "socks", "settings": { "servers": [ { "address": "127.0.0.1", "port": 1080 } ] } },
                         { "tag": "spare", "protocol": "vless" }, { "tag": "direct", "protocol": "freedom" }, { "tag": "block", "protocol": "blackhole" } ],
          "routing": { "balancers": [ { "tag": "colitu-spare", "selector": [ "proxy", "spare" ] } ],
                       "rules": [ { "type": "field", "ip": [ "geoip:ru" ], "outboundTag": "direct" },
                                  { "type": "field", "inboundTag": [ "colitu-verify-spare" ], "outboundTag": "spare" },
                                  { "type": "field", "network": "tcp,udp", "balancerTag": "colitu-spare" },
                                  { "type": "field", "port": "53", "outboundTag": "proxy" } ] } }
        """;
        var x = JsonNode.Parse(ColituGeckoRules.ApplyOnly(xray, false, Plan())!)!.AsObject();
        x["outbounds"]!.AsArray().Select(item => S(item!["tag"])).Should().Equal("proxy", "direct", "block");
        x["routing"]!["balancers"].Should().BeNull();
        Rules(x, false).Select(rule => S(rule["outboundTag"])).Should().Equal("direct", "proxy");

        const string singbox = """
        { "inbounds": [ { "type": "mixed", "tag": "socks" } ],
          "outbounds": [ { "type": "socks", "tag": "proxy", "server": "127.0.0.1", "server_port": 1080 },
                         { "type": "hysteria2", "tag": "spare" }, { "type": "direct", "tag": "direct" } ],
          "route": { "final": "spare", "rules": [ { "inbound": [ "colitu-verify-spare" ], "outbound": "spare" },
                                                  { "rule_set": [ "geoip-ru" ], "outbound": "direct" },
                                                  { "action": "sniff" } ] } }
        """;
        var b = JsonNode.Parse(ColituGeckoRules.ApplyOnly(singbox, true, Plan())!)!.AsObject();
        b["outbounds"]!.AsArray().Select(item => S(item!["tag"])).Should().Equal("proxy", "direct");
        S(b["route"]!["final"]).Should().Be("proxy");
        Rules(b, true).Should().HaveCount(2).And.NotContain(rule => S(rule["outbound"]) == "spare");
        ColituGeckoRules.IsGeckoOnly(b.ToJsonString(), true, 42000).Should().BeTrue();
        ColituGeckoRules.IsGeckoOnly(singbox, true, 1080).Should().BeFalse("a spare could still carry traffic");
    }

    [Fact]
    public void GeckoProfile_HoldsNoSecret()
    {
        var profile = ColituGeckoRules.Profile("colitu-api", 42000);
        profile.IndexId.Should().Be(ColituGeckoRules.ProfileIndexId);
        profile.ConfigType.Should().Be(EConfigType.SOCKS);
        profile.Address.Should().Be("127.0.0.1");
        profile.Username.Should().BeNullOrEmpty();
        profile.Password.Should().BeNullOrEmpty();
    }

    [Fact]
    public void TunFront_GetsTheDirectRules_Once_AndANonTunConfigIsUntouched()
    {
        // The sing-box TUN front of an Xray tunnel: a TUN inbound and "proxy" to Xray's local port.
        const string front = """
        { "inbounds": [ { "type": "tun", "tag": "tun-in" } ],
          "outbounds": [ { "type": "socks", "tag": "proxy", "server": "127.0.0.1", "server_port": 10808 } ],
          "route": { "final": "proxy", "rules": [ { "process_name": [ "xray.exe", "sing-box.exe" ], "outbound": "direct" } ] } }
        """;
        var applied = ColituGeckoRules.AddSidecarDirectRules(front, ClientPath)!;
        var root = JsonNode.Parse(applied)!.AsObject();
        var rules = Rules(root, true);
        S(rules[0]["action"]).Should().Be("hijack-dns");
        ColituGeckoRules.IsDirectRule(rules[1], ClientPath).Should().BeTrue();
        root["outbounds"]!.AsArray().Should().Contain(item => S(item!["tag"]) == "direct" && S(item!["type"]) == "direct", "a direct outbound is added when missing");
        root["outbounds"]![0]!["server_port"]!.GetValue<int>().Should().Be(10808, "the front's own proxy (to Xray) is left alone");
        ColituGeckoRules.AddSidecarDirectRules(applied, ClientPath).Should().BeSameAs(applied, "already there");

        var xray = XrayConfig();
        ColituGeckoRules.AddSidecarDirectRules(xray, ClientPath).Should().BeSameAs(xray, "no TUN inbound");
        ColituGeckoRules.AddSidecarDirectRules("not json", ClientPath).Should().BeNull();
    }

    // ── Sidecar process ────────────────────────────────────────────────────

    [Fact]
    public void Sidecar_MissingBinary_Throws_AndLogsNoSecret()
    {
        var dir = Path.Combine(Path.GetTempPath(), "colitu-gecko-test-" + Guid.NewGuid().ToString("N"));
        ColituGecko.PathOverride = Path.Combine(dir, "missing", "gecko-client.exe");
        var log = new List<string>();
        try
        {
            var start = () => ColituGecko.Instance.Start(new ColituGeckoLaunch(42000, SocksUser, SocksPassword), Signed(), null, log.Add);
            start.Should().Throw<Exception>().Which.Message.Should().NotContain(ConfigSecret).And.NotContain(SocksPassword);
            ColituGecko.Instance.Running.Should().BeFalse();
            ColituGecko.Instance.SendConfig(Signed()).Should().BeFalse("nothing runs");
            log.Should().OnlyContain(line => !line.Contains(ConfigSecret) && !line.Contains(SocksPassword));
            Directory.Exists(dir).Should().BeFalse("nothing is written to disk");
        }
        finally
        {
            ColituGecko.PathOverride = null;
            ColituGecko.Instance.Stop();
        }
    }

    [Fact]
    public void DefaultPath_IsInTheBinFolder()
    {
        ColituGecko.DefaultPath.Should().EndWith(Path.Combine("bin", "colitu-gecko", "gecko-client.exe"));
    }

    // ── Grip failure reports to the panel ──────────────────────────────────

    private static ColituGeckoEvent Grip(string entry = "e1", string cls = "frozen") =>
        ColituGeckoRules.ParseEvent($"gecko-client: event {{\"event\":\"grip\",\"entry\":\"{entry}\",\"skin\":\"s1\",\"rev\":3,\"class\":\"{cls}\",\"bytes\":20000,\"bucket\":\"4-32KiB\"}}")!;

    [Fact]
    public void GripEvent_KeepsTheEntryForThePanel_ButNeverDescribesIt()
    {
        var evt = Grip("entry-77");
        evt.Entry.Should().Be("entry-77");
        evt.Describe().Should().NotContain("entry-77");
    }

    [Fact]
    public async Task Reports_GoInBatchesOfAHundred_InOrder_AsTheContractSays()
    {
        var reports = new ColituGeckoReports { App = "3.1.4" };
        var now = DateTimeOffset.FromUnixTimeSeconds(1_763_190_000);
        for (var i = 0; i < 150; i++) reports.Add(Grip($"e{i}"), now);
        var bodies = new List<string>();
        (await reports.FlushAsync(b => { bodies.Add(b); return Task.FromResult(System.Net.HttpStatusCode.NoContent); }, now)).Should().Be(100);
        reports.Waiting.Should().Be(50);
        (await reports.FlushAsync(b => { bodies.Add(b); return Task.FromResult(System.Net.HttpStatusCode.NoContent); }, now.AddSeconds(10)))
            .Should().Be(0, "at most one call a minute");
        (await reports.FlushAsync(b => { bodies.Add(b); return Task.FromResult(System.Net.HttpStatusCode.NoContent); }, now.AddSeconds(61))).Should().Be(50);
        var first = JsonNode.Parse(bodies[0])!.AsObject();
        first["v"]!.GetValue<int>().Should().Be(1);
        var list = first["reports"]!.AsArray();
        list.Count.Should().Be(100);
        list[0]!["entry"]!.GetValue<string>().Should().Be("e0");
        list[0]!["rev"]!.GetValue<long>().Should().Be(3);
        list[0]!["at"]!.GetValue<long>().Should().Be(1_763_190_000);
        list[0]!.AsObject().Select(p => p.Key).Should().BeEquivalentTo(["entry", "skin", "rev", "class", "bucket", "at", "app"]);
        list[0]!["app"]!.GetValue<string>().Should().Be("3.1.4");
    }

    [Theory]
    [InlineData(0, "<50")] [InlineData(49.9, "<50")] [InlineData(50, "50-100")] [InlineData(99.9, "50-100")]
    [InlineData(100, "100-200")] [InlineData(199.9, "100-200")] [InlineData(200, "200-400")] [InlineData(399.9, "200-400")]
    [InlineData(400, ">400")] [InlineData(5000, ">400")]
    public void Buckets_Rtt_EveryBoundary(double ms, string label) => ColituGeckoBuckets.Rtt(ms).Should().Be(label);

    [Theory]
    [InlineData(0, "<1")] [InlineData(0.99, "<1")] [InlineData(1, "1-5")] [InlineData(4.99, "1-5")]
    [InlineData(5, "5-20")] [InlineData(19.9, "5-20")] [InlineData(20, "20-100")] [InlineData(99.9, "20-100")]
    [InlineData(100, ">100")] [InlineData(900, ">100")]
    public void Buckets_Rate_EveryBoundary(double mbit, string label) => ColituGeckoBuckets.Rate(mbit).Should().Be(label);

    [Theory]
    [InlineData(0, "<10s")] [InlineData(9.9, "<10s")] [InlineData(10, "10-60s")] [InlineData(59.9, "10-60s")]
    [InlineData(60, "1-10m")] [InlineData(599.9, "1-10m")] [InlineData(600, ">10m")] [InlineData(86400, ">10m")]
    public void Buckets_Life_EveryBoundary(double seconds, string label) => ColituGeckoBuckets.Life(TimeSpan.FromSeconds(seconds)).Should().Be(label);

    [Fact]
    public void Buckets_RefuseUnusableValues_AndBadVersions()
    {
        ColituGeckoBuckets.Rtt(-1).Should().BeNull();
        ColituGeckoBuckets.Rate(double.NaN).Should().BeNull();
        ColituGeckoBuckets.Life(TimeSpan.FromSeconds(-1)).Should().BeNull();
        ColituGeckoBuckets.MbitPerSecond(1_000_000, TimeSpan.Zero).Should().BeNull();
        ColituGeckoBuckets.MbitPerSecond(12_500_000, TimeSpan.FromSeconds(10)).Should().Be(10);
        ColituGeckoBuckets.App("3.1.4+abc-1").Should().Be("3.1.4+abc-1");
        ColituGeckoBuckets.App("").Should().BeNull();
        ColituGeckoBuckets.App(new string('1', 33)).Should().BeNull();
        ColituGeckoBuckets.App("3.1 4").Should().BeNull();
    }

    [Fact]
    public async Task Reports_Ok_CarriesOnlyLabels_AndNoRawNumber()
    {
        var reports = new ColituGeckoReports { App = "3.1.4" };
        var now = DateTimeOffset.FromUnixTimeSeconds(1_763_190_000);
        reports.AddOk("e1", "s1", "3", now, rttMs: 73.4, rateMbit: ColituGeckoBuckets.MbitPerSecond(5_000_000, TimeSpan.FromSeconds(10)), life: TimeSpan.FromSeconds(754)).Should().BeTrue();
        string? body = null;
        await reports.FlushAsync(b => { body = b; return Task.FromResult(System.Net.HttpStatusCode.NoContent); }, now);
        var r = JsonNode.Parse(body!)!["reports"]!.AsArray()[0]!.AsObject();
        r.Select(p => p.Key).Should().BeEquivalentTo(["entry", "skin", "rev", "class", "at", "rtt", "rate", "life", "app"]);
        r["class"]!.GetValue<string>().Should().Be("ok");
        r["rtt"]!.GetValue<string>().Should().Be("50-100");
        r["rate"]!.GetValue<string>().Should().Be("1-5");
        r["life"]!.GetValue<string>().Should().Be(">10m");
        body.Should().NotContain("73").And.NotContain("754").And.NotContain("5000000");
        foreach (var key in new[] { "rtt", "rate", "life" }) r[key]!.GetValue<string>().Should().MatchRegex("^[<>0-9a-z-]+$");
    }

    [Fact]
    public async Task Reports_Ok_OnePerEntryPerTenMinutes_AndUnknownValuesAreLeftOut()
    {
        var reports = new ColituGeckoReports { App = null };
        var now = DateTimeOffset.FromUnixTimeSeconds(1_763_190_000);
        reports.AddOk("e1", "s1", "3", now).Should().BeTrue();
        reports.AddOk("e1", "s1", "3", now.AddMinutes(9)).Should().BeFalse();
        reports.AddOk("e2", "s1", "3", now.AddMinutes(9)).Should().BeTrue();
        reports.AddOk("e1", "s1", "3", now.AddMinutes(10)).Should().BeTrue();
        reports.Waiting.Should().Be(3);
        string? body = null;
        await reports.FlushAsync(b => { body = b; return Task.FromResult(System.Net.HttpStatusCode.NoContent); }, now.AddMinutes(11));
        JsonNode.Parse(body!)!["reports"]!.AsArray()[0]!.AsObject().Select(p => p.Key).Should().BeEquivalentTo(["entry", "skin", "rev", "class", "at"]);
    }

    [Fact]
    public async Task Reports_KeepAtMostThreeHundred_AndSurviveAFailure()
    {
        var reports = new ColituGeckoReports();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 320; i++) reports.Add(Grip($"e{i}"), now);
        reports.Waiting.Should().Be(ColituGeckoReports.MaxWaiting);
        (await reports.FlushAsync(_ => throw new System.Net.Http.HttpRequestException("down"), now)).Should().Be(0);
        reports.Waiting.Should().Be(ColituGeckoReports.MaxWaiting, "a failed call loses nothing");
        string? body = null;
        await reports.FlushAsync(b => { body = b; return Task.FromResult(System.Net.HttpStatusCode.NoContent); }, now.AddMinutes(2));
        JsonNode.Parse(body!)!["reports"]!.AsArray()[0]!["entry"]!.GetValue<string>().Should().Be("e20", "the oldest went first when the cap was hit");
    }

    private static string GripEndLine(string cls, string entry = "e1") =>
        $"gecko-client: event {{\"event\":\"grip_end\",\"entry\":\"{entry}\",\"skin\":\"s1\",\"rev\":3,\"class\":\"{cls}\",\"life_ms\":754321,\"sent\":111,\"received\":98765,\"rtt_ms\":73,\"goodput\":333444}}";

    [Fact]
    public async Task GripEnd_Ok_BecomesASlicedReport_WithoutRawNumbers()
    {
        var reports = new ColituGeckoReports { App = "3.1.4" };
        var evt = ColituGeckoRules.ParseEvent(GripEndLine("ok"))!;
        evt.Describe().Should().NotContain("754321").And.NotContain("333444");
        var now = DateTimeOffset.FromUnixTimeSeconds(1_763_190_000);
        reports.AddEnd(evt, now).Should().BeTrue();
        string? body = null;
        await reports.FlushAsync(b => { body = b; return Task.FromResult(System.Net.HttpStatusCode.NoContent); }, now);
        var r = JsonNode.Parse(body!)!["reports"]!.AsArray()[0]!.AsObject();
        r.Select(p => p.Key).Should().BeEquivalentTo(["entry", "skin", "rev", "class", "at", "rtt", "rate", "life", "app"]);
        r["class"]!.GetValue<string>().Should().Be("ok");
        r["rtt"]!.GetValue<string>().Should().Be("50-100");
        r["rate"]!.GetValue<string>().Should().Be("<1", "98765 B in 754 s is 0.001 Mbit/s; the goodput (upload direction) is not used");
        r["life"]!.GetValue<string>().Should().Be(">10m");
        foreach (var raw in new[] { "73,", "754321", "333444", "98765", "111" }) body.Should().NotContain(raw);
    }

    [Fact]
    public void GripEnd_RateIsTheBusierDirectionOverLife_AndNonOkOrIncompleteGivesNothing()
    {
        var reports = new ColituGeckoReports { App = null };
        var now = DateTimeOffset.UtcNow;
        reports.AddEnd(ColituGeckoRules.ParseEvent(GripEndLine("frozen"))!, now).Should().BeFalse();
        reports.AddEnd(ColituGeckoRules.ParseEvent(GripEndLine("lost"))!, now).Should().BeFalse();
        reports.AddEnd(ColituGeckoRules.ParseEvent("gecko-client: event {\"event\":\"grip_end\",\"class\":\"ok\"}")!, now).Should().BeFalse();
        reports.Waiting.Should().Be(0);
        // goodput 0 and rtt 0: not measured; 2_500_000 B in 10 s = 2 Mbit/s.
        var evt = ColituGeckoRules.ParseEvent("gecko-client: event {\"event\":\"grip_end\",\"entry\":\"e1\",\"skin\":\"s1\",\"rev\":3,\"class\":\"ok\",\"life_ms\":10000,\"sent\":1,\"received\":2500000,\"rtt_ms\":0,\"goodput\":0}")!;
        reports.AddEnd(evt, now).Should().BeTrue();
        // a download seen on the canary: 158539591 B in 25389 ms is about 50 Mbit/s; the goodput (642 B/s) is the upload reading.
        var big = ColituGeckoRules.ParseEvent("gecko-client: event {\"event\":\"grip_end\",\"entry\":\"e3\",\"skin\":\"s1\",\"rev\":3,\"class\":\"ok\",\"life_ms\":25389,\"sent\":1000,\"received\":158539591,\"rtt_ms\":0,\"goodput\":642}")!;
        reports.AddEnd(big, now).Should().BeTrue();
        // an upload: sent counts when it is the larger one (25 MB in 10 s = 20 Mbit/s).
        var up = ColituGeckoRules.ParseEvent("gecko-client: event {\"event\":\"grip_end\",\"entry\":\"e4\",\"skin\":\"s1\",\"rev\":3,\"class\":\"ok\",\"life_ms\":10000,\"sent\":25000000,\"received\":100,\"rtt_ms\":0,\"goodput\":0}")!;
        reports.AddEnd(up, now).Should().BeTrue();
        // too short a life for a rate (under 1 s), still a life slice.
        var brief = ColituGeckoRules.ParseEvent("gecko-client: event {\"event\":\"grip_end\",\"entry\":\"e2\",\"skin\":\"s1\",\"rev\":3,\"class\":\"ok\",\"life_ms\":900,\"sent\":1,\"received\":5000,\"rtt_ms\":0,\"goodput\":0}")!;
        reports.AddEnd(brief, now).Should().BeTrue();
        var body = "";
        reports.FlushAsync(b => { body = b; return Task.FromResult(System.Net.HttpStatusCode.NoContent); }, now).GetAwaiter().GetResult();
        var list = JsonNode.Parse(body)!["reports"]!.AsArray();
        list[0]!.AsObject().Select(p => p.Key).Should().BeEquivalentTo(["entry", "skin", "rev", "class", "at", "rate", "life"]);
        list[0]!["rate"]!.GetValue<string>().Should().Be("1-5");
        list[1]!["rate"]!.GetValue<string>().Should().Be("20-100");
        list[2]!["rate"]!.GetValue<string>().Should().Be("20-100");
        list[3]!.AsObject().Select(p => p.Key).Should().BeEquivalentTo(["entry", "skin", "rev", "class", "at", "life"]);
    }

    [Fact]
    public async Task Reports_StopOn404_PauseOn429()
    {
        var reports = new ColituGeckoReports();
        var now = DateTimeOffset.UtcNow;
        reports.Add(Grip(), now);
        await reports.FlushAsync(_ => Task.FromResult(System.Net.HttpStatusCode.TooManyRequests), now);
        reports.Waiting.Should().Be(1);
        var calls = 0;
        await reports.FlushAsync(_ => { calls++; return Task.FromResult(System.Net.HttpStatusCode.NoContent); }, now.AddMinutes(5));
        calls.Should().Be(0, "a 429 pauses for ten minutes");
        await reports.FlushAsync(_ => Task.FromResult(System.Net.HttpStatusCode.NotFound), now.AddMinutes(11));
        reports.Waiting.Should().Be(0);
        reports.Add(Grip(), now.AddMinutes(12));
        await reports.FlushAsync(_ => { calls++; return Task.FromResult(System.Net.HttpStatusCode.NoContent); }, now.AddMinutes(20));
        calls.Should().Be(0, "Gecko is not offered any more");
        reports.Resume();
        await reports.FlushAsync(_ => { calls++; return Task.FromResult(System.Net.HttpStatusCode.NoContent); }, now.AddMinutes(21));
        calls.Should().Be(1);
    }

    [Fact]
    public void Reports_IgnoreOtherEventsAndIncompleteGrips()
    {
        var reports = new ColituGeckoReports();
        reports.Add(ColituGeckoRules.ParseEvent("gecko-client: event {\"event\":\"lost\"}")!, DateTimeOffset.UtcNow).Should().BeFalse();
        reports.Add(ColituGeckoRules.ParseEvent("gecko-client: event {\"event\":\"grip\",\"skin\":\"s1\",\"class\":\"frozen\"}")!, DateTimeOffset.UtcNow).Should().BeFalse();
        reports.Waiting.Should().Be(0);
    }
}
