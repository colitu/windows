using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using v2rayN.Services;

namespace v2rayN.Views;

/// <summary>
/// Passport rewards (colitu-android screens/PassportReveal.kt): the queued rewards play one after
/// another while the window is open: the cinematic reveal for a new stamp or level, the flying
/// piece, the seal that lands, the turning achievement card, chapters, expeditions and the
/// Companion stamp. With Windows animations off they appear with a short fade instead.
/// Also the first-visit intro and the story-sized share card.
/// </summary>
public partial class ColituMainWindow
{
    private readonly List<StampEvent> _revealQueue = [];
    private DispatcherTimer? _revealPoll;
    private bool _revealShowing;
    private Action? _revealDone;
    private DispatcherTimer? _revealAuto;

    private void InitPassportReveals()
    {
        _revealPoll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _revealPoll.Tick += (_, _) => PollPassportRewards();
        _revealPoll.Start();
    }

    /// <summary>Takes what the tick queued (and newly reached goals) while the window is on screen, and plays it.</summary>
    private void PollPassportRewards()
    {
        try
        {
            if (_revealShowing || !IsVisible || WindowState == WindowState.Minimized || AppView.Visibility != Visibility.Visible
                || UpdatePrompt.Visibility == Visibility.Visible || TunPrompt.Visibility == Visibility.Visible)
            {
                return;
            }
            if (_revealQueue.Count == 0)
            {
                _revealQueue.AddRange(_passport.TakePending());
                _revealQueue.AddRange(_passport.Goals.CheckNew());
            }
            if (_revealQueue.Count > 0)
            {
                ShowNextReveal();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ColituMainWindow.PollPassportRewards", ex);
        }
    }

    private void ShowNextReveal()
    {
        while (_revealQueue.Count > 0)
        {
            var e = _revealQueue[0];
            // A Chapter 02 country before the chapter opens stays quiet; it plays when the chapter opens.
            if (e is StampEvent.Unlocked && ColituPassport.Chapter2.Any(s => s.Code == e.Code) && !_passport.Chapter2Open
                || e is StampEvent.Unlocked or StampEvent.Piece or StampEvent.LevelUp or StampEvent.Mastered && ColituPassport.StampOf(e.Code) == null)
            {
                _revealQueue.RemoveAt(0);
                continue;
            }
            break;
        }
        if (_revealQueue.Count == 0)
        {
            return;
        }
        var next = _revealQueue[0];
        _revealShowing = true;
        _revealDone = () =>
        {
            if (_revealQueue.Count > 0) _revealQueue.RemoveAt(0);
        };
        PassportRevealLayer.Children.Clear();
        PassportRevealLayer.Visibility = Visibility.Visible;
        try
        {
            switch (next)
            {
                case StampEvent.Piece piece:
                    PieceReveal(piece);
                    break;
                case StampEvent.Collected collected:
                    CollectionReveal(collected);
                    break;
                case StampEvent.Achieved achieved:
                    AchievementReveal(achieved);
                    break;
                case StampEvent.Chapter chapter:
                    ChapterReveal(chapter);
                    break;
                case StampEvent.Expedition expedition:
                    ExpeditionReveal(expedition);
                    break;
                case StampEvent.Special:
                    SpecialReveal();
                    break;
                default:
                    CinematicReveal(next);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ColituMainWindow.ShowNextReveal", ex);
            FinishReveal();
        }
    }

    /// <summary>Closes the current reward and moves on to the next one.</summary>
    private void FinishReveal()
    {
        _revealAuto?.Stop();
        _revealAuto = null;
        var done = _revealDone;
        _revealDone = null;
        done?.Invoke();
        PassportRevealLayer.Children.Clear();
        PassportRevealLayer.Visibility = Visibility.Collapsed;
        _revealShowing = false;
        ApplyPassportHome();
        if (_page == "passport" && PassportSheetLayer.Visibility != Visibility.Visible) RenderPassport();
        if (_revealQueue.Count > 0)
        {
            Dispatcher.BeginInvoke(ShowNextReveal, DispatcherPriority.Background);
        }
    }

    private void PassportRevealLayer_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _revealDone != null)
        {
            e.Handled = true;
            FinishReveal();
        }
    }

    // ── Building blocks ────────────────────────────────────────────────────

    /// <summary>A dark full-window stage; returns the centred column the reward builds in.</summary>
    private StackPanel RevealStage(string name, bool clickToClose)
    {
        var dim = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0xF0, 0x05, 0x04, 0x0F)) };
        PassportRevealLayer.Children.Add(dim);
        Animate(dim, OpacityProperty, 0, 1, 0, 0.29);
        if (clickToClose)
        {
            dim.MouseLeftButtonUp += (_, _) => FinishReveal();
        }
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 460, Margin = new Thickness(28, 70, 28, 28) };
        AutomationProperties.SetName(column, name);
        AutomationProperties.SetLiveSetting(column, AutomationLiveSetting.Assertive);
        PassportRevealLayer.Children.Add(column);
        return column;
    }

    /// <summary>An animation from <paramref name="from"/> to <paramref name="to"/> starting at <paramref name="begin"/> s for <paramref name="seconds"/> s; instant-ish without motion.</summary>
    private static void Animate(IAnimatable target, DependencyProperty property, double from, double to, double begin, double seconds, IEasingFunction? ease = null)
    {
        if (!PassportUi.Motion)
        {
            // Animations off: everything arrives together with a short fade (opacity), no movement.
            if (property == OpacityProperty)
            {
                target.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(180)));
            }
            else
            {
                target.BeginAnimation(property, null);
                if (target is DependencyObject d) d.SetValue(property, to);
            }
            return;
        }
        if (target is DependencyObject start) start.SetValue(property, from);
        target.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            BeginTime = TimeSpan.FromSeconds(begin),
            EasingFunction = ease ?? new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        });
    }

    /// <summary>Fades and lifts an element in.</summary>
    private static void Rise(FrameworkElement element, double begin, double seconds = 0.43, double offset = 24)
    {
        var shift = new TranslateTransform();
        element.RenderTransform = shift;
        element.Opacity = 0;
        Animate(element, OpacityProperty, 0, 1, begin, seconds);
        Animate(shift, TranslateTransform.YProperty, offset, 0, begin, seconds);
    }

    /// <summary>Buttons at the end of a reward; usable once they are in.</summary>
    private StackPanel RevealButtons(double begin, params (string Text, Action Action, bool Primary)[] buttons)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 28, 0, 0), Width = 340 };
        foreach (var (text, action, primary) in buttons)
        {
            var button = primary ? PassportUi.Primary(text, action) : PassportUi.Secondary(text, action);
            button.Margin = new Thickness(0, 0, 0, 10);
            panel.Children.Add(button);
        }
        Rise(panel, begin, 0.4, 12);
        var delay = PassportUi.Motion ? begin + 0.2 : 0.05;
        panel.IsEnabled = false;
        After(delay, () =>
        {
            panel.IsEnabled = true;
            if (panel.Children.Count > 0) ((UIElement)panel.Children[^1]).Focus();
        });
        return panel;
    }

    private void After(double seconds, Action action)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(0.01, seconds)) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_revealShowing || PassportRevealLayer.Visibility == Visibility.Visible) action();
        };
        timer.Start();
    }

    /// <summary>Closes an automatic reward (piece, expedition) by itself after a moment.</summary>
    private void AutoClose(double seconds)
    {
        _revealAuto = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _revealAuto.Tick += (_, _) => FinishReveal();
        _revealAuto.Start();
    }

    private static TextBlock RevealTitle(string text, double size = 30)
    {
        var title = PassportUi.Label(text.ToUpper(Loc.I.Culture), size, PassportUi.Text, FontWeights.Bold, TextAlignment.Center);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        return title;
    }

    /// <summary>A light burst and ring behind the art when the material lands.</summary>
    private static Grid Burst(Color accent, double size, double begin)
    {
        var grid = new Grid { Width = size, Height = size, IsHitTestVisible = false, RenderTransformOrigin = new Point(0.5, 0.5), Opacity = 0 };
        var glow = new RadialGradientBrush(Color.FromArgb(0x73, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B));
        grid.Children.Add(new Ellipse { Fill = glow });
        grid.Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(0x80, accent.R, accent.G, accent.B)), StrokeThickness = 2 });
        var scale = new ScaleTransform(0.4, 0.4);
        grid.RenderTransform = scale;
        if (PassportUi.Motion)
        {
            Animate(scale, ScaleTransform.ScaleXProperty, 0.4, 1, begin, 0.85);
            Animate(scale, ScaleTransform.ScaleYProperty, 0.4, 1, begin, 0.85);
            grid.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
            {
                BeginTime = TimeSpan.FromSeconds(begin),
                KeyFrames = { new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)), new LinearDoubleKeyFrame(0.3, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.85))) }
            });
        }
        else
        {
            scale.ScaleX = scale.ScaleY = 1;
            grid.Opacity = 0.3;
        }
        return grid;
    }

    // ── New stamp / level / Diamond complete ──────────────────────────────

    /// <summary>
    /// The screen dims, the stamp appears, its material sweeps in from the left with a glint (a
    /// level-up turns once), then "GERMANY UNLOCKED" and the share button.
    /// </summary>
    private void CinematicReveal(StampEvent e)
    {
        var loc = Loc.I;
        var stamp = ColituPassport.StampOf(e.Code)!;
        var after = _passport.ProgressOf(stamp.Code);
        var levelUp = e is not StampEvent.Unlocked;
        var before = levelUp ? after with { Pieces = Math.Max(0, after.Pieces - 1) } : StampProgress.None;
        var country = PassportUi.Country(stamp.Code);
        var title = e switch
        {
            StampEvent.LevelUp up => loc.Format("passport.r.levelUnlocked", ("level", PassportUi.LevelName(up.Level))),
            StampEvent.Mastered => loc["passport.masteredShort"],
            _ => loc.Format("passport.r.unlocked", ("country", country))
        };
        var accent = after.Level is { } l ? PassportUi.LevelColor(l) : Color.FromRgb(0xC4, 0xB5, 0xFD);
        PassportUi.Play(levelUp ? "passport_level" : "passport_reveal");

        var column = RevealStage($"{title}. {country}", clickToClose: false);
        const double size = 280;
        var art = new Grid { Width = size, Height = size, RenderTransformOrigin = new Point(0.5, 0.5) };
        art.Children.Add(Burst(accent, 440, 1.44).Also(b => b.Margin = new Thickness(-80)));
        art.Children.Add(PassportUi.StampArt(stamp, before, size));
        var material = PassportUi.StampArt(stamp, after, size);
        var clip = new RectangleGeometry(new Rect(0, 0, 0, size));
        material.Clip = clip;
        art.Children.Add(material);
        // The glint that rides the edge of the sweep, drawn only on the stamp.
        var glintMove = new TranslateTransform(-60, 0);
        var glint = new Rectangle
        {
            Width = 52,
            HorizontalAlignment = HorizontalAlignment.Left,
            Fill = new LinearGradientBrush(new GradientStopCollection { new(Colors.Transparent, 0), new(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF), 0.75), new(Colors.Transparent, 1) }, 0),
            RenderTransform = glintMove,
            IsHitTestVisible = false
        };
        var glintHost = new Grid { Width = size, Height = size, OpacityMask = new ImageBrush(PassportUi.Image(PassportUi.StampImageName(stamp.Code, after.Level))) { Stretch = Stretch.Uniform } };
        glintHost.Children.Add(glint);
        art.Children.Add(glintHost);
        column.Children.Add(art);

        var scale = new ScaleTransform();
        art.RenderTransform = scale;
        Animate(art, OpacityProperty, 0, 1, 0.19, 0.58);
        if (PassportUi.Motion)
        {
            Animate(scale, ScaleTransform.ScaleYProperty, 0.82, 1, 0.19, 0.58);
            if (levelUp)
            {
                // A level-up turns once while the new material comes in (a flat turn: width through zero).
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimationUsingKeyFrames
                {
                    KeyFrames =
                    {
                        new LinearDoubleKeyFrame(0.82, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.19))),
                        new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.48)), new CubicEase()),
                        new EasingDoubleKeyFrame(-1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.98)), new SineEase { EasingMode = EasingMode.EaseInOut }),
                        new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.49)), new SineEase { EasingMode = EasingMode.EaseInOut })
                    }
                });
            }
            else
            {
                Animate(scale, ScaleTransform.ScaleXProperty, 0.82, 1, 0.19, 0.58);
            }
            clip.BeginAnimation(RectangleGeometry.RectProperty, new RectAnimation(new Rect(0, 0, 0, size), new Rect(0, 0, size, size), TimeSpan.FromSeconds(0.72)) { BeginTime = TimeSpan.FromSeconds(0.77) });
            glintMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-40, size - 12, TimeSpan.FromSeconds(0.72)) { BeginTime = TimeSpan.FromSeconds(0.77) });
            glint.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
            {
                KeyFrames = { new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)), new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.78))), new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.48))) }
            });
        }
        else
        {
            clip.Rect = new Rect(0, 0, size, size);
            glint.Opacity = 0;
        }

        var words = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
        words.Children.Add(PassportUi.Kicker(levelUp ? loc["passport.r.newLevel"] : loc["passport.r.newStamp"], new SolidColorBrush(accent), TextAlignment.Center));
        var big = RevealTitle(title);
        big.Margin = new Thickness(0, 8, 0, 6);
        words.Children.Add(big);
        words.Children.Add(PassportUi.Label($"{country} · {loc.Format("passport.stampNo", ("n", stamp.NumberText))}", 14, new SolidColorBrush(accent), align: TextAlignment.Center));
        Rise(words, 1.49);
        column.Children.Add(words);
        column.Children.Add(RevealButtons(1.97,
            (loc["passport.r.story"], () =>
            {
                SharePassportCard(stamp);
                FinishReveal();
            }, true),
            (loc["passport.r.add"], FinishReveal, false)));
    }

    // ── One puzzle piece ───────────────────────────────────────────────────

    /// <summary>The stamp with its puzzle; a piece flies up into its slot, the slot flashes and the count ticks up. Closes by itself.</summary>
    private void PieceReveal(StampEvent.Piece e)
    {
        var loc = Loc.I;
        var stamp = ColituPassport.StampOf(e.Code)!;
        var progress = _passport.ProgressOf(stamp.Code);
        // The level the piece belongs to: a 12th piece completes the level that came before.
        var level = e.LevelPieces == StampProgress.PiecesPerLevel && !progress.Mastered && progress.Level is { } now && now > StampLevel.Bronze ? now - 1 : progress.Level ?? StampLevel.Bronze;
        var color = PassportUi.LevelColor(level);
        var filled = e.LevelPieces;
        PassportUi.Play("passport_piece");

        var column = RevealStage($"{loc["passport.r.piece"]}. {PassportUi.Country(stamp.Code)}", clickToClose: true);
        const double size = 260;
        var art = new Grid { Width = size, Height = size };
        var shown = progress with { Pieces = (int)level * StampProgress.PiecesPerLevel + Math.Max(0, filled - 1) };
        art.Children.Add(PassportUi.StampArt(stamp, shown, size, puzzle: true, piecesOverride: Math.Max(0, filled - 1)));
        var slot = PassportUi.SlotRect(size, PassportUi.PieceSlot(filled));
        var source = PassportUi.Image(PassportUi.StampImageName(stamp.Code, level));
        var k = source == null ? 1 : source.Width / size;
        // The piece: the stamp's own pixels of that slot, flying up from below into place.
        var piece = new Rectangle
        {
            Width = slot.Width,
            Height = slot.Height,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(slot.X, slot.Y, 0, 0),
            Fill = new ImageBrush(source)
            {
                Stretch = Stretch.Fill,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(slot.X * k, slot.Y * k, slot.Width * k, slot.Height * k)
            },
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 1.5
        };
        var fly = new TranslateTransform();
        piece.RenderTransform = fly;
        art.Children.Add(piece);
        var flash = new Rectangle { Width = slot.Width, Height = slot.Height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = piece.Margin, Fill = Brushes.White, Opacity = 0, IsHitTestVisible = false };
        art.Children.Add(flash);
        art.OpacityMask = null;
        column.Children.Add(art);
        if (PassportUi.Motion)
        {
            Animate(fly, TranslateTransform.YProperty, size - slot.Y + 120, 0, 0.15, 0.7, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 });
            Animate(piece, OpacityProperty, 0, 1, 0.15, 0.2);
            flash.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
            {
                BeginTime = TimeSpan.FromSeconds(0.85),
                KeyFrames = { new LinearDoubleKeyFrame(0.65, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.05))), new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5))) }
            });
        }

        var words = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
        var plus = PassportUi.Label(loc["passport.r.piece"], 34, new SolidColorBrush(color), FontWeights.Bold, TextAlignment.Center);
        words.Children.Add(plus);
        words.Children.Add(PassportUi.Label($"{PassportUi.Country(stamp.Code)} · {PassportUi.LevelName(level)} · {loc.Format("passport.pieces", ("n", filled))}", 14, PassportUi.Muted, align: TextAlignment.Center));
        Rise(words, 0.85);
        column.Children.Add(words);
        var hint = PassportUi.Label(loc["passport.r.click"], 12.5, PassportUi.Dim, align: TextAlignment.Center);
        hint.Margin = new Thickness(0, 18, 0, 0);
        Rise(hint, 1.45, 0.3, 0);
        column.Children.Add(hint);
        column.MouseLeftButtonUp += (_, _) => FinishReveal();
        AutoClose(PassportUi.Motion ? 2.6 : 2.2);
    }

    // ── Collection complete ────────────────────────────────────────────────

    /// <summary>The seal lands on the passport like a stamp ("thud"), ink splashes; the new cover can be used at once.</summary>
    private void CollectionReveal(StampEvent.Collected e)
    {
        var loc = Loc.I;
        if (PassportCollection.Of(e.Code) is not { } set)
        {
            FinishReveal();
            return;
        }
        var color = PassportUi.SealColor(set);
        PassportUi.Play("passport_seal");
        var column = RevealStage($"{loc["passport.r.setDone"]}: {set.Title}", clickToClose: false);
        var stage = new Grid { Width = 240, Height = 240 };
        // Ink splash: a ring that spreads when the seal hits.
        var ring = new Ellipse { Stroke = new SolidColorBrush(color), StrokeThickness = 3, RenderTransformOrigin = new Point(0.5, 0.5), Opacity = 0 };
        var ringScale = new ScaleTransform(0.5, 0.5);
        ring.RenderTransform = ringScale;
        stage.Children.Add(ring);
        var seal = PassportUi.Seal(set, true, 190);
        seal.RenderTransformOrigin = new Point(0.5, 0.5);
        var sealScale = new ScaleTransform();
        seal.RenderTransform = new TransformGroup { Children = { sealScale, new RotateTransform(-12) } };
        stage.Children.Add(seal);
        column.Children.Add(stage);
        Animate(seal, OpacityProperty, 0, 1, 0.1, 0.18);
        if (PassportUi.Motion)
        {
            var drop = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 };
            Animate(sealScale, ScaleTransform.ScaleXProperty, 2.4, 1, 0.1, 0.36, drop);
            Animate(sealScale, ScaleTransform.ScaleYProperty, 2.4, 1, 0.1, 0.36, drop);
            Animate(ringScale, ScaleTransform.ScaleXProperty, 0.5, 1.35, 0.42, 0.7);
            Animate(ringScale, ScaleTransform.ScaleYProperty, 0.5, 1.35, 0.42, 0.7);
            ring.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
            {
                BeginTime = TimeSpan.FromSeconds(0.42),
                KeyFrames = { new LinearDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.Zero)), new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.7))) }
            });
        }
        var words = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        words.Children.Add(PassportUi.Kicker(loc["passport.r.setDone"], new SolidColorBrush(color), TextAlignment.Center));
        var big = RevealTitle(set.Title);
        big.Margin = new Thickness(0, 8, 0, 6);
        words.Children.Add(big);
        if (set.Cover is { } cover)
        {
            words.Children.Add(PassportUi.Label(loc.Format("passport.r.newCover", ("cover", loc[$"passport.cover.{cover.Key()}"])), 14, new SolidColorBrush(color), align: TextAlignment.Center));
        }
        Rise(words, 0.9);
        column.Children.Add(words);
        var buttons = new List<(string, Action, bool)>();
        if (set.Cover is { } useCover)
        {
            buttons.Add((loc["passport.r.useCover"], () =>
            {
                _passport.Goals.UpdateCover(useCover);
                FinishReveal();
            }, true));
        }
        buttons.Add((loc["passport.r.great"], FinishReveal, set.Cover == null));
        column.Children.Add(RevealButtons(1.4, buttons.ToArray()));
    }

    // ── Achievement ────────────────────────────────────────────────────────

    /// <summary>The card turns from "???" to the achievement, with a sparkle.</summary>
    private void AchievementReveal(StampEvent.Achieved e)
    {
        var loc = Loc.I;
        if (PassportAchievement.Of(e.Code) is not { } a)
        {
            FinishReveal();
            return;
        }
        PassportUi.Play("passport_level");
        var column = RevealStage($"{(a.Secret ? loc["passport.r.secretAch"] : loc["passport.r.ach"])}: {a.Title}", clickToClose: false);
        var card = new Border
        {
            Width = 180,
            Height = 220,
            CornerRadius = new CornerRadius(24),
            Background = PassportUi.GlassFill,
            BorderBrush = PassportUi.Lilac,
            BorderThickness = new Thickness(1.5),
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        var back = PassportUi.Label("???", 40, PassportUi.Dim, FontWeights.Bold, TextAlignment.Center);
        back.VerticalAlignment = VerticalAlignment.Center;
        var front = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var badge = PassportUi.Badge(a, true, 96);
        badge.HorizontalAlignment = HorizontalAlignment.Center;
        front.Children.Add(badge);
        var name = PassportUi.Label(a.Title, 14, PassportUi.Text, FontWeights.SemiBold, TextAlignment.Center);
        name.Margin = new Thickness(12, 10, 12, 0);
        front.Children.Add(name);
        var flip = new ScaleTransform();
        card.RenderTransform = flip;
        card.Child = PassportUi.Motion ? back : front;
        column.Children.Add(Burst(Color.FromRgb(0xC4, 0xB5, 0xFD), 300, 0.95).Also(b => b.Margin = new Thickness(0, 0, 0, -260)));
        column.Children.Add(card);
        Animate(card, OpacityProperty, 0, 1, 0.1, 0.3);
        if (PassportUi.Motion)
        {
            Animate(flip, ScaleTransform.ScaleXProperty, 1, 0, 0.55, 0.22, new SineEase { EasingMode = EasingMode.EaseIn });
            After(0.78, () =>
            {
                card.Child = front;
                Animate(flip, ScaleTransform.ScaleXProperty, 0, 1, 0, 0.3, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 });
            });
        }
        var words = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
        words.Children.Add(PassportUi.Kicker(a.Secret ? loc["passport.r.secretAch"] : loc["passport.r.ach"], align: TextAlignment.Center));
        var big = RevealTitle(a.Title, 28);
        big.Margin = new Thickness(0, 8, 0, 6);
        words.Children.Add(big);
        words.Children.Add(PassportUi.Label(loc[$"passport.ach.{a.Id}"], 14, PassportUi.Muted, align: TextAlignment.Center));
        Rise(words, 1.2);
        column.Children.Add(words);
        column.Children.Add(RevealButtons(1.6,
            (loc["passport.ach.use"], () =>
            {
                _passport.Goals.UpdateTitle(a.Id);
                FinishReveal();
            }, true),
            (loc["passport.r.great"], FinishReveal, false)));
    }

    // ── Chapter, expedition, Companion ─────────────────────────────────────

    private void ChapterReveal(StampEvent.Chapter e)
    {
        var loc = Loc.I;
        PassportUi.Play("passport_reveal");
        var column = RevealStage(loc[$"passport.ch{e.Code}"], clickToClose: false);
        var kicker = PassportUi.Kicker(loc["passport.r.chapterOpen"], align: TextAlignment.Center);
        Rise(kicker, 0.3);
        column.Children.Add(kicker);
        var big = RevealTitle(loc[$"passport.ch{e.Code}"], 32);
        big.Margin = new Thickness(0, 10, 0, 10);
        Rise(big, 0.55, 0.6, 30);
        column.Children.Add(big);
        var body = PassportUi.Label(loc[$"passport.r.chapterBody{e.Code}"], 14, PassportUi.Muted, align: TextAlignment.Center);
        Rise(body, 1.2);
        column.Children.Add(body);
        column.Children.Add(RevealButtons(1.8, (loc["passport.r.great"], () =>
        {
            if (int.TryParse(e.Code, out var chapter)) _passportChapter = chapter;
            FinishReveal();
        }, true)));
    }

    /// <summary>A dated visa stamp for a finished expedition; closes by itself.</summary>
    private void ExpeditionReveal(StampEvent.Expedition e)
    {
        var loc = Loc.I;
        if (ExpeditionGoal.Of(e.Code) is not { } goal)
        {
            FinishReveal();
            return;
        }
        PassportUi.Play("passport_piece");
        var column = RevealStage($"{loc["passport.r.expedition"]}: {loc[$"passport.x.{goal.Id}"]}", clickToClose: true);
        var visa = VisaStamp(goal, 200);
        var scale = new ScaleTransform();
        visa.RenderTransform = new TransformGroup { Children = { scale, new RotateTransform(-8) } };
        visa.RenderTransformOrigin = new Point(0.5, 0.5);
        column.Children.Add(visa);
        Animate(visa, OpacityProperty, 0, 1, 0.1, 0.2);
        if (PassportUi.Motion)
        {
            Animate(scale, ScaleTransform.ScaleXProperty, 1.8, 1, 0.1, 0.35, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 });
            Animate(scale, ScaleTransform.ScaleYProperty, 1.8, 1, 0.1, 0.35, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 });
        }
        var words = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
        words.Children.Add(PassportUi.Kicker(loc["passport.r.expedition"], align: TextAlignment.Center));
        var title = PassportUi.Label(loc[$"passport.x.{goal.Id}"], 18, PassportUi.Text, FontWeights.SemiBold, TextAlignment.Center);
        title.Margin = new Thickness(0, 8, 0, 4);
        words.Children.Add(title);
        words.Children.Add(PassportUi.Label(loc.Format("passport.week.total", ("n", _passport.Weekly.Total)), 12.5, PassportUi.Lilac, align: TextAlignment.Center));
        Rise(words, 0.6);
        column.Children.Add(words);
        column.MouseLeftButtonUp += (_, _) => FinishReveal();
        AutoClose(2.4);
    }

    /// <summary>A rectangular ink visa with a dashed inner frame, the week and the goal's icon.</summary>
    private Grid VisaStamp(ExpeditionGoal goal, double size)
    {
        var grid = new Grid { Width = size, Height = size * 0.66 };
        grid.Children.Add(new Border { BorderBrush = PassportUi.Lilac, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(10) });
        grid.Children.Add(new Rectangle { Margin = new Thickness(7), RadiusX = 6, RadiusY = 6, Stroke = PassportUi.Lilac, StrokeThickness = 1.2, StrokeDashArray = [5, 3] });
        var column = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        column.Children.Add(PassportUi.Label(Loc.I["passport.week.visa"], size * 0.08, PassportUi.Lilac, FontWeights.Bold, TextAlignment.Center));
        column.Children.Add(PassportUi.Icon(PassportUi.ExpeditionIcon(goal), PassportUi.Lilac, size * 0.16));
        column.Children.Add(PassportUi.Label(_passport.Weekly.WeekKey(), size * 0.065, PassportUi.Lilac, align: TextAlignment.Center));
        grid.Children.Add(column);
        return grid;
    }

    /// <summary>The Companion stamp drops in like a seal, then why you got it.</summary>
    private void SpecialReveal()
    {
        var loc = Loc.I;
        PassportUi.Play("passport_seal");
        var column = RevealStage($"{loc["passport.r.special"]}: Companion", clickToClose: false);
        var stamp = PassportUi.CompanionStamp(true, 200);
        stamp.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform();
        stamp.RenderTransform = scale;
        column.Children.Add(stamp);
        Animate(stamp, OpacityProperty, 0, 1, 0.1, 0.2);
        if (PassportUi.Motion)
        {
            Animate(scale, ScaleTransform.ScaleXProperty, 2.2, 1, 0.1, 0.36, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 });
            Animate(scale, ScaleTransform.ScaleYProperty, 2.2, 1, 0.1, 0.36, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 });
        }
        var words = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
        words.Children.Add(PassportUi.Kicker(loc["passport.r.special"], align: TextAlignment.Center));
        var big = RevealTitle("Companion");
        big.Margin = new Thickness(0, 8, 0, 6);
        words.Children.Add(big);
        words.Children.Add(PassportUi.Label(loc["passport.companion.body"], 14, PassportUi.Muted, align: TextAlignment.Center));
        Rise(words, 0.8);
        column.Children.Add(words);
        column.Children.Add(RevealButtons(1.3, (loc["passport.r.great"], FinishReveal, true)));
    }

    // ── First visit ────────────────────────────────────────────────────────

    /// <summary>The cover turns in, the passport is named, the three steps arrive one by one, then "Start". Shown once.</summary>
    private void ShowPassportIntro()
    {
        if (_revealShowing) return;
        var loc = Loc.I;
        _revealShowing = true;
        _revealDone = () => _passport.Social.IntroSeen = true;
        PassportRevealLayer.Children.Clear();
        PassportRevealLayer.Visibility = Visibility.Visible;
        PassportRevealLayer.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(0x0B, 0x0A, 0x22)) });
        PassportRevealLayer.Children.Add(new Image { Source = PassportUi.Image("passport_bg.jpg"), Stretch = Stretch.UniformToFill, Opacity = 0.7, IsHitTestVisible = false });
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 420, Margin = new Thickness(28, 70, 28, 28) };
        AutomationProperties.SetName(column, loc["passport.intro.title"]);
        PassportRevealLayer.Children.Add(column);

        var hero = new Grid { Height = 220 };
        hero.Children.Add(new Ellipse { Width = 260, Height = 260, Fill = (Brush)FindResource("PassportGlow"), Margin = new Thickness(0, -20, 0, -20) });
        var cover = PassportUi.Cover(_passport.Goals.ActiveCover(PassportPremium), 200);
        cover.RenderTransformOrigin = new Point(0.5, 0.5);
        var turn = new ScaleTransform();
        cover.RenderTransform = turn;
        hero.Children.Add(cover);
        column.Children.Add(hero);
        Animate(hero, OpacityProperty, 0, 1, 0, 0.9);
        if (PassportUi.Motion)
        {
            Animate(turn, ScaleTransform.ScaleXProperty, 0.25, 1, 0, 0.9, new CubicEase { EasingMode = EasingMode.EaseOut });
            Animate(turn, ScaleTransform.ScaleYProperty, 0.8, 1, 0, 0.9);
        }

        var words = new StackPanel { Margin = new Thickness(0, 26, 0, 0) };
        words.Children.Add(PassportUi.Kicker("COLITU PASSPORT", align: TextAlignment.Center));
        var title = PassportUi.Label(loc["passport.intro.title"], 28, PassportUi.Text, FontWeights.SemiBold, TextAlignment.Center);
        title.Margin = new Thickness(0, 8, 0, 6);
        words.Children.Add(title);
        words.Children.Add(PassportUi.Label(loc["passport.intro.body"], 14, PassportUi.Muted, align: TextAlignment.Center));
        Rise(words, 0.78, 0.5, 20);
        column.Children.Add(words);

        var stepsBox = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
        var index = 0;
        foreach (var (icon, top, bottom) in new[] { ("power", "passport.step1", "passport.step1b"), ("star", "passport.step2", "passport.step2b"), ("arrowUp", "passport.step3", "passport.step3b") })
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(PassportUi.IconWell(icon, 36));
            var a = PassportUi.Label(loc[top], 14, PassportUi.Text, FontWeights.SemiBold);
            a.Margin = new Thickness(12, 0, 8, 0);
            a.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(a, 1);
            row.Children.Add(a);
            var b = PassportUi.Label(loc[bottom], 12.5, PassportUi.Lilac, wrap: false);
            b.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(b, 2);
            row.Children.Add(b);
            var card = new Border { Background = new SolidColorBrush(Color.FromArgb(0x14, 0x9F, 0x8C, 0xFF)), CornerRadius = new CornerRadius(16), Padding = new Thickness(12), Margin = new Thickness(0, 5, 0, 5), Child = row };
            var shift = new TranslateTransform();
            card.RenderTransform = shift;
            card.Opacity = 0;
            var begin = 1.3 + index++ * 0.31;
            Animate(card, OpacityProperty, 0, 1, begin, 0.42);
            Animate(shift, TranslateTransform.XProperty, 40, 0, begin, 0.42);
            stepsBox.Children.Add(card);
        }
        column.Children.Add(stepsBox);
        var start = RevealButtons(2.35, (loc["passport.intro.start"], FinishReveal, true));
        start.Width = double.NaN;
        column.Children.Add(start);
    }

    // ── Share card ─────────────────────────────────────────────────────────

    /// <summary>
    /// A story-sized card (1080 × 1920): one stamp in its material with "GOLD UNLOCKED", or the
    /// whole passport when <paramref name="stamp"/> is null, with the comparison QR. Copied to the
    /// clipboard and saved under Pictures\Colitu.
    /// </summary>
    private void SharePassportCard(ColituStamp? stamp)
    {
        var loc = Loc.I;
        try
        {
            const int w = 1080;
            const int h = 1920;
            var font = (FontFamily)FindResource("ColituFont");
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                if (PassportUi.Image("passport_share_bg.jpg") is { } bg) dc.DrawImage(bg, new Rect(0, 0, w, h));
                void Text(string value, double y, double size, Color color, FontWeight weight, double spacing = 0)
                {
                    var text = new FormattedText(value, loc.Culture, Loc.I.FlowDirection, new Typeface(font, FontStyles.Normal, weight, FontStretches.Normal), size, new SolidColorBrush(color), 1.0);
                    // Long titles shrink to fit the card.
                    for (var fit = size; text.Width > w - 120 && fit > 40;)
                    {
                        fit -= 4;
                        text.SetFontSize(fit);
                    }
                    dc.DrawText(text, new Point((w - text.Width) / 2, y - text.Baseline));
                }
                var lilac = Color.FromRgb(0xC4, 0xB5, 0xFD);
                var white = Color.FromRgb(0xF4, 0xF3, 0xFA);
                var total = ColituPassport.Stamps.Count;
                Text("COLITU PASSPORT", 170, 40, lilac, FontWeights.Medium);
                if (stamp != null)
                {
                    var progress = _passport.ProgressOf(stamp.Code);
                    if (PassportUi.Image(PassportUi.StampImageName(stamp.Code, progress.Level)) is { } art) dc.DrawImage(art, new Rect(220, 270, 640, 640));
                    var headline = progress.Level is { } level
                        ? loc.Format("passport.r.levelUnlocked", ("level", PassportUi.LevelName(level)))
                        : loc.Format("passport.r.unlocked", ("country", PassportUi.Country(stamp.Code)));
                    Text(headline.ToUpper(loc.Culture), 1080, 92, white, FontWeights.Bold);
                    Text($"{PassportUi.Country(stamp.Code)} · {loc.Format("passport.stampNo", ("n", stamp.NumberText))}", 1170, 46, progress.Level is { } l ? PassportUi.LevelColor(l) : lilac, FontWeights.Medium);
                }
                else
                {
                    if (PassportUi.Image($"passport_cover_{_passport.Goals.ActiveCover(PassportPremium).Key()}.png") is { } cover)
                    {
                        const double height = 780;
                        var width = height * cover.PixelWidth / cover.PixelHeight;
                        dc.DrawImage(cover, new Rect((w - width) / 2, 250, width, height));
                    }
                    Text((_passport.Complete ? loc["passport.complete"] : loc["passport.title"]).ToUpper(loc.Culture), 1170, 88, white, FontWeights.Bold);
                }
                var box = new Rect(120, 1300, 840, 240);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x5A, 0x3A, 0x2F, 0x8F)), new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0x9F, 0x8C, 0xFF)), 3), box, 48, 48);
                Text($"{_passport.Count} / {total}", 1420, 92, white, FontWeights.Bold);
                Text(loc["passport.s.collected"].ToUpper(loc.Culture), 1495, 34, lilac, FontWeights.Medium);
                if (PassportUi.QrModules(_passport.Social.MyLink()) is { } m)
                {
                    const double side = 190;
                    var n = m.GetLength(0);
                    var cell = side / n;
                    var left = (w - side) / 2;
                    const double top = 1580;
                    dc.DrawRoundedRectangle(Brushes.White, null, new Rect(left - 14, top - 14, side + 28, side + 28), 18, 18);
                    var dot = new SolidColorBrush(Color.FromRgb(0x16, 0x15, 0x3A));
                    for (var x = 0; x < n; x++)
                    {
                        for (var y = 0; y < n; y++)
                        {
                            if (m[x, y]) dc.DrawRectangle(dot, null, new Rect(left + x * cell, top + y * cell, cell + 0.4, cell + 0.4));
                        }
                    }
                }
                Text(PassportIdentityLine(), 1832, 32, lilac, FontWeights.Medium);
                Text(loc["passport.s.cta"], 1880, 32, Color.FromRgb(0xA9, 0xAD, 0xC2), FontWeights.Medium);
            }
            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();

            var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Colitu");
            Directory.CreateDirectory(folder);
            var file = System.IO.Path.Combine(folder, $"colitu-passport-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            using (var stream = File.Create(file))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(stream);
            }
            try
            {
                Clipboard.SetImage(bitmap);
            }
            catch (Exception ex)
            {
                Logging.SaveLog("ColituMainWindow.SharePassportCard clipboard", ex);
            }
            _passport.Weekly.Bump(ExpeditionGoal.Share);
            ShowToast(loc["passport.shareSaved"]);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ColituMainWindow.SharePassportCard", ex);
            ShowToast(loc["passport.shareFailed"], true);
        }
    }
}

internal static class PassportFluent
{
    /// <summary>Applies <paramref name="change"/> and returns the element (inline layout tweaks).</summary>
    public static T Also<T>(this T value, Action<T> change)
    {
        change(value);
        return value;
    }
}
