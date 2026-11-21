using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

/// <summary>ColituWhitelist.CheckAsync: resolve → optional hook (once, before any probe) → probes.</summary>
public class ColituWhitelistCheckOrderTests
{
    [Fact]
    public async Task CheckCallsTheHookOnceWithTheResolvedAddressesBeforeAnyProbe()
    {
        var addresses = new Dictionary<string, IPAddress?>
        {
            ["www.google.com"] = IPAddress.Parse("142.250.74.4"),
            ["de.example"] = IPAddress.Parse("203.0.113.7"),
            ["ya.ru"] = IPAddress.Parse("87.250.250.242"),
            ["vk.com"] = IPAddress.Parse("93.186.225.194"),
            ["lan.example"] = IPAddress.Parse("192.168.1.10"),
            ["gone.example"] = null
        };
        var probes = 0;
        var hookCalls = new List<(IReadOnlyList<IPAddress> Addresses, int ProbesBefore)>();
        var check = await ColituWhitelist.CheckAsync(
            [new ColituWhitelistTarget("www.google.com", 443), new ColituWhitelistTarget("de.example", 2053), new ColituWhitelistTarget("gone.example", 443)],
            ["ya.ru", "vk.com", "lan.example"],
            null, requirePin: false,
            (host, _) => Task.FromResult(addresses[host]),
            (address, _, _) =>
            {
                Interlocked.Increment(ref probes);
                return Task.FromResult<(int?, bool)>((address.ToString() is "87.250.250.242" or "93.186.225.194" ? 30 : null, false));
            },
            list =>
            {
                hookCalls.Add((list, Volatile.Read(ref probes)));
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);
        hookCalls.Should().ContainSingle();
        hookCalls[0].ProbesBefore.Should().Be(0);
        hookCalls[0].Addresses.Select(address => address.ToString()).Should().BeEquivalentTo("142.250.74.4", "203.0.113.7", "87.250.250.242", "93.186.225.194");
        probes.Should().Be(4);
        check!.ForeignOk.Should().Be(0);
        check.ForeignTotal.Should().Be(3);
        check.DomesticOk.Should().Be(2);
        check.Whitelist.Should().BeTrue();
    }

    [Fact]
    public async Task WithoutAHookTheProbesRunAsBefore()
    {
        var probes = 0;
        var check = await ColituWhitelist.CheckAsync(
            [new ColituWhitelistTarget("www.google.com", 443)], ["ya.ru", "vk.com"], null, requirePin: false,
            (_, _) => Task.FromResult<IPAddress?>(IPAddress.Parse("87.250.250.242")),
            (_, _, _) =>
            {
                Interlocked.Increment(ref probes);
                return Task.FromResult<(int?, bool)>((20, false));
            },
            null,
            TestContext.Current.CancellationToken);
        probes.Should().Be(3);
        check!.ForeignOk.Should().Be(1);
        check.DomesticOk.Should().Be(2);
        // Pinning required and impossible: no check at all.
        (await ColituWhitelist.CheckAsync([], ["ya.ru"], null, requirePin: true, TestContext.Current.CancellationToken)).Should().BeNull();
    }
}
