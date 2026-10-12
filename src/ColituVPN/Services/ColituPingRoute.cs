using System.Text.Json;
using System.Text.Json.Nodes;

namespace v2rayN.Services;

/// <summary>
/// Location pings in TUN mode: the app pins its probe sockets to the physical adapter
/// (<see cref="ColituLatency"/>), and as a second line every sing-box config that runs the TUN
/// adapter (plain sing-box, and the sing-box TUN front of an Xray tunnel) sends this app's TCP
/// connections to the probe ports straight out through <c>direct</c>. Without it a probe that
/// lands in the TUN measures "tunnel + exit to server", not this PC's distance to the server.
/// The rule is the first route rule: no ip/domain/rule-set rule, sniff or user rule decides first.
/// It matches only this process, TCP, and <see cref="ColituLatency.AllowedPorts"/>; other traffic
/// of the app (panel API) keeps its normal route.
/// </summary>
public static class ColituPingRoute
{
    private const string DirectTag = "direct";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>File name of this app's executable, as sing-box sees it (process_name).</summary>
    public static string AppProcessName =>
        Path.GetFileName(Environment.ProcessPath) is { Length: > 0 } name ? name : "ColituVPN.exe";

    /// <summary>
    /// The config with the ping rule (and a <c>direct</c> outbound when it had none); the config
    /// unchanged when it has no TUN inbound or already has the rule; null when it is not readable.
    /// </summary>
    public static string? ApplySingbox(string config, string processName)
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
        if (root == null || processName.IsNullOrEmpty())
        {
            return null;
        }
        var hasTun = (root["inbounds"] as JsonArray)?.OfType<JsonObject>()
            .Any(inbound => string.Equals(inbound["type"]?.GetValue<string>(), "tun", StringComparison.OrdinalIgnoreCase)) == true;
        if (!hasTun)
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
        if (rules.OfType<JsonObject>().Any(rule => IsPingRule(rule, processName)))
        {
            return config;
        }
        rules.Insert(0, NewRule(processName));
        return root.ToJsonString(Indented);
    }

    /// <summary>The ping rule: this process, TCP, the probe ports, out through <c>direct</c>.</summary>
    public static JsonObject NewRule(string processName)
    {
        var ports = new JsonArray();
        foreach (var port in ColituLatency.AllowedPorts.Order())
        {
            ports.Add(port);
        }
        return new JsonObject
        {
            ["process_name"] = new JsonArray(processName),
            ["network"] = new JsonArray("tcp"),
            ["port"] = ports,
            ["outbound"] = DirectTag
        };
    }

    /// <summary>The rule is a ping rule for <paramref name="processName"/> (same shape as <see cref="NewRule"/>).</summary>
    public static bool IsPingRule(JsonObject rule, string processName) =>
        rule["outbound"]?.GetValue<string>() == DirectTag
        && rule["process_name"] is JsonArray names && names.Count == 1 && names[0]?.GetValue<string>() == processName
        && rule["port"] is JsonArray ports
        && ports.Select(port => port?.GetValue<int>() ?? 0).Order().SequenceEqual(ColituLatency.AllowedPorts.Order());
}
