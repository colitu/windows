using System.IO;
using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

/// <summary>
/// 2026-10-12 update: ad-blocking resolvers from the panel's bootstrap, the TUN ping targets kept
/// for the first automatic connect, and stale update packages removed.
/// </summary>
public class ColituStep3FixesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("colitu-test-").FullName;

    public void Dispose()
    {
        ColituAdBlock.ResetForTest(null);
        try { Directory.Delete(_folder, true); } catch { }
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    [Fact]
    public void AdBlock_ServersOf_ReadsOnlyHttpsUrls()
    {
        ColituAdBlock.ServersOf(Json("""{"ad_block":{"doh":["https://dns-a.example.test:3443/dns-query","http://plain.example.test/dns-query","https://user:pw@x.example.test/q","not a url","https://dns-b.example.test/dns-query"]}}"""))
            .Should().Equal("https://dns-a.example.test:3443/dns-query", "https://dns-b.example.test/dns-query");
        ColituAdBlock.ServersOf(Json("""{"ad_block":{"doh":[]}}""")).Should().BeEmpty();
        ColituAdBlock.ServersOf(Json("""{"user":{}}""")).Should().BeNull("an older panel without the field changes nothing");
        ColituAdBlock.ServersOf(Json("""{"ad_block":{"doh":"https://x.example.test"}}""")).Should().BeNull();
    }

    [Fact]
    public void AdBlock_Remember_KeepsThePanelList_FieldMissingKeepsIt()
    {
        var path = Path.Combine(_folder, "adblock.json");
        ColituAdBlock.ResetForTest(null);
        ColituAdBlock.Servers.Should().Equal(ColituAdBlock.Embedded, "without a panel answer the build's list is used");

        ColituAdBlock.Remember(Json("""{"ad_block":{"doh":["https://dns-a.example.test/dns-query"]}}"""), path).Should().BeTrue();
        ColituAdBlock.Servers.Should().Equal("https://dns-a.example.test/dns-query");
        File.ReadAllText(path).Should().Contain("dns-a.example.test");

        ColituAdBlock.Remember(Json("""{"user":{}}"""), path).Should().BeFalse();
        ColituAdBlock.Servers.Should().Equal("https://dns-a.example.test/dns-query");

        ColituAdBlock.Remember(Json("""{"ad_block":{"doh":["https://dns-a.example.test/dns-query"]}}"""), path).Should().BeFalse("unchanged");

        // The panel has no list configured yet: ad blocking keeps working with the kept list.
        ColituAdBlock.Remember(Json("""{"ad_block":{"doh":[]}}"""), path).Should().BeFalse();
        ColituAdBlock.Servers.Should().Equal("https://dns-a.example.test/dns-query");
        File.ReadAllText(path).Should().Contain("dns-a.example.test");
    }

    [Fact]
    public void PingTargets_SavedAndLoaded()
    {
        var path = Path.Combine(_folder, "targets.json");
        ColituPingRoute.LoadTargets(path).Should().BeEmpty();
        ColituPingRoute.SaveTargets(path, [IPAddress.Parse("203.0.113.7"), IPAddress.Parse("10.0.0.1"), IPAddress.Parse("198.51.100.20")]);
        ColituPingRoute.LoadTargets(path).Select(address => address.ToString()).Should().Equal("203.0.113.7", "198.51.100.20");
        File.WriteAllText(path, "{ broken");
        ColituPingRoute.LoadTargets(path).Should().BeEmpty();
    }

    [Fact]
    public void Updates_StalePackagesRemoved_NewerKept()
    {
        File.WriteAllText(Path.Combine(_folder, "ColituVPN-new-290.exe"), "old");
        File.WriteAllText(Path.Combine(_folder, "ColituVPN-new-291.exe"), "same");
        File.WriteAllText(Path.Combine(_folder, "ColituVPN-new-292.exe"), "newer");
        File.WriteAllText(Path.Combine(_folder, "apply-update.ps1"), "script");
        File.WriteAllText(Path.Combine(_folder, "apply-update.log"), "log");

        ColituUpdateService.DeleteStalePackages(_folder, 291);

        Directory.GetFiles(_folder).Select(Path.GetFileName).Should().BeEquivalentTo(["ColituVPN-new-292.exe", "apply-update.log"]);
    }
}
