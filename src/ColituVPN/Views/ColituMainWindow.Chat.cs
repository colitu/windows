using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using v2rayN.Services;

namespace v2rayN.Views;

/// <summary>
/// Support → "Chat with the assistant": Colitu's assistant (the website's chat bot) answers in the
/// app's language; "was this helpful", a typing indicator, and "talk to a person", which opens an
/// ordinary support request with the chat (it shows in the list on the left).
/// </summary>
public partial class ColituMainWindow
{
    private readonly ColituBotService _bot = ColituBotService.Instance;
    private readonly List<ChatLine> _chatLines = [];
    private string? _chatConversationId;
    private bool _chatBusy;
    private bool _chatAskFeedback;
    private bool _chatHandoverOpen;
    private bool _chatHandedOver;

    private sealed record ChatLine(string Role, string Text, IReadOnlyList<ColituBotSource>? Sources = null, string? TicketId = null);

    private void SupportChat_Click(object sender, RoutedEventArgs e) => ShowSupportChat();

    private void ShowSupportChat()
    {
        _supportSelecting = true;
        SupportList.SelectedItem = null;
        _supportSelecting = false;
        ShowSupportPane(SupportChat);
        RenderSupportChat();
        Dispatcher.BeginInvoke(() => SupportChatBox.Focus(), DispatcherPriority.Input);
    }

    /// <summary>Signing out, or "new chat": nothing of the last conversation stays on screen.</summary>
    private void ResetSupportChat()
    {
        _chatLines.Clear();
        _chatConversationId = null;
        _chatAskFeedback = false;
        _chatHandoverOpen = false;
        _chatHandedOver = false;
        if (SupportChatNote != null)
        {
            SupportChatNote.Clear();
            SupportChatBox.Clear();
            RenderSupportChat();
        }
    }

    private void SupportChatReset_Click(object sender, RoutedEventArgs e)
    {
        if (_chatBusy)
        {
            return;
        }
        ResetSupportChat();
        SupportChatBox.Focus();
    }

    private async void SupportChatBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Enter sends, Shift+Enter starts a new line (as in a support thread).
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            await AskSupportChatAsync();
        }
    }

    private async void SupportChatSend_Click(object sender, RoutedEventArgs e) => await AskSupportChatAsync();

    private async Task AskSupportChatAsync()
    {
        var question = SupportChatBox.Text.Trim();
        if (_chatBusy || question.Length == 0)
        {
            return;
        }
        if (question.Length > ColituBotService.MaxQuestionChars)
        {
            _chatLines.Add(new ChatLine("note", Loc.I["bot.err.invalid"]));
            RenderSupportChat();
            return;
        }
        SupportChatBox.Clear();
        _chatLines.Add(new ChatLine("user", question));
        _chatAskFeedback = false;
        _chatHandoverOpen = false;
        SetChatBusy(true);
        var epoch = _accountEpoch;
        try
        {
            var answer = await _bot.AskAsync(_chatConversationId, question);
            if (epoch != _accountEpoch)
            {
                return;
            }
            _chatConversationId = answer.ConversationId ?? _chatConversationId;
            _chatLines.Add(new ChatLine("assistant", answer.Answer, answer.Sources));
            _chatAskFeedback = true;
            _chatHandoverOpen = answer.Handoff && !_chatHandedOver;
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"ColituMainWindow.AskSupportChat: {ex.GetType().Name}");
            var key = ColituBotService.ErrorKey(ex);
            _chatLines.Add(new ChatLine("note", Loc.I[key]));
            // A chat that got too long can still go to a person.
            _chatHandoverOpen = key == "bot.err.long" && !_chatHandedOver;
        }
        finally
        {
            SetChatBusy(false);
            SupportChatBox.Focus();
        }
    }

    private async void ChatFeedback(bool helpful)
    {
        var id = _chatConversationId;
        _chatAskFeedback = false;
        if (!helpful && !_chatHandedOver)
        {
            _chatHandoverOpen = true;
        }
        if (helpful)
        {
            _chatLines.Add(new ChatLine("note", Loc.I["bot.thanks"]));
        }
        RenderSupportChat();
        if (id != null)
        {
            await _bot.FeedbackAsync(id, helpful);
        }
    }

    private void SupportChatHandoverCancel_Click(object sender, RoutedEventArgs e)
    {
        _chatHandoverOpen = false;
        RenderSupportChat();
    }

    private async void SupportChatHandover_Click(object sender, RoutedEventArgs e)
    {
        if (_chatBusy)
        {
            return;
        }
        var note = SupportChatNote.Text.Trim();
        if (_chatConversationId == null && note.Length == 0)
        {
            _chatLines.Add(new ChatLine("note", Loc.I["bot.err.noteNeeded"]));
            RenderSupportChat();
            SupportChatNote.Focus();
            return;
        }
        SetChatBusy(true);
        SupportChatHandoverSpinner.Visibility = Visibility.Visible;
        var epoch = _accountEpoch;
        try
        {
            var handover = await _bot.EscalateAsync(_chatConversationId, note);
            if (epoch != _accountEpoch)
            {
                return;
            }
            if (note.Length > 0 && _chatConversationId == null)
            {
                _chatLines.Add(new ChatLine("user", note));
            }
            _chatHandedOver = true;
            _chatHandoverOpen = false;
            _chatAskFeedback = false;
            SupportChatNote.Clear();
            _chatLines.Add(new ChatLine("note", Loc.I.Format("bot.escalated", ("ticket", handover.TicketNumber)), TicketId: handover.SupportConversationId));
            await LoadSupportListAsync(showSpinner: false);
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"ColituMainWindow.SupportChatHandover: {ex.GetType().Name}");
            _chatLines.Add(new ChatLine("note", Loc.I[ColituBotService.ErrorKey(ex)]));
        }
        finally
        {
            SupportChatHandoverSpinner.Visibility = Visibility.Collapsed;
            SetChatBusy(false);
        }
    }

    private void SetChatBusy(bool busy)
    {
        _chatBusy = busy;
        SupportChatSendButton.IsEnabled = !busy;
        SupportChatHandoverButton.IsEnabled = !busy;
        RenderSupportChat();
    }

    // ── Rendering ──────────────────────────────────────────────────────────
    private void RenderSupportChat()
    {
        if (SupportChatMessages == null)
        {
            return;
        }
        SupportChatMessages.Children.Clear();
        SupportChatMessages.Children.Add(ChatBubble(Loc.I["bot.greeting"], mine: false));
        foreach (var line in _chatLines)
        {
            SupportChatMessages.Children.Add(line.Role switch
            {
                "user" => ChatBubble(line.Text, mine: true),
                "assistant" => ChatBubble(PlainAnswer(line.Text), mine: false, line.Sources),
                _ => ChatNote(line.Text, line.TicketId)
            });
        }
        if (_chatBusy)
        {
            SupportChatMessages.Children.Add(TypingIndicator());
        }
        else if (_chatAskFeedback)
        {
            SupportChatMessages.Children.Add(FeedbackRow());
        }
        if (!_chatBusy && !_chatHandoverOpen && !_chatHandedOver && _chatLines.Any(line => line.Role == "assistant"))
        {
            var human = new Button { Content = Loc.I["bot.human"], Style = (Style)FindResource("LinkButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 2, 0, 8) };
            human.Click += (_, _) => { _chatHandoverOpen = true; RenderSupportChat(); SupportChatNote.Focus(); };
            SupportChatMessages.Children.Add(human);
        }
        SupportChatHandover.Visibility = _chatHandoverOpen ? Visibility.Visible : Visibility.Collapsed;
        Dispatcher.BeginInvoke(() => SupportChatScroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    private FrameworkElement ChatBubble(string text, bool mine, IReadOnlyList<ColituBotSource>? sources = null)
    {
        var body = new StackPanel();
        if (!mine)
        {
            body.Children.Add(new TextBlock { Text = Loc.I["bot.name"], FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("LilacBrush"), Margin = new Thickness(0, 0, 0, 5) });
        }
        var content = new TextBox
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Left,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = mine ? (Brush)FindResource("OnAccentBrush") : (Brush)FindResource("TextBrush"),
            FontSize = 14,
            Padding = new Thickness(0),
            FocusVisualStyle = null,
            Cursor = Cursors.IBeam,
            SelectionBrush = (Brush)FindResource("VioletBrush"),
            Template = PlainTextTemplate()
        };
        body.Children.Add(content);
        foreach (var source in (sources ?? []).Where(item => IsHelpLink(item.Url)).Take(3))
        {
            var link = new Button { Content = "↗ " + source.Title, Style = (Style)FindResource("LinkButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), FontSize = 12.5 };
            var url = source.Url;
            link.Click += (_, _) => OpenUrl(url);
            body.Children.Add(link);
        }
        return new Border
        {
            Child = body,
            MaxWidth = 480,
            Padding = new Thickness(15, 11, 15, 11),
            Margin = new Thickness(mine ? 80 : 0, 0, mine ? 0 : 80, 10),
            HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            CornerRadius = mine ? new CornerRadius(18, 18, 6, 18) : new CornerRadius(18, 18, 18, 6),
            Background = mine ? (Brush)FindResource("ActionBrush") : (Brush)FindResource("Surface2Brush"),
            BorderBrush = mine ? null : (Brush)FindResource("LineBrush"),
            BorderThickness = new Thickness(mine ? 0 : 1)
        };
    }

    /// <summary>
    /// The assistant writes Markdown for the website: links become their text (the help pages it
    /// used are the buttons under the answer), bold and inline code marks are dropped.
    /// </summary>
    internal static string PlainAnswer(string text)
    {
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[([^\]\r\n]+)\]\((?:https?://|/)[^)\s]+\)", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.+?)\*\*", "$1");
        return System.Text.RegularExpressions.Regex.Replace(text, @"`([^`\r\n]+)`", "$1");
    }

    /// <summary>Only Colitu's own pages open from an answer (the help centre and the site).</summary>
    internal static bool IsHelpLink(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && (uri.IdnHost.Equals("colitu.com", StringComparison.OrdinalIgnoreCase) || uri.IdnHost.EndsWith(".colitu.com", StringComparison.OrdinalIgnoreCase));

    private FrameworkElement ChatNote(string text, string? ticketId)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(24, 2, 24, 12) };
        panel.Children.Add(new TextBlock { Text = text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = (Brush)FindResource("MutedBrush") });
        if (ticketId != null)
        {
            var open = new Button { Content = Loc.I["bot.openTicket"], Style = (Style)FindResource("LinkButton"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            open.Click += async (_, _) => await OpenSupportThreadAsync(ticketId);
            panel.Children.Add(open);
        }
        return panel;
    }

    private FrameworkElement TypingIndicator()
    {
        var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        for (var i = 0; i < 3; i++)
        {
            var dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 4, 0), Fill = (Brush)FindResource("LilacBrush"), Opacity = 0.3 };
            var pulse = new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(450)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(150 * i) };
            dot.BeginAnimation(OpacityProperty, pulse);
            dots.Children.Add(dot);
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(dots);
        row.Children.Add(new TextBlock { Text = Loc.I["bot.typing"], FontSize = 12.5, Foreground = (Brush)FindResource("MutedBrush"), VerticalAlignment = VerticalAlignment.Center });
        return new Border
        {
            Child = row,
            Padding = new Thickness(15, 11, 15, 11),
            Margin = new Thickness(0, 0, 80, 10),
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(18, 18, 18, 6),
            Background = (Brush)FindResource("Surface2Brush"),
            BorderBrush = (Brush)FindResource("LineBrush"),
            BorderThickness = new Thickness(1)
        };
    }

    private FrameworkElement FeedbackRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 8) };
        row.Children.Add(new TextBlock { Text = Loc.I["bot.helpful"], FontSize = 12.5, Foreground = (Brush)FindResource("MutedBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        foreach (var (key, helpful) in new[] { ("bot.yes", true), ("bot.no", false) })
        {
            var button = new Button { Content = Loc.I[key], Style = (Style)FindResource("SecondaryButton"), Height = 30, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 0, 8, 0), FontSize = 12.5 };
            button.Click += (_, _) => ChatFeedback(helpful);
            row.Children.Add(button);
        }
        return row;
    }
}
