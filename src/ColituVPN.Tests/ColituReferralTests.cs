using System.Text.Json;
using AwesomeAssertions;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

/// <summary>Invite codes at sign-up and the invite card (GET /referral): extraction, request body, parsing.</summary>
public class ColituReferralTests
{
    private const string Sample = """
        {"data":{"code":"K7M2QX9A","link":"https://colitu.com/r/K7M2QX9A","invitee_days":7,"reward_days":7,"monthly_cap":6,
          "invited":5,"pending":2,"rewarded":2,"rejected":1,"days_earned":14,"rewarded_this_month":2,
          "recent":[{"status":"rewarded","days":7,"created_at":"2026-10-09T10:00:00Z"},
                    {"status":"rejected","reason":"same_network","days":0,"created_at":"2026-10-08T10:00:00Z"}]}}
        """;

    [Theory]
    [InlineData("K7M2QX9A", "K7M2QX9A")]
    [InlineData("  k7m2qx9a  ", "K7M2QX9A")]
    [InlineData("https://colitu.com/r/K7M2QX9A", "K7M2QX9A")]
    [InlineData("https://colitu.com/r/k7m2qx9a/", "K7M2QX9A")]
    [InlineData("https://colitu.com/r/K7M2QX9A?utm_source=chat#x", "K7M2QX9A")]
    [InlineData("colitu.com/r/abc123", "ABC123")]
    [InlineData("  https://colitu.com/r/ABCDEFGHIJKL  ", "ABCDEFGHIJKL")]
    public void NormalizeInvite_ExtractsAndUpperCasesTheCode(string input, string expected)
    {
        ColituReferral.NormalizeInvite(input).Should().Be(expected);
        ColituReferral.IsInvalidInvite(input).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeInvite_EmptyIsNull_AndNotAnError(string? input)
    {
        ColituReferral.NormalizeInvite(input).Should().BeNull();
        ColituReferral.IsInvalidInvite(input).Should().BeFalse();
    }

    [Theory]
    [InlineData("ABC12")]               // too short
    [InlineData("ABCDEFGHIJKLM")]       // too long
    [InlineData("ABC-123")]
    [InlineData("ABC 123")]
    [InlineData("https://colitu.com/r/AB")]
    [InlineData("https://colitu.com/pricing")]
    [InlineData("ıııııııı")]            // dotless i must not become "I"
    public void NormalizeInvite_RejectsWhatIsNotACode(string input)
    {
        ColituReferral.NormalizeInvite(input).Should().BeNull();
        ColituReferral.IsInvalidInvite(input).Should().BeTrue();
    }

    [Fact]
    public void RegisterBody_SendsSourceAndInvite()
    {
        var body = ColituReferral.RegisterBody(" a@b.co ", "secret-password", "tr", "https://colitu.com/r/k7m2qx9a");
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(body));
        var root = doc.RootElement;
        root.GetProperty("email").GetString().Should().Be("a@b.co");
        root.GetProperty("password").GetString().Should().Be("secret-password");
        root.GetProperty("locale").GetString().Should().Be("tr");
        root.GetProperty("source").GetString().Should().Be("app:windows");
        root.GetProperty("invite").GetString().Should().Be("K7M2QX9A");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("zz")]
    public void RegisterBody_OmitsAnEmptyInvite(string? invite)
    {
        var body = ColituReferral.RegisterBody("a@b.co", "secret-password", "en", invite);
        body.Should().NotContainKey("invite");
        body["source"].Should().Be("app:windows");
    }

    [Fact]
    public void Parse_ReadsTheContract()
    {
        var info = ColituReferral.Parse(Sample);
        info.Should().NotBeNull();
        info!.Code.Should().Be("K7M2QX9A");
        info.Link.Should().Be("https://colitu.com/r/K7M2QX9A");
        info.InviteeDays.Should().Be(7);
        info.RewardDays.Should().Be(7);
        info.MonthlyCap.Should().Be(6);
        info.Invited.Should().Be(5);
        info.Pending.Should().Be(2);
        info.Rewarded.Should().Be(2);
        info.Rejected.Should().Be(1);
        info.DaysEarned.Should().Be(14);
        info.RewardedThisMonth.Should().Be(2);
        info.RewardsLeft.Should().Be(4);
        info.Recent.Should().HaveCount(2);
        info.Recent![0].Status.Should().Be("rewarded");
        info.Recent[1].Reason.Should().Be("same_network");
    }

    [Fact]
    public void RewardsLeft_NeverGoesBelowZero()
    {
        new ColituReferralInfo { MonthlyCap = 6, RewardedThisMonth = 9 }.RewardsLeft.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"data":null}""")]
    [InlineData("""{"data":{"invited":1}}""")]
    public void Parse_UnreadableOrEmptyIsNull(string? json)
    {
        ColituReferral.Parse(json).Should().BeNull();
    }

    [Fact]
    public void Parse_RebuildsALinkThatIsNotHttps()
    {
        var info = ColituReferral.Parse("""{"data":{"code":"K7M2QX9A","link":"javascript:alert(1)","recent":null}}""");
        info.Should().NotBeNull();
        info!.Link.Should().Be("https://colitu.com/r/K7M2QX9A");
        info.Recent.Should().BeEmpty();
    }

    [Fact]
    public void ShareText_IsTheLocalizedSentenceAndTheLink()
    {
        var info = ColituReferral.Parse(Sample)!;
        var previous = Loc.I.Language;
        try
        {
            Loc.I.SetLanguage("tr");
            ColituReferral.ShareText(info, Loc.I).Should().Be("Colitu VPN kullanıyorum: hızlı ve gizlilik odaklı. Bu bağlantıyla hesap açarsan 7 gün Premium kazanırsın: https://colitu.com/r/K7M2QX9A");
            Loc.I.SetLanguage("en");
            ColituReferral.ShareText(info, Loc.I).Should().EndWith(": https://colitu.com/r/K7M2QX9A").And.Contain("7 days");
            Loc.I.SetLanguage("ru");
            ColituReferral.ShareText(info, Loc.I).Should().Contain("Colitu").And.EndWith("https://colitu.com/r/K7M2QX9A");
        }
        finally
        {
            Loc.I.SetLanguage(previous);
        }
    }

    [Fact]
    public void ShareUrls_TelegramGetsLinkAndTextApart_WhatsAppOneTextWithTheLink()
    {
        var info = ColituReferral.Parse(Sample)!;
        var previous = Loc.I.Language;
        try
        {
            Loc.I.SetLanguage("en");
            var telegram = ColituReferral.TelegramUrl(info, Loc.I);
            telegram.Should().StartWith("https://t.me/share/url?url=https%3A%2F%2Fcolitu.com%2Fr%2FK7M2QX9A&text=");
            telegram.Substring(telegram.IndexOf("&text=", StringComparison.Ordinal)).Should().NotContain("colitu.com");
            telegram.Should().Contain("7%20days");

            var whatsapp = ColituReferral.WhatsAppUrl(info, Loc.I);
            whatsapp.Should().StartWith("https://wa.me/?text=");
            Uri.UnescapeDataString(whatsapp["https://wa.me/?text=".Length..]).Should().Be(ColituReferral.ShareText(info, Loc.I));
        }
        finally
        {
            Loc.I.SetLanguage(previous);
        }
    }

    [Theory]
    [InlineData("https://colitu.com/r/K7M2QX9A", true)]
    [InlineData("https://www.colitu.com/r/K7M2QX9A", true)]
    [InlineData("http://colitu.com/r/K7M2QX9A", false)]
    [InlineData("https://evilcolitu.com/r/K7M2QX9A", false)]
    [InlineData("https://colitu.com.evil.example/r/K7M2QX9A", false)]
    [InlineData("", false)]
    public void IsColituLink_AcceptsOnlyHttpsOnColitu(string link, bool expected) =>
        ColituReferral.IsColituLink(link).Should().Be(expected);

    [Fact]
    public void ShouldShowCard_NeedsEligibilityAndRespectsPauseDisabledAndTheDismissWindow()
    {
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var info = ColituReferral.Parse(Sample)!;

        ColituReferral.ShouldShowCard(info, eligible: true, null, now, paused: false).Should().BeTrue();
        ColituReferral.ShouldShowCard(info, eligible: false, null, now, paused: false).Should().BeFalse();
        ColituReferral.ShouldShowCard(info, eligible: true, null, now, paused: true).Should().BeFalse();
        ColituReferral.ShouldShowCard(null, eligible: true, null, now, paused: false).Should().BeFalse();

        info.Enabled = false;
        ColituReferral.ShouldShowCard(info, eligible: true, null, now, paused: false).Should().BeFalse();
        info.Enabled = null;

        ColituReferral.ShouldShowCard(info, true, now.AddDays(-13).ToString("o"), now, false).Should().BeFalse();
        ColituReferral.ShouldShowCard(info, true, now.AddDays(-14).ToString("o"), now, false).Should().BeTrue();
        // An unreadable stamp, or one far in the future (clock change), does not hide the card for good.
        ColituReferral.ShouldShowCard(info, true, "garbage", now, false).Should().BeTrue();
        ColituReferral.ShouldShowCard(info, true, now.AddDays(30).ToString("o"), now, false).Should().BeTrue();
    }
}
