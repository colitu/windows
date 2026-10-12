using System.Globalization;
using AwesomeAssertions;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

/// <summary>Locations list order: by name (default), by ping, by load; multihop routes sorted in their own section.</summary>
public class ColituLocationSortTests
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static string? Name(string? code) => (code ?? "").ToUpperInvariant() switch
    {
        "DE" => "Almanya",
        "TR" => "Türkiye",
        "CZ" => "Çekya",
        "CH" => "İsviçre",
        "IS" => "İzlanda",
        "US" => "ABD",
        "UZ" => "Özbekistan",
        "NL" => "Hollanda",
        _ => null
    };

    private static ColituVpnServer Server(string id, string code, string city, double? load = null) =>
        new() { Id = id, CountryCode = code, City = city, Name = city, Load = load };

    private static readonly ColituVpnServer[] Servers =
    [
        Server("de-fra", "DE", "Frankfurt", 70),
        Server("de-ber", "DE", "Berlin", 20),
        Server("tr-ist", "TR", "İstanbul", 50),
        Server("cz-prg", "CZ", "Prag"),
        Server("us-nyc", "US", "New York", 10),
        Server("nl-ams", "NL", "Amsterdam", 90),
    ];

    private static readonly Dictionary<string, int> Pings = new()
    {
        ["de-fra"] = 40,
        ["de-ber"] = 90,
        ["tr-ist"] = 25,
        ["us-nyc"] = 140,
        // cz-prg: never measured; nl-ams: unreachable (0 is not a ping)
        ["nl-ams"] = 0,
    };

    private static int? Ping(ColituVpnServer server) => Pings.TryGetValue(server.Id!, out var ms) ? ms : null;

    private static List<string> Order(string mode, bool flat = false) =>
        ColituLocationSort.Groups(Servers, mode, Ping, Name, Turkish, flat).Select(group => group.Name).ToList();

    [Fact]
    public void Name_TurkishCollation_IsTheDefault()
    {
        // tr-TR: Ç after C, İ after I, Ö after O, Ü after U.
        Order(ColituLocationSort.Name).Should().Equal("ABD", "Almanya", "Çekya", "Hollanda", "Türkiye");
        Order("unknown").Should().Equal(Order(ColituLocationSort.Name));
        ColituLocationSort.Normalize(null).Should().Be(ColituLocationSort.Name);
        ColituLocationSort.Normalize(" PING ").Should().Be(ColituLocationSort.Ping);
    }

    [Fact]
    public void Name_TurkishLetters_SortAfterTheirBaseLetter()
    {
        var servers = new[] { Server("ch", "CH", "Zürih"), Server("is", "IS", "Reykjavik"), Server("uz", "UZ", "Taşkent"), Server("nl", "NL", "Amsterdam") };
        ColituLocationSort.Groups(servers, ColituLocationSort.Name, _ => null, Name, Turkish, flat: false)
            .Select(group => group.Name).Should().Equal("Hollanda", "İsviçre", "İzlanda", "Özbekistan");
    }

    [Fact]
    public void Ping_LowestFirst_CountryByItsBestCity_UnmeasuredLast()
    {
        var groups = ColituLocationSort.Groups(Servers, ColituLocationSort.Ping, Ping, Name, Turkish, flat: false);

        // Germany sits at 40 ms (Frankfurt), not at Berlin's 90; Czechia (never measured) and the
        // Netherlands (unreachable) come last, in name order.
        groups.Select(group => group.Name).Should().Equal("Türkiye", "Almanya", "ABD", "Çekya", "Hollanda");
        groups[1].Servers.Select(server => server.City).Should().Equal("Frankfurt", "Berlin");
    }

    [Fact]
    public void Ping_Flat_SortsEveryServer()
    {
        ColituLocationSort.Groups(Servers, ColituLocationSort.Ping, Ping, Name, Turkish, flat: true)
            .Select(group => group.Servers[0].Id).Should().Equal("tr-ist", "de-fra", "de-ber", "us-nyc", "cz-prg", "nl-ams");
    }

    [Fact]
    public void Load_LeastLoadedFirst_UnknownLast()
    {
        var groups = ColituLocationSort.Groups(Servers, ColituLocationSort.Load, Ping, Name, Turkish, flat: false);

        groups.Select(group => group.Name).Should().Equal("ABD", "Almanya", "Türkiye", "Hollanda", "Çekya");
        groups[1].Servers.Select(server => server.City).Should().Equal("Berlin", "Frankfurt");
    }

    [Fact]
    public void Routes_SortedInsideTheirSection()
    {
        var routes = new[]
        {
            new ColituVpnServer { Id = "r-b", Name = "Berlin → Helsinki", Load = 60 },
            new ColituVpnServer { Id = "r-a", Name = "Amsterdam → İstanbul", Load = 30 },
            new ColituVpnServer { Id = "r-c", Name = "Çekya → Almanya" },
        };
        var pings = new Dictionary<string, int> { ["r-b"] = 50, ["r-c"] = 30 };
        int? RoutePing(ColituVpnServer route) => pings.TryGetValue(route.Id!, out var ms) ? ms : null;

        ColituLocationSort.Routes(routes, ColituLocationSort.Name, RoutePing, Turkish).Select(route => route.Id).Should().Equal("r-a", "r-b", "r-c");
        ColituLocationSort.Routes(routes, ColituLocationSort.Ping, RoutePing, Turkish).Select(route => route.Id).Should().Equal("r-c", "r-b", "r-a");
        ColituLocationSort.Routes(routes, ColituLocationSort.Load, RoutePing, Turkish).Select(route => route.Id).Should().Equal("r-a", "r-b", "r-c");
    }

    [Fact]
    public void Preference_DefaultsToName_AndIsNormalized()
    {
        new ColituVpnPreferences().LocationSort.Should().Be(ColituLocationSort.Name);
        (new ColituVpnPreferences() with { LocationSort = "LOAD" }).Normalize().LocationSort.Should().Be(ColituLocationSort.Load);
        (new ColituVpnPreferences() with { LocationSort = "bogus" }).Normalize().LocationSort.Should().Be(ColituLocationSort.Name);
    }

    [Fact]
    public void Labels_ExistInAllLanguages()
    {
        foreach (var mode in ColituLocationSort.Modes)
        {
            Loc.Has(ColituLocationSort.LabelKey(mode)).Should().BeTrue(mode);
        }
        Loc.Has("locations.sortTitle").Should().BeTrue();
    }
}
