namespace v2rayN.Services;

/// <summary>
/// The connection method a user can pick on the home screen (Fast, Stealth, Resilient, Classic,
/// Light or automatic). The pick only changes which transport is tried first.
/// </summary>
public static class ColituTransportChoice
{
    /// <summary>The methods the menu offers, in menu order.</summary>
    public static readonly string[] Methods = ["hysteria2", "vless-reality", "vless-xhttp", "trojan", "shadowsocks"];

    /// <summary>A known method in its canonical spelling, or "" (automatic) for anything else.</summary>
    public static string Normalize(string? protocol)
    {
        var trimmed = protocol?.Trim() ?? "";
        return Methods.FirstOrDefault(method => string.Equals(method, trimmed, StringComparison.OrdinalIgnoreCase)) ?? "";
    }

    /// <summary>
    /// <paramref name="ordered"/> with the items of <paramref name="picked"/> moved to the front, the
    /// rest in their order. Unchanged when nothing is picked or the method is not offered.
    /// </summary>
    public static List<T> PutFirst<T>(List<T> ordered, Func<T, string> protocolOf, string? picked)
    {
        var method = Normalize(picked);
        if (method.Length == 0)
        {
            return ordered;
        }
        var first = ordered.Where(item => string.Equals(protocolOf(item), method, StringComparison.OrdinalIgnoreCase)).ToList();
        return first.Count == 0 ? ordered : first.Concat(ordered.Where(item => !first.Contains(item))).ToList();
    }
}
