using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using v2rayN.Services;

namespace v2rayN.Views;

/// <summary>
/// Anonymous usage statistics (the settings switch and the periodic send) and the in-app survey card.
/// The collecting itself lives in <see cref="ColituInsights"/> and the connection code.
/// </summary>
public partial class ColituMainWindow
{
    private ColituSurveyInfo? _survey;
    private int? _surveyScore;
    private string? _surveyChoice;
    private bool _surveyChecking;
    private DateTimeOffset _surveyRetryAt;

    // ── Settings switches ──────────────────────────────────────────────────
    /// <summary>Called from <see cref="ApplyPreferencesToUi"/>, which already holds the "applying" flag.</summary>
    private void ApplyInsightUi()
    {
        SettingsStats.IsChecked = ColituInsights.Instance.StatsEnabled;
        SettingsSurveys.IsChecked = ColituInsights.Instance.SurveysEnabled;
    }

    private void Insight_Click(object sender, RoutedEventArgs e)
    {
        if (_applyingPreferences || !IsLoaded || sender is not CheckBox box)
        {
            return;
        }
        var on = box.IsChecked == true;
        if (ReferenceEquals(box, SettingsStats))
        {
            // Off: nothing more is collected or sent, and what is pending is deleted.
            ColituInsights.Instance.SetStatsEnabled(on);
        }
        else
        {
            ColituInsights.Instance.SetSurveysEnabled(on);
            if (!on)
            {
                HideSurvey();
            }
        }
        ShowToast(Loc.I["settings.saved"]);
    }

    /// <summary>Every data refresh: one report if due (on the thread pool, never the UI thread) and the daily survey check.</summary>
    private void OnInsightTick()
    {
        _ = Task.Run(ColituInsightSender.TrySendAsync);
        _ = CheckSurveyAsync();
    }

    // ── Survey ─────────────────────────────────────────────────────────────
    /// <summary>At most once a day, not while connecting, not while a card is open.</summary>
    private async Task CheckSurveyAsync()
    {
        if (_surveyChecking || SurveyCard.Visibility == Visibility.Visible || !_auth.HasSession || _auth.PendingVerification
            || _vpn.Status is ColituVpnStatus.Connecting or ColituVpnStatus.Reconnecting
            || DateTimeOffset.UtcNow < _surveyRetryAt
            || !ColituInsights.Instance.SurveyCheckDue(DateTime.Now))
        {
            return;
        }
        _surveyChecking = true;
        var epoch = _accountEpoch;
        try
        {
            var survey = await ColituSurveyService.Instance.FetchAsync();
            if (epoch != _accountEpoch)
            {
                return;
            }
            ColituInsights.Instance.MarkSurveyChecked(DateTime.Now);
            if (survey != null && ColituInsights.Instance.SurveysEnabled)
            {
                ShowSurvey(survey);
            }
        }
        catch (Exception ex)
        {
            // The day is not used up: try again later.
            _surveyRetryAt = DateTimeOffset.UtcNow.AddMinutes(10);
            Logging.SaveLog("ColituMainWindow.CheckSurveyAsync", ex);
        }
        finally
        {
            _surveyChecking = false;
        }
    }

    private void ShowSurvey(ColituSurveyInfo survey)
    {
        _survey = survey;
        _surveyScore = null;
        _surveyChoice = null;
        SurveyComment.Clear();
        RenderSurvey();
        SurveyCard.Visibility = Visibility.Visible;
        FadeIn(SurveyCard, 10);
    }

    /// <summary>Builds the question and its choices (again after a language change; the selection stays).</summary>
    private void RenderSurvey()
    {
        if (_survey is not { } survey)
        {
            return;
        }
        var loc = Loc.I;
        SurveyQuestion.Text = loc[$"survey.q.{survey.Kind}"];
        SurveyBody.Children.Clear();
        SurveyThanks.Visibility = Visibility.Collapsed;
        SurveyContent.Visibility = Visibility.Visible;

        if (ColituSurvey.ScoreRange(survey.Kind) is { } range)
        {
            var row = new UniformGrid { Rows = 1 };
            for (var value = range.Min; value <= range.Max; value++)
            {
                var option = new RadioButton
                {
                    Content = value.ToString(loc.Culture),
                    GroupName = "SurveyScale",
                    Tag = value,
                    FontSize = 12.5,
                    MinHeight = 34,
                    // Eleven cells share the card width (~38 px each): the default 10 px side
                    // padding left no room for "10".
                    Padding = new Thickness(0, 6, 0, 6),
                    Style = (Style)FindResource("SegmentOption"),
                    IsChecked = value == _surveyScore
                };
                option.Checked += SurveyScore_Checked;
                row.Children.Add(option);
            }
            var host = new Border { Style = (Style)FindResource("SegmentHost"), Child = row };
            SurveyBody.Children.Add(host);

            var labels = new Grid { Margin = new Thickness(6, 8, 6, 0) };
            var kind = survey.Kind == ColituSurvey.Nps ? "nps" : "csat";
            labels.Children.Add(new TextBlock { Text = loc[$"survey.{kind}.low"], FontSize = 11.5, Foreground = (Brush)FindResource("DimBrush"), HorizontalAlignment = HorizontalAlignment.Left });
            labels.Children.Add(new TextBlock { Text = loc[$"survey.{kind}.high"], FontSize = 11.5, Foreground = (Brush)FindResource("DimBrush"), HorizontalAlignment = HorizontalAlignment.Right });
            SurveyBody.Children.Add(labels);
        }
        else
        {
            var choices = survey.Kind == ColituSurvey.Pmf ? ColituSurvey.PmfAnswers : ColituSurvey.ChurnReasons;
            foreach (var answer in choices)
            {
                var option = new RadioButton
                {
                    Content = new TextBlock { Text = loc[$"survey.{survey.Kind}.{answer}"], FontSize = 13.5, TextWrapping = TextWrapping.Wrap },
                    GroupName = "SurveyChoice",
                    Tag = answer,
                    Margin = new Thickness(0, 0, 0, 8),
                    Style = (Style)FindResource("OptionCard"),
                    IsChecked = answer == _surveyChoice
                };
                option.Checked += SurveyChoice_Checked;
                SurveyBody.Children.Add(option);
            }
        }
        SurveyCommentPanel.Visibility = ColituSurvey.AsksComment(survey.Kind) ? Visibility.Visible : Visibility.Collapsed;
        UpdateSurveySend();
    }

    private void SurveyScore_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: int value })
        {
            _surveyScore = value;
            UpdateSurveySend();
        }
    }

    private void SurveyChoice_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string answer })
        {
            _surveyChoice = answer;
            UpdateSurveySend();
        }
    }

    private void UpdateSurveySend()
    {
        SurveySend.IsEnabled = _survey is { } survey && ColituSurvey.IsValidAnswer(survey.Kind, _surveyScore, _surveyChoice);
    }

    private async void SurveySend_Click(object sender, RoutedEventArgs e)
    {
        if (_survey is not { } survey || !ColituSurvey.IsValidAnswer(survey.Kind, _surveyScore, _surveyChoice))
        {
            return;
        }
        var body = ColituSurvey.AnswerBody(survey, _surveyScore, _surveyChoice, SurveyComment.Text, dismissed: false);
        _survey = null;
        _ = ColituSurveyService.Instance.SubmitAsync(body);
        SurveyContent.Visibility = Visibility.Collapsed;
        SurveyThanks.Visibility = Visibility.Visible;
        await Task.Delay(TimeSpan.FromMilliseconds(1800));
        if (_survey == null)
        {
            SurveyCard.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Closing an unanswered card tells the panel it was dismissed.</summary>
    private void SurveyClose_Click(object sender, RoutedEventArgs e)
    {
        if (_survey is { } survey)
        {
            _survey = null;
            _ = ColituSurveyService.Instance.SubmitAsync(ColituSurvey.AnswerBody(survey, null, null, null, dismissed: true));
        }
        SurveyCard.Visibility = Visibility.Collapsed;
    }

    /// <summary>Hides the card without telling the panel anything (surveys switched off, signed out).</summary>
    private void HideSurvey()
    {
        _survey = null;
        _surveyScore = null;
        _surveyChoice = null;
        SurveyCard.Visibility = Visibility.Collapsed;
    }
}
