using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using v2rayN.Services;

namespace v2rayN.Views;

/// <summary>
/// Colitu Passport page and its home card (colitu-android screens/PassportHome.kt). The minute
/// tick runs here while the tunnel is up, also with the window hidden in the tray; the rewards
/// wait in the passport's queue until the window can show them.
/// </summary>
public partial class ColituMainWindow
{
    private readonly ColituPassport _passport = ColituPassport.Instance;
    private DispatcherTimer? _passportTick;
    private int _passportChapter = 1;
    private bool _passportCompanionChecked;

    private bool PassportPremium => _auth.CurrentSubscription?.Active == true;

    private void InitPassport()
    {
        _passportTick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(1) };
        _passportTick.Tick += (_, _) => OnPassportMinute();
        _passportTick.Start();
        InitPassportReveals();
        _vpn.StatusChanged += _ => Dispatcher.BeginInvoke(() =>
        {
            if (_vpn.Status == ColituVpnStatus.Connected && _vpn.ConnectedServer is { } server)
            {
                _passport.RememberCity(PassportCountry(server), server.IsMultihop ? server.Exit?.City : server.City);
            }
            ApplyPassportHome();
        });
        Loc.I.Changed += () =>
        {
            ApplyPassportHome();
            if (_page == "passport") RenderPassport();
        };
        PassportPage.SizeChanged += (_, e) =>
        {
            if (e.WidthChanged && _page == "passport") LayoutPassport();
        };
        ApplyPassportHome();
    }

    /// <summary>The country a tunnel counts for: the exit of a multihop route, else the server's own.</summary>
    private static string? PassportCountry(ColituVpnServer server) =>
        server.IsMultihop ? server.Exit?.Country ?? server.CountryCode : server.CountryCode;

    /// <summary>One minute of the running tunnel (the same <see cref="ColituPassport.Tick"/> as the phones).</summary>
    private void OnPassportMinute()
    {
        try
        {
            if (_servers.Count > 0)
            {
                // Tester accounts see the panel's alpha nodes: no daily cap for them.
                _passport.Tester = _servers.Any(server => server.Alpha);
            }
            if (_vpn.Status != ColituVpnStatus.Connected || _vpn.ConnectedServer is not { } server)
            {
                return;
            }
            _ = SendPassportMetricsAsync();
            var events = _passport.Tick(PassportCountry(server));
            if (events.Count > 0)
            {
                NotifyPassport(events);
            }
            ApplyPassportHome();
            if (_page == "passport" && PassportSheetLayer.Visibility != Visibility.Visible)
            {
                RenderPassport();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ColituMainWindow.OnPassportMinute", ex);
        }
    }

    private bool _passportMetricsSending;

    /// <summary>The optional anonymous counters, once a day while signed in (off unless the user turned them on).</summary>
    private async Task SendPassportMetricsAsync()
    {
        if (_passportMetricsSending || !_auth.HasSession || _passport.Social.MetricsPayload() is not { } body)
        {
            return;
        }
        _passportMetricsSending = true;
        try
        {
            if (await _auth.PostPassportMetricsAsync(body))
            {
                _passport.Social.MetricsSent();
            }
        }
        finally
        {
            _passportMetricsSending = false;
        }
    }

    /// <summary>Tray notification while the window is hidden; the biggest news of the minute only.</summary>
    private void NotifyPassport(IReadOnlyList<StampEvent> events)
    {
        if (!_passport.Notify || (IsVisible && WindowState != WindowState.Minimized))
        {
            return;
        }
        var e = events.FirstOrDefault(x => x is StampEvent.Mastered or StampEvent.LevelUp)
            ?? events.FirstOrDefault(x => x is StampEvent.Unlocked)
            ?? events[0];
        var loc = Loc.I;
        var country = PassportUi.Country(e.Code);
        var (title, body) = e switch
        {
            StampEvent.Unlocked => (loc.Format("passport.n.unlockedTitle", ("country", country)), loc["passport.n.unlockedBody"]),
            StampEvent.Piece piece => (loc.Format("passport.n.pieceTitle", ("country", country)), loc.Format("passport.pieces", ("n", piece.LevelPieces))),
            StampEvent.LevelUp up => (loc.Format("passport.levelUp", ("country", country), ("level", PassportUi.LevelName(up.Level))), loc["passport.n.levelBody"]),
            StampEvent.Mastered => (loc.Format("passport.mastered", ("country", country)), loc["passport.n.levelBody"]),
            _ => ("", "")
        };
        if (title.Length > 0)
        {
            try
            {
                TrayIcon.ShowNotification(title, body);
            }
            catch (Exception ex)
            {
                Logging.SaveLog("ColituMainWindow.NotifyPassport", ex);
            }
        }
    }

    // ── Home card ──────────────────────────────────────────────────────────

    private void PassportHomeCard_Click(object sender, RoutedEventArgs e) => Navigate("passport");

    private void ApplyPassportHome()
    {
        if (PassportHomeCover == null)
        {
            return;
        }
        PassportHomeCover.Source = PassportUi.Image($"passport_cover_{_passport.Goals.ActiveCover(PassportPremium).Key()}.png");
        PassportHomeGoal.Text = _passport.Count == 0 ? Loc.I["passport.goal.first"] : NextPassportGoal().Text;
        PassportHomeCount.Text = $"{_passport.Count}/{ColituPassport.Stamps.Count}";
        AutomationProperties.SetName(PassportHomeCard, $"{Loc.I["passport.title"]}: {PassportHomeCount.Text}. {PassportHomeGoal.Text}");
    }

    // ── Next goal ──────────────────────────────────────────────────────────

    private enum PassportGoalKind
    {
        Today,
        Level,
        Done
    }

    private sealed record PassportGoal(string Text, double Fraction, string Icon, PassportGoalKind Kind, string? Code = null, string? Target = null, int Left = 0);

    /// <summary>The nearest reward: today's next piece while one is open, else the stamp closest to its next level.</summary>
    private PassportGoal NextPassportGoal()
    {
        var loc = Loc.I;
        var (minutes, pieces) = _passport.Today;
        if (!_passport.Tester && pieces < ColituPassport.PieceMinutes.Length)
        {
            var next = ColituPassport.PieceMinutes[pieces];
            if (minutes < next)
            {
                return new PassportGoal(loc.Format("passport.goal.today", ("n", next - minutes)), minutes / (double)next, "star", PassportGoalKind.Today, Left: next - minutes);
            }
        }
        var best = _passport.Progress.Where(item => !item.Value.Mastered).OrderBy(item => item.Value.ToNext).FirstOrDefault();
        if (best.Value?.Level is not { } level)
        {
            return new PassportGoal(loc["passport.goal.done"], 1, "check", PassportGoalKind.Done);
        }
        var target = PassportUi.LevelName(level == StampLevel.Diamond ? level : level + 1);
        return new PassportGoal(
            loc.Format("passport.goal.level", ("country", PassportUi.Country(best.Key)), ("level", target), ("n", best.Value.ToNext)),
            best.Value.LevelPieces / (double)StampProgress.PiecesPerLevel,
            "arrowUp",
            PassportGoalKind.Level,
            best.Key,
            target,
            best.Value.ToNext);
    }

    // ── Page ───────────────────────────────────────────────────────────────

    private void PassportBack_Click(object sender, RoutedEventArgs e) => Navigate("home");

    private void PassportDuel_Click(object sender, RoutedEventArgs e) => OpenDuelSheet();

    private void PassportHow_Click(object sender, RoutedEventArgs e) => OpenHowSheet();

    /// <summary>Opening the page: build it, the first-visit intro, and a quiet check for the Companion stamp.</summary>
    private void OnPassportOpened()
    {
        RenderPassport();
        PassportScroll.ScrollToTop();
        // Keyboard users land on the page's first control; a mouse click leaves no focus ring or tooltip.
        if (InputManager.Current.MostRecentInputDevice is KeyboardDevice)
        {
            Dispatcher.BeginInvoke(() => PassportBackButton.Focus(), DispatcherPriority.Loaded);
        }
        if (!_passport.Social.IntroSeen)
        {
            ShowPassportIntro();
        }
        _ = CheckCompanionAsync();
        _ = SendPassportMetricsAsync();
    }

    /// <summary>Whoever invited someone earns the Companion stamp (the invite page's numbers, read once a session).</summary>
    private async Task CheckCompanionAsync()
    {
        if (_passportCompanionChecked || _passport.Social.HasCompanion || !_auth.HasSession)
        {
            return;
        }
        _passportCompanionChecked = true;
        try
        {
            var info = _invite ?? await ColituReferralService.Instance.LoadAsync();
            if (info.Invited > 0)
            {
                _passport.Social.MarkCompanion();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ColituMainWindow.CheckCompanionAsync", ex);
        }
    }

    private void RenderPassport()
    {
        try
        {
            BuildPassportLeft();
            BuildPassportRight();
            LayoutPassport();
            ApplyPassportHome();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ColituMainWindow.RenderPassport", ex);
        }
    }

    /// <summary>Two columns on a wide window; one under the other when it is narrow. Stamps wrap by themselves.</summary>
    private void LayoutPassport()
    {
        var width = PassportPage.ActualWidth - 88;
        var wide = width >= 720;
        PassportLeftColumn.Width = wide ? new GridLength(300) : new GridLength(1, GridUnitType.Star);
        PassportGapColumn.Width = new GridLength(wide ? 40 : 0);
        Grid.SetRow(PassportRight, wide ? 0 : 1);
        Grid.SetColumn(PassportRight, wide ? 2 : 0);
        Grid.SetColumnSpan(PassportRight, wide ? 1 : 3);
        PassportRight.Margin = new Thickness(0, wide ? 0 : 28, 0, 0);
        PassportLeft.MaxWidth = wide ? double.PositiveInfinity : 420;
    }

    /// <summary>Fixed stamp size of the grid (Android: three a row on a phone; here as many as fit).</summary>
    private const double PassportStampSize = 116;

    private void BuildPassportLeft()
    {
        var loc = Loc.I;
        PassportLeft.Children.Clear();
        var count = _passport.Count;
        var total = ColituPassport.Stamps.Count;

        // Cover with a soft glow; a click edits the passport.
        var coverHeight = ActualHeight < 760 ? 132 : 156;
        var hero = new Grid { Width = coverHeight * 1.25, Height = coverHeight * 1.1, Margin = new Thickness(0, 4, 0, 0) };
        hero.Children.Add(new Ellipse { Fill = (Brush)FindResource("PassportGlow"), Width = coverHeight * 1.25, Height = coverHeight * 1.25, IsHitTestVisible = false, Margin = new Thickness(0, -coverHeight * 0.13, 0, -coverHeight * 0.13) });
        hero.Children.Add(PassportUi.Cover(_passport.Goals.ActiveCover(PassportPremium), coverHeight));
        var heroButton = PassportUi.Plain(hero, OpenIdentitySheet, loc["passport.id.edit"]);
        heroButton.HorizontalAlignment = HorizontalAlignment.Center;
        if (ColituPassport.DemoEnabled)
        {
            // Demo build only (COLITU_PASSPORT_DEMO=1): a right click fills a demo passport, a second one empties it.
            heroButton.MouseRightButtonUp += (_, _) =>
            {
                _passport.DebugCycle();
                RenderPassport();
            };
        }
        PassportLeft.Children.Add(heroButton);

        var numbers = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0), FontSize = 34, FontWeight = FontWeights.Bold };
        numbers.Inlines.Add(new System.Windows.Documents.Run(count.ToString(loc.Culture)) { Foreground = PassportUi.Lilac });
        numbers.Inlines.Add(new System.Windows.Documents.Run($" / {total}") { Foreground = PassportUi.Muted });
        AutomationProperties.SetName(numbers, $"{count} / {total} {loc["passport.countries"]}");
        PassportLeft.Children.Add(numbers);
        var countries = PassportUi.Label(loc["passport.countries"], 12.5, PassportUi.Muted, align: TextAlignment.Center);
        PassportLeft.Children.Add(countries);
        var bar = PassportUi.Bar(count / (double)total, 4, PassportUi.Accent, 160);
        bar.Margin = new Thickness(0, 10, 0, 0);
        PassportLeft.Children.Add(bar);
        var identity = PassportUi.Label(PassportIdentityLine(), 12.5, PassportUi.Lilac, align: TextAlignment.Center, wrap: false);
        identity.Margin = new Thickness(8, 8, 8, 18);
        PassportLeft.Children.Add(identity);

        PassportLeft.Children.Add(count == 0 ? PassportFirstSteps() : PassportNextCard());

        var week = _passport.Weekly.Goals();
        var tiles = new UniformGrid { Rows = 1, Columns = 3, Margin = new Thickness(-4, 10, -4, 0) };
        tiles.Children.Add(PassportTile("send", $"{week.Count(_passport.Weekly.Done)}/{week.Count}", loc["passport.tile.week"], OpenWeeklySheet));
        tiles.Children.Add(PassportTile("grid", $"{PassportCollection.All.Count(_passport.Goals.Completed)}/{PassportCollection.All.Count}", loc["passport.tile.sets"], OpenSetsSheet));
        tiles.Children.Add(PassportTile("sealCheck", $"{PassportAchievement.All.Count(_passport.Goals.Achieved)}/{PassportAchievement.All.Count}", loc["passport.tile.badges"], OpenBadgesSheet));
        PassportLeft.Children.Add(tiles);

        var note = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
        note.Children.Add(PassportUi.Icon("lock", PassportUi.Dim, 12));
        var noteText = PassportUi.Label(loc["passport.local"], 11.5, PassportUi.Dim);
        noteText.MaxWidth = 270;
        noteText.Margin = new Thickness(6, 0, 0, 0);
        note.Children.Add(noteText);
        PassportLeft.Children.Add(note);
    }

    /// <summary>"Night Explorer · Collector #048213", the nickname first when set.</summary>
    private string PassportIdentityLine()
    {
        var goals = _passport.Goals;
        return string.Join(" · ", new[]
        {
            goals.Nickname.Length > 0 ? goals.Nickname : null,
            goals.TitleOf(goals.Title),
            Loc.I.Format("passport.id.collector", ("n", goals.CollectorNo))
        }.Where(part => !string.IsNullOrEmpty(part)));
    }

    /// <summary>A small tile: icon and value on one line, a short label under it.</summary>
    private Button PassportTile(string icon, string value, string label, Action onClick)
    {
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var top = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        top.Children.Add(PassportUi.Icon(icon, PassportUi.Lilac, 14));
        var number = PassportUi.Label(value, 14, PassportUi.Text, FontWeights.SemiBold, wrap: false);
        number.Margin = new Thickness(6, 0, 0, 0);
        top.Children.Add(number);
        column.Children.Add(top);
        var caption = PassportUi.Label(label, 11, PassportUi.Muted, align: TextAlignment.Center, wrap: false);
        caption.Margin = new Thickness(0, 2, 0, 0);
        column.Children.Add(caption);
        var button = PassportUi.Plain(column, onClick, $"{label}: {value}");
        button.Background = PassportUi.Tile;
        button.BorderBrush = PassportUi.Line;
        button.BorderThickness = new Thickness(1);
        button.Padding = new Thickness(4, 8, 4, 8);
        button.Margin = new Thickness(4, 0, 4, 0);
        return button;
    }

    /// <summary>First visit: the whole game in three steps, and a way to start.</summary>
    private FrameworkElement PassportFirstSteps()
    {
        var loc = Loc.I;
        var body = new StackPanel();
        var steps = new UniformGrid { Rows = 1, Columns = 3 };
        foreach (var (icon, top, bottom) in new[] { ("power", "passport.step1", "passport.step1b"), ("star", "passport.step2", "passport.step2b"), ("arrowUp", "passport.step3", "passport.step3b") })
        {
            var step = new StackPanel { Margin = new Thickness(2, 0, 2, 0) };
            step.Children.Add(PassportUi.IconWell(icon, 28));
            var a = PassportUi.Label(loc[top], 11, PassportUi.Text, align: TextAlignment.Center);
            a.Margin = new Thickness(0, 5, 0, 0);
            step.Children.Add(a);
            step.Children.Add(PassportUi.Label(loc[bottom], 11, PassportUi.Lilac, align: TextAlignment.Center));
            steps.Children.Add(step);
        }
        body.Children.Add(steps);
        if (_vpn.Status is ColituVpnStatus.Disconnected or ColituVpnStatus.Error)
        {
            var connect = PassportUi.Primary(loc["home.connect"], () => _ = PassportConnectAsync(null), 40);
            connect.Margin = new Thickness(0, 12, 0, 0);
            connect.HorizontalAlignment = HorizontalAlignment.Center;
            connect.MinWidth = 200;
            body.Children.Add(connect);
        }
        return PassportUi.Glass(body, padding: new Thickness(10, 12, 10, 12));
    }

    private FrameworkElement PassportNextCard()
    {
        var goal = NextPassportGoal();
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(PassportUi.Ring(goal.Fraction, 42, 3.5, PassportUi.Icon(goal.Icon, PassportUi.Lilac, 15)));
        var text = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(PassportUi.Kicker(Loc.I["passport.next"]));
        var label = PassportUi.Label(goal.Text, 13.5, PassportUi.Text, FontWeights.SemiBold);
        label.Margin = new Thickness(0, 3, 0, 0);
        text.Children.Add(label);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var chevron = PassportUi.Icon("chevron", PassportUi.Muted, 12);
        Grid.SetColumn(chevron, 2);
        row.Children.Add(chevron);
        return PassportUi.Glass(row, OpenNextSheet, $"{Loc.I["passport.nx.title"]}: {goal.Text}", new Thickness(12));
    }

    private void BuildPassportRight()
    {
        var loc = Loc.I;
        PassportRight.Children.Clear();

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) };
        foreach (var n in new[] { 1, 2, 3 })
        {
            var open = n switch { 1 => true, 2 => _passport.Chapter2Open, _ => _passport.Goals.Chapter3Open };
            var content = new StackPanel();
            content.Children.Add(PassportUi.Label($"0{n}", 11.5, n == _passportChapter ? PassportUi.Lilac : PassportUi.Dim, FontWeights.SemiBold, TextAlignment.Center));
            var name = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            if (!open)
            {
                var lockIcon = PassportUi.Icon("lock", PassportUi.Dim, 12);
                lockIcon.Margin = new Thickness(0, 0, 4, 0);
                name.Children.Add(lockIcon);
            }
            var title = new TextBlock { Text = loc[$"passport.chName{n}"], FontSize = 15, FontWeight = FontWeights.SemiBold };
            title.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(Foreground)) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(RadioButton), 1) });
            name.Children.Add(title);
            content.Children.Add(name);
            var tab = new RadioButton
            {
                Content = content,
                GroupName = "PassportChapter",
                IsChecked = n == _passportChapter,
                Style = (Style)FindResource("PassportChapterTab"),
                Margin = new Thickness(10, 0, 10, 0)
            };
            AutomationProperties.SetName(tab, loc[$"passport.ch{n}"] + (open ? "" : $" · {loc["passport.chapterLocked"]}"));
            var chapter = n;
            tab.Checked += (_, _) =>
            {
                if (_passportChapter == chapter) return;
                _passportChapter = chapter;
                BuildPassportRight();
                LayoutPassport();
                Dispatcher.BeginInvoke(() => PassportRight.Children.OfType<StackPanel>().FirstOrDefault()?.Children.OfType<RadioButton>().ElementAtOrDefault(chapter - 1)?.Focus(), DispatcherPriority.Loaded);
            };
            tabs.Children.Add(tab);
        }
        PassportRight.Children.Add(tabs);

        if (_passportChapter == 3)
        {
            PassportRight.Children.Add(PassportLegacy());
        }
        else
        {
            var list = _passportChapter == 2 ? ColituPassport.Chapter2 : ColituPassport.Stamps;
            if (_passportChapter == 2 && !_passport.Chapter2Open)
            {
                var locked = PassportUi.Label(loc["passport.ch2.locked"], 12.5, PassportUi.Muted, align: TextAlignment.Center);
                locked.Margin = new Thickness(0, 0, 0, 16);
                PassportRight.Children.Add(locked);
            }
            PassportRight.Children.Add(PassportStampGrid(list));
        }
        PassportRight.Children.Add(PassportSpecialStamps());
    }

    /// <summary>The chapter's stamps: art, a dot in the level's colour and the name. No boxes.</summary>
    private WrapPanel PassportStampGrid(IEnumerable<ColituStamp> stamps)
    {
        var grid = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(-6, 0, -6, 0) };
        foreach (var stamp in stamps)
        {
            var progress = _passport.ProgressOf(stamp.Code);
            var column = new StackPanel();
            column.Children.Add(PassportUi.StampArt(stamp, progress, PassportStampSize, lockSize: 26));
            var name = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0), MaxWidth = PassportStampSize + 8 };
            if (progress.Level is { } level)
            {
                name.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = PassportUi.LevelBrush(level), Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            }
            name.Children.Add(PassportUi.Label(PassportUi.Country(stamp.Code), 12, progress.Unlocked ? PassportUi.Text : PassportUi.Dim, wrap: false));
            column.Children.Add(name);
            var a11y = progress.Level is { } l
                ? Loc.I.Format("passport.stampA11y", ("country", PassportUi.Country(stamp.Code)), ("level", PassportUi.LevelName(l)), ("n", progress.LevelPieces))
                : Loc.I.Format("passport.stampLockedA11y", ("country", PassportUi.Country(stamp.Code)));
            var tile = PassportUi.Plain(column, () => OpenStampSheet(stamp), a11y);
            tile.Padding = new Thickness(4, 4, 4, 6);
            tile.Margin = new Thickness(6, 0, 6, 10);
            tile.Width = PassportStampSize + 8;
            grid.Children.Add(tile);
        }
        return grid;
    }

    /// <summary>Chapter 03 — Legacy: mastery goals with their progress, open or not.</summary>
    private StackPanel PassportLegacy()
    {
        var loc = Loc.I;
        var panel = new StackPanel();
        if (!_passport.Goals.Chapter3Open)
        {
            var locked = PassportUi.Label(loc["passport.legacy.locked"], 12.5, PassportUi.Muted, align: TextAlignment.Center);
            locked.Margin = new Thickness(0, 0, 0, 12);
            panel.Children.Add(locked);
        }
        foreach (var (label, have, level) in new[]
        {
            (loc["passport.legacy.gold1"], _passport.Goals.Legacy(StampLevel.Gold, ColituPassport.Stamps), StampLevel.Gold),
            (loc["passport.legacy.diamond1"], _passport.Goals.Legacy(StampLevel.Diamond, ColituPassport.Stamps), StampLevel.Diamond),
            (loc["passport.legacy.gold2"], _passport.Goals.Legacy(StampLevel.Gold, ColituPassport.Chapter2), StampLevel.Gold)
        })
        {
            var body = new StackPanel();
            var top = new Grid();
            top.Children.Add(PassportUi.Label(label, 14, PassportUi.Text, FontWeights.SemiBold));
            top.Children.Add(new TextBlock { Text = $"{have}/10", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = PassportUi.LevelBrush(level), HorizontalAlignment = HorizontalAlignment.Right });
            body.Children.Add(top);
            var bar = PassportUi.Bar(have / 10.0, 8, PassportUi.LevelBrush(level));
            bar.Margin = new Thickness(0, 10, 0, 0);
            body.Children.Add(bar);
            var card = PassportUi.Glass(body, padding: new Thickness(16));
            card.Margin = new Thickness(0, 0, 0, 10);
            panel.Children.Add(card);
        }
        var keep = PassportUi.Label(loc["passport.legacy.keep"], 12, PassportUi.Dim, align: TextAlignment.Center);
        keep.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(keep);
        return panel;
    }

    /// <summary>Special stamps outside the chapters (the Companion stamp), earned or as a hint.</summary>
    private StackPanel PassportSpecialStamps()
    {
        var loc = Loc.I;
        var has = _passport.Social.HasCompanion;
        var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        panel.Children.Add(PassportUi.Kicker(loc["passport.special"], PassportUi.Dim, TextAlignment.Center));
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        row.Children.Add(PassportUi.CompanionStamp(has, 76));
        var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 240 };
        text.Children.Add(PassportUi.Label("Companion", 14, has ? PassportUi.Text : PassportUi.Muted, FontWeights.SemiBold));
        text.Children.Add(PassportUi.Label(has ? loc["passport.companion.have"] : loc["passport.companion.how"], 12, PassportUi.Muted));
        row.Children.Add(text);
        AutomationProperties.SetName(row, "Companion: " + (has ? loc["passport.companion.have"] : loc["passport.companion.how"]));
        panel.Children.Add(row);
        return panel;
    }

    // ── Connect ────────────────────────────────────────────────────────────

    /// <summary>The best open server of a stamp's country (no multihop), or null.</summary>
    private ColituVpnServer? PassportServerFor(string code) =>
        _servers.Where(server => !server.IsMultihop && server.Available && !server.Locked && ColituPassport.StampFor(server.CountryCode)?.Code == code)
            .OrderBy(server => server.PingSortValue)
            .FirstOrDefault();

    private bool PassportOnCountry(string? code) =>
        _vpn.Status == ColituVpnStatus.Connected && _vpn.ConnectedServer is { } server && ColituPassport.StampFor(PassportCountry(server))?.Code == code;

    /// <summary>Connects (or switches) to <paramref name="server"/>, or to the saved choice when null; stays on the passport.</summary>
    private async Task PassportConnectAsync(ColituVpnServer? server)
    {
        try
        {
            var active = _vpn.Status is ColituVpnStatus.Connected or ColituVpnStatus.Reconnecting;
            if (server != null)
            {
                _vpn.SelectServer(server);
                ApplyLocationCard();
            }
            if (active && server != null)
            {
                ShowToast(Loc.I.Format("locations.switching", ("server", ServerLabel(server) ?? PassportUi.Country(server.CountryCode ?? ""))));
                await _vpn.SwitchServerAsync(server);
            }
            else if (!active)
            {
                _lastToggle = DateTime.MinValue;
                await ToggleConnectionAsync();
            }
        }
        catch (ColituPlanRequiredException)
        {
            _planRequired = true;
            ApplyAccount();
            Navigate("plan");
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message, true);
        }
        ApplyStatus();
        if (_page == "passport") RenderPassport();
    }
}
