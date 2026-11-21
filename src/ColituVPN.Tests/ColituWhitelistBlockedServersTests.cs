using AwesomeAssertions;
using v2rayN.Services;
using v2rayN.Views;
using Xunit;

namespace v2rayN.Tests;

/// <summary>The panel's blocked_servers never hide or demote the "Beyaz Liste" rows.</summary>
public class ColituWhitelistBlockedServersTests
{
    static ColituWhitelistBlockedServersTests() => _ = System.IO.Packaging.PackUriHelper.UriSchemePack;

    private static ColituVpnServer S(string id, params string[] protocols) =>
        new() { Id = id, Name = id, CountryCode = "FI", Country = "FI", Available = true, WhitelistOk = true, Protocols = protocols.ToList() };

    private static readonly ColituVpnServer[] Servers =
        new[] { "a", "b", "c", "d" }.Select(id => S(id, "vless-reality", "vless-reality-relay")).ToArray();

    private static readonly string[] Blocked = new[] { "a", "b" };

    [Fact]
    public void AllFourWhitelistRowsExist_WhateverIsBlocked() =>
        ColituLocationSort.WhitelistRows(Servers, "RU").Select(server => server.Id).Should().Equal("a", "b", "c", "d");

    [Fact]
    public void OrdinaryRows_KeepTheBlockedSet() =>
        ColituLocationSort.AddressBlocked(Servers, Blocked, whitelistMode: false).Should().Equal(Blocked);

    [Fact]
    public void WhitelistMode_IgnoresBlockedForWhitelistServers_OnlyThose()
    {
        ColituLocationSort.AddressBlocked(Servers, Blocked, whitelistMode: true).Should().BeEmpty();
        var plain = S("e", "vless-reality");
        ColituLocationSort.AddressBlocked([.. Servers, plain], [.. Blocked, "e"], whitelistMode: true).Should().Equal("e");
    }

    [Fact]
    public void WhitelistRow_IsNeverDimmed()
    {
        var row = ColituServerRow.Whitelist(Servers[0]);
        row.IsWhitelistRow.Should().BeTrue();
        row.Dimmed.Should().BeFalse();
        row.RowOpacity.Should().Be(1);
    }
}
