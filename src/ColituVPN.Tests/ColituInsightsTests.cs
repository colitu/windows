using System.IO;
using System.Text.Json;
using AwesomeAssertions;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

/// <summary>Anonymous statistics (histograms, caps, reset, request JSON) and the in-app surveys.</summary>
public class ColituInsightsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "colitu-insights-" + Guid.NewGuid().ToString("N"));
    private long _tick;

    public ColituInsightsTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string FilePath => Path.Combine(_dir, "insights.json");

    private ColituInsights Create(bool existingInstall = false) =>
        new(() => FilePath, () => _tick, () => existingInstall);

    // ── Histogram buckets ──────────────────────────────────────────────────
    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(500, 0)]
    [InlineData(501, 1)]
    [InlineData(1000, 1)]
    [InlineData(1001, 2)]
    [InlineData(2000, 2)]
    [InlineData(3000, 3)]
    [InlineData(3001, 4)]
    [InlineData(5000, 4)]
    [InlineData(8000, 5)]
    [InlineData(8001, 6)]
    [InlineData(15000, 6)]
    [InlineData(15001, 7)]
    [InlineData(30000, 7)]
    [InlineData(30001, 8)]
    [InlineData(600000, 8)]
    public void BucketIndex_UsesTheUpperBounds(long ms, int bucket)
    {
        ColituInsightSpec.BucketIndex(ms).Should().Be(bucket);
    }

    [Fact]
    public void Observe_FillsTheBucketsOfTheReport()
    {
        var insights = Create();
        insights.Count("connect_attempts");
        insights.Observe("connect_ms", 400);
        insights.Observe("connect_ms", 900);
        insights.Observe("connect_ms", 900);
        insights.Observe("connect_ms", 99999);
        var report = insights.BuildReport(Now, "2.9.0", true)!;
        report.Histograms["connect_ms"].Should().Equal(1, 2, 0, 0, 0, 0, 0, 0, 1);
        report.Histograms.Should().NotContainKey("recovery_ms");
    }

    // ── Counters: caps, unknown names, reset ───────────────────────────────
    [Fact]
    public void Counters_AreCappedAtTenThousandPerReport()
    {
        var insights = Create();
        insights.Count("connect_attempts", 12000);
        insights.Count("sessions", 3);
        var report = insights.BuildReport(Now, "2.9.0", false)!;
        report.Counters["connect_attempts"].Should().Be(10000);
        report.Counters["sessions"].Should().Be(3);

        // The panel took 10000; what exceeded the cap is sent with the next report.
        insights.Commit(report, Now);
        var next = insights.BuildReport(Now + ColituInsightSpec.MinSendInterval, "2.9.0", false)!;
        next.Counters.Should().Equal(new Dictionary<string, long> { ["connect_attempts"] = 2000 });
    }

    [Fact]
    public void HistogramTotal_IsCappedAtTenThousand()
    {
        var capped = ColituInsights.CapBuckets([6000, 6000, 0, 0, 0, 0, 0, 0, 10]);
        capped.Sum().Should().Be(10000);
        capped[0].Should().Be(6000);
        capped[1].Should().Be(4000);
        capped[8].Should().Be(0);
    }

    [Fact]
    public void UnknownNamesAreIgnored()
    {
        var insights = Create();
        insights.Count("account_email");
        insights.Observe("page_load_ms", 100);
        insights.AddFeatures(["visited_sites", "kill_switch"]);
        insights.HasData.Should().BeFalse();
        insights.BuildReport(Now, "2.9.0", true).Should().BeNull();
    }

    [Fact]
    public void Commit_ResetsWhatWasSent_AndKeepsWhatCameInMeanwhile()
    {
        var insights = Create();
        insights.Count("connect_attempts", 4);
        insights.Observe("connect_ms", 700);
        insights.AddFeatures(["auto_connect"]);
        var report = insights.BuildReport(Now, "2.9.0", true)!;

        insights.Count("connect_attempts", 2);   // during the send
        insights.Observe("connect_ms", 700);
        insights.Commit(report, Now);

        var next = insights.BuildReport(Now + ColituInsightSpec.MinSendInterval, "2.9.0", true)!;
        next.Counters["connect_attempts"].Should().Be(2);
        next.Histograms["connect_ms"].Should().Equal(0, 1, 0, 0, 0, 0, 0, 0, 0);
        next.Features.Should().BeEmpty();
    }

    [Fact]
    public void Commit_WithEverythingSent_LeavesNothing()
    {
        var insights = Create();
        insights.Count("sessions");
        insights.Commit(insights.BuildReport(Now, "2.9.0", true)!, Now);
        insights.HasData.Should().BeFalse();
    }

    // ── Timing of reports ──────────────────────────────────────────────────
    [Fact]
    public void Report_IsDueOnlyEverySixHours_AndAfterAFailureWaitsHalfAnHour()
    {
        var insights = Create();
        insights.Count("sessions");
        insights.Commit(insights.BuildReport(Now, "2.9.0", true)!, Now);

        insights.Count("sessions");
        insights.BuildReport(Now + TimeSpan.FromHours(5), "2.9.0", true).Should().BeNull();
        var due = Now + TimeSpan.FromHours(6);
        insights.BuildReport(due, "2.9.0", true).Should().NotBeNull();

        insights.SendFailed(due);
        insights.BuildReport(due + TimeSpan.FromMinutes(10), "2.9.0", true).Should().BeNull();
        insights.BuildReport(due + TimeSpan.FromMinutes(31), "2.9.0", true).Should().NotBeNull();
    }

    [Fact]
    public void NoData_NoReport()
    {
        Create().BuildReport(Now, "2.9.0", true).Should().BeNull();
    }

    // ── Switching the statistics off ───────────────────────────────────────
    [Fact]
    public void TurningStatsOff_ClearsPendingAndCollectsNothing()
    {
        var insights = Create();
        insights.Count("connect_attempts", 3);
        insights.Observe("connect_ms", 1200);
        insights.AddFeatures(["kill_switch"]);
        insights.SetStatsEnabled(false);

        insights.StatsEnabled.Should().BeFalse();
        insights.HasData.Should().BeFalse();

        insights.Count("connect_attempts");
        insights.ConnectFinished(true, 800);
        insights.FunctionalCheck(true);
        insights.Observe("connect_ms", 100);
        insights.AddFeatures(["auto_connect"]);
        insights.BuildReport(Now, "2.9.0", true).Should().BeNull();

        insights.SetStatsEnabled(true);
        insights.HasData.Should().BeFalse();
    }

    [Fact]
    public void Settings_SurviveARestart()
    {
        var first = Create();
        first.SetStatsEnabled(false);
        first.SetSurveysEnabled(false);
        var second = Create();
        second.StatsEnabled.Should().BeFalse();
        second.SurveysEnabled.Should().BeFalse();

        var third = Create();
        third.SetStatsEnabled(true);
        third.Count("sessions", 2);
        Create().BuildReport(Now, "2.9.0", true)!.Counters["sessions"].Should().Be(2);
    }

    [Fact]
    public void DefaultsAreOn_AndADamagedFileFallsBackToThem()
    {
        File.WriteAllText(FilePath, "{ not json");
        var insights = Create();
        insights.StatsEnabled.Should().BeTrue();
        insights.SurveysEnabled.Should().BeTrue();
    }

    // ── Connection events ──────────────────────────────────────────────────
    [Fact]
    public void ConnectFinished_CountsAttemptsAndOnlyTheVeryFirstAsFirstConnect()
    {
        var insights = Create();
        insights.ConnectFinished(false, 20000);
        insights.ConnectFinished(true, 2500);
        insights.ConnectFinished(true, 1800);
        var report = insights.BuildReport(Now, "2.9.0", true)!;
        report.Counters.Should().Equal(new Dictionary<string, long>
        {
            ["connect_attempts"] = 3,
            ["connect_successes"] = 2,
            ["first_connect_attempts"] = 1,
            ["sessions"] = 2
        });
        // Durations are those of connects that worked.
        report.Histograms["connect_ms"].Sum().Should().Be(2);
    }

    [Fact]
    public void FirstConnect_Succeeded_IsZeroOrOne()
    {
        var insights = Create();
        insights.ConnectFinished(true, 900);
        insights.ConnectFinished(true, 900);
        var counters = insights.BuildReport(Now, "2.9.0", true)!.Counters;
        counters["first_connect_attempts"].Should().Be(1);
        counters["first_connect_successes"].Should().Be(1);
    }

    [Fact]
    public void FirstConnect_IsUsedUpEvenWhileStatsAreOff_AndNotCountedOnAnExistingInstall()
    {
        var off = Create();
        off.SetStatsEnabled(false);
        off.ConnectFinished(true, 900);
        off.SetStatsEnabled(true);
        off.ConnectFinished(true, 900);
        off.BuildReport(Now, "2.9.0", true)!.Counters.Should().NotContainKey("first_connect_attempts");

        File.Delete(FilePath);
        var updated = Create(existingInstall: true);
        updated.ConnectFinished(true, 900);
        updated.BuildReport(Now, "2.9.0", true)!.Counters.Should().NotContainKey("first_connect_attempts");
    }

    [Fact]
    public void FunctionalChecks_CountOkSeparately()
    {
        var insights = Create();
        insights.FunctionalCheck(false);
        insights.FunctionalCheck(true);
        insights.FunctionalCheck(true);
        var counters = insights.BuildReport(Now, "2.9.0", true)!.Counters;
        counters["functional_checks"].Should().Be(3);
        counters["functional_ok"].Should().Be(2);
    }

    [Fact]
    public void Recovery_CountsOnEnd_AndTimesTheSuccess()
    {
        var insights = Create();
        _tick = 1_000;
        insights.BeginRecovery();
        _tick = 1_000 + 2_500;
        insights.BeginRecovery();           // a second start does not restart the clock
        _tick = 1_000 + 4_000;
        insights.EndRecovery(true);
        insights.EndRecovery(true);         // nothing open: nothing counted

        var report = insights.BuildReport(Now, "2.9.0", true)!;
        report.Counters["recoveries"].Should().Be(1);
        report.Counters["recovery_successes"].Should().Be(1);
        report.Histograms["recovery_ms"].Should().Equal(0, 0, 0, 0, 1, 0, 0, 0, 0);
    }

    [Fact]
    public void Recovery_StartedButNeverEnded_CountsNothing_EvenAfterARestart()
    {
        var insights = Create();
        insights.BeginRecovery().Should().BeTrue();
        insights.BeginRecovery().Should().BeFalse();      // already open: ignored
        insights.BeginRecovery().Should().BeFalse();
        insights.BuildReport(Now, "2.9.0", true).Should().BeNull();          // nothing counted at the start

        // The app closed in the middle of the recovery: the next run starts with nothing open.
        var restarted = Create();
        restarted.EndRecovery(true).Should().BeNull();
        restarted.BuildReport(Now, "2.9.0", true).Should().BeNull();
    }

    [Fact]
    public void Recovery_GivenUp_IsCountedWithoutSuccess_AndAbandonedCountsNothing()
    {
        var insights = Create();
        insights.BeginRecovery();
        insights.EndRecovery(false);
        insights.BeginRecovery();
        insights.AbandonRecovery();
        insights.EndRecovery(true);

        var report = insights.BuildReport(Now, "2.9.0", true)!;
        report.Counters.Should().Equal(new Dictionary<string, long> { ["recoveries"] = 1 });
        report.Histograms.Should().BeEmpty();
    }

    [Fact]
    public void UnexpectedDisconnect_IsCountedOncePerOutage()
    {
        var insights = Create();
        insights.UnexpectedDisconnect();
        insights.UnexpectedDisconnect();
        insights.BeginRecovery();
        insights.EndRecovery(true);        // back: the next outage counts again
        insights.UnexpectedDisconnect();
        insights.BuildReport(Now, "2.9.0", true)!.Counters["unexpected_disconnects"].Should().Be(2);
    }

    [Fact]
    public void Flap_IsASwitchBackToThePreviousProtocolWithinTwoMinutes()
    {
        var insights = Create();
        insights.ProtocolSwitch("hysteria2", "vless-reality", true, Now);
        insights.ProtocolSwitch("vless-reality", "hysteria2", true, Now + TimeSpan.FromSeconds(90));   // back: flap
        insights.ProtocolSwitch("hysteria2", "trojan", true, Now + TimeSpan.FromMinutes(10));
        insights.ProtocolSwitch("trojan", "hysteria2", true, Now + TimeSpan.FromMinutes(13));          // too late
        insights.ProtocolSwitch("hysteria2", "trojan", false, Now + TimeSpan.FromMinutes(14));         // failed

        var counters = insights.BuildReport(Now, "2.9.0", true)!.Counters;
        counters["protocol_switches"].Should().Be(5);
        counters["protocol_switch_successes"].Should().Be(4);
        counters["flaps"].Should().Be(1);
    }

    // ── Features ───────────────────────────────────────────────────────────
    [Fact]
    public void FeaturesOf_ReportsOnlyTheFeaturesInUse()
    {
        var none = new ColituVpnPreferences(KillSwitchEnabled: false, AutoConnectEnabled: false, PrivacyModeEnabled: false, AdvancedMode: false);
        ColituInsightSpec.FeaturesOf(none, manualServer: false, multihop: false).Should().BeEmpty();

        var all = new ColituVpnPreferences(KillSwitchEnabled: true, AutoConnectEnabled: true, AdBlockEnabled: true,
            PrivacyModeEnabled: true, AdvancedMode: true, SplitTunnelMode: ColituSplitTunnelModes.Bypass);
        ColituInsightSpec.FeaturesOf(all, manualServer: true, multihop: true).Should().BeEquivalentTo(
            "split_tunnel", "shield", "ad_block", "advanced_mode", "multihop", "auto_connect", "kill_switch", "manual_server");
    }

    // ── Request JSON ───────────────────────────────────────────────────────
    [Fact]
    public void RequestJson_HasTheContractedShape()
    {
        var insights = Create();
        insights.Count("connect_attempts", 2);
        insights.Count("connect_successes", 1);
        insights.Observe("connect_ms", 1500);
        insights.AddFeatures(["kill_switch", "auto_connect"]);
        var report = insights.BuildReport(Now, "2.9.1", true)!;

        using var doc = JsonDocument.Parse(report.ToJson());
        var root = doc.RootElement;
        root.EnumerateObject().Select(item => item.Name).Should().BeEquivalentTo(
            "platform", "app_version", "counters", "histograms", "kill_switch", "features");
        root.GetProperty("platform").GetString().Should().Be("windows");
        root.GetProperty("app_version").GetString().Should().Be("2.9.1");
        root.GetProperty("kill_switch").GetBoolean().Should().BeTrue();
        root.GetProperty("counters").GetProperty("connect_attempts").GetInt64().Should().Be(2);
        root.GetProperty("counters").EnumerateObject().Should().HaveCount(2);   // non-zero only
        var buckets = root.GetProperty("histograms").GetProperty("connect_ms").EnumerateArray().Select(item => item.GetInt32()).ToArray();
        buckets.Should().Equal(0, 0, 1, 0, 0, 0, 0, 0, 0);
        root.GetProperty("features").EnumerateArray().Select(item => item.GetString()).Should().Equal("auto_connect", "kill_switch");
    }

    // ── Surveys ────────────────────────────────────────────────────────────
    [Fact]
    public void SurveyCheck_IsDueOncePerLocalDay_AndNeverWhenSurveysAreOff()
    {
        var insights = Create();
        var day = new DateTime(2026, 10, 11, 23, 59, 0);
        insights.SurveyCheckDue(day).Should().BeTrue();
        insights.MarkSurveyChecked(day);
        insights.SurveyCheckDue(day).Should().BeFalse();
        insights.SurveyCheckDue(day.AddMinutes(2)).Should().BeTrue();      // past midnight
        Create().SurveyCheckDue(day).Should().BeFalse();                   // remembered across restarts

        insights.SetSurveysEnabled(false);
        insights.SurveyCheckDue(day.AddDays(3)).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"survey":null}""", null, null)]
    [InlineData("""{"survey":{"kind":"csat","conversation_id":"c-1"}}""", "csat", "c-1")]
    [InlineData("""{"survey":{"kind":"NPS"}}""", "nps", null)]
    [InlineData("""{"survey":{"kind":"pmf","conversation_id":""}}""", "pmf", null)]
    [InlineData("""{"survey":{"kind":"churn"}}""", "churn", null)]
    [InlineData("""{"survey":{"kind":"poll"}}""", null, null)]
    [InlineData("not json", null, null)]
    [InlineData("", null, null)]
    public void Survey_Parse(string json, string? kind, string? conversation)
    {
        var survey = ColituSurvey.Parse(json);
        survey?.Kind.Should().Be(kind);
        survey?.ConversationId.Should().Be(conversation);
        (survey == null).Should().Be(kind == null);
    }

    [Theory]
    [InlineData("csat", 1, null, true)]
    [InlineData("csat", 5, null, true)]
    [InlineData("csat", 0, null, false)]
    [InlineData("csat", 6, null, false)]
    [InlineData("nps", 0, null, true)]
    [InlineData("nps", 10, null, true)]
    [InlineData("nps", 11, null, false)]
    [InlineData("nps", null, null, false)]
    [InlineData("pmf", null, "very", true)]
    [InlineData("pmf", null, "price", false)]
    [InlineData("churn", null, "other_vpn", true)]
    [InlineData("churn", null, "very", false)]
    [InlineData("churn", null, null, false)]
    public void Survey_IsValidAnswer(string kind, int? score, string? choice, bool valid)
    {
        ColituSurvey.IsValidAnswer(kind, score, choice).Should().Be(valid);
    }

    private static JsonElement Body(Dictionary<string, object> body) => JsonDocument.Parse(JsonSerializer.Serialize(body)).RootElement;

    [Fact]
    public void AnswerBody_Csat_HasScoreCommentAndConversation()
    {
        var survey = new ColituSurveyInfo { Kind = "csat", ConversationId = "c-1" };
        var root = Body(ColituSurvey.AnswerBody(survey, 4, null, "  fast reply  ", false));
        root.GetProperty("kind").GetString().Should().Be("csat");
        root.GetProperty("score").GetInt32().Should().Be(4);
        root.GetProperty("comment").GetString().Should().Be("fast reply");
        root.GetProperty("conversation_id").GetString().Should().Be("c-1");
        root.GetProperty("dismissed").GetBoolean().Should().BeFalse();
        root.GetProperty("platform").GetString().Should().Be("windows");
        root.TryGetProperty("answer", out _).Should().BeFalse();
    }

    [Fact]
    public void AnswerBody_Nps_HasNoCommentAndNoConversation()
    {
        var root = Body(ColituSurvey.AnswerBody(new ColituSurveyInfo { Kind = "nps" }, 9, null, "ignored", false));
        root.GetProperty("score").GetInt32().Should().Be(9);
        root.TryGetProperty("comment", out _).Should().BeFalse();
        root.TryGetProperty("conversation_id", out _).Should().BeFalse();
    }

    [Fact]
    public void AnswerBody_PmfAndChurn_UseAnswerAndReason()
    {
        var pmf = Body(ColituSurvey.AnswerBody(new ColituSurveyInfo { Kind = "pmf" }, null, "somewhat", null, false));
        pmf.GetProperty("answer").GetString().Should().Be("somewhat");
        pmf.TryGetProperty("reason", out _).Should().BeFalse();

        var churn = Body(ColituSurvey.AnswerBody(new ColituSurveyInfo { Kind = "churn" }, null, "blocked", "", false));
        churn.GetProperty("reason").GetString().Should().Be("blocked");
        churn.TryGetProperty("comment", out _).Should().BeFalse();       // empty comment is left out
    }

    [Fact]
    public void AnswerBody_Dismissed_CarriesNoAnswer()
    {
        var root = Body(ColituSurvey.AnswerBody(new ColituSurveyInfo { Kind = "csat", ConversationId = "c-1" }, 5, null, "x", true));
        root.GetProperty("dismissed").GetBoolean().Should().BeTrue();
        root.GetProperty("kind").GetString().Should().Be("csat");
        root.GetProperty("conversation_id").GetString().Should().Be("c-1");
        root.TryGetProperty("score", out _).Should().BeFalse();
        root.TryGetProperty("comment", out _).Should().BeFalse();
    }

    [Fact]
    public void AnswerBody_CommentIsClipped()
    {
        var root = Body(ColituSurvey.AnswerBody(new ColituSurveyInfo { Kind = "churn" }, null, "other", new string('a', 5000), false));
        root.GetProperty("comment").GetString()!.Length.Should().Be(ColituSurvey.MaxComment);
    }
}
