using System.IO;
using System.Linq;
using AwesomeAssertions;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

/// <summary>Colitu Passport rules, the same as the phones (colitu-android data/ColituPassport.kt).</summary>
public class ColituPassportTests
{
    private DateTime _now = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Local);

    private ColituPassport NewPassport(ColituPassportStore? store = null) => new(store ?? new ColituPassportStore(null), () => _now);

    /// <summary>Ticks <paramref name="minutes"/> one minute apart in <paramref name="country"/>; returns every event.</summary>
    private List<StampEvent> Run(ColituPassport passport, string country, int minutes)
    {
        var events = new List<StampEvent>();
        for (var i = 0; i < minutes; i++)
        {
            _now = _now.AddMinutes(1);
            events.AddRange(passport.Tick(country));
        }
        return events;
    }

    [Fact]
    public void FiveMinutesInARow_UnlockTheBronzeStamp()
    {
        var passport = NewPassport();
        Run(passport, "DE", 4).Should().BeEmpty();
        passport.ProgressOf("DE").Unlocked.Should().BeFalse();

        var events = Run(passport, "DE", 1);

        events.Should().ContainSingle().Which.Should().Be(new StampEvent.Unlocked("DE"));
        passport.ProgressOf("DE").Level.Should().Be(StampLevel.Bronze);
        passport.Count.Should().Be(1);
    }

    [Fact]
    public void AGapOrAnotherCountry_StartsTheFiveMinutesOver()
    {
        var passport = NewPassport();
        Run(passport, "SE", 3);
        _now = _now.AddMinutes(2); // a 3-minute gap since the last tick (over 90 s)
        Run(passport, "SE", 4).Should().BeEmpty();
        Run(passport, "FI", 2);
        Run(passport, "SE", 4).Should().BeEmpty();
        Run(passport, "SE", 1).Should().ContainSingle().Which.Should().Be(new StampEvent.Unlocked("SE"));
    }

    [Fact]
    public void UkCountsAsGreatBritain_AndUnknownCodesAreIgnored()
    {
        var passport = NewPassport();
        Run(passport, "uk", 5);
        passport.ProgressOf("GB").Unlocked.Should().BeTrue();
        passport.Tick("").Should().BeEmpty();
        passport.Tick(null).Should().BeEmpty();
        ColituPassport.StampFor("UK")!.Code.Should().Be("GB");
    }

    [Fact]
    public void Minute10And60_EachAddAPiece_AtMostTwoADay()
    {
        var passport = NewPassport();
        var events = Run(passport, "NL", 10);
        events.OfType<StampEvent.Piece>().Should().ContainSingle().Which.Should().Be(new StampEvent.Piece("NL", 1));
        passport.Today.Should().Be((10, 1));

        Run(passport, "NL", 49).OfType<StampEvent.Piece>().Should().BeEmpty();
        Run(passport, "NL", 1).OfType<StampEvent.Piece>().Should().ContainSingle();
        Run(passport, "NL", 120).OfType<StampEvent.Piece>().Should().BeEmpty();

        passport.Today.Should().Be((180, 2));
        passport.ProgressOf("NL").Pieces.Should().Be(2);
    }

    [Fact]
    public void ThePieceGoesToTheCountryConnectedAtThatMinute()
    {
        var passport = NewPassport();
        Run(passport, "SE", 7); // unlock SE (minutes 1-7)
        Run(passport, "DE", 2); // minutes 8-9 in a locked country
        // Minute 10 in Germany, still locked: the piece goes to the last unlocked stamp (Sweden).
        Run(passport, "DE", 1).OfType<StampEvent.Piece>().Single().Code.Should().Be("SE");
        Run(passport, "DE", 2).OfType<StampEvent.Unlocked>().Single().Code.Should().Be("DE");
        Run(passport, "DE", 48).OfType<StampEvent.Piece>().Single().Code.Should().Be("DE");
    }

    [Fact]
    public void TheDayStartsOverAtLocalMidnight()
    {
        _now = new DateTime(2026, 10, 24, 22, 0, 0, DateTimeKind.Local);
        var passport = NewPassport();
        Run(passport, "PL", 60);
        passport.Today.Should().Be((60, 2));
        _now = new DateTime(2026, 10, 24, 23, 59, 0, DateTimeKind.Local);

        Run(passport, "PL", 10).OfType<StampEvent.Piece>().Should().ContainSingle("minute 10 of the new day");
        passport.Today.Should().Be((10, 1));
        passport.ProgressOf("PL").Pieces.Should().Be(3);
    }

    [Fact]
    public void Testers_GetAPieceEveryTenMinutes_WithoutTheDailyCap()
    {
        var passport = NewPassport();
        passport.Tester = true;
        Run(passport, "FR", 100).OfType<StampEvent.Piece>().Should().HaveCount(10);
        passport.Today.Pieces.Should().Be(10);
    }

    [Fact]
    public void TwelvePieces_TakeTheStampUpALevel_UntilDiamondIsComplete()
    {
        var progress = new StampProgress(11, 0, null, 1);
        progress.Level.Should().Be(StampLevel.Bronze);
        progress.ToNext.Should().Be(1);
        (progress with { Pieces = 12 }).Level.Should().Be(StampLevel.Silver);
        (progress with { Pieces = 24 }).Level.Should().Be(StampLevel.Gold);
        (progress with { Pieces = 36 }).Level.Should().Be(StampLevel.Diamond);
        (progress with { Pieces = 47 }).Mastered.Should().BeFalse();
        (progress with { Pieces = 48 }).Mastered.Should().BeTrue();
        (progress with { Pieces = 48 }).LevelPieces.Should().Be(12);
        StampProgress.None.Level.Should().BeNull();

        var passport = NewPassport();
        passport.Tester = true;
        Run(passport, "JP", 5);
        var events = Run(passport, "JP", 115); // minutes 6-120: 12 pieces
        events.Should().Contain(new StampEvent.Piece("JP", 12));
        events.Should().Contain(new StampEvent.LevelUp("JP", StampLevel.Silver));
        passport.ProgressOf("JP").Level.Should().Be(StampLevel.Silver);

        Run(passport, "JP", 360).Should().ContainSingle(e => e is StampEvent.Mastered);
        passport.ProgressOf("JP").Mastered.Should().BeTrue();
    }

    [Fact]
    public void RewardsWaitInTheQueue_UntilTheAppTakesThem()
    {
        var passport = NewPassport();
        Run(passport, "LV", 10);
        passport.Social.MarkCompanion();
        passport.Social.MarkCompanion();

        passport.TakePending().Should().Equal(new StampEvent.Unlocked("LV"), new StampEvent.Piece("LV", 1), new StampEvent.Special(PassportSocial.Companion));
        passport.TakePending().Should().BeEmpty();
    }

    [Fact]
    public void ThePassport_IsKeptInItsFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"colitu-passport-test-{Guid.NewGuid():N}.json");
        try
        {
            var passport = NewPassport(new ColituPassportStore(path));
            Run(passport, "US", 10);
            passport.Goals.UpdateNickname("  Deniz  ");

            var again = NewPassport(new ColituPassportStore(path));
            again.ProgressOf("US").Pieces.Should().Be(1);
            again.Goals.Nickname.Should().Be("Deniz");
            again.TakePending().Should().HaveCount(2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WeeklyExpeditions_FollowTheFixedRotation()
    {
        // (ISO year × 53 + week) × 3 + i, modulo 6: the same three goals as on the phones.
        PassportWeekly.GoalsOf(new DateTime(2026, 10, 24)).Should().Equal(ExpeditionGoal.Days10, ExpeditionGoal.SetStamp, ExpeditionGoal.Pieces);
        PassportWeekly.GoalsOf(new DateTime(2026, 10, 26)).Should().Equal(ExpeditionGoal.NewCountry, ExpeditionGoal.Share, ExpeditionGoal.Customize);
        PassportWeekly.WeekKey(new DateTime(2027, 1, 1)).Should().Be("2026-W53");
    }

    [Fact]
    public void WeeklyProgress_CountsThisWeeksGoalsOnly_AndStartsOverNextWeek()
    {
        _now = new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Local); // NewCountry, Share, Customize
        var passport = NewPassport();
        passport.Weekly.Bump(ExpeditionGoal.Pieces);
        passport.Weekly.Progress(ExpeditionGoal.Pieces).Should().Be(0);
        Run(passport, "EE", 5);
        passport.Goals.UpdateTitle(null);
        passport.Weekly.Done(ExpeditionGoal.NewCountry).Should().BeTrue();
        passport.Weekly.Done(ExpeditionGoal.Customize).Should().BeTrue();

        passport.Goals.CheckNew().OfType<StampEvent.Expedition>().Select(e => e.Code).Should().BeEquivalentTo("new_country", "customize");
        passport.Goals.CheckNew().OfType<StampEvent.Expedition>().Should().BeEmpty();
        passport.Weekly.Total.Should().Be(2);

        _now = _now.AddDays(7);
        passport.Weekly.Progress(ExpeditionGoal.NewCountry).Should().Be(0);
    }

    [Fact]
    public void Collections_AndAchievements_ArriveOnce()
    {
        var passport = NewPassport();
        foreach (var code in new[] { "SE", "FI", "NO" })
        {
            Run(passport, code, 5);
        }
        var first = passport.Goals.CheckNew();
        first.Should().Contain(new StampEvent.Collected("nordic"));
        first.Should().Contain(new StampEvent.Achieved("first"));
        passport.Goals.Covers(premium: false).Should().Contain(CoverTheme.Nordic);
        passport.Goals.CheckNew().Should().NotContain(e => e is StampEvent.Collected || e is StampEvent.Achieved);
        passport.Goals.Achieved(PassportAchievement.Night).Should().BeFalse();
    }

    [Fact]
    public void TimeAchievements_NightAndGlobetrotter()
    {
        _now = new DateTime(2026, 10, 24, 1, 0, 0, DateTimeKind.Local);
        var passport = NewPassport();
        Run(passport, "SE", 10);
        passport.Goals.Achieved(PassportAchievement.Night).Should().BeTrue();
        passport.Goals.Achieved(PassportAchievement.Early).Should().BeFalse();

        foreach (var code in new[] { "FI", "DE", "NL", "PL" })
        {
            passport.Goals.Achieved(PassportAchievement.Globetrotter).Should().BeFalse();
            Run(passport, code, 1);
        }
        passport.Goals.Achieved(PassportAchievement.Globetrotter).Should().BeTrue();
    }

    [Fact]
    public void Chapter2_OpensWithTheWholeOrigin_AndReplaysItsStamps()
    {
        var passport = NewPassport();
        Run(passport, "NO", 5);
        foreach (var stamp in ColituPassport.Stamps.Take(9)) Run(passport, stamp.Code, 5);
        passport.Chapter2Open.Should().BeFalse();
        passport.Goals.CheckNew().Should().NotContain(e => e is StampEvent.Chapter);

        Run(passport, "JP", 5);
        passport.Chapter2Open.Should().BeTrue();
        var opened = passport.Goals.CheckNew();
        opened.Should().ContainInOrder(new StampEvent.Chapter("2"), new StampEvent.Unlocked("NO"));
        opened.Should().Contain(new StampEvent.Achieved("origin"));
    }

    [Fact]
    public void DuelLink_RoundTrips_AndMatchesThePhonesFormat()
    {
        // Same bytes as Android: Base64 URL-safe, no padding, of "1|nick|collector|title|levels".
        var levels = "12345000003000000000";
        var link = PassportDuel.Link("Deniz Ç", "482913", "night", levels);
        link.Should().Be("https://colitu.com/p#MXxEZW5peiDDh3w0ODI5MTN8bmlnaHR8MTIzNDUwMDAwMDMwMDAwMDAwMDA");

        var parsed = PassportDuel.Parse(link)!;
        parsed.Nick.Should().Be("Deniz Ç");
        parsed.Collector.Should().Be("482913");
        parsed.Title.Should().Be("night");
        parsed.Levels["SE"].Should().Be(1);
        parsed.Levels["GB"].Should().Be(5);
        parsed.Levels["PL"].Should().Be(0);
        parsed.Levels["NO"].Should().Be(3);
        parsed.CountIn(ColituPassport.Stamps).Should().Be(5);

        PassportDuel.Link("a|b", "1", null, "0").Should().Be(PassportDuel.Link("ab", "1", null, "0"));
        PassportDuel.Parse("https://colitu.com/p#" + "not base64!").Should().BeNull();
        PassportDuel.Parse("https://example.com/p#MXx8fHw").Should().BeNull();
        PassportDuel.Parse(null).Should().BeNull();
    }

    [Fact]
    public void MyLink_CarriesEveryStampLevel_AndFriendsAreKeptOncePerCollector()
    {
        var passport = NewPassport();
        passport.Tester = true;
        Run(passport, "FI", 130); // unlocked, 13 pieces: Silver
        var mine = PassportDuel.Parse(passport.Social.MyLink())!;
        mine.Levels["FI"].Should().Be(2);
        mine.Levels["SE"].Should().Be(0);
        mine.Collector.Should().Be(passport.Goals.CollectorNo);

        var friend = PassportDuel.Parse(PassportDuel.Link("Ada", "111111", null, "11110000000000000000"))!;
        passport.Social.AddFriend(friend.ToFriend(1));
        passport.Social.AddFriend(friend.ToFriend(2));
        passport.Social.AddFriend(mine.ToFriend(3)); // never yourself
        passport.Social.Friends.Should().ContainSingle().Which.CountIn(ColituPassport.Stamps).Should().Be(4);
        passport.Social.RemoveFriend("111111");
        passport.Social.Friends.Should().BeEmpty();
    }

    [Fact]
    public void AnonymousCounters_AreOffByDefault_TotalsOnly_OnceADay()
    {
        var passport = NewPassport();
        passport.Social.MetricsPayload().Should().BeNull();
        passport.Social.Metrics = true;
        Run(passport, "SE", 10);

        var body = passport.Social.MetricsPayload()!;
        body["platform"].Should().Be("windows");
        body["stamps"].Should().Be(1);
        body["levels"].Should().BeEquivalentTo(new[] { 1, 0, 0, 0 });
        body["pieces_today"].Should().Be(1);
        body.Keys.Should().BeEquivalentTo("v", "stamps", "chapter", "levels", "expeditions", "pieces_today", "opened_today", "platform");

        passport.Social.MetricsSent();
        passport.Social.MetricsPayload().Should().BeNull();
        _now = _now.AddDays(1);
        passport.Social.MetricsPayload().Should().NotBeNull();
    }

    [Fact]
    public void Covers_FollowThePlanAndTheSets()
    {
        var passport = NewPassport();
        passport.Goals.ActiveCover(premium: false).Should().Be(CoverTheme.Classic);
        passport.Goals.ActiveCover(premium: true).Should().Be(CoverTheme.Premium);
        passport.Goals.UpdateCover(CoverTheme.Pacific);
        passport.Goals.ActiveCover(premium: true).Should().Be(CoverTheme.Premium, "Pacific is not earned yet");
        passport.Goals.CollectorNo.Should().MatchRegex("^[1-9][0-9]{5}$");
        passport.Goals.CollectorNo.Should().Be(passport.Goals.CollectorNo);
    }
}
