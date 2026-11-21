using System.Diagnostics;
using System.IO;
using AwesomeAssertions;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

/// <summary>
/// ColituGecko, local lab (no core): runs only when COLITU_GECKO_LAB_CLIENT names a
/// gecko-client binary and COLITU_GECKO_LAB_CONFIG a file with a lab configuration (the exact bytes <c>GET /gecko/config</c>
/// returns) for a Gecko server the caller started, COLITU_GECKO_LAB_CONFIG_SIG its signature (the header value)
/// and COLITU_GECKO_LAB_CONFIG_KEY the public key to pin instead of the production one (<c>id=hex</c>). Optional:
/// COLITU_GECKO_LAB_OUT (log file). gecko-client gets the command line and standard input
/// <see cref="ColituGecko"/> builds; the test waits for the verdict, hands over the configuration
/// again (<c>config_b64</c>) and stops it.
/// </summary>
[Collection("ColituGecko sidecar")]
public class ColituGeckoLabTests
{
    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

    [Fact]
    public async Task Lab_SessionComesUp_TakesANewConfig_AndStops()
    {
        Assert.SkipWhen(Env("COLITU_GECKO_LAB_CLIENT") == null || Env("COLITU_GECKO_LAB_CONFIG") == null || Env("COLITU_GECKO_LAB_CONFIG_SIG") == null || Env("COLITU_GECKO_LAB_CONFIG_KEY") == null, "local Gecko lab not set up");
        var output = new List<string>();
        var body = await File.ReadAllBytesAsync(Env("COLITU_GECKO_LAB_CONFIG")!, TestContext.Current.CancellationToken);
        var config = ColituGeckoRules.Sign(body, Env("COLITU_GECKO_LAB_CONFIG_SIG"))!;
        if (Env("COLITU_GECKO_LAB_CONFIG_KEY")!.Split('=') is [var configId, var configKey] && int.TryParse(configId, out var configKeyId))
        {
            ColituGeckoRules.TestConfigKeys = new Dictionary<int, string> { [configKeyId] = configKey };
        }
        ColituGecko.PathOverride = Env("COLITU_GECKO_LAB_CLIENT");
        // The canary's descriptors are signed with the lab key, which the app does not pin.
        if (Env("COLITU_GECKO_LAB_SKIN_KEY") is { } lab && lab.Split('=') is [var id, var key] && int.TryParse(id, out var keyId))
        {
            ColituGeckoRules.TestSkinKeys = new Dictionary<int, string> { [keyId] = key };
        }
        var exits = new List<string>();
        void OnExit(string reason) => exits.Add(reason);
        ColituGecko.Instance.Exited += OnExit;
        try
        {
            var inbound = ColituWarmSpare.NewVerifyInbound();
            var launch = new ColituGeckoLaunch(inbound.Port, inbound.User, inbound.Password);
            output.Add("args: " + string.Join(" ", ColituGeckoRules.Arguments(launch)));
            var watch = Stopwatch.StartNew();
            var run = ColituGecko.Instance.Start(launch, config, null, line => { lock (output) output.Add(line); });
            var verdict = await run.Ready.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            output.Add($"verdict: '{verdict}' after {watch.ElapsedMilliseconds} ms (pid {run.ProcessId})");
            verdict.Should().Be("");
            ColituGecko.Instance.SendConfig(config).Should().BeTrue();
            await Task.Delay(1000, TestContext.Current.CancellationToken);
            ColituGecko.Instance.Running.Should().BeTrue();

            ColituGecko.Instance.Stop();
            ColituGecko.Instance.Running.Should().BeFalse();
            exits.Should().BeEmpty("a stop is not an unexpected exit");
            lock (output) output.Should().NotContain(line => line.Contains(inbound.Password));
        }
        finally
        {
            ColituGeckoRules.TestSkinKeys = null;
            ColituGeckoRules.TestConfigKeys = null;
            ColituGecko.Instance.Exited -= OnExit;
            ColituGecko.Instance.Stop();
            ColituGecko.PathOverride = null;
            if (Env("COLITU_GECKO_LAB_OUT") is { } path)
            {
                lock (output) File.AppendAllLines(path, output);
            }
        }
    }
}
