using System.Net;
using AwesomeAssertions;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

/// <summary>Premium encouragement on Home: which banner a free-plan account sees, and the invite texts.</summary>
public class ColituUpsellTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private const long Gb = 1L << 30;

    private static ColituSubscription Free(long used, long? limit = Gb, string? periodEnd = "2026-11-01T00:00:00Z", long? remaining = null, string status = "active") => new()
    {
        PlanName = "Free",
        Status = status,
        Active = status is "active" or "trialing",
        DeviceLimit = 1,
        TrafficLimitBytes = limit,
        TrafficUsedBytes = used,
        TrafficRemainingBytes = remaining ?? (limit is { } l ? Math.Max(0, l - used) : null),
        TrafficPeriodEnd = periodEnd
    };

    [Fact]
    public void PaidOrGiftedPremium_AndNoPlan_ShowNothing()
    {
        ColituUpsell.Decide(null, null, Now).Kind.Should().Be(ColituUpsellKind.None);
        ColituUpsell.Decide(new ColituSubscription { PlanName = "Premium", Status = "active", Active = true }, null, Now).Kind.Should().Be(ColituUpsellKind.None);
        ColituUpsell.Decide(new ColituSubscription { PlanName = "Free", Status = "expired", Active = false }, null, Now).Kind.Should().Be(ColituUpsellKind.None);
    }

    [Fact]
    public void FreePlan_WithTrafficLeft_IsTheCompactLine()
    {
        var state = ColituUpsell.Decide(Free(400L << 20), null, Now);
        state.Kind.Should().Be(ColituUpsellKind.Free);
        state.LeftBytes.Should().Be(Gb - (400L << 20));
        state.DeviceLimit.Should().Be(1);
        state.BlocksConnect.Should().BeFalse();
    }

    [Theory]
    [InlineData(0.79, ColituUpsellKind.Free)]
    [InlineData(0.80, ColituUpsellKind.Low)]
    [InlineData(0.99, ColituUpsellKind.Low)]
    [InlineData(1.00, ColituUpsellKind.UsedUp)]
    [InlineData(1.20, ColituUpsellKind.UsedUp)]
    public void FreePlan_TurnsStrongerFrom80Percent(double share, ColituUpsellKind expected)
    {
        ColituUpsell.Decide(Free((long)Math.Ceiling(Gb * share)), null, Now).Kind.Should().Be(expected);
    }

    [Fact]
    public void FresherUsage_WinsOverTheEntitlement()
    {
        ColituUpsell.Decide(Free(0), (long)(Gb * 0.9), Now).Kind.Should().Be(ColituUpsellKind.Low);
        ColituUpsell.Decide(Free(0), Gb, Now).Percent.Should().Be(100);
    }

    [Fact]
    public void UsedUp_BlocksConnect_AndSaysWhenItRenews()
    {
        var state = ColituUpsell.Decide(Free(Gb, remaining: 0), null, Now);
        state.Kind.Should().Be(ColituUpsellKind.UsedUp);
        state.BlocksConnect.Should().BeTrue();
        state.RenewsAt.Should().Be(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        state.LeftBytes.Should().Be(0);

        ColituUpsell.Decide(Free(10, remaining: 0), null, Now).Kind.Should().Be(ColituUpsellKind.UsedUp);
        ColituUpsell.Decide(Free(10, status: "quota_exceeded"), null, Now).Kind.Should().Be(ColituUpsellKind.UsedUp);
    }

    [Fact]
    public void APeriodThatEnded_CountsAsRenewed()
    {
        var state = ColituUpsell.Decide(Free(Gb, periodEnd: "2026-10-01T00:00:00Z", remaining: 0), null, Now);
        state.Kind.Should().Be(ColituUpsellKind.Free);
        state.LeftBytes.Should().Be(Gb);
        state.BlocksConnect.Should().BeFalse();
    }

    [Fact]
    public void PremiumThatEndedThisWeek_IsMentionedOnce()
    {
        ColituUpsell.Decide(Free(0), null, Now, Now.AddDays(-2)).Kind.Should().Be(ColituUpsellKind.PremiumEnded);
        ColituUpsell.Decide(Free(0), null, Now, Now.AddDays(-8)).Kind.Should().Be(ColituUpsellKind.Free);
        ColituUpsell.Decide(Free(0), null, Now, null).Kind.Should().Be(ColituUpsellKind.Free);
        // Running low or used up says more than "Premium ended".
        ColituUpsell.Decide(Free(Gb), null, Now, Now.AddDays(-1)).Kind.Should().Be(ColituUpsellKind.UsedUp);
    }

    [Fact]
    public void RememberPremium_KeepsTheLastPremiumEnd()
    {
        var premium = new ColituSubscription { PlanName = "Premium", Status = "active", Active = true, ExpiresAt = "2026-10-28T10:00:00Z" };
        ColituUpsell.RememberPremium(premium, null).Should().Be("2026-10-28T10:00:00Z");
        ColituUpsell.RememberPremium(Free(0), "2026-10-28T10:00:00Z").Should().Be("2026-10-28T10:00:00Z");
        ColituUpsell.RememberPremium(null, null).Should().BeNull();

        // Cut short (refund, admin): it ended now, not at the old expiry.
        ColituUpsell.PremiumEndedAt("2026-12-01T00:00:00Z", Now).Should().Be(Now);
        ColituUpsell.PremiumEndedAt("2026-10-26T00:00:00Z", Now).Should().Be(new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero));
        ColituUpsell.PremiumEndedAt("garbage", Now).Should().BeNull();
    }

    [Fact]
    public void PanelUsageNotices_AreLeftToTheBanner_OnlyWhenItShowsTheSame()
    {
        var usage = new ColituNotice { Id = "usage_80:1790812800", Kind = "usage_80" };
        var campaign = new ColituNotice { Id = "c1", Kind = "campaign" };
        var low = ColituUpsell.Decide(Free((long)(Gb * 0.85)), null, Now);
        ColituUpsell.CoversNotice(low, usage).Should().BeTrue();
        ColituUpsell.CoversNotice(low, campaign).Should().BeFalse();
        ColituUpsell.CoversNotice(ColituUpsell.Decide(Free(0), null, Now), usage).Should().BeFalse();
    }

    [Fact]
    public void QuotaErrors_AreFoundInsideTheConnectFailure()
    {
        var api = new ColituApiException(HttpStatusCode.Forbidden, "traffic quota exceeded", "QUOTA_EXCEEDED");
        ColituUpsell.IsQuotaError(new ColituConnectException("failed", api)).Should().BeTrue();
        ColituUpsell.IsQuotaError(new ColituConnectException("failed", new ColituApiException(HttpStatusCode.Forbidden, "x", "ENTITLEMENT_INACTIVE"))).Should().BeFalse();
        ColituUpsell.IsQuotaError(null).Should().BeFalse();
    }

    // ── Strings ───────────────────────────────────────────────────────────
    [Fact]
    public void UpsellStrings_ExistInEveryLanguage()
    {
        foreach (var key in Loc.Keys.Where(key => key.StartsWith("upsell.") || key is "premium.ends" or "paused.premiumHint" or "invite.card.split"))
        {
            Loc.ValuesOf(key).Should().HaveCount(Loc.Languages.Length, key).And.OnlyContain(value => value.Length > 0, key);
        }
        Loc.Has("upsell.free").Should().BeTrue();
    }

    private static string InLanguage(string language, Func<string> text)
    {
        var previous = Loc.I.Language;
        try
        {
            Loc.I.SetLanguage(language);
            return text();
        }
        finally
        {
            Loc.I.SetLanguage(previous);
        }
    }

    [Fact]
    public void InviteCard_UsesTheServerDays_WithTheRightPlural()
    {
        var info = new ColituReferralInfo { InviteeDays = 1, RewardDays = 1, Link = "https://colitu.com/r/K7M2QX9A" };
        InLanguage("en", () => ColituReferral.CardText(info, Loc.I)).Should().Be("You and your friend each get 1 day of Premium");
        InLanguage("ru", () => ColituReferral.CardText(info, Loc.I)).Should().Be("Вам и другу — 1 день Premium каждому");
        InLanguage("tr", () => ColituReferral.CardText(info, Loc.I)).Should().Be("Siz de arkadaşınız da 1 gün Premium kazanırsınız");
        InLanguage("en", () => ColituReferral.ShareMessage(info, Loc.I)).Should().EndWith("get 1 day of Premium:");

        var split = new ColituReferralInfo { InviteeDays = 3, RewardDays = 1, Link = info.Link };
        InLanguage("en", () => ColituReferral.CardText(split, Loc.I)).Should().Be("Your friend gets 3 days of Premium, you get 1 day");
    }

    [Fact]
    public void GiftedPremiumEnding_SaysPremium_NotTrial()
    {
        var gifted = new ColituSubscription { PlanName = "Premium", Status = "active", Active = true, NextPlan = "free", EndsAt = Now.AddHours(20).ToString("O") };
        InLanguage("en", () => ColituTrial.BannerText(gifted, Now, null, Loc.I)!).Should().StartWith("Your Premium ends in 1 day.");
    }
}
