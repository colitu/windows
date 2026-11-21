namespace v2rayN.Services;

/// <summary>
/// The connection method a user can pick on the home screen (Fast, Stealth, Resilient, Classic,
/// Light, ColituGecko or automatic). A server transport only changes which transport is tried first;
/// ColituGecko is a connection of its own (Gecko only, independent of the server).
/// </summary>
public static class ColituTransportChoice
{
    /// <summary>
    /// The methods the menu offers, in menu order. Never a whitelist transport (vless-ws,
    /// vless-reality-relay): Whitelist is a way in, picked as a server row, not a protocol.
    /// </summary>
    public static readonly string[] Methods = ["hysteria2", "vless-reality", "vless-xhttp", "trojan", "shadowsocks"];

    /// <summary>ColituGecko: in the menu only while the panel offers it (<see cref="Menu"/>).</summary>
    public const string Gecko = ColituGeckoRules.Protocol;

    /// <summary>
    /// The menu, in order: automatic, ColituGecko (when offered: enabled; offered but unavailable now:
    /// shown disabled; not offered or not known yet: left out), then the server transports.
    /// </summary>
    public static List<ColituMethodEntry> Menu(ColituGeckoOffer offer)
    {
        var menu = new List<ColituMethodEntry> { new("", true) };
        if (offer is ColituGeckoOffer.Offered or ColituGeckoOffer.Unavailable)
        {
            menu.Add(new ColituMethodEntry(Gecko, offer == ColituGeckoOffer.Offered));
        }
        menu.AddRange(Methods.Select(method => new ColituMethodEntry(method, true)));
        return menu;
    }

    /// <summary>The pick the UI shows: ColituGecko picked while the panel does not offer it reads as automatic (the pick stays stored).</summary>
    public static string Shown(string? picked, ColituGeckoOffer offer) =>
        string.Equals(picked, Gecko, StringComparison.Ordinal) && offer == ColituGeckoOffer.NotOffered ? "" : picked ?? "";

    /// <summary>
    /// The home protocol chip while a Whitelist row is picked: it shows the whitelist (locked, no
    /// menu) whatever method the user picked; the pick stays stored and is back on a normal
    /// server. Null: the normal chip.
    /// </summary>
    public static string? WhitelistChipLabel(bool whitelistSelected, string whitelistLabel) =>
        whitelistSelected ? whitelistLabel : null;

    /// <summary>A known method in its canonical spelling, or "" (automatic) for anything else.</summary>
    public static string Normalize(string? protocol)
    {
        var trimmed = protocol?.Trim() ?? "";
        if (string.Equals(trimmed, Gecko, StringComparison.OrdinalIgnoreCase))
        {
            return Gecko;
        }
        return Methods.FirstOrDefault(method => string.Equals(method, trimmed, StringComparison.OrdinalIgnoreCase)) ?? "";
    }

    /// <summary>
    /// <paramref name="ordered"/> with the items of <paramref name="picked"/> moved to the front, the
    /// rest in their order. Unchanged when nothing is picked or the method is not offered.
    /// </summary>
    public static List<T> PutFirst<T>(List<T> ordered, Func<T, string> protocolOf, string? picked)
    {
        var method = Normalize(picked);
        if (method.Length == 0 || method == Gecko)
        {
            return ordered;
        }
        var first = ordered.Where(item => string.Equals(protocolOf(item), method, StringComparison.OrdinalIgnoreCase)).ToList();
        return first.Count == 0 ? ordered : first.Concat(ordered.Where(item => !first.Contains(item))).ToList();
    }
}

/// <summary>One entry of the protocol menu; a disabled one is shown greyed out (offered, unavailable now).</summary>
public sealed record ColituMethodEntry(string Method, bool Enabled);
