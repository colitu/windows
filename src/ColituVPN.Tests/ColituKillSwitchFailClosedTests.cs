using System.IO;
using AwesomeAssertions;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

/// <summary>
/// Fail-closed (owner decision 2026-10-12) as behaviour: <see cref="ColituKillSwitchHold"/> drives a
/// fake kill switch through the paths of a connect (failed, cancelled, automatic after a crash,
/// network change, quit) and the test reads what was released. ColituVpnService delegates every
/// one of these decisions to it.
/// </summary>
public class ColituKillSwitchFailClosedTests
{
    private sealed class FakeKillSwitch : IColituKillSwitchControl
    {
        public bool Engaged { get; set; } = true;
        public List<string> Releases { get; } = [];

        public Task ReleaseAsync(string reason)
        {
            Releases.Add(reason);
            Engaged = false;
            return Task.CompletedTask;
        }
    }

    private static (FakeKillSwitch KillSwitch, ColituKillSwitchHold Hold) Armed()
    {
        var killSwitch = new FakeKillSwitch();
        var hold = new ColituKillSwitchHold(killSwitch);
        // As ColituVpnService does: every release goes through the hold too.
        return (killSwitch, hold);
    }

    // ── Connect by hand ──────────────────────────────────────────────────
    [Fact]
    public async Task FailedConnect_KeepsTheKillSwitch_AndSaysSo()
    {
        var (killSwitch, hold) = Armed();
        hold.ConnectStarted();

        (await hold.ConnectFailedAsync(killSwitchEnabled: true, devicePaused: false, noPlan: false)).Should().BeTrue();

        killSwitch.Releases.Should().BeEmpty("the real address must not leave");
        killSwitch.Engaged.Should().BeTrue();
        hold.HeldAfterFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false, false, "connection failed, kill switch is off")]
    [InlineData(true, true, false, "device paused")]
    [InlineData(true, false, true, "no active plan")]
    public async Task FailedConnect_NothingToReconnectTo_OrOff_Releases(bool enabled, bool paused, bool noPlan, string reason)
    {
        var (killSwitch, hold) = Armed();

        (await hold.ConnectFailedAsync(enabled, paused, noPlan)).Should().BeFalse();

        killSwitch.Releases.Should().Equal(reason);
    }

    [Fact]
    public async Task FailedConnect_WithoutAnArmedKillSwitch_DoesNothing()
    {
        var (killSwitch, hold) = Armed();
        killSwitch.Engaged = false;

        (await hold.ConnectFailedAsync(true, false, false)).Should().BeFalse();

        killSwitch.Releases.Should().BeEmpty();
        hold.HeldAfterFailure.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task CancelledConnect_ReleasesOnlyAnArmThisAttemptMade(bool armedByThisAttempt, int releases)
    {
        var (killSwitch, hold) = Armed();

        await hold.ConnectCancelledAsync(armedByThisAttempt);

        killSwitch.Releases.Should().HaveCount(releases);
    }

    [Fact]
    public async Task RetryAfterAFailure_ClearsTheFailureState_ConnectedClearsAll()
    {
        var (_, hold) = Armed();
        await hold.ConnectFailedAsync(true, false, false);

        hold.ConnectStarted();
        hold.HeldAfterFailure.Should().BeFalse("a new attempt is running");

        await hold.ConnectFailedAsync(true, false, false);
        hold.Connected();
        hold.HeldAfterFailure.Should().BeFalse();
        hold.HeldFromPreviousRun.Should().BeFalse();
    }

    // ── Automatic connect after a crash (W3-01) ──────────────────────────
    [Fact]
    public async Task CrashThenAutoConnectFails_KeepsTheKillSwitch()
    {
        var (killSwitch, hold) = Armed();
        hold.HoldFromPreviousRun();

        var kept = await hold.AutoConnectEndedAsync(ColituAutoConnectOutcome.Failed, killSwitchEnabled: true, connecting: false);

        kept.Should().BeTrue();
        killSwitch.Releases.Should().BeEmpty("a failed automatic connect is fail-closed like a failed connect by hand");
        killSwitch.Engaged.Should().BeTrue();
        hold.HeldAfterFailure.Should().BeTrue("the screen says the connect failed, not that it is reconnecting");
        hold.HeldFromPreviousRun.Should().BeFalse();
    }

    [Theory]
    [InlineData(ColituAutoConnectOutcome.NothingToConnect, true, "auto-connect: nothing to connect to")]
    [InlineData(ColituAutoConnectOutcome.NotAttempted, true, "auto-connect did not run")]
    [InlineData(ColituAutoConnectOutcome.Failed, false, "auto-connect failed, kill switch is off")]
    public async Task CrashThenNoAutoConnect_OrKillSwitchOff_Releases(ColituAutoConnectOutcome outcome, bool enabled, string reason)
    {
        var (killSwitch, hold) = Armed();
        hold.HoldFromPreviousRun();

        (await hold.AutoConnectEndedAsync(outcome, enabled, connecting: false)).Should().BeFalse();

        killSwitch.Releases.Should().Equal(reason);
    }

    [Fact]
    public async Task CrashThenTheUserConnectsMeanwhile_NothingIsReleased()
    {
        var (killSwitch, hold) = Armed();
        hold.HoldFromPreviousRun();

        await hold.AutoConnectEndedAsync(ColituAutoConnectOutcome.NotAttempted, true, connecting: true);
        await hold.AutoConnectEndedAsync(ColituAutoConnectOutcome.Connected, true, connecting: false);

        killSwitch.Releases.Should().BeEmpty();
    }

    [Fact]
    public async Task AutoConnectFails_WithoutACrash_LeavesTheKillSwitchToTheConnectPath()
    {
        var (killSwitch, hold) = Armed();

        (await hold.AutoConnectEndedAsync(ColituAutoConnectOutcome.Failed, true, connecting: false)).Should().BeFalse();

        killSwitch.Releases.Should().BeEmpty();
    }

    [Fact]
    public async Task SignOutAfterACrash_ReleasesTheHeldKillSwitch()
    {
        var (killSwitch, hold) = Armed();
        hold.HoldFromPreviousRun();

        (await hold.ReleaseHeldAsync("signed out", connecting: false)).Should().BeTrue();

        killSwitch.Releases.Should().Equal("signed out");
    }

    // ── One retry per new network (W3-04) ────────────────────────────────
    [Fact]
    public async Task NetworkChange_AfterAFailure_RetriesOncePerNewNetwork()
    {
        var (_, hold) = Armed();
        await hold.ConnectFailedAsync(true, false, false);
        hold.FailedOn("home-wifi");

        hold.TakeNetworkRetry("home-wifi").Should().BeFalse("the failure happened on this network");
        hold.TakeNetworkRetry("hotel-wifi").Should().BeTrue();
        hold.TakeNetworkRetry("hotel-wifi").Should().BeFalse("one retry per network");
        hold.TakeNetworkRetry(null).Should().BeFalse();
    }

    [Fact]
    public void NetworkChange_WithoutAHeldFailure_NoRetry()
    {
        var (_, hold) = Armed();

        hold.TakeNetworkRetry("hotel-wifi").Should().BeFalse();
    }

    // ── Quit and sign-out ────────────────────────────────────────────────
    [Theory]
    [InlineData(ColituVpnStatus.Error, true, true)]
    [InlineData(ColituVpnStatus.Disconnected, true, true)]
    [InlineData(ColituVpnStatus.Connected, false, true)]
    [InlineData(ColituVpnStatus.Connecting, false, true)]
    [InlineData(ColituVpnStatus.Error, false, false)]
    [InlineData(ColituVpnStatus.Disconnected, false, false)]
    public void QuitOrSignOut_DisconnectsWheneverATunnelRunsOrTheKillSwitchBlocks(ColituVpnStatus status, bool engaged, bool disconnect)
    {
        ColituKillSwitchHold.DisconnectOnExit(status, engaged).Should().Be(disconnect);
    }

    [Fact]
    public void ServiceAndWindow_UseTheHold()
    {
        // The paths above are the ones the app runs: the service and the window call the hold.
        var root = AppSource();
        var service = File.ReadAllText(Path.Combine(root, "Services", "ColituVpnService.cs"));
        service.Should().Contain("_hold.ConnectFailedAsync(").And.Contain("_hold.ConnectCancelledAsync(")
            .And.Contain("_hold.AutoConnectEndedAsync(").And.Contain("_hold.TakeNetworkRetry(")
            .And.Contain("_hold.Released();").And.NotContain("auto-connect did not connect");
        File.ReadAllText(Path.Combine(root, "Views", "ColituMainWindow.xaml.cs")).Should().Contain("ColituKillSwitchHold.DisconnectOnExit(");
    }

    [Theory]
    [InlineData("tr", "Bağlanılamadı. Kill switch internetinizi koruyor; yeniden deneyin ya da bağlantıyı kesin.")]
    [InlineData("en", "Couldn’t connect. The kill switch is protecting your internet; try again or disconnect.")]
    [InlineData("ru", "Не удалось подключиться. Kill switch защищает ваш интернет; повторите попытку или отключитесь.")]
    public void FailedConnectMessage_InEveryLanguage_WithTheSignInPageHint(string language, string expected)
    {
        var previous = Loc.I.Language;
        try
        {
            Loc.I.SetLanguage(language);
            Loc.I["home.sub.blockedFailed"].Should().StartWith(expected).And.Contain("Wi-Fi");
        }
        finally
        {
            Loc.I.SetLanguage(previous);
        }
    }

    private static string AppSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ColituVPN", "Services")))
        {
            dir = dir.Parent;
        }
        return Path.Combine(dir!.FullName, "ColituVPN");
    }
}
