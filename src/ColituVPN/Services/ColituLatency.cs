using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace v2rayN.Services;

/// <summary>
/// Ping of a location: the TCP connect time from this PC to the node's latency
/// probe address (the panel's latency_host / latency_port), best of two tries,
/// like the phone apps. The socket is pinned to the physical adapter with
/// IP_UNICAST_IF, the same way the core keeps its own traffic out of the TUN
/// adapter, so the number describes this PC's own path to the server even while
/// the VPN is connected. In system-proxy mode sockets bypass the proxy anyway.
/// </summary>
public static class ColituLatency
{
    private const int TimeoutMs = 2500;
    private const int IpUnicastIf = 31;

    /// <summary>At most this many servers are pinged per round (a bigger list is cut, not dialled).</summary>
    internal const int MaxProbes = 200;

    /// <summary>At most this many pings run at the same time.</summary>
    internal const int MaxConcurrency = 8;

    /// <summary>
    /// TCP ports a node's probe address may use. A tampered server list cannot point the ping at
    /// other services (SMB, RDP, mail...) to scan them from this PC's address.
    /// </summary>
    internal static readonly IReadOnlySet<int> AllowedPorts =
        new HashSet<int> { 443, 2053, 2083, 2087, 2096, 3443, 4433, 7443, 8388, 8443, 9443 };

    /// <summary>Pings of every server with a probe address (see <see cref="ProbeTargets"/>), by server id.</summary>
    public static async Task<Dictionary<string, int>> MeasureAllAsync(IEnumerable<ColituVpnServer> servers) =>
        (await MeasureRoundAsync(servers, requirePin: false)).Ms;

    /// <summary>
    /// One ping round. With <paramref name="requirePin"/> (the VPN is up) a probe whose socket
    /// cannot be pinned to the physical adapter is not dialled at all: it would measure the tunnel.
    /// Such servers are listed in <see cref="ColituPingRound.Unpinned"/>, not counted as failed.
    /// </summary>
    public static async Task<ColituPingRound> MeasureRoundAsync(IEnumerable<ColituVpnServer> servers, bool requirePin)
    {
        var index = ColituNetwork.PhysicalInterfaceIndex();
        using var gate = new SemaphoreSlim(MaxConcurrency);
        var probes = ProbeTargets(servers)
            .Select(async server =>
            {
                await gate.WaitAsync();
                try
                {
                    var (ms, unpinned) = await MeasureAsync(server.Host!, server.Port!.Value, index, requirePin);
                    return (Id: server.Id!, Ms: ms, Unpinned: unpinned);
                }
                finally
                {
                    gate.Release();
                }
            });
        var results = await Task.WhenAll(probes);
        return new ColituPingRound(
            results.Where(result => result.Ms != null)
                .ToDictionary(result => result.Id, result => result.Ms!.Value, StringComparer.OrdinalIgnoreCase),
            results.Where(result => result.Unpinned).Select(result => result.Id).ToHashSet(StringComparer.OrdinalIgnoreCase),
            index);
    }

    /// <summary>The servers one round pings: those with a probe address, one per id, at most <see cref="MaxProbes"/>.</summary>
    public static List<ColituVpnServer> ProbeTargets(IEnumerable<ColituVpnServer> servers) =>
        servers
            .Where(CanProbe)
            .GroupBy(server => server.Id!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(MaxProbes)
            .ToList();

    /// <summary>
    /// The public IPv4 addresses the probes of <paramref name="servers"/> dial (same resolver and
    /// filters as <see cref="MeasureAsync(string, int, int?, bool)"/>), for the TUN ping rule
    /// (<see cref="ColituPingRoute"/>). Whatever does not resolve within <paramref name="budget"/>
    /// is left out: those probes rely on the adapter pin alone.
    /// </summary>
    public static async Task<IReadOnlyList<IPAddress>> ResolveTargetsAsync(IEnumerable<ColituVpnServer> servers, TimeSpan budget, CancellationToken token = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(budget);
        using var gate = new SemaphoreSlim(MaxConcurrency);
        var hosts = ProbeTargets(servers)
            .Select(server => server.Host!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var lookups = hosts.Select(async host =>
        {
            try
            {
                await gate.WaitAsync(deadline.Token);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            try
            {
                return await ColituNetwork.ResolveServerAsync(host, deadline.Token);
            }
            catch
            {
                return null;
            }
            finally
            {
                gate.Release();
            }
        });
        var addresses = await Task.WhenAll(lookups);
        return ColituPingRoute.Targets(addresses.OfType<IPAddress>());
    }

    /// <summary>The server has a probe address: a missing result from <see cref="MeasureAllAsync"/> means the ping failed.</summary>
    public static bool CanProbe(ColituVpnServer server) =>
        server.Id.IsNotEmpty() && server.Host.IsNotEmpty() && server.Port is { } port && AllowedPorts.Contains(port);

    public static async Task<int?> MeasureAsync(string host, int port, int? interfaceIndex) =>
        (await MeasureAsync(host, port, interfaceIndex, requirePin: false)).Ms;

    /// <summary>Best of two connects; <c>Unpinned</c> when <paramref name="requirePin"/> kept the probe from dialling.</summary>
    public static async Task<(int? Ms, bool Unpinned)> MeasureAsync(string host, int port, int? interfaceIndex, bool requirePin)
    {
        if (requirePin && interfaceIndex is not > 0)
        {
            return (null, true);
        }
        IPAddress? address;
        try
        {
            // Resolved first (DNS over HTTPS, cached) so the lookup is not counted as ping
            // and a fake-IP answer from another VPN client is never dialled.
            using var resolveTimeout = new CancellationTokenSource(TimeoutMs);
            address = await ColituNetwork.ResolveServerAsync(host, resolveTimeout.Token);
        }
        catch
        {
            return (null, false);
        }
        if (address == null || address.AddressFamily != AddressFamily.InterNetwork || !IsPublic(address))
        {
            return (null, false);
        }

        int? best = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var (ms, unpinned) = await ProbeAsync(address, port, interfaceIndex, requirePin, TimeoutMs);
            if (unpinned)
            {
                return (null, true);
            }
            if (ms != null && (best == null || ms < best))
            {
                best = ms;
            }
        }
        return (best, false);
    }

    /// <summary>
    /// VPN nodes are on the internet: a probe address in loopback, private, CGNAT or
    /// link-local space (a bad or tampered server list) is never dialled, so the ping
    /// cannot be turned into a scan of this computer or its local network.
    /// </summary>
    internal static bool IsPublic(IPAddress address)
    {
        var b = address.GetAddressBytes();
        if (b.Length != 4) return false;
        return !(b[0] is 0 or 10 or 127
            || b[0] >= 224
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168));
    }

    /// <summary>TCP connect time in ms over the adapter <paramref name="interfaceIndex"/>; null when it fails or times out.</summary>
    internal static async Task<int?> ConnectMsAsync(IPAddress address, int port, int? interfaceIndex, int timeoutMs = TimeoutMs) =>
        (await ProbeAsync(address, port, interfaceIndex, requirePin: false, timeoutMs)).Ms;

    /// <summary>
    /// One connect. <c>Unpinned</c>: <paramref name="requirePin"/> was set and the socket could not be
    /// pinned to the adapter, so nothing was dialled.
    /// </summary>
    internal static async Task<(int? Ms, bool Unpinned)> ProbeAsync(IPAddress address, int port, int? interfaceIndex, bool requirePin, int timeoutMs)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            var pinned = false;
            if (interfaceIndex is > 0)
            {
                try
                {
                    // IPv4 IP_UNICAST_IF takes the index in network byte order.
                    socket.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)IpUnicastIf,
                        BitConverter.GetBytes(IPAddress.HostToNetworkOrder(interfaceIndex.Value)));
                    pinned = true;
                }
                catch (SocketException)
                {
                    // Not pinned: the measurement still works, possibly through the tunnel.
                }
            }
            if (requirePin && !pinned)
            {
                return (null, true);
            }
            using var timeout = new CancellationTokenSource(timeoutMs);
            var watch = Stopwatch.StartNew();
            await socket.ConnectAsync(new IPEndPoint(address, port), timeout.Token);
            watch.Stop();
            return (Math.Max(1, (int)watch.ElapsedMilliseconds), false);
        }
        catch
        {
            return (null, false);
        }
    }
}

/// <summary>One ping round: successful pings by server id, servers not dialled because the socket could not be pinned, the adapter used.</summary>
public sealed record ColituPingRound(Dictionary<string, int> Ms, IReadOnlySet<string> Unpinned, int? InterfaceIndex);
