using System.IO;
using v2rayN.Services;
using AwesomeAssertions;
using Xunit;

namespace v2rayN.Tests;

public class ColituSecurityAuditTests
{
    [Fact]
    public void TurningKillSwitchOffWhileConnectedInTunRestartsWithStrictRouteFirst()
    {
        // Connected, TUN, core built without strict route, filters still armed: restart before release.
        ColituVpnService.NeedsStrictRouteBeforeRelease(connected: true, tun: true, strictRoute: false, killSwitchEngaged: true).Should().BeTrue();
        // Already strict, proxy mode, not connected or nothing armed: release as before.
        ColituVpnService.NeedsStrictRouteBeforeRelease(true, true, true, true).Should().BeFalse();
        ColituVpnService.NeedsStrictRouteBeforeRelease(true, false, false, true).Should().BeFalse();
        ColituVpnService.NeedsStrictRouteBeforeRelease(false, true, false, true).Should().BeFalse();
        ColituVpnService.NeedsStrictRouteBeforeRelease(true, true, false, false).Should().BeFalse();
    }

    [Fact]
    public void InAppKillSwitchFallbackHonoursBlockLocalNetwork()
    {
        ColituKillSwitch.PermittedLocalNetworks(allowLan: true).Should().BeEquivalentTo(ColituKillSwitch.LocalNetworks);
        ColituKillSwitch.PermittedLocalNetworks(allowLan: false).Should().BeEmpty();
    }

    [Fact]
    public void SkippedStrictRouteRestartKeepsTheFilters()
    {
        // Nothing to restart: release at once.
        ColituVpnService.DecideStrictRelease(false, ColituCoreRestartOutcome.Skipped, false).Should().Be(ColituStrictReleaseAction.Release);
        // Restart ran: the strict core is up, filters may go.
        ColituVpnService.DecideStrictRelease(true, ColituCoreRestartOutcome.Restarted, false).Should().Be(ColituStrictReleaseAction.Release);
        // Restart skipped (lock busy, not connected, cancelled): the old non-strict core still runs.
        ColituVpnService.DecideStrictRelease(true, ColituCoreRestartOutcome.Skipped, false).Should().Be(ColituStrictReleaseAction.Defer);
        ColituVpnService.DecideStrictRelease(true, ColituCoreRestartOutcome.Skipped, true).Should().Be(ColituStrictReleaseAction.Defer);
        // Restart failed: never leave the user offline; open up and reconnect (strict route is on now).
        ColituVpnService.DecideStrictRelease(true, ColituCoreRestartOutcome.Failed, false).Should().Be(ColituStrictReleaseAction.ReleaseAndReconnect);
        ColituVpnService.DecideStrictRelease(true, ColituCoreRestartOutcome.Failed, true).Should().Be(ColituStrictReleaseAction.Release);
    }

    [Fact]
    public void ApiBaseOverridesOnlyInDebugBuilds()
    {
#if DEBUG
        ColituAuthService.ApiBaseOverridesEnabled.Should().BeTrue();
#else
        ColituAuthService.ApiBaseOverridesEnabled.Should().BeFalse();
#endif
    }

    [Fact]
    public void ApiConfigFileIsReadOnlyInsideDebugBlock()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ColituVPN", "Services")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull();
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "ColituVPN", "Services", "ColituAuthService.cs"));
        var start = source.IndexOf("private static string? ResolveApiBaseOverride()", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        var body = source[start..source.IndexOf("internal static bool IsAllowedApiBaseUrl", start, StringComparison.Ordinal)];
        var debugStart = body.IndexOf("#if DEBUG", StringComparison.Ordinal);
        var debugEnd = body.IndexOf("#endif", StringComparison.Ordinal);
        var file = body.IndexOf("colitu-api.json", StringComparison.Ordinal);
        var env = body.IndexOf("COLITU_API_BASE_URL", StringComparison.Ordinal);
        debugStart.Should().BeGreaterThan(0);
        file.Should().BeGreaterThan(debugStart).And.BeLessThan(debugEnd);
        env.Should().BeGreaterThan(debugStart).And.BeLessThan(debugEnd);
    }
}
