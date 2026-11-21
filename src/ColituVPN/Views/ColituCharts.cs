using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using v2rayN.Services;

namespace v2rayN.Views;

/// <summary>
/// Live speed of the last minute: download as a soft violet area, upload as a lilac line, on a
/// shared scale. Redraws when <see cref="Push"/> is called (once a second while visible).
/// </summary>
public sealed class ColituSpeedGraph : FrameworkElement
{
    private static readonly Brush DownFill = Frozen(new LinearGradientBrush(Color.FromArgb(0x66, 0x9F, 0x8C, 0xFF), Color.FromArgb(0x05, 0x9F, 0x8C, 0xFF), 90));
    private static readonly Pen DownPen = FrozenPen(Color.FromRgb(0x9F, 0x8C, 0xFF), 1.8);
    private static readonly Pen UpPen = FrozenPen(Color.FromRgb(0x5E, 0xE0, 0xA0), 1.6);
    private static readonly Pen GridPen = FrozenPen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 1);

    private ColituSpeedSample[] _samples = [];

    public void Push(IEnumerable<ColituSpeedSample> samples)
    {
        _samples = samples.ToArray();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0)
        {
            return;
        }
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        for (var i = 1; i <= 3; i++)
        {
            var y = Math.Round(h * i / 4) + 0.5;
            dc.DrawLine(GridPen, new Point(0, y), new Point(w, y));
        }
        if (_samples.Length < 2)
        {
            return;
        }
        // Headroom so a steady stream does not touch the top; at least 64 KB/s so idle noise stays low.
        var max = Math.Max(64 * 1024, _samples.Max(sample => Math.Max(sample.Up, sample.Down))) * 1.15;
        var step = w / (ColituTrafficMeter.Capacity - 1);
        var start = w - (_samples.Length - 1) * step;
        Point At(int index, double value) => new(start + index * step, h - value / max * (h - 2) - 1);

        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(start, h), true, true);
            for (var i = 0; i < _samples.Length; i++)
            {
                ctx.LineTo(At(i, _samples[i].Down), true, true);
            }
            ctx.LineTo(new Point(w, h), false, false);
        }
        area.Freeze();
        dc.DrawGeometry(DownFill, null, area);
        dc.DrawGeometry(null, DownPen, Line(_samples.Select(sample => sample.Down).ToArray(), At));
        dc.DrawGeometry(null, UpPen, Line(_samples.Select(sample => sample.Up).ToArray(), At));
    }

    private static StreamGeometry Line(double[] values, Func<int, double, Point> at)
    {
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(at(0, values[0]), false, false);
            for (var i = 1; i < values.Length; i++)
            {
                ctx.LineTo(at(i, values[i]), true, true);
            }
        }
        line.Freeze();
        return line;
    }

    internal static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    internal static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }
}

/// <summary>
/// Daily traffic of the last 30 days as rounded bars (download + upload stacked as one total),
/// today in full violet; the hovered bar shows its day and total in the tooltip.
/// </summary>
public sealed class ColituUsageBars : FrameworkElement
{
    private static readonly Brush Bar = ColituSpeedGraph.Frozen(new LinearGradientBrush(Color.FromRgb(0xB4, 0xA4, 0xFF), Color.FromRgb(0x7C, 0x6C, 0xF0), 90));
    private static readonly Brush BarDim = ColituSpeedGraph.Frozen(new SolidColorBrush(Color.FromArgb(0x59, 0x9F, 0x8C, 0xFF)));
    private static readonly Brush Empty = ColituSpeedGraph.Frozen(new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)));
    private static readonly Pen GridPen = ColituSpeedGraph.FrozenPen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 1);

    private ColituDailyUsage[] _days = [];
    private Func<ColituDailyUsage, string>? _describe;

    public ColituUsageBars()
    {
        ToolTipService.SetInitialShowDelay(this, 0);
        ToolTip = "";
        MouseMove += (_, e) => UpdateTip(e.GetPosition(this).X);
    }

    public void SetDays(IReadOnlyList<ColituDailyUsage> days, Func<ColituDailyUsage, string> describe)
    {
        _days = days.ToArray();
        _describe = describe;
        InvalidateVisual();
    }

    private void UpdateTip(double x)
    {
        if (_days.Length == 0 || _describe == null || ActualWidth <= 0)
        {
            return;
        }
        var index = Math.Clamp((int)(x / (ActualWidth / _days.Length)), 0, _days.Length - 1);
        ToolTip = _describe(_days[index]);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0)
        {
            return;
        }
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        dc.DrawLine(GridPen, new Point(0, h - 0.5), new Point(w, h - 0.5));
        if (_days.Length == 0)
        {
            return;
        }
        var max = Math.Max(1, _days.Max(day => day.Total));
        var slot = w / _days.Length;
        var barWidth = Math.Max(3, slot * 0.62);
        for (var i = 0; i < _days.Length; i++)
        {
            var total = _days[i].Total;
            var x = i * slot + (slot - barWidth) / 2;
            var barHeight = total <= 0 ? 3 : Math.Max(4, total / (double)max * (h - 4));
            var radius = Math.Min(barWidth / 2, 4);
            var brush = total <= 0 ? Empty : i == _days.Length - 1 ? Bar : BarDim;
            dc.DrawRoundedRectangle(brush, null, new Rect(x, h - barHeight, barWidth, barHeight), radius, radius);
        }
    }

    /// <summary>"21 Nov" style short day label from the panel's yyyy-MM-dd.</summary>
    public static string DayLabel(string date, CultureInfo culture) =>
        DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day.ToString("d MMM", culture)
            : date;
}
