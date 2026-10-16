using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace v2rayN.Services;

/// <summary>
/// CSL alpha (Colitu Session Layer, colitu-csl repo, contract colitu-panel docs/csl-alpha.md §3):
/// open TCP connections survive a carrier change. Opt-in in Advanced mode, used only when the
/// connected server's settings carry <c>csl</c>.
/// <para>
/// Chain: TUN / local proxy TCP → <c>colitu-csl</c> outbound (SOCKS) → <c>csl-client</c> sidecar →
/// carriers through two extra loopback SOCKS inbounds of the core, <c>colitu-csl-primary</c> (the
/// primary outbound) and <c>colitu-csl-spare</c> (the warm spare, only when it runs on the same
/// server) → the node's loopback CSL port. UDP stays as it is. Every hop on this PC is loopback, so
/// the TUN never sees csl-client's traffic and the kill switch's loopback permit covers it.
/// </para>
/// <para>
/// Fallback: csl-client missing, exiting, or without a carrier within <see cref="CarrierDeadline"/>
/// → the core is reloaded without CSL (the path every other user has), retried at the next connect.
/// </para>
/// </summary>
internal static class ColituCslRules
{
    public const string OutboundTag = "colitu-csl";
    public const string PrimaryInboundTag = "colitu-csl-primary";
    public const string SpareInboundTag = "colitu-csl-spare";
    public const string PrimaryLabel = "primary";
    public const string SpareLabel = "spare";
    public static readonly TimeSpan CarrierDeadline = TimeSpan.FromSeconds(10);

    /// <summary>Inbounds whose rules are the app's own (checks, CSL carriers): never sent into CSL.</summary>
    private static readonly HashSet<string> InternalInbounds = new(StringComparer.Ordinal)
    {
        ColituWarmSpare.VerifyInboundTag, ColituWarmSpare.SpareVerifyInboundTag, ColituWarmSpare.CheckInboundTag,
        PrimaryInboundTag, SpareInboundTag, "api"
    };

    /// <summary>
    /// Why CSL is not used for this core start; null when it is. Checked in this order, so the
    /// reason names the first thing missing.
    /// </summary>
    public static string? OffReason(bool preferenceOn, ColituCslConfig? csl, bool multihop, bool binaryPresent, string? earlierFailure)
    {
        if (!preferenceOn) return "turned off";
        if (csl == null) return "the server offers no CSL";
        if (!csl.IsUsable) return "unsupported CSL settings";
        if (multihop) return "multihop route";
        if (!binaryPresent) return "csl-client missing";
        if (earlierFailure != null) return $"failed earlier in this connect: {earlierFailure}";
        return null;
    }

    /// <summary>
    /// The state of a starting csl-client: still waiting (null), on (empty string) or off with a
    /// reason. Exiting wins over everything; "SOCKS5 on" means its session has a carrier.
    /// </summary>
    public static string? Verdict(bool ready, bool exited, int? exitCode, TimeSpan elapsed, TimeSpan? deadline = null)
    {
        var limit = deadline ?? CarrierDeadline;
        if (exited) return ready ? $"csl-client exited (code {exitCode?.ToString() ?? "?"})" : $"csl-client exited before a carrier came up (code {exitCode?.ToString() ?? "?"})";
        if (ready) return "";
        if (elapsed >= limit) return $"no carrier within {limit.TotalSeconds:0} s";
        return null;
    }

    /// <summary>csl-client logs this once its session has a carrier and the SOCKS port is open.</summary>
    public static bool IsReadyLine(string? line) => line?.Contains("csl-client: SOCKS5 on ", StringComparison.Ordinal) == true;

    public static string FamilyOf(EConfigType type) => type is EConfigType.Hysteria2 or EConfigType.TUIC ? "udp" : "tcp";

    /// <summary>
    /// <c>socks5://user:pass@127.0.0.1:port/127.0.0.1:serverPort?family=tcp#label</c> (colitu-csl carrier
    /// syntax). Lab mode (<paramref name="labDirectTcp"/>, tests only): <c>tcp://127.0.0.1:port#label</c>,
    /// the carrier port leading straight to a local csl-server.
    /// </summary>
    public static string CarrierSpec(ColituCslCarrier carrier, int serverPort, bool labDirectTcp = false) => labDirectTcp
        ? $"tcp://127.0.0.1:{carrier.Inbound.Port}#{carrier.Label}"
        : $"socks5://{Uri.EscapeDataString(carrier.Inbound.User)}:{Uri.EscapeDataString(carrier.Inbound.Password)}@127.0.0.1:{carrier.Inbound.Port}/127.0.0.1:{serverPort}?family={carrier.Family}#{carrier.Label}";

    /// <summary>The csl-client command line (the credential only as a file path, never on the line).</summary>
    public static List<string> Arguments(ColituCslLaunch launch, string credentialFile)
    {
        var args = new List<string> { "-socks", $"127.0.0.1:{launch.SocksPort}", "-credential-file", credentialFile };
        foreach (var carrier in launch.Carriers)
        {
            args.Add("-carrier");
            args.Add(CarrierSpec(carrier, launch.ServerPort, launch.LabDirectTcp));
        }
        args.Add("-v");
        return args;
    }

    /// <summary>The carriers a core config with CSL gives csl-client: the primary, and the spare when attached.</summary>
    public static List<ColituCslCarrier> Carriers(ColituCslPlan plan, bool spareAttached)
    {
        var carriers = new List<ColituCslCarrier> { new(PrimaryLabel, plan.Primary, plan.PrimaryFamily) };
        if (spareAttached && plan.Spare != null)
        {
            carriers.Add(new(SpareLabel, plan.Spare, plan.SpareFamily ?? "tcp"));
        }
        return carriers;
    }

    /// <summary>
    /// Adds CSL to a generated main-core config; <paramref name="plan"/> null returns
    /// <paramref name="config"/> itself (the same string), so a config without CSL is unchanged
    /// byte for byte. Null when the config does not have the expected shape (it is then used without CSL).
    /// </summary>
    public static ColituCslApplied? Apply(string config, bool singBox, ColituCslPlan? plan)
    {
        if (plan == null)
        {
            return new ColituCslApplied(config, false);
        }
        return singBox ? ApplySingbox(config, plan) : ApplyXray(config, plan);
    }

    /// <summary>
    /// Xray: the <c>colitu-csl</c> SOCKS outbound to csl-client; before every rule that sends
    /// traffic to the tunnel (<c>proxy</c> or the warm spare balancer) a copy limited to TCP that
    /// sends it to csl-client; the carrier inbounds first, each to its own outbound.
    /// </summary>
    public static ColituCslApplied? ApplyXray(string config, ColituCslPlan plan)
    {
        if (Parse(config) is not { } root || root["outbounds"] is not JsonArray outbounds)
        {
            return null;
        }
        var objects = outbounds.OfType<JsonObject>().ToList();
        if (!objects.Any(item => Tag(item) == ColituWarmSpare.ProxyTag) || objects.Any(item => Tag(item) == OutboundTag))
        {
            return null;
        }
        var spareAttached = plan.Spare != null && objects.Any(item => Tag(item) == ColituWarmSpare.XraySpareTag);
        outbounds.Add(new JsonObject
        {
            ["tag"] = OutboundTag,
            ["protocol"] = "socks",
            ["settings"] = new JsonObject
            {
                ["servers"] = new JsonArray(new JsonObject { ["address"] = "127.0.0.1", ["port"] = plan.SocksPort })
            }
        });
        var routing = root["routing"] as JsonObject ?? new JsonObject();
        root["routing"] = routing;
        var rules = routing["rules"] as JsonArray ?? new JsonArray();
        routing["rules"] = rules;
        var dnsTag = Str(root["dns"]?["tag"]);
        foreach (var rule in rules.OfType<JsonObject>().ToList())
        {
            var toTunnel = Str(rule["outboundTag"]) == ColituWarmSpare.ProxyTag || Str(rule["balancerTag"]) == ColituWarmSpare.XrayBalancerTag;
            if (!toTunnel || Strings(rule["inboundTag"]).Any(tag => InternalInbounds.Contains(tag) || tag == dnsTag) || !XrayCarriesTcp(rule["network"]))
            {
                continue;
            }
            var copy = (JsonObject)rule.DeepClone();
            copy.Remove("balancerTag");
            copy["outboundTag"] = OutboundTag;
            copy["network"] = "tcp";
            rules.Insert(rules.IndexOf(rule), copy);
        }
        // Xray sends traffic no rule matched to the first outbound.
        if (outbounds.OfType<JsonObject>().FirstOrDefault() is { } first && Tag(first) == ColituWarmSpare.ProxyTag)
        {
            rules.Add(new JsonObject { ["type"] = "field", ["network"] = "tcp", ["outboundTag"] = OutboundTag });
        }
        if (spareAttached)
        {
            AddXrayInbound(root, rules, plan.Spare!, SpareInboundTag, ColituWarmSpare.XraySpareTag);
        }
        AddXrayInbound(root, rules, plan.Primary, PrimaryInboundTag, ColituWarmSpare.ProxyTag);
        return new ColituCslApplied(root.ToJsonString(Indented), spareAttached);
    }

    /// <summary>
    /// sing-box: the same with sing-box fields; the primary is <c>proxy</c>, or <c>proxy-main</c>
    /// when a warm spare made <c>proxy</c> a urltest group; route.final <c>proxy</c> gets a TCP rule
    /// to csl-client at the end. Inverted and logical rules are left alone (their TCP stays outside CSL).
    /// </summary>
    public static ColituCslApplied? ApplySingbox(string config, ColituCslPlan plan)
    {
        if (Parse(config) is not { } root || root["outbounds"] is not JsonArray outbounds)
        {
            return null;
        }
        var objects = outbounds.OfType<JsonObject>().ToList();
        if (!objects.Any(item => Tag(item) == ColituWarmSpare.ProxyTag) || objects.Any(item => Tag(item) == OutboundTag))
        {
            return null;
        }
        var primaryTag = objects.Any(item => Tag(item) == ColituWarmSpare.SingboxPrimaryTag) ? ColituWarmSpare.SingboxPrimaryTag : ColituWarmSpare.ProxyTag;
        var spareAttached = plan.Spare != null && objects.Any(item => Tag(item) == ColituWarmSpare.SingboxSpareTag);
        outbounds.Add(new JsonObject
        {
            ["type"] = "socks",
            ["tag"] = OutboundTag,
            ["server"] = "127.0.0.1",
            ["server_port"] = plan.SocksPort,
            ["version"] = "5"
        });
        var route = root["route"] as JsonObject ?? new JsonObject();
        root["route"] = route;
        var rules = route["rules"] as JsonArray ?? new JsonArray();
        route["rules"] = rules;
        foreach (var rule in rules.OfType<JsonObject>().ToList())
        {
            if (Str(rule["outbound"]) != ColituWarmSpare.ProxyTag || Str(rule["action"]) is { } action && action != "route"
                || Str(rule["type"]) == "logical" || rule["invert"]?.GetValueKind() == JsonValueKind.True
                || Strings(rule["inbound"]).Any(InternalInbounds.Contains) || !SingboxCarriesTcp(rule["network"]))
            {
                continue;
            }
            var copy = (JsonObject)rule.DeepClone();
            copy["outbound"] = OutboundTag;
            copy["network"] = new JsonArray("tcp");
            rules.Insert(rules.IndexOf(rule), copy);
        }
        var final = Str(route["final"]) ?? Tag(objects[0]);
        if (final == ColituWarmSpare.ProxyTag)
        {
            rules.Add(new JsonObject { ["network"] = new JsonArray("tcp"), ["outbound"] = OutboundTag });
        }
        if (spareAttached)
        {
            AddSingboxInbound(root, rules, plan.Spare!, SpareInboundTag, ColituWarmSpare.SingboxSpareTag);
        }
        AddSingboxInbound(root, rules, plan.Primary, PrimaryInboundTag, primaryTag);
        return new ColituCslApplied(root.ToJsonString(Indented), spareAttached);
    }

    private static bool XrayCarriesTcp(JsonNode? network) => Str(network) is not { } value
        || value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains("tcp", StringComparer.OrdinalIgnoreCase);

    private static bool SingboxCarriesTcp(JsonNode? network) => network == null || Strings(network).Contains("tcp", StringComparer.OrdinalIgnoreCase);

    private static void AddXrayInbound(JsonObject root, JsonArray rules, ColituVerifyInbound inbound, string tag, string outbound)
    {
        var inbounds = root["inbounds"] as JsonArray ?? new JsonArray();
        root["inbounds"] = inbounds;
        inbounds.Add(new JsonObject
        {
            ["tag"] = tag,
            ["listen"] = "127.0.0.1",
            ["port"] = inbound.Port,
            ["protocol"] = "socks",
            ["settings"] = new JsonObject
            {
                ["auth"] = "password",
                ["accounts"] = new JsonArray(new JsonObject { ["user"] = inbound.User, ["pass"] = inbound.Password }),
                ["udp"] = false
            }
        });
        rules.Insert(0, new JsonObject { ["type"] = "field", ["inboundTag"] = new JsonArray(tag), ["outboundTag"] = outbound });
    }

    private static void AddSingboxInbound(JsonObject root, JsonArray rules, ColituVerifyInbound inbound, string tag, string outbound)
    {
        var inbounds = root["inbounds"] as JsonArray ?? new JsonArray();
        root["inbounds"] = inbounds;
        inbounds.Add(new JsonObject
        {
            ["type"] = "socks",
            ["tag"] = tag,
            ["listen"] = "127.0.0.1",
            ["listen_port"] = inbound.Port,
            ["users"] = new JsonArray(new JsonObject { ["username"] = inbound.User, ["password"] = inbound.Password })
        });
        rules.Insert(0, new JsonObject { ["inbound"] = new JsonArray(tag), ["outbound"] = outbound });
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static JsonObject? Parse(string config)
    {
        try
        {
            return JsonNode.Parse(config) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Tag(JsonObject item) => Str(item["tag"]);

    private static string? Str(JsonNode? node) => node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;

    private static IEnumerable<string> Strings(JsonNode? node) => node switch
    {
        JsonArray array => array.Select(Str).OfType<string>(),
        _ when Str(node) is { } single => [single],
        _ => []
    };

    /// <summary>
    /// Writes the device's CSL credential to <paramref name="path"/>, readable by the current user
    /// and SYSTEM only (no inherited access). Replaces an older file.
    /// </summary>
    public static void WriteCredentialFile(string path, string credential)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        DeleteCredentialFile(path);
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        if (WindowsIdentity.GetCurrent().User is { } user)
        {
            security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        }
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        using var stream = new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.None, security);
        stream.Write(Encoding.ASCII.GetBytes(credential));
    }

    /// <summary>Deletes the credential file; true when none is left.</summary>
    public static bool DeleteCredentialFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            return !File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>The <c>csl</c> field of a <c>/config</c> envelope. The credential is a secret: never logged.</summary>
public sealed class ColituCslConfig
{
    private static readonly Regex CredentialShape = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    [JsonPropertyName("version")] public int Version { get; set; }
    [JsonPropertyName("port")] public int Port { get; set; }
    [JsonPropertyName("credential")] public string? Credential { get; set; }

    /// <summary>Version 1, a port, and a credential of 64 lowercase hex digits.</summary>
    [JsonIgnore]
    public bool IsUsable => Version == 1 && Port is > 0 and < 65536 && Credential != null && CredentialShape.IsMatch(Credential);

    public override string ToString() => $"csl v{Version} port {Port} credential {(string.IsNullOrEmpty(Credential) ? "none" : "redacted")}";
}

/// <summary>
/// CSL for the next core start: csl-client's SOCKS port, and the carrier inbounds (port and
/// per-run account) to the primary and, when it runs on the same server, the warm spare.
/// </summary>
internal sealed record ColituCslPlan(string PrimaryIndexId, int SocksPort, int ServerPort, ColituVerifyInbound Primary, string PrimaryFamily,
    ColituVerifyInbound? Spare, string? SpareFamily);

/// <summary>A config with CSL in it, and whether the spare carrier is there too.</summary>
internal sealed record ColituCslApplied(string Config, bool SpareAttached);

internal sealed record ColituCslCarrier(string Label, ColituVerifyInbound Inbound, string Family);

internal sealed record ColituCslLaunch(int SocksPort, int ServerPort, IReadOnlyList<ColituCslCarrier> Carriers, bool LabDirectTcp = false)
{
    public string Describe() => string.Join(", ", Carriers.Select(item => $"{item.Label}/{item.Family}"));
}

/// <summary>A started csl-client: <see cref="Ready"/> ends with "" (on) or the reason it is off.</summary>
internal sealed record ColituCslRun(int ProcessId, ColituCslLaunch Launch, Task<string> Ready);

/// <summary>The csl-client sidecar process (<c>bin\colitu-csl\csl-client.exe</c>); one at a time.</summary>
internal sealed class ColituCsl
{
    public static ColituCsl Instance { get; } = new();

    internal static string ExecutablePath => PathOverride ?? Path.Combine(Utils.StartupPath(), "bin", "colitu-csl", "csl-client.exe");

    /// <summary>Tests only: another csl-client binary.</summary>
    internal static string? PathOverride { get; set; }

    /// <summary>In the app's per-user config folder; deleted whenever csl-client stops.</summary>
    internal static string CredentialPath => CredentialPathOverride ?? ColituHardening.UserConfigPath("colitu-csl-credential.txt");

    /// <summary>Tests only.</summary>
    internal static string? CredentialPathOverride { get; set; }

    /// <summary>How long csl-client gets for its first carrier (tests shorten it).</summary>
    internal static TimeSpan Deadline { get; set; } = ColituCslRules.CarrierDeadline;

    /// <summary>A csl-client that was on exited by itself (not stopped): the reason.</summary>
    public event Action<string>? Exited;

    private readonly object _gate = new();
    private Process? _process;
    private int _generation;

    public bool Running
    {
        get
        {
            lock (_gate)
            {
                try
                {
                    return _process is { HasExited: false };
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
        }
    }

    /// <summary>
    /// Writes the credential file and starts csl-client. Never waits: <see cref="ColituCslRun.Ready"/>
    /// tells when it has a carrier, exited, or missed <see cref="ColituCslRules.CarrierDeadline"/>
    /// (it is then stopped). Throws when the file or the process cannot be created.
    /// </summary>
    public ColituCslRun Start(ColituCslLaunch launch, string credential, Action<string> log)
    {
        Stop();
        var credentialPath = CredentialPath;
        ColituCslRules.WriteCredentialFile(credentialPath, credential);
        var info = new ProcessStartInfo(ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = new UTF8Encoding(false),
            WorkingDirectory = Path.GetDirectoryName(ExecutablePath)!
        };
        foreach (var arg in ColituCslRules.Arguments(launch, credentialPath))
        {
            info.ArgumentList.Add(arg);
        }
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var watch = Stopwatch.StartNew();
        int generation;
        var isReady = false;
        var lines = 0;
        lock (_gate)
        {
            generation = ++_generation;
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not { Length: > 0 } line) return;
                if (ColituCslRules.IsReadyLine(line) && ColituCslRules.Verdict(true, false, null, watch.Elapsed) == "")
                {
                    isReady = true;
                    ready.TrySetResult("");
                }
                // Carrier events name labels and loopback ports only; a few per run are enough.
                if (Interlocked.Increment(ref lines) <= 40)
                {
                    log($"CSL alpha | {StripTimestamp(line)}");
                }
            };
            process.Exited += (_, _) =>
            {
                int? code = null;
                try
                {
                    code = process.ExitCode;
                }
                catch (InvalidOperationException)
                {
                }
                var reason = ColituCslRules.Verdict(isReady, true, code, watch.Elapsed)!;
                bool current;
                lock (_gate)
                {
                    current = generation == _generation;
                }
                if (!ready.TrySetResult(reason) && current)
                {
                    // It was on: the service falls back to the core without CSL.
                    Exited?.Invoke(reason);
                }
                if (current)
                {
                    ColituCslRules.DeleteCredentialFile(credentialPath);
                }
            };
            try
            {
                process.Start();
            }
            catch
            {
                ColituCslRules.DeleteCredentialFile(credentialPath);
                process.Dispose();
                throw;
            }
            _process = process;
        }
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        log($"CSL alpha | csl-client started (pid {process.Id}, carriers {launch.Describe()}, SOCKS 127.0.0.1:{launch.SocksPort})");
        var deadline = Deadline;
        _ = Task.Delay(deadline).ContinueWith(_ =>
        {
            if (ColituCslRules.Verdict(isReady, false, null, watch.Elapsed, deadline) is { Length: > 0 } reason && ready.TrySetResult(reason))
            {
                StopGeneration(generation);
            }
        }, TaskScheduler.Default);
        return new ColituCslRun(process.Id, launch, ready.Task);
    }

    /// <summary>Stops csl-client (if any) and deletes the credential file. Not reported as an exit.</summary>
    public void Stop()
    {
        Process? process;
        lock (_gate)
        {
            _generation++;
            process = _process;
            _process = null;
        }
        Kill(process);
        ColituCslRules.DeleteCredentialFile(CredentialPath);
    }

    private void StopGeneration(int generation)
    {
        lock (_gate)
        {
            if (generation != _generation)
            {
                return;
            }
        }
        Stop();
    }

    private static void Kill(Process? process)
    {
        if (process == null)
        {
            return;
        }
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(2000);
            }
        }
        catch
        {
            // Already gone.
        }
        process.Dispose();
    }

    private static string StripTimestamp(string line) =>
        Regex.Replace(line, @"^\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2}(\.\d+)? ", "");
}
