using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace v2rayN.Services;

/// <summary>The survey the panel wants asked (GET /client/survey).</summary>
public sealed class ColituSurveyInfo
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("conversation_id")] public string? ConversationId { get; set; }
}

internal sealed class ColituSurveyEnvelope
{
    [JsonPropertyName("survey")] public ColituSurveyInfo? Survey { get; set; }
}

/// <summary>The pure parts of the in-app surveys: kinds, answers, request bodies.</summary>
public static class ColituSurvey
{
    public const string Csat = "csat";
    public const string Nps = "nps";
    public const string Pmf = "pmf";
    public const string Churn = "churn";

    public const string ApiPath = "/client/survey";
    public const int MaxComment = 1000;

    /// <summary>Answers of the PMF question: very, somewhat or not disappointed.</summary>
    public static readonly string[] PmfAnswers = ["very", "somewhat", "not"];

    /// <summary>Reasons for not renewing Premium.</summary>
    public static readonly string[] ChurnReasons = ["price", "speed", "blocked", "not_needed", "other_vpn", "technical", "other"];

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Reads a response body: null for <c>{"survey":null}</c>, an unknown kind or anything unreadable.</summary>
    public static ColituSurveyInfo? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            return Sanitize(JsonSerializer.Deserialize<ColituSurveyEnvelope>(json, JsonOptions)?.Survey);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static ColituSurveyInfo? Sanitize(ColituSurveyInfo? survey)
    {
        var kind = (survey?.Kind ?? "").Trim().ToLowerInvariant();
        if (kind is not (Csat or Nps or Pmf or Churn))
        {
            return null;
        }
        var conversation = survey!.ConversationId?.Trim();
        return new ColituSurveyInfo
        {
            Kind = kind,
            ConversationId = string.IsNullOrEmpty(conversation) || conversation.Length > 100 ? null : conversation
        };
    }

    /// <summary>The scale of a numeric survey: CSAT 1-5, NPS 0-10; null for the others.</summary>
    public static (int Min, int Max)? ScoreRange(string kind) => kind switch
    {
        Csat => (1, 5),
        Nps => (0, 10),
        _ => null
    };

    /// <summary>Whether this survey takes a free-text comment (CSAT and the cancellation reason).</summary>
    public static bool AsksComment(string kind) => kind is Csat or Churn;

    /// <summary>True when the choice is a valid answer for the survey (a score in range, a known PMF answer or reason).</summary>
    public static bool IsValidAnswer(string kind, int? score, string? choice)
    {
        return kind switch
        {
            Csat or Nps => score is { } value && ScoreRange(kind) is { } range && value >= range.Min && value <= range.Max,
            Pmf => choice != null && Array.IndexOf(PmfAnswers, choice) >= 0,
            Churn => choice != null && Array.IndexOf(ChurnReasons, choice) >= 0,
            _ => false
        };
    }

    /// <summary>The <c>POST /client/survey</c> body. Closing the card sends <c>dismissed: true</c> and no answer.</summary>
    public static Dictionary<string, object> AnswerBody(ColituSurveyInfo survey, int? score, string? choice, string? comment, bool dismissed)
    {
        var body = new Dictionary<string, object>
        {
            ["kind"] = survey.Kind,
            ["dismissed"] = dismissed,
            ["platform"] = ColituInsightSpec.Platform
        };
        if (!string.IsNullOrEmpty(survey.ConversationId))
        {
            body["conversation_id"] = survey.ConversationId;
        }
        if (dismissed)
        {
            return body;
        }
        switch (survey.Kind)
        {
            case Csat or Nps when score is { } value:
                body["score"] = value;
                break;
            case Pmf when choice != null:
                body["answer"] = choice;
                break;
            case Churn when choice != null:
                body["reason"] = choice;
                break;
        }
        var text = (comment ?? "").Trim();
        if (AsksComment(survey.Kind) && text.Length > 0)
        {
            body["comment"] = text.Length <= MaxComment ? text : text[..MaxComment];
        }
        return body;
    }
}

/// <summary>Talks to the panel's survey endpoint. Errors never reach the user.</summary>
public sealed class ColituSurveyService
{
    public static ColituSurveyService Instance { get; } = new();

    private ColituSurveyService() { }

    /// <summary>
    /// The survey due, or null when none (also when the panel is too old to know the endpoint, 404).
    /// Throws on a network or API failure so the caller can try again later instead of marking the day done.
    /// </summary>
    public async Task<ColituSurveyInfo?> FetchAsync(CancellationToken token = default)
    {
        try
        {
            var envelope = await ColituAuthService.Instance.GetAuthorizedJsonAsync<ColituSurveyEnvelope>(ApiPath, token);
            return ColituSurvey.Sanitize(envelope?.Survey);
        }
        catch (ColituApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private const string ApiPath = ColituSurvey.ApiPath;

    /// <summary>Sends an answer or the dismissal. Fire and forget: a failure is only logged.</summary>
    public async Task SubmitAsync(Dictionary<string, object> body)
    {
        try
        {
            await ColituAuthService.Instance.PostAuthorizedAsync(ApiPath, body);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ColituSurveyService.SubmitAsync", ex);
        }
    }
}
