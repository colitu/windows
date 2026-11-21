using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace v2rayN.Views;

/// <summary>Colour set of the ColituGecko label: Dark for the app's dark surfaces, Light for light ones, Auto picks by the BgColor resource.</summary>
public enum ColituGeckoPalette
{
    Auto,
    Dark,
    Light,
}

/// <summary>
/// The word "ColituGecko" as a brand-gradient text (indigo/violet, or violet/light violet on dark) that
/// slowly slides across the letters, plus an optional breathing dot. Only this label is animated.
/// Animations run only while the label is visible in a non-minimised window, and not at all when Windows
/// "Show animations" is off (static gradient and dot).
/// </summary>
public sealed class ColituGeckoLabel : StackPanel
{
    public static readonly DependencyProperty PaletteProperty = DependencyProperty.Register(
        nameof(Palette), typeof(ColituGeckoPalette), typeof(ColituGeckoLabel),
        new PropertyMetadata(ColituGeckoPalette.Auto, (d, _) => ((ColituGeckoLabel)d).Rebuild()));

    public static readonly DependencyProperty ShowDotProperty = DependencyProperty.Register(
        nameof(ShowDot), typeof(bool), typeof(ColituGeckoLabel),
        new PropertyMetadata(true, (d, _) => ((ColituGeckoLabel)d).Rebuild()));

    /// <summary>Tests: true/false forces reduced motion on/off; null follows <see cref="SystemParameters.ClientAreaAnimation"/>.</summary>
    public static bool? ReducedMotionOverride { get; set; }

    private const double DotSize = 6.5;
    private readonly TextBlock _text = new() { Text = "ColituGecko", VerticalAlignment = VerticalAlignment.Center };
    private readonly Ellipse _dot = new() { Width = DotSize, Height = DotSize, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5) };
    private readonly TranslateTransform _slide = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly List<(IAnimatable Target, DependencyProperty Property, AnimationTimeline Animation)> _animations = new();
    private readonly List<AnimationClock> _clocks = new();
    private bool _loaded;

    public ColituGeckoLabel()
    {
        Orientation = Orientation.Horizontal;
        _dot.RenderTransform = _scale;
        Children.Add(_dot);
        Children.Add(_text);
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        IsVisibleChanged += (_, _) => Refresh();
        Rebuild();
    }

    public ColituGeckoPalette Palette
    {
        get => (ColituGeckoPalette)GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    public bool ShowDot
    {
        get => (bool)GetValue(ShowDotProperty);
        set => SetValue(ShowDotProperty, value);
    }

    /// <summary>The gradient text brush (the word's Foreground).</summary>
    public Brush? TextBrush => _text.Foreground;

    /// <summary>The word itself (tests and automation).</summary>
    public TextBlock Word => _text;

    /// <summary>Current gradient slide (0..1 of one loop); tests.</summary>
    public double SlideOffset => _slide.X;

    /// <summary>True while the storyboard is running (not paused, not stopped).</summary>
    public bool IsAnimating { get; private set; }

    public static bool ReducedMotion => ReducedMotionOverride ?? !SystemParameters.ClientAreaAnimation;

    /// <summary>Darkest stop, light stop and dot colour for a palette (dark = on a dark surface).</summary>
    public static (Color Deep, Color Light, Color Dot) Colors(bool dark) => dark
        ? (Color.FromRgb(0x8F, 0x7C, 0xF0), Color.FromRgb(0xC4, 0xB8, 0xFF), Color.FromRgb(0xB4, 0xA4, 0xFF))
        : (Color.FromRgb(0x26, 0x22, 0x80), Color.FromRgb(0x66, 0x50, 0xCC), Color.FromRgb(0x66, 0x50, 0xCC));

    /// <summary>Freezes the animation at a moment of its loop (tests, screenshots). Needs the label to be loaded.</summary>
    public void SeekTo(TimeSpan time)
    {
        if (!IsAnimating)
        {
            return;
        }
        foreach (var clock in _clocks)
        {
            clock.Controller?.Pause();
            clock.Controller?.Seek(time, TimeSeekOrigin.BeginTime);
        }
        IsAnimating = false;
    }

    private bool IsDark()
    {
        if (Palette != ColituGeckoPalette.Auto)
        {
            return Palette == ColituGeckoPalette.Dark;
        }
        return (TryFindResource("BgColor") is Color bg ? (0.2126 * bg.R + 0.7152 * bg.G + 0.0722 * bg.B) / 255 : 0) < 0.5;
    }

    private void Rebuild()
    {
        var (deep, light, dot) = Colors(IsDark());
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            SpreadMethod = GradientSpreadMethod.Repeat,
            RelativeTransform = _slide,
        };
        brush.GradientStops.Add(new GradientStop(deep, 0));
        brush.GradientStops.Add(new GradientStop(light, 0.5));
        brush.GradientStops.Add(new GradientStop(deep, 1));
        _text.Foreground = brush;
        _dot.Fill = new SolidColorBrush(dot);
        _dot.Visibility = ShowDot ? Visibility.Visible : Visibility.Collapsed;
        BuildStoryboard();
        Refresh();
    }

    // Animations are applied as clocks (a Storyboard cannot resolve the brush's transform), so they can be
    // started, paused and removed together.
    private void BuildStoryboard()
    {
        Stop();
        _animations.Clear();
        _animations.Add((_slide, TranslateTransform.XProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(3.5)) { RepeatBehavior = RepeatBehavior.Forever }));
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        void Breathe(IAnimatable target, DependencyProperty property, double from) =>
            _animations.Add((target, property, new DoubleAnimation(from, 1, TimeSpan.FromSeconds(1)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease }));
        Breathe(_dot, OpacityProperty, 0.45);
        Breathe(_scale, ScaleTransform.ScaleXProperty, 0.85);
        Breathe(_scale, ScaleTransform.ScaleYProperty, 0.85);
    }

    private void Attach()
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        if (Window.GetWindow(this) is { } window)
        {
            window.StateChanged += OnWindowStateChanged;
        }
        Rebuild();
    }

    private void Detach()
    {
        if (!_loaded)
        {
            return;
        }
        _loaded = false;
        SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
        if (Window.GetWindow(this) is { } window)
        {
            window.StateChanged -= OnWindowStateChanged;
        }
        Stop();
    }

    private void OnSystemParameterChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
        {
            Dispatcher.BeginInvoke(Refresh);
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var minimized = Window.GetWindow(this) is { WindowState: WindowState.Minimized };
        if (_loaded && IsVisible && !minimized && !ReducedMotion)
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    private void Start()
    {
        if (_animations.Count == 0 || IsAnimating)
        {
            return;
        }
        foreach (var (target, property, animation) in _animations)
        {
            var clock = animation.CreateClock();
            _clocks.Add(clock);
            target.ApplyAnimationClock(property, clock);
        }
        IsAnimating = true;
    }

    private void Stop()
    {
        if (_clocks.Count > 0)
        {
            foreach (var (target, property, _) in _animations)
            {
                target.ApplyAnimationClock(property, null);
            }
            _clocks.Clear();
        }
        IsAnimating = false;
        _slide.X = 0;
        _scale.ScaleX = _scale.ScaleY = 1;
        _dot.Opacity = 1;
    }
}
