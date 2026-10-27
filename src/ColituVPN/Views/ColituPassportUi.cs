using System.Media;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using QRCoder;
using v2rayN.Services;
using Path = System.Windows.Shapes.Path;

namespace v2rayN.Views;

/// <summary>
/// Building blocks of the Colitu Passport screens (colitu-android screens/Passport*.kt): stamp
/// art with its puzzle, covers, seals, badges, rings, bars, icons and the passport sounds.
/// </summary>
internal static class PassportUi
{
    public static readonly Brush Text = Frozen(Color.FromRgb(0xF4, 0xF3, 0xFA));
    public static readonly Brush Muted = Frozen(Color.FromRgb(0xA9, 0xAD, 0xC2));
    // Faint text: 5:1 on the passport cards (was #6F7186, 3.7:1).
    public static readonly Brush Dim = Frozen(Color.FromRgb(0x8B, 0x8D, 0xA2));
    public static readonly Brush Lilac = Frozen(Color.FromRgb(0xC4, 0xB5, 0xFD));
    public static readonly Brush Line = Frozen(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
    public static readonly Brush Glass10 = Frozen(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
    public static readonly Brush Tile = Frozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    public static readonly Brush LilacWell = Frozen(Color.FromArgb(0x26, 0x9F, 0x8C, 0xFF));
    public static readonly Brush LilacSoft = Frozen(Color.FromArgb(0x33, 0x9F, 0x8C, 0xFF));

    public static readonly Brush Accent = FrozenBrush(new LinearGradientBrush(Color.FromRgb(0xB4, 0xA4, 0xFF), Color.FromRgb(0x8B, 0x78, 0xFF), 0));

    public static readonly Brush GlassFill = FrozenBrush(new LinearGradientBrush(Color.FromArgb(0x22, 0x9F, 0x8C, 0xFF), Color.FromArgb(0x0F, 0x9F, 0x8C, 0xFF), 90));
    public static readonly Brush GlassEdge = Frozen(Color.FromArgb(0x33, 0x9F, 0x8C, 0xFF));

    private static Brush Frozen(Color color) => FrozenBrush(new SolidColorBrush(color));

    private static Brush FrozenBrush(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    // ── Motion ───────────────────────────────────────────────────────────

    /// <summary>Windows "Show animations" (Settings › Accessibility › Visual effects); off means short fades only.</summary>
    public static bool Motion => SystemParameters.ClientAreaAnimation && RenderCapability.Tier >> 16 > 0;

    // ── Levels ───────────────────────────────────────────────────────────

    public static Color LevelColor(StampLevel level) => level switch
    {
        StampLevel.Bronze => Color.FromRgb(0xDD, 0xA2, 0x7A),
        StampLevel.Silver => Color.FromRgb(0xD3, 0xDA, 0xE8),
        StampLevel.Gold => Color.FromRgb(0xF2, 0xC7, 0x6E),
        _ => Color.FromRgb(0xC4, 0xB5, 0xFD)
    };

    public static Brush LevelBrush(StampLevel level) => Frozen(LevelColor(level));

    public static string LevelName(StampLevel level) => Loc.I[$"passport.level.{level.Key()}"];

    public static string Country(string code) => ColituServerNames.CountryName(code) ?? code;

    /// <summary>Time on a country's servers: "45 dk", "14 sa".</summary>
    public static string UsedTime(int minutes) =>
        minutes < 60 ? Loc.I.Format("passport.mins", ("n", minutes)) : Loc.I.Format("passport.hours", ("n", minutes / 60));

    // ── Images ───────────────────────────────────────────────────────────

    private static readonly Dictionary<string, BitmapImage?> Images = [];

    public static BitmapImage? Image(string name)
    {
        if (Images.TryGetValue(name, out var cached)) return cached;
        BitmapImage? image = null;
        try
        {
            image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri($"pack://application:,,,/Resources/Passport/{name}", UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"PassportUi.Image {name}", ex);
            image = null;
        }
        Images[name] = image;
        return image;
    }

    public static string StampImageName(string code, StampLevel? level) =>
        $"stamp_{code.ToLowerInvariant()}_{level?.Key() ?? "locked"}.png";

    public static Image Picture(string name, double width, double height = double.NaN)
    {
        var image = new Image { Source = Image(name), Width = width, Stretch = Stretch.Uniform };
        if (!double.IsNaN(height)) image.Height = height;
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    public static Image Cover(CoverTheme theme, double height)
    {
        var image = Picture($"passport_cover_{theme.Key()}.png", double.NaN, height);
        image.Width = height * 720 / 900;
        return image;
    }

    // ── Stamp art ────────────────────────────────────────────────────────

    /// <summary>Fixed order in which the 12 puzzle pieces fill (row-major indices).</summary>
    public static readonly int[] PieceOrder = [4, 0, 11, 6, 2, 9, 7, 1, 10, 3, 8, 5];

    /// <summary>Grid index (row-major) of the <paramref name="filled"/>-th piece of a puzzle.</summary>
    public static int PieceSlot(int filled) => PieceOrder[Math.Clamp(filled - 1, 0, PieceOrder.Length - 1)];

    /// <summary>
    /// One stamp: grey and locked, or in its level's material. <paramref name="puzzle"/> shades the
    /// pieces still missing on the way to the next level (drawn only on the stamp's own pixels);
    /// <paramref name="live"/> adds a slow glint (stronger per level) when animations are on.
    /// </summary>
    public static Grid StampArt(ColituStamp stamp, StampProgress progress, double size, double lockSize = 0, bool puzzle = false, bool live = false, int? piecesOverride = null)
    {
        var level = progress.Level;
        var name = StampImageName(stamp.Code, level);
        var grid = new Grid { Width = size, Height = size };
        var picture = Picture(name, size, size);
        AutomationProperties.SetName(picture, Country(stamp.Code));
        grid.Children.Add(picture);
        var mask = new ImageBrush(Image(name)) { Stretch = Stretch.Uniform };
        mask.Freeze();
        if (puzzle && level != null && !progress.Mastered)
        {
            var canvas = PuzzleOverlay(size, piecesOverride ?? progress.LevelPieces);
            canvas.OpacityMask = mask;
            grid.Children.Add(canvas);
        }
        if (live && level != null && Motion)
        {
            grid.Children.Add(Glint(size, level.Value, mask));
        }
        if (level == null && lockSize > 0)
        {
            grid.Children.Add(LockBadge(lockSize));
        }
        return grid;
    }

    public static Grid LockBadge(double size)
    {
        var badge = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        badge.Children.Add(new Ellipse { Fill = Frozen(Color.FromArgb(0xCC, 0x14, 0x14, 0x1B)), Stroke = Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1 });
        badge.Children.Add(Icon("lock", Text, size * 0.46));
        return badge;
    }

    /// <summary>3 × 4 jigsaw over the stamp; missing pieces shaded.</summary>
    public static Canvas PuzzleOverlay(double size, int filled)
    {
        var canvas = new Canvas { Width = size, Height = size, IsHitTestVisible = false };
        var inset = size * 0.06;
        var w = size - inset * 2;
        var h = size - inset * 2;
        var cw = w / 3;
        var ch = h / 4;
        var done = PieceOrder.Take(filled).ToHashSet();
        var shade = Frozen(Color.FromArgb(0xC7, 0x05, 0x04, 0x0F));
        for (var i = 0; i < StampProgress.PiecesPerLevel; i++)
        {
            if (done.Contains(i)) continue;
            var rect = new Rectangle { Width = cw + 0.5, Height = ch + 0.5, Fill = shade, Tag = i };
            Canvas.SetLeft(rect, inset + i % 3 * cw);
            Canvas.SetTop(rect, inset + i / 3 * ch);
            canvas.Children.Add(rect);
        }
        canvas.Children.Add(new Path { Data = Jigsaw(inset, w, h), Stroke = Frozen(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1.3 });
        return canvas;
    }

    /// <summary>Top-left corner of a puzzle slot, for the flying piece.</summary>
    public static Rect SlotRect(double size, int slot)
    {
        var inset = size * 0.06;
        var cw = (size - inset * 2) / 3;
        var ch = (size - inset * 2) / 4;
        return new Rect(inset + slot % 3 * cw, inset + slot / 3 * ch, cw, ch);
    }

    private static Geometry Jigsaw(double o, double w, double h)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            var cw = w / 3;
            var ch = h / 4;
            var k = Math.Min(cw, ch) * 0.15;
            for (var i = 1; i < 3; i++)
            {
                var x = o + cw * i;
                for (var j = 0; j < 4; j++)
                {
                    var y0 = o + ch * j;
                    var ym = y0 + ch / 2;
                    var d = (i + j) % 2 == 0 ? 1 : -1;
                    g.BeginFigure(new Point(x, y0), false, false);
                    g.LineTo(new Point(x, ym - k), true, true);
                    g.BezierTo(new Point(x + d * k * 2.4, ym - k * 1.9), new Point(x + d * k * 2.4, ym + k * 1.9), new Point(x, ym + k), true, true);
                    g.LineTo(new Point(x, y0 + ch), true, true);
                }
            }
            for (var j = 1; j < 4; j++)
            {
                var y = o + ch * j;
                for (var i = 0; i < 3; i++)
                {
                    var x0 = o + cw * i;
                    var xm = x0 + cw / 2;
                    var d = (i + j) % 2 == 0 ? -1 : 1;
                    g.BeginFigure(new Point(x0, y), false, false);
                    g.LineTo(new Point(xm - k, y), true, true);
                    g.BezierTo(new Point(xm - k * 1.9, y + d * k * 2.4), new Point(xm + k * 1.9, y + d * k * 2.4), new Point(xm + k, y), true, true);
                    g.LineTo(new Point(x0 + cw, y), true, true);
                }
            }
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>A diagonal light that crosses the stamp every few seconds; Diamond the brightest.</summary>
    private static Rectangle Glint(double size, StampLevel level, Brush mask)
    {
        var intensity = level switch { StampLevel.Bronze => 0.35, StampLevel.Silver => 0.55, StampLevel.Gold => 0.75, _ => 1.0 };
        var shine = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1), MappingMode = BrushMappingMode.RelativeToBoundingBox };
        shine.GradientStops.Add(new GradientStop(Colors.Transparent, 0.35));
        shine.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(0x60 * intensity), 0xFF, 0xFF, 0xFF), 0.5));
        shine.GradientStops.Add(new GradientStop(Colors.Transparent, 0.65));
        var move = new TranslateTransform(-size * 1.2, -size * 1.2);
        shine.RelativeTransform = move;
        var rect = new Rectangle { Width = size, Height = size, Fill = shine, OpacityMask = mask, IsHitTestVisible = false };
        var period = level == StampLevel.Diamond ? 5.5 : 8.0;
        var x = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(period) };
        x.KeyFrames.Add(new LinearDoubleKeyFrame(-1.2, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        x.KeyFrames.Add(new EasingDoubleKeyFrame(1.2, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.8)), new SineEase()));
        x.KeyFrames.Add(new LinearDoubleKeyFrame(1.2, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(period))));
        move.BeginAnimation(TranslateTransform.XProperty, x);
        move.BeginAnimation(TranslateTransform.YProperty, x);
        return rect;
    }

    // ── Seals, badges, Companion ─────────────────────────────────────────

    public static FrameworkElement Seal(PassportCollection set, bool earned, double size)
    {
        if (set.Secret && !earned)
        {
            return QuestionMark(size, Dim);
        }
        var image = Picture($"seal_{set.Id}{(earned ? "" : "_grey")}.png", size, size);
        image.Opacity = earned ? 1 : 0.4;
        AutomationProperties.SetName(image, set.Title);
        return image;
    }

    public static FrameworkElement Badge(PassportAchievement a, bool earned, double size)
    {
        if (a.Secret && !earned)
        {
            return QuestionMark(size, Dim);
        }
        var image = Picture($"badge_{a.Id}{(earned ? "" : "_grey")}.png", size, size);
        image.Opacity = earned ? 1 : 0.35;
        AutomationProperties.SetName(image, a.Title);
        return image;
    }

    public static Grid QuestionMark(double size, Brush brush)
    {
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(new Ellipse { Fill = Frozen(Color.FromRgb(0x1C, 0x1C, 0x25)), Stroke = Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1.5 });
        grid.Children.Add(new TextBlock { Text = "?", FontSize = size * 0.4, FontWeight = FontWeights.Bold, Foreground = brush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        return grid;
    }

    /// <summary>The Companion stamp: two linked holders in a perforated lilac frame (no art ships for it).</summary>
    public static Grid CompanionStamp(bool earned, double size)
    {
        var color = earned ? Lilac : Dim;
        var grid = new Grid { Width = size, Height = size, Opacity = earned ? 1 : 0.45 };
        var inset = size * 0.1;
        grid.Children.Add(new Rectangle { Margin = new Thickness(inset), RadiusX = 4, RadiusY = 4, Fill = Frozen(Color.FromRgb(0x1B, 0x18, 0x40)), Stroke = color, StrokeThickness = 3 });
        grid.Children.Add(new Rectangle { Margin = new Thickness(inset + 6), RadiusX = 3, RadiusY = 3, Stroke = color, StrokeThickness = 1, Opacity = 0.5, StrokeDashArray = [3, 2] });
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var people = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        people.Children.Add(Icon("person", color, size * 0.22));
        people.Children.Add(Icon("person", color, size * 0.22));
        column.Children.Add(people);
        column.Children.Add(new TextBlock { Text = "COMPANION", Margin = new Thickness(0, 4, 0, 0), FontSize = Math.Max(8, size * 0.09), FontWeight = FontWeights.SemiBold, Foreground = color, HorizontalAlignment = HorizontalAlignment.Center });
        grid.Children.Add(column);
        return grid;
    }

    // ── Rings and bars ───────────────────────────────────────────────────

    /// <summary>A progress ring (lilac sweep on a faint track) with <paramref name="content"/> in the middle.</summary>
    public static Grid Ring(double fraction, double size, double stroke, UIElement? content)
    {
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(new Ellipse { Stroke = Glass10, StrokeThickness = stroke, Margin = new Thickness(0) });
        var arc = new Path { Stroke = Accent, StrokeThickness = stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        SetArc(arc, size, stroke, fraction);
        grid.Children.Add(arc);
        if (content != null) grid.Children.Add(content);
        return grid;
    }

    public static void SetArc(Path arc, double size, double stroke, double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 0.9999);
        var r = (size - stroke) / 2;
        var c = size / 2;
        if (fraction <= 0)
        {
            arc.Data = Geometry.Empty;
            return;
        }
        var angle = fraction * 2 * Math.PI;
        var end = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        arc.Data = new PathGeometry([figure]);
    }

    /// <summary>A thin rounded progress bar.</summary>
    public static Grid Bar(double fraction, double height, Brush fill, double width = double.NaN)
    {
        var grid = new Grid { Height = height, Width = width };
        grid.Children.Add(new Border { Background = Glass10, CornerRadius = new CornerRadius(height / 2) });
        var bar = new Border { Background = fill, CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = HorizontalAlignment.Left };
        grid.SizeChanged += (_, _) => bar.Width = Math.Max(0, grid.ActualWidth * Math.Clamp(fraction, 0, 1));
        grid.Children.Add(bar);
        return grid;
    }

    /// <summary>Twelve short segments: the level's puzzle as a bar.</summary>
    public static UniformGrid SegmentBar(int filled, Color color)
    {
        var grid = new UniformGrid { Rows = 1, Columns = StampProgress.PiecesPerLevel };
        var on = Frozen(color);
        for (var i = 0; i < StampProgress.PiecesPerLevel; i++)
        {
            grid.Children.Add(new Border { Height = 6, Margin = new Thickness(1.5, 0, 1.5, 0), CornerRadius = new CornerRadius(3), Background = i < filled ? on : Glass10 });
        }
        AutomationProperties.SetName(grid, Loc.I.Format("passport.pieces", ("n", filled)));
        return grid;
    }

    // ── Text and cards ───────────────────────────────────────────────────

    public const double MinFontSize = 12;

    public static TextBlock Label(string text, double size = 14, Brush? brush = null, FontWeight? weight = null, TextAlignment align = TextAlignment.Left, bool wrap = true)
    {
        return new TextBlock
        {
            Text = text,
            // No text under 12 (readability); icons draw their own glyphs.
            FontSize = Math.Max(MinFontSize, size),
            Foreground = brush ?? Text,
            FontWeight = weight ?? FontWeights.Normal,
            TextAlignment = align,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis
        };
    }

    public static TextBlock Kicker(string text, Brush? brush = null, TextAlignment align = TextAlignment.Left) =>
        Label(text.ToUpper(Loc.I.Culture), 11.5, brush ?? Lilac, FontWeights.SemiBold, align, false);

    /// <summary>Soft glass card (lighter than the app's panels), optionally clickable.</summary>
    public static FrameworkElement Glass(UIElement content, Action? onClick = null, string? name = null, Thickness? padding = null)
    {
        if (onClick == null)
        {
            return new Border { Background = GlassFill, BorderBrush = GlassEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22), Padding = padding ?? new Thickness(18), Child = content };
        }
        var button = Plain(content, onClick, name);
        button.Background = GlassFill;
        button.BorderBrush = GlassEdge;
        button.BorderThickness = new Thickness(1);
        button.Padding = padding ?? new Thickness(18);
        return button;
    }

    public static Button Plain(object content, Action onClick, string? name = null)
    {
        var button = new Button { Content = content, Style = (Style)Application.Current.FindResource("PassportPlain") };
        button.Click += (_, _) => onClick();
        if (name != null) AutomationProperties.SetName(button, name);
        return button;
    }

    public static Button Primary(string text, Action onClick, double height = 48)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = text, FontSize = 14.5, TextTrimming = TextTrimming.CharacterEllipsis },
            Height = height,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Style = (Style)Application.Current.FindResource("PrimaryButton"),
            FocusVisualStyle = (Style)Application.Current.FindResource("PassportFocus")
        };
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => onClick();
        return button;
    }

    public static Button Secondary(string text, Action onClick, double height = 46)
    {
        var button = Primary(text, onClick, height);
        button.Style = (Style)Application.Current.FindResource("SecondaryButton");
        return button;
    }

    public static Button Link(string text, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            Style = (Style)Application.Current.FindResource("LinkButton"),
            FocusVisualStyle = (Style)Application.Current.FindResource("PassportFocus")
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>The app's toggle switch with a label; the label names the switch for screen readers.</summary>
    public static Grid SwitchRow(string label, string? body, bool value, Action<bool> onChange)
    {
        var row = new Grid { Margin = new Thickness(0, 6, 0, 6), MinHeight = 40 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Label(label, 14, Text, FontWeights.SemiBold));
        if (body != null) text.Children.Add(Label(body, 12, Muted));
        row.Children.Add(text);
        var toggle = new CheckBox
        {
            IsChecked = value,
            Style = (Style)Application.Current.FindResource("Switch"),
            FocusVisualStyle = (Style)Application.Current.FindResource("PassportFocus"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        AutomationProperties.SetName(toggle, label);
        toggle.Checked += (_, _) => onChange(true);
        toggle.Unchecked += (_, _) => onChange(false);
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);
        return row;
    }

    // ── Icons (24 × 24 strokes, the ColituIcons set of the phones) ───────

    private static readonly Dictionary<string, string> IconData = new()
    {
        ["power"] = "M12 3v8 M6.3 6.7a8 8 0 1 0 11.4 0",
        ["star"] = "M12 3l2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1-4.4-4.3 6.1-.9z",
        ["arrowUp"] = "M12 20V5 M6 11l6-6 6 6",
        ["grid"] = "M4 4h6.5v6.5H4z M13.5 4H20v6.5h-6.5z M4 13.5h6.5V20H4z M13.5 13.5H20V20h-6.5z",
        ["check"] = "M5 12.5l4.5 4.5L19 7.5",
        ["lock"] = "M6 11h12v10H6z M8.5 11V8a3.5 3.5 0 0 1 7 0v3",
        ["send"] = "M21 3L10.5 13.5 M21 3l-6.5 18-4-7.5-7.5-4z",
        ["sealCheck"] = "M12 2.8l2.2 1.6 2.7-.1.8 2.6 2.2 1.6-.9 2.6.9 2.6-2.2 1.6-.8 2.6-2.7-.1L12 21.2l-2.2-1.6-2.7.1-.8-2.6-2.2-1.6.9-2.6-.9-2.6 2.2-1.6.8-2.6 2.7.1z M8.6 12.2l2.3 2.3 4.6-4.8",
        ["globe"] = "M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18z M3 12h18 M12 3c2.5 2.6 3.8 5.6 3.8 9s-1.3 6.4-3.8 9c-2.5-2.6-3.8-5.6-3.8-9s1.3-6.4 3.8-9z",
        ["person"] = "M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8z M4.5 20.5c1.2-3.6 4-5.5 7.5-5.5s6.3 1.9 7.5 5.5",
        ["eye"] = "M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z",
        ["bolt"] = "M13 2L4.5 13.5H12L11 22l8.5-11.5H12z",
        ["shield"] = "M12 2.5L4.5 5.5v6c0 5 3.3 9 7.5 10.5 4.2-1.5 7.5-5.5 7.5-10.5v-6z M8.8 12l2.2 2.2 4.4-4.6",
        ["arrowUpRight"] = "M7 17L17 7 M8 7h9v9",
        ["chevron"] = "M9 5l7 7-7 7",
        ["x"] = "M6 6l12 12 M18 6L6 18"
    };

    public static Path Icon(string name, Brush brush, double size)
    {
        var path = new Path
        {
            Data = Geometry.Parse(IconData.GetValueOrDefault(name, IconData["star"])),
            Stroke = brush,
            StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        return path;
    }

    /// <summary>A round lilac well with an icon (steps, rules).</summary>
    public static Grid IconWell(string icon, double size, Brush? fill = null, Brush? stroke = null)
    {
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(new Ellipse { Fill = fill ?? LilacWell });
        grid.Children.Add(Icon(icon, stroke ?? Lilac, size * 0.45));
        return grid;
    }

    public static string AchievementIcon(PassportAchievement a) => a.Id switch
    {
        "first" or "diamond" => "star",
        "globe" => "globe",
        "nordic_master" => "shield",
        "night" => "eye",
        "early" => "bolt",
        "longhaul" => "power",
        "pathfinder" => "arrowUpRight",
        _ => "sealCheck"
    };

    public static string ExpeditionIcon(ExpeditionGoal goal) => goal.Id switch
    {
        "new_country" => "globe",
        "share" => "send",
        "customize" => "person",
        "days10" => "power",
        "set_stamp" => "grid",
        _ => "star"
    };

    public static Color SealColor(PassportCollection set) => set.Id switch
    {
        "nordic" => Color.FromRgb(0x7F, 0xD6, 0xE0),
        "baltic" => Color.FromRgb(0xE8, 0xB0, 0x67),
        "atlantic" => Color.FromRgb(0x8F, 0xB2, 0xFF),
        "pacific" => Color.FromRgb(0xFF, 0xA9, 0x8A),
        "gold_rush" => Color.FromRgb(0xF2, 0xC7, 0x6E),
        _ => Color.FromRgb(0xC4, 0xB5, 0xFD)
    };

    // ── QR ───────────────────────────────────────────────────────────────

    /// <summary>The QR modules of <paramref name="text"/> (true = dark), quiet zone included.</summary>
    public static bool[,]? QrModules(string text)
    {
        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
            var rows = data.ModuleMatrix;
            var n = rows.Count;
            var modules = new bool[n, n];
            for (var y = 0; y < n; y++)
            {
                for (var x = 0; x < n; x++) modules[x, y] = rows[y][x];
            }
            return modules;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("PassportUi.QrModules", ex);
            return null;
        }
    }

    public static DrawingImage? QrImage(string text)
    {
        if (QrModules(text) is not { } m) return null;
        var n = m.GetLength(0);
        var geometry = new GeometryGroup();
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                if (m[x, y]) geometry.Children.Add(new RectangleGeometry(new Rect(x, y, 1, 1)));
            }
        }
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, n, n))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x16, 0x15, 0x3A)), null, geometry));
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    // ── Sounds ───────────────────────────────────────────────────────────

    private static readonly Dictionary<string, SoundPlayer> Players = [];

    /// <summary>A short passport sound (only while the window shows a reward and sounds are on).</summary>
    public static void Play(string name)
    {
        if (!ColituPassport.Instance.Social.Sound) return;
        try
        {
            if (!Players.TryGetValue(name, out var player))
            {
                var stream = Application.GetResourceStream(new Uri($"pack://application:,,,/Resources/Passport/{name}.wav", UriKind.Absolute))?.Stream;
                if (stream == null) return;
                player = new SoundPlayer(stream);
                player.Load();
                Players[name] = player;
            }
            player.Play();
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"PassportUi.Play {name}", ex);
        }
    }
}
