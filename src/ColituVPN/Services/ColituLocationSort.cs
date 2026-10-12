using System.Globalization;

namespace v2rayN.Services;

/// <summary>
/// Order of the Locations list, like the phone apps: by name (the list's order before the setting
/// existed, the default), by ping or by load. Pure logic; the view renders what it returns and keeps
/// the "Best server" row on top.
/// <list type="bullet">
/// <item>Name: country name, then city, compared in the app language's culture (tr-TR for Turkish).</item>
/// <item>Ping: lowest ping first; a country with several cities sits where its best city would, its
/// cities sorted the same way; unmeasured / unreachable locations last, by name.</item>
/// <item>Load: least loaded first (a country by its least loaded city); unknown load last, by name.</item>
/// </list>
/// Double-VPN routes keep their own section after the locations and are sorted inside it.
/// </summary>
public static class ColituLocationSort
{
    public const string Name = "name";
    public const string Ping = "ping";
    public const string Load = "load";

    /// <summary>The choices in the order the picker shows them.</summary>
    public static readonly IReadOnlyList<string> Modes = [Name, Ping, Load];

    public static string Normalize(string? mode)
    {
        var value = (mode ?? "").Trim().ToLowerInvariant();
        return Modes.Contains(value) ? value : Name;
    }

    /// <summary>Localization key of a mode's label (locations.sort.*).</summary>
    public static string LabelKey(string mode) => "locations.sort." + Normalize(mode);

    /// <summary>
    /// Groups the locations by country (see <see cref="ColituServerGroups.Build"/>) and orders the groups
    /// and the cities inside them by <paramref name="mode"/>. Ties keep the name order.
    /// </summary>
    public static List<ColituServerGroup> Groups(IEnumerable<ColituVpnServer> servers, string? mode, Func<ColituVpnServer, int?> ping,
        Func<string?, string?> countryName, CultureInfo culture, bool flat)
    {
        var groups = ColituServerGroups.Build(servers, countryName, StringComparer.Create(culture, true), flat);
        var key = KeyFor(Normalize(mode), ping);
        if (key == null)
        {
            return groups;
        }
        return groups
            .Select(group => group.IsSingle ? group : new ColituServerGroup
            {
                Key = group.Key,
                Name = group.Name,
                CountryCode = group.CountryCode,
                Servers = OrderByKey(group.Servers, key)
            })
            // OrderBy is stable: groups with the same key stay in name order.
            .OrderBy(group => Best(group.Servers, key) is null ? 1 : 0)
            .ThenBy(group => Best(group.Servers, key) ?? 0)
            .ToList();
    }

    /// <summary>Double-VPN routes: by name, or by their entry ping / load with unknown values last.</summary>
    public static List<ColituVpnServer> Routes(IEnumerable<ColituVpnServer> routes, string? mode, Func<ColituVpnServer, int?> ping, CultureInfo culture)
    {
        var byName = routes.OrderBy(route => route.Name ?? "", StringComparer.Create(culture, true)).ToList();
        return KeyFor(Normalize(mode), ping) is { } key ? OrderByKey(byName, key) : byName;
    }

    /// <summary>Ping above 0 ms, or the load in percent; null when unknown (sorted last).</summary>
    private static Func<ColituVpnServer, double?>? KeyFor(string mode, Func<ColituVpnServer, int?> ping) => mode switch
    {
        Ping => server => ping(server) is > 0 and var ms ? (double?)ms : null,
        Load => server => server.Load is >= 0 and var load ? (double?)load : null,
        _ => null
    };

    private static List<ColituVpnServer> OrderByKey(IEnumerable<ColituVpnServer> servers, Func<ColituVpnServer, double?> key) =>
        servers
            .OrderBy(server => key(server) is null ? 1 : 0)
            .ThenBy(server => key(server) ?? 0)
            .ToList();

    private static double? Best(IEnumerable<ColituVpnServer> servers, Func<ColituVpnServer, double?> key) =>
        servers.Select(key).Where(value => value != null).Min();
}
