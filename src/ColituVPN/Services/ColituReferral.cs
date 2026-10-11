using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace v2rayN.Services;

/// <summary>One line of <c>recent</c> in the invite card. Invitees are never named.</summary>
public sealed class ColituReferralRecent
{
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
    [JsonPropertyName("days")] public int Days { get; set; }
    [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }
}

/// <summary>The account's invite card from <c>GET /api/v1/referral</c> (the <c>data</c> object).</summary>
public sealed class ColituReferralInfo
{
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("link")] public string Link { get; set; } = "";
    [JsonPropertyName("invitee_days")] public int InviteeDays { get; set; }
    [JsonPropertyName("reward_days")] public int RewardDays { get; set; }
    [JsonPropertyName("monthly_cap")] public int MonthlyCap { get; set; }
    [JsonPropertyName("invited")] public int Invited { get; set; }
    [JsonPropertyName("pending")] public int Pending { get; set; }
    [JsonPropertyName("rewarded")] public int Rewarded { get; set; }
    [JsonPropertyName("rejected")] public int Rejected { get; set; }
    [JsonPropertyName("days_earned")] public int DaysEarned { get; set; }
    [JsonPropertyName("rewarded_this_month")] public int RewardedThisMonth { get; set; }
    [JsonPropertyName("recent")] public List<ColituReferralRecent>? Recent { get; set; }

    /// <summary>Rewards still available this month ("left / cap").</summary>
    [JsonIgnore] public int RewardsLeft => Math.Max(0, MonthlyCap - RewardedThisMonth);
}

internal sealed class ColituReferralEnvelope
{
    [JsonPropertyName("data")] public ColituReferralInfo? Data { get; set; }
}

/// <summary>The pure parts of the invite feature: code extraction, request body, response parsing, share text.</summary>
public static class ColituReferral
{
    public const string ApiPath = "/referral";
    public const string SourceWindows = "app:windows";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Regex LinkCode = new(@"/r/([A-Za-z0-9]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CodeShape = new(@"^[A-Za-z0-9]{6,12}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The invite code from what the user typed: a bare code or a pasted <c>https://colitu.com/r/CODE</c> link.
    /// Upper-cased; null when empty or not 6-12 letters/digits (ASCII only, so a dotless "ı" never turns into "I").
    /// </summary>
    public static string? NormalizeInvite(string? input)
    {
        var text = (input ?? "").Trim();
        if (text.Length == 0)
        {
            return null;
        }
        var match = LinkCode.Match(text);
        var candidate = match.Success ? match.Groups[1].Value : text;
        return CodeShape.IsMatch(candidate) ? candidate.ToUpperInvariant() : null;
    }

    /// <summary>True when the box holds something that is not a usable code (shown as an error before sending).</summary>
    public static bool IsInvalidInvite(string? input) =>
        !string.IsNullOrWhiteSpace(input) && NormalizeInvite(input) == null;

    /// <summary>The <c>POST /auth/register</c> body: the invite is left out when empty.</summary>
    public static Dictionary<string, object> RegisterBody(string email, string password, string locale, string? invite)
    {
        var body = new Dictionary<string, object>
        {
            ["email"] = email.Trim(),
            ["password"] = password,
            ["locale"] = locale,
            ["source"] = SourceWindows
        };
        var code = NormalizeInvite(invite);
        if (code != null)
        {
            body["invite"] = code;
        }
        return body;
    }

    /// <summary>Reads a response body (<c>{"data":{...}}</c>); null when unreadable or without a link.</summary>
    public static ColituReferralInfo? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            var data = JsonSerializer.Deserialize<ColituReferralEnvelope>(json, JsonOptions)?.Data;
            return data == null ? null : Sanitize(data);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Only an https link is ever shown or copied; a missing one is rebuilt from the code.</summary>
    internal static ColituReferralInfo? Sanitize(ColituReferralInfo? info)
    {
        if (info == null)
        {
            return null;
        }
        info.Code = (info.Code ?? "").Trim();
        var link = (info.Link ?? "").Trim();
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            link = info.Code.Length > 0 ? $"{ColituAuthService.WebBaseUrl}/r/{Uri.EscapeDataString(info.Code)}" : "";
        }
        info.Link = link;
        info.Recent ??= [];
        return info.Link.Length == 0 ? null : info;
    }

    /// <summary>The ready-made share message: localized sentence, a blank space, the link.</summary>
    public static string ShareText(ColituReferralInfo info, Loc loc) =>
        loc.Format("invite.shareText", ("days", info.InviteeDays)) + " " + info.Link;
}

/// <summary>Talks to the panel's invite endpoint.</summary>
public sealed class ColituReferralService
{
    public static ColituReferralService Instance { get; } = new();

    private ColituReferralService() { }

    /// <summary>The account's invite card; throws on a network or API failure (the page offers a retry).</summary>
    public async Task<ColituReferralInfo> LoadAsync(CancellationToken token = default)
    {
        var envelope = await ColituAuthService.Instance.GetAuthorizedJsonAsync<ColituReferralEnvelope>(ColituReferral.ApiPath, token);
        return ColituReferral.Sanitize(envelope?.Data)
            ?? throw new ColituApiException(System.Net.HttpStatusCode.BadGateway, Loc.I["err.generic"], "INVALID_REFERRAL_RESPONSE");
    }
}
