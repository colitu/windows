using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using AwesomeAssertions;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

public class ColituNetworkTests
{
    [Theory]
    [InlineData("198.18.0.36", true)]
    [InlineData("198.19.255.1", true)]
    [InlineData("198.17.0.1", false)]
    [InlineData("45.138.101.216", false)]
    [InlineData("192.168.0.1", false)]
    public void FakeIpPool_IsRecognised(string address, bool placeholder)
    {
        ColituNetwork.IsPlaceholder(IPAddress.Parse(address)).Should().Be(placeholder);
    }

    [Fact]
    public void PhysicalInterface_IsNeverAVirtualAdapter()
    {
        var name = ColituNetwork.PhysicalInterfaceName();
        if (name == null)
        {
            return; // no physical adapter with a gateway on this machine (CI)
        }
        var adapter = NetworkInterface.GetAllNetworkInterfaces().Single(item => item.Name == name);
        adapter.NetworkInterfaceType.Should().BeOneOf(NetworkInterfaceType.Ethernet, NetworkInterfaceType.Wireless80211, NetworkInterfaceType.GigabitEthernet);
        adapter.Description.Should().NotContainAny("Wintun", "TAP", "WireGuard", "Hyper-V", "VMware", "VirtualBox");
    }

    [Fact]
    public async Task LiteralAddress_IsReturnedWithoutLookup()
    {
        (await ColituNetwork.ResolveServerAsync("45.138.101.216")).Should().Be(IPAddress.Parse("45.138.101.216"));
    }

    [Fact]
    public void ShareLink_UsesThePinnedAddressAndKeepsTheNameForTls()
    {
        var payload = JsonDocument.Parse("""{"schema_version":1,"protocol":"trojan","endpoint":{"host":"vpn.example.com","port":443},"credentials":{"password":"p"},"transport":{"type":"tcp"},"security":{"type":"tls","server_name":"vpn.example.com"}}""").RootElement.Clone();

        ColituShareLinkBuilder.HostOf(payload).Should().Be("vpn.example.com");
        var link = ColituShareLinkBuilder.Build(payload, "x", "203.0.113.7");

        link.Should().StartWith("trojan://p@203.0.113.7:443?").And.Contain("sni=vpn.example.com");
    }

    [Fact]
    public void ShareLink_WithoutPin_DialsTheName()
    {
        var payload = JsonDocument.Parse("""{"schema_version":1,"protocol":"shadowsocks","endpoint":{"host":"vpn.example.com","port":8388},"credentials":{"method":"aes-128-gcm","password":"p"},"transport":{"type":"tcp"},"security":{"type":"none"}}""").RootElement.Clone();

        ColituShareLinkBuilder.Build(payload, "x").Should().Contain("@vpn.example.com:8388");
    }
    [Fact]
    public void FirstARecord_SkipsTheQuestionAndCnames()
    {
        // Reply to an A query for www.example.com: a CNAME to example.com (name compressed) then its A record.
        var reply = new byte[]
        {
            0x12, 0x34, 0x81, 0x80, 0x00, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00,
            3, (byte)'w', (byte)'w', (byte)'w', 7, (byte)'e', (byte)'x', (byte)'a', (byte)'m', (byte)'p', (byte)'l', (byte)'e', 3, (byte)'c', (byte)'o', (byte)'m', 0,
            0x00, 0x01, 0x00, 0x01,
            0xC0, 0x0C, 0x00, 0x05, 0x00, 0x01, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x02, 0xC0, 0x10,
            0xC0, 0x10, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x04, 45, 138, 101, 216
        };

        ColituNetwork.FirstARecord(reply).Should().Be(IPAddress.Parse("45.138.101.216"));
    }

    [Fact]
    public void FirstARecord_IsNullForErrorsAndTruncatedReplies()
    {
        var nameError = new byte[] { 0x12, 0x34, 0x81, 0x83, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        ColituNetwork.FirstARecord(nameError).Should().BeNull();
        var truncated = new byte[] { 0x12, 0x34, 0x81, 0x80, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x0C, 0x00, 0x01 };
        ColituNetwork.FirstARecord(truncated).Should().BeNull();
    }

    [Theory]
    [InlineData("hysteria2://p%40ss@vpn.example.com:8443?sni=vpn.example.com&insecure=0&mport=20000-30000#Colitu x", "hysteria2://p%40ss@1.2.3.4:8443?sni=vpn.example.com&insecure=0&mport=20000-30000#Colitu x")]
    [InlineData("vless://uuid@vpn.example.com:443?security=reality&sni=www.apple.com&type=tcp#Colitu x", "vless://uuid@1.2.3.4:443?security=reality&sni=www.apple.com&type=tcp#Colitu x")]
    [InlineData("trojan://pw@vpn.example.com:4433?security=tls&sni=vpn.example.com&type=tcp#Colitu x", "trojan://pw@1.2.3.4:4433?security=tls&sni=vpn.example.com&type=tcp#Colitu x")]
    [InlineData("ss://YWVzLTEyOC1nY206cA_-@vpn.example.com:8388#Colitu x", "ss://YWVzLTEyOC1nY206cA_-@1.2.3.4:8388#Colitu x")]
    public void StoredLinks_DialThePinnedAddress(string link, string pinned)
    {
        ColituShareLinkBuilder.DialHostOf(link).Should().Be("vpn.example.com");
        ColituShareLinkBuilder.WithDialAddress(link, IPAddress.Parse("1.2.3.4")).Should().Be(pinned);
    }

    [Theory]
    [InlineData("hysteria2://p@1.2.3.4:8443?sni=vpn.example.com#x")]
    [InlineData("vless://uuid@[2001:db8::1]:443?sni=a#x")]
    [InlineData("not a link")]
    public void StoredLinks_ThatDialAnAddress_AreLeftAlone(string link)
    {
        ColituShareLinkBuilder.DialHostOf(link).Should().BeNull();
    }

    [Fact]
    public void StoredLinks_PinAnIPv6AddressInBrackets()
    {
        ColituShareLinkBuilder.WithDialAddress("ss://abc@vpn.example.com:8388#x", IPAddress.Parse("2001:db8::1"))
            .Should().Be("ss://abc@[2001:db8::1]:8388#x");
    }
    [Fact]
    public async Task AdapterDns_ResolvesARealName_WhenAskedLive()
    {
        // Real network: only with COLITU_LIVE_DNS=1 (never in CI).
        if (Environment.GetEnvironmentVariable("COLITU_LIVE_DNS") != "1" || ColituNetwork.PhysicalInterfaceName() == null)
        {
            return;
        }
        var address = await ColituNetwork.ResolveViaAdapterDnsAsync("colitu.com", CancellationToken.None);
        address.Should().NotBeNull();
        ColituNetwork.IsPlaceholder(address!).Should().BeFalse();
    }

    [Fact]
    public async Task UdpDns_ResolvesARealName_AgainstAGivenServer()
    {
        // Real network: only with COLITU_LIVE_DNS_SERVER=<resolver address> (never in CI).
        if (Environment.GetEnvironmentVariable("COLITU_LIVE_DNS_SERVER") is not { Length: > 0 } server)
        {
            return;
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var address = await ColituNetwork.QueryUdpDnsAsync(IPAddress.Parse(server), "colitu.com", 0, timeout.Token);
        address.Should().NotBeNull();
    }
}
