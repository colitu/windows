using System.IO;
using AwesomeAssertions;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

/// <summary>
/// 2026-10-12 owner decision: with the kill switch on, a failed connect keeps it armed
/// (fail-closed); Disconnect, turning it off and quitting the app release it.
/// </summary>
public class ColituKillSwitchFailClosedTests
{
    [Fact]
    public void FailedConnect_KeepsTheKillSwitchArmed()
    {
        // Every server and transport tried, or no internet: the real address must not leave.
        ColituVpnService.ReleaseKillSwitchAfterFailure(killSwitchEnabled: true, devicePaused: false, planError: false)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void NothingToReconnectTo_ReleasesTheKillSwitch(bool devicePaused, bool planError)
    {
        ColituVpnService.ReleaseKillSwitchAfterFailure(killSwitchEnabled: true, devicePaused, planError)
            .Should().BeTrue();
    }

    [Fact]
    public void KillSwitchTurnedOff_ReleasesAfterAFailure()
    {
        ColituVpnService.ReleaseKillSwitchAfterFailure(killSwitchEnabled: false, devicePaused: false, planError: false)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("tr", "Bağlanılamadı. Kill switch internetinizi koruyor; yeniden deneyin ya da bağlantıyı kesin.")]
    [InlineData("en", "Couldn’t connect. The kill switch is protecting your internet; try again or disconnect.")]
    [InlineData("ru", "Не удалось подключиться. Kill switch защищает ваш интернет; повторите попытку или отключитесь.")]
    public void FailedConnectMessage_InEveryLanguage(string language, string expected)
    {
        var previous = Loc.I.Language;
        try
        {
            Loc.I.SetLanguage(language);
            Loc.I["home.sub.blockedFailed"].Should().Be(expected);
        }
        finally
        {
            Loc.I.SetLanguage(previous);
        }
    }

    [Fact]
    public void Source_FailedConnectDoesNotReleaseUnconditionally_AndExitReleases()
    {
        var root = FindAppSource();
        var service = File.ReadAllText(Path.Combine(root, "Services", "ColituVpnService.cs"));
        // The old fail-open release of a manual connect is gone.
        service.Should().NotContain("\"connection failed\")");
        service.Should().Contain("ReleaseKillSwitchAfterFailure(Preferences.KillSwitchEnabled");
        // Disconnect and turning the kill switch off still release it.
        service.Should().Contain("ReleaseKillSwitchAsync(\"disconnected by the user\")");
        service.Should().Contain("ReleaseKillSwitchAsync(\"kill switch turned off\")");
        // Quitting disconnects while the kill switch holds the internet closed.
        var window = File.ReadAllText(Path.Combine(root, "Views", "ColituMainWindow.xaml.cs"));
        window.Should().Contain("|| _vpn.KillSwitchEngaged)");
    }

    private static string FindAppSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ColituVPN", "Services")))
        {
            dir = dir.Parent;
        }
        return Path.Combine(dir!.FullName, "ColituVPN");
    }
}
