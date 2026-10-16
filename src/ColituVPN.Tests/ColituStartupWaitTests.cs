using AwesomeAssertions;
using Xunit;

namespace v2rayN.Tests;

/// <summary>Start-up waits briefly for the panel; while the kill switch is held from the last run the PC is offline anyway.</summary>
public class ColituStartupWaitTests
{
    [Fact]
    public void HeldKillSwitch_WaitsTwoSeconds_OtherwiseSix()
    {
        v2rayN.Views.ColituMainWindow.StartupPanelWaitFor(killSwitchHeld: true).Should().Be(TimeSpan.FromSeconds(2));
        v2rayN.Views.ColituMainWindow.StartupPanelWaitFor(killSwitchHeld: false).Should().Be(TimeSpan.FromSeconds(6));
    }
}
