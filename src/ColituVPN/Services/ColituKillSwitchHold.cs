namespace v2rayN.Services;

/// <summary>How an automatic connect at start ended.</summary>
public enum ColituAutoConnectOutcome
{
    /// <summary>The tunnel is up.</summary>
    Connected,

    /// <summary>Not tried: auto-connect is off, the user connected or disconnected meanwhile.</summary>
    NotAttempted,

    /// <summary>Nothing to connect to: no active plan, or this device is paused.</summary>
    NothingToConnect,

    /// <summary>Tried and failed (every attempt, or no network for minutes).</summary>
    Failed
}

/// <summary>The kill switch as the hold rules see it: whether it blocks, and how to open it.</summary>
public interface IColituKillSwitchControl
{
    bool Engaged { get; }

    Task ReleaseAsync(string reason);
}

/// <summary>
/// Fail-closed (owner decision 2026-10-12): the one place that decides whether the kill switch may
/// open after a connect did not work. A connect that fails, by hand or automatically after a crash,
/// keeps it armed; only "nothing to connect to" (paused device, no plan), the kill switch being
/// off, a cancelled attempt that armed it itself, Disconnect, sign-out and quitting open it.
/// </summary>
public sealed class ColituKillSwitchHold(IColituKillSwitchControl killSwitch)
{
    private volatile bool _heldFromPreviousRun;
    private volatile bool _heldAfterFailure;
    private string? _retriedNetwork;

    /// <summary>Armed by a previous run that ended without disarming (crash, power loss); kept for the auto-connect.</summary>
    public bool HeldFromPreviousRun => _heldFromPreviousRun && killSwitch.Engaged;

    /// <summary>A connect failed with the kill switch on: the internet stays closed until a retry connects or the user disconnects.</summary>
    public bool HeldAfterFailure => _heldAfterFailure && killSwitch.Engaged;

    public void HoldFromPreviousRun() => _heldFromPreviousRun = true;

    /// <summary>A new attempt starts: "couldn't connect" no longer describes the state.</summary>
    public void ConnectStarted() => _heldAfterFailure = false;

    public void Connected()
    {
        _heldFromPreviousRun = false;
        _heldAfterFailure = false;
        _retriedNetwork = null;
    }

    /// <summary>The kill switch was released (by any path).</summary>
    public void Released()
    {
        _heldFromPreviousRun = false;
        _heldAfterFailure = false;
        _retriedNetwork = null;
    }

    /// <summary>Whether a failed connect releases an engaged kill switch: only when there is nothing to reconnect to or it is off.</summary>
    public static bool ReleaseAfterFailure(bool killSwitchEnabled, bool devicePaused, bool planError)
        => !killSwitchEnabled || devicePaused || planError;

    /// <summary>A connect failed. True when the kill switch stays armed (fail-closed).</summary>
    public async Task<bool> ConnectFailedAsync(bool killSwitchEnabled, bool devicePaused, bool noPlan)
    {
        if (!killSwitch.Engaged)
        {
            return false;
        }
        if (ReleaseAfterFailure(killSwitchEnabled, devicePaused, noPlan))
        {
            await killSwitch.ReleaseAsync(devicePaused ? "device paused" : noPlan ? "no active plan" : "connection failed, kill switch is off");
            return false;
        }
        _heldAfterFailure = true;
        return true;
    }

    /// <summary>The user cancelled a connect: an arm this attempt made itself goes again.</summary>
    public async Task ConnectCancelledAsync(bool armedByThisAttempt)
    {
        if (armedByThisAttempt && killSwitch.Engaged)
        {
            await killSwitch.ReleaseAsync("connection attempt cancelled");
        }
    }

    /// <summary>
    /// The automatic connect after start ended. With the kill switch held from a crashed run, a
    /// failed connect keeps it (fail-closed, as a failed connect by hand); "nothing to connect to"
    /// or a kill switch that is now off opens it. True when it stays armed after a failure.
    /// </summary>
    public async Task<bool> AutoConnectEndedAsync(ColituAutoConnectOutcome outcome, bool killSwitchEnabled, bool connecting)
    {
        if (outcome == ColituAutoConnectOutcome.Connected || !_heldFromPreviousRun || connecting || !killSwitch.Engaged)
        {
            return false;
        }
        if (outcome == ColituAutoConnectOutcome.Failed && killSwitchEnabled)
        {
            _heldFromPreviousRun = false;
            _heldAfterFailure = true;
            return true;
        }
        await killSwitch.ReleaseAsync(outcome switch
        {
            ColituAutoConnectOutcome.NothingToConnect => "auto-connect: nothing to connect to",
            ColituAutoConnectOutcome.Failed => "auto-connect failed, kill switch is off",
            _ => "auto-connect did not run"
        });
        return false;
    }

    /// <summary>The held state of a crashed run ends without a connect (sign-out, no plan): open it. True when released.</summary>
    public async Task<bool> ReleaseHeldAsync(string reason, bool connecting)
    {
        if (!_heldFromPreviousRun || connecting)
        {
            return false;
        }
        await killSwitch.ReleaseAsync(reason);
        return true;
    }

    /// <summary>The network a held failure happened on: it gets no automatic retry, only a different one does.</summary>
    public void FailedOn(string? network) => _retriedNetwork = network;

    /// <summary>
    /// After a failed connect the internet stays closed; when the network changes (another Wi-Fi,
    /// the adapter came back) one automatic retry runs per network. True when this network gets it.
    /// </summary>
    public bool TakeNetworkRetry(string? network)
    {
        if (!HeldAfterFailure || string.IsNullOrEmpty(network) || string.Equals(network, _retriedNetwork, StringComparison.Ordinal))
        {
            return false;
        }
        _retriedNetwork = network;
        return true;
    }

    /// <summary>Quitting or signing out disconnects whenever a tunnel runs or the kill switch blocks: nothing stays closed behind the user.</summary>
    public static bool DisconnectOnExit(ColituVpnStatus status, bool killSwitchEngaged)
        => status is ColituVpnStatus.Connected or ColituVpnStatus.Connecting or ColituVpnStatus.Reconnecting || killSwitchEngaged;
}
