using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace v2rayN.Services;

/// <summary>
/// Location pings in TUN mode: the app pins its probe sockets to the physical adapter
/// (<see cref="ColituLatency"/>), and as a second line every sing-box config that runs the TUN
/// adapter (plain sing-box, and the sing-box TUN front of an Xray tunnel) sends this app's TCP
/// connections to the probe addresses straight out through <c>direct</c>. Without it a probe that
/// lands in the TUN measures "tunnel + exit to server", not this PC's distance to the server.
///
/// The rule goes around the tunnel (and the kill switch, which lets the core's own traffic out),
/// so it is as narrow as a sing-box rule can be; its fields are AND'ed:
/// - <c>process_path_regex</c>: the full path of this exe, anchored and case-insensitive (Windows
///   paths), and only when that exe and its folders can be changed by administrators alone. A copy
///   of the app, or another program with the same file name, does not match.
/// - <c>ip_cidr</c>: the resolved addresses of the servers' probe targets (one /32 each). Any other
///   destination of this app (the panel API) keeps its normal route through the tunnel.
/// - <c>network</c> tcp and <c>port</c> <see cref="ColituLatency.AllowedPorts"/>.
/// Without a trusted path or a known target address no rule is written; pings then rely on the
/// adapter pin alone. The rule is the first route rule: no ip/domain/rule-set rule, sniff or
/// user rule decides first.
/// </summary>
public static class ColituPingRoute
{
    private const string DirectTag = "direct";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>At most this many target addresses go into the rule.</summary>
    internal const int MaxTargets = 256;

    /// <summary>
    /// This app's exe when it and its folder (and the folder above) can be written by
    /// administrators alone (the install folder in Program Files); otherwise null (a development
    /// build, a portable copy, a folder a standard user controls).
    /// </summary>
    public static string? TrustedAppPath()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return null;
        }
        try
        {
            var file = new FileInfo(path);
            var folder = file.Directory;
            if (!file.Exists || folder == null || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return null;
            }
            if (!AdminOnly(file.GetAccessControl()) || !AdminOnly(folder.GetAccessControl())
                || folder.Attributes.HasFlag(FileAttributes.ReparsePoint)
                || (folder.Parent is { } parent && !AdminOnly(parent.GetAccessControl())))
            {
                return null;
            }
            return file.FullName;
        }
        catch
        {
            // Unreadable ACL or path: not trusted.
            return null;
        }
    }

    // Owner and every "allow" entry that grants a write, delete or ACL right belong to SYSTEM,
    // Administrators, TrustedInstaller or CREATOR OWNER (which only applies to what those create).
    private static readonly FileSystemRights WriteRights = FileSystemRights.WriteData | FileSystemRights.AppendData
        | FileSystemRights.WriteExtendedAttributes | FileSystemRights.WriteAttributes | FileSystemRights.Delete
        | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    private const string TrustedInstallerSid = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    internal static bool AdminOnly(FileSystemSecurity security)
    {
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !IsTrustedSid(owner))
        {
            return false;
        }
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & WriteRights) != 0
                && rule.IdentityReference is SecurityIdentifier sid && !IsTrustedSid(sid) && !sid.IsWellKnown(WellKnownSidType.CreatorOwnerSid))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsTrustedSid(SecurityIdentifier sid) =>
        sid.IsWellKnown(WellKnownSidType.LocalSystemSid)
        || sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)
        || string.Equals(sid.Value, TrustedInstallerSid, StringComparison.Ordinal);

    /// <summary>
    /// The config with the ping rule (and a <c>direct</c> outbound when it had none); the config
    /// unchanged when it has no TUN inbound, already has the rule, or there is no trusted path or
    /// usable target; null when it is not readable.
    /// </summary>
    public static string? ApplySingbox(string config, string? appPath, IEnumerable<IPAddress> targets)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(config) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
        if (root == null)
        {
            return null;
        }
        var hasTun = (root["inbounds"] as JsonArray)?.OfType<JsonObject>()
            .Any(inbound => string.Equals(inbound["type"]?.GetValue<string>(), "tun", StringComparison.OrdinalIgnoreCase)) == true;
        if (!hasTun || NewRule(appPath, targets) is not { } rule)
        {
            return config;
        }

        var outbounds = root["outbounds"] as JsonArray ?? new JsonArray();
        root["outbounds"] = outbounds;
        if (!outbounds.OfType<JsonObject>().Any(outbound => outbound["tag"]?.GetValue<string>() == DirectTag))
        {
            outbounds.Add(new JsonObject { ["type"] = "direct", ["tag"] = DirectTag });
        }

        var route = root["route"] as JsonObject ?? new JsonObject();
        root["route"] = route;
        var rules = route["rules"] as JsonArray ?? new JsonArray();
        route["rules"] = rules;
        if (rules.OfType<JsonObject>().Any(existing => IsPingRule(existing, appPath!)))
        {
            return config;
        }
        rules.Insert(0, rule);
        return root.ToJsonString(Indented);
    }

    /// <summary>
    /// The ping rule: this exe (full path), TCP, the probe ports, only the given public IPv4
    /// targets, out through <c>direct</c>. Null without a path or a usable target.
    /// </summary>
    public static JsonObject? NewRule(string? appPath, IEnumerable<IPAddress> targets)
    {
        if (string.IsNullOrEmpty(appPath) || !Path.IsPathFullyQualified(appPath))
        {
            return null;
        }
        var cidrs = new JsonArray();
        foreach (var address in Targets(targets))
        {
            cidrs.Add($"{address}/32");
        }
        if (cidrs.Count == 0)
        {
            return null;
        }
        var ports = new JsonArray();
        foreach (var port in ColituLatency.AllowedPorts.Order())
        {
            ports.Add(port);
        }
        return new JsonObject
        {
            ["process_path_regex"] = new JsonArray(PathRegex(appPath)),
            ["ip_cidr"] = cidrs,
            ["network"] = new JsonArray("tcp"),
            ["port"] = ports,
            ["outbound"] = DirectTag
        };
    }

    /// <summary>The targets kept by <see cref="SaveTargets"/>; empty when there is no readable file.</summary>
    public static IReadOnlyList<IPAddress> LoadTargets(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return [];
            }
            var values = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? [];
            return Targets(values.Select(value => IPAddress.TryParse(value, out var address) ? address : null).OfType<IPAddress>());
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Keeps <paramref name="targets"/> for the next start; failures only cost the rule at the next automatic connect.</summary>
    public static void SaveTargets(string path, IEnumerable<IPAddress> targets)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(Targets(targets).Select(address => address.ToString()).ToArray()));
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"ColituPingRoute.SaveTargets: {ex.GetType().Name}");
        }
    }

    /// <summary>Public IPv4 targets, without duplicates, at most <see cref="MaxTargets"/>.</summary>
    internal static List<IPAddress> Targets(IEnumerable<IPAddress> targets) =>
        targets
            .Where(address => address != null)
            .Select(address => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork && ColituLatency.IsPublic(address))
            .Distinct()
            .Take(MaxTargets)
            .ToList();

    /// <summary>
    /// An RE2 (Go regexp) pattern that matches exactly <paramref name="path"/>, ignoring case:
    /// ASCII punctuation is escaped, space and control characters are written as \xHH, letters
    /// and digits (ASCII or not) stay literal.
    /// </summary>
    internal static string PathRegex(string path)
    {
        var pattern = new StringBuilder("(?i)^");
        foreach (var rune in path.EnumerateRunes())
        {
            var value = rune.Value;
            if (value < 0x80 && char.IsAsciiLetterOrDigit((char)value))
            {
                pattern.Append((char)value);
            }
            else if (value is > 0x20 and < 0x7f)
            {
                // ASCII punctuation: RE2 accepts a backslash before any of it.
                pattern.Append('\\').Append((char)value);
            }
            else if (value <= 0x20 || value == 0x7f)
            {
                // Space and control characters as two-digit hex (the same in RE2 and .NET).
                pattern.Append("\\x").Append(value.ToString("x2"));
            }
            else
            {
                // Other letters (ü, ç...) are never regex syntax: literal.
                pattern.Append(rune.ToString());
            }
        }
        return pattern.Append('$').ToString();
    }

    /// <summary>The rule is a ping rule for <paramref name="appPath"/> (same shape as <see cref="NewRule"/>).</summary>
    public static bool IsPingRule(JsonObject rule, string appPath) =>
        rule["outbound"]?.GetValue<string>() == DirectTag
        && rule["process_path_regex"] is JsonArray paths && paths.Count == 1 && paths[0]?.GetValue<string>() == PathRegex(appPath)
        && rule["ip_cidr"] is JsonArray { Count: > 0 }
        && rule["process_name"] == null
        && rule["port"] is JsonArray ports
        && ports.Select(port => port?.GetValue<int>() ?? 0).Order().SequenceEqual(ColituLatency.AllowedPorts.Order());
}
