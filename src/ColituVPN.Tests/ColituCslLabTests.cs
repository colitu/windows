using System.Diagnostics;
using System.IO;
using AwesomeAssertions;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

/// <summary>
/// CSL alpha, local end-to-end lab (no server, no core): runs only when COLITU_CSL_LAB_CLIENT is
/// set. The caller starts <c>csl-server -listen 127.0.0.1:9440 -allow-private</c>,
/// <c>cslbench source -listen 127.0.0.1:9500</c> and two loopback TCP forwarders to 9440 (the
/// carriers), and passes COLITU_CSL_LAB_BENCH, COLITU_CSL_LAB_FWD_A / _B (ports),
/// COLITU_CSL_LAB_FWD_A_PID (killed mid-download) and COLITU_CSL_LAB_OUT (log file).
/// csl-client gets the command line <see cref="ColituCsl"/> builds, with lab carriers
/// (tcp:// straight to the forwarders instead of the core's SOCKS inbounds).
/// </summary>
[Collection("ColituCsl sidecar")]
public class ColituCslLabTests
{
    private const string Credential = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

    private static int FreePort() => ColituWarmSpare.NewVerifyInbound().Port;

    [Fact]
    public async Task Lab_DownloadSurvivesACarrierDying_AndTheCredentialFileIsGoneAfterStop()
    {
        Assert.SkipWhen(Env("COLITU_CSL_LAB_CLIENT") == null, "local CSL lab not set up");
        var output = new List<string>();
        var dir = Path.Combine(Path.GetTempPath(), "colitu-csl-lab-" + Guid.NewGuid().ToString("N"));
        ColituCsl.PathOverride = Env("COLITU_CSL_LAB_CLIENT");
        ColituCsl.CredentialPathOverride = Path.Combine(dir, "colitu-csl-credential.txt");
        var exits = new List<string>();
        void OnExit(string reason) => exits.Add(reason);
        ColituCsl.Instance.Exited += OnExit;
        try
        {
            var socks = FreePort();
            var launch = new ColituCslLaunch(socks, 9440,
            [
                new(ColituCslRules.PrimaryLabel, new ColituVerifyInbound(int.Parse(Env("COLITU_CSL_LAB_FWD_A")!), "u", "p"), "tcp"),
                new(ColituCslRules.SpareLabel, new ColituVerifyInbound(int.Parse(Env("COLITU_CSL_LAB_FWD_B")!), "u", "p"), "tcp")
            ], LabDirectTcp: true);
            output.Add("args: " + string.Join(" ", ColituCslRules.Arguments(launch, ColituCsl.CredentialPathOverride)));
            var watch = Stopwatch.StartNew();
            var run = ColituCsl.Instance.Start(launch, Credential, line => { lock (output) output.Add(line); });
            var verdict = await run.Ready.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            output.Add($"verdict: '{verdict}' after {watch.ElapsedMilliseconds} ms (pid {run.ProcessId})");
            verdict.Should().Be("");
            File.Exists(ColituCsl.CredentialPathOverride).Should().BeTrue();

            var bench = Process.Start(new ProcessStartInfo(Env("COLITU_CSL_LAB_BENCH")!)
            {
                ArgumentList = { "get", "-socks", $"127.0.0.1:{socks}", "-target", "127.0.0.1:9500", "-size", "64MiB", "-rate", "16MiB" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            })!;
            var stdout = bench.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var stderr = bench.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await Task.Delay(1500, TestContext.Current.CancellationToken);
            using (var forwarderA = Process.GetProcessById(int.Parse(Env("COLITU_CSL_LAB_FWD_A_PID")!)))
            {
                forwarderA.Kill();
                output.Add($"killed carrier A's forwarder (pid {forwarderA.Id}) at {watch.ElapsedMilliseconds} ms");
            }
            await bench.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
            output.Add($"cslbench exit {bench.ExitCode}: {(await stdout).Trim()} {(await stderr).Trim()}");
            bench.ExitCode.Should().Be(0);
            ColituCsl.Instance.Running.Should().BeTrue("the session lives on carrier B");
            exits.Should().BeEmpty();

            ColituCsl.Instance.Stop();
            ColituCsl.Instance.Running.Should().BeFalse();
            File.Exists(ColituCsl.CredentialPathOverride).Should().BeFalse();
            exits.Should().BeEmpty("a stop is not an unexpected exit");
            output.Add("stopped; credential file deleted");
        }
        finally
        {
            ColituCsl.Instance.Exited -= OnExit;
            ColituCsl.Instance.Stop();
            ColituCsl.PathOverride = null;
            ColituCsl.CredentialPathOverride = null;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            if (Env("COLITU_CSL_LAB_OUT") is { } path)
            {
                lock (output) File.AppendAllLines(path, output);
            }
        }
    }

    [Fact]
    public async Task Lab_SilentCarrier_IsStoppedAfterTenSeconds()
    {
        Assert.SkipWhen(Env("COLITU_CSL_LAB_SILENT") == null, "no silent listener in the lab");
        var output = new List<string>();
        var dir = Path.Combine(Path.GetTempPath(), "colitu-csl-lab-" + Guid.NewGuid().ToString("N"));
        ColituCsl.PathOverride = Env("COLITU_CSL_LAB_CLIENT");
        ColituCsl.CredentialPathOverride = Path.Combine(dir, "colitu-csl-credential.txt");
        try
        {
            // The port accepts and never answers the handshake.
            var launch = new ColituCslLaunch(FreePort(), 9440, [new(ColituCslRules.PrimaryLabel, new ColituVerifyInbound(int.Parse(Env("COLITU_CSL_LAB_SILENT")!), "u", "p"), "tcp")], LabDirectTcp: true);
            var watch = Stopwatch.StartNew();
            var run = ColituCsl.Instance.Start(launch, Credential, line => { lock (output) output.Add(line); });
            var verdict = await run.Ready.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            output.Add($"silent carrier verdict: '{verdict}' after {watch.ElapsedMilliseconds} ms");
            verdict.Should().NotBeEmpty();
            watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(11));
            await Task.Delay(500, TestContext.Current.CancellationToken);
            ColituCsl.Instance.Running.Should().BeFalse();
            File.Exists(ColituCsl.CredentialPathOverride).Should().BeFalse();
        }
        finally
        {
            ColituCsl.Instance.Stop();
            ColituCsl.PathOverride = null;
            ColituCsl.CredentialPathOverride = null;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            if (Env("COLITU_CSL_LAB_OUT") is { } path)
            {
                lock (output) File.AppendAllLines(path, output);
            }
        }
    }

    [Fact]
    public async Task Lab_NoCarrier_FallsBackWithAReason()
    {
        Assert.SkipWhen(Env("COLITU_CSL_LAB_CLIENT") == null, "local CSL lab not set up");
        var output = new List<string>();
        var dir = Path.Combine(Path.GetTempPath(), "colitu-csl-lab-" + Guid.NewGuid().ToString("N"));
        ColituCsl.PathOverride = Env("COLITU_CSL_LAB_CLIENT");
        ColituCsl.CredentialPathOverride = Path.Combine(dir, "colitu-csl-credential.txt");
        try
        {
            // Nothing listens on these ports.
            var launch = new ColituCslLaunch(FreePort(), 9440, [new(ColituCslRules.PrimaryLabel, new ColituVerifyInbound(FreePort(), "u", "p"), "tcp")], LabDirectTcp: true);
            var watch = Stopwatch.StartNew();
            var run = ColituCsl.Instance.Start(launch, Credential, line => { lock (output) output.Add(line); });
            var verdict = await run.Ready.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            output.Add($"fallback verdict: '{verdict}' after {watch.ElapsedMilliseconds} ms");
            verdict.Should().NotBeEmpty();
            watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(11));
            await Task.Delay(500, TestContext.Current.CancellationToken);
            ColituCsl.Instance.Running.Should().BeFalse();
            File.Exists(ColituCsl.CredentialPathOverride).Should().BeFalse();
        }
        finally
        {
            ColituCsl.Instance.Stop();
            ColituCsl.PathOverride = null;
            ColituCsl.CredentialPathOverride = null;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            if (Env("COLITU_CSL_LAB_OUT") is { } path)
            {
                lock (output) File.AppendAllLines(path, output);
            }
        }
    }
}
