using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using v2rayN.Views;
using Xunit;

namespace v2rayN.Tests;

/// <summary>ColituGecko brand-gradient label: gradient only on the Gecko word, reduced motion, contrast, rendered proof.</summary>
public class ColituGeckoLabelTests
{
    private const string ShotDir = @"F:\DEV\wt\gecko-ui-shots";

    // ── helpers ──────────────────────────────────────────────────────────
    private static void Sta(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
        {
            throw new InvalidOperationException(error.Message, error);
        }
    }

    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static double Lum(Color c)
    {
        static double F(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * F(c.R) + 0.7152 * F(c.G) + 0.0722 * F(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        var (hi, lo) = (Math.Max(Lum(a), Lum(b)), Math.Min(Lum(a), Lum(b)));
        return (hi + 0.05) / (lo + 0.05);
    }

    private static Window Host(UIElement content, Color background, double width)
    {
        return new Window
        {
            Width = width,
            Height = 100,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -3000,
            Top = 0,
            SizeToContent = SizeToContent.Height,
            Background = new SolidColorBrush(background),
            Content = content,
        };
    }

    private static void Save(FrameworkElement element, string file)
    {
        element.UpdateLayout();
        var w = (int)Math.Ceiling(element.ActualWidth);
        var h = (int)Math.Ceiling(element.ActualHeight);
        var bmp = new RenderTargetBitmap(w * 3, h * 3, 288, 288, PixelFormats.Pbgra32);
        bmp.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(ShotDir);
        using var stream = File.Create(Path.Combine(ShotDir, file));
        encoder.Save(stream);
    }

    private static TextBlock Plain(string text, Brush foreground, double size, FontWeight weight, FontFamily? font) => new()
    {
        Text = text,
        Foreground = foreground,
        FontSize = size,
        FontWeight = weight,
        VerticalAlignment = VerticalAlignment.Center,
        FontFamily = font ?? new FontFamily("Segoe UI"),
    };

    private static FontFamily? BrandFont()
    {
        try
        {
            return (FontFamily)Application.Current.FindResource("ColituFont");
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureApp()
    {
        if (Application.Current == null)
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        }
        if (!Application.Current!.Resources.Contains("ColituFont"))
        {
            try
            {
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/ColituVPN;component/Styles/ColituBrand.xaml"),
                });
            }
            catch
            {
                // The font falls back to Segoe UI for the proof shots.
            }
            Application.Current.Resources["ColituFont"] = new FontFamily(
                new Uri("pack://application:,,,/ColituVPN;component/Resources/Fonts/"), "./#Colitu Sans");
        }
    }

    /// <summary>Settings row (title + hint) or home chip, on the app's dark card or a light surface.</summary>
    private static (FrameworkElement Root, ColituGeckoLabel Label, Window Host) Build(bool chip, bool dark)
    {
        var font = BrandFont();
        var text = new SolidColorBrush(dark ? Color.FromRgb(0xF4, 0xF3, 0xFA) : Color.FromRgb(0x16, 0x15, 0x3A));
        var muted = new SolidColorBrush(dark ? Color.FromRgb(0x9A, 0x9A, 0xAB) : Color.FromRgb(0x5B, 0x5F, 0x72));
        var label = new ColituGeckoLabel { Palette = dark ? ColituGeckoPalette.Dark : ColituGeckoPalette.Light };
        FrameworkElement root;
        if (chip)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            System.Windows.Documents.TextElement.SetFontSize(line, 12);
            System.Windows.Documents.TextElement.SetFontWeight(line, FontWeights.Bold);
            System.Windows.Documents.TextElement.SetFontFamily(line, font ?? new FontFamily("Segoe UI"));
            line.Children.Add(Plain("PROTOKOL · ", text, 12, FontWeights.Bold, font));
            line.Children.Add(label);
            line.Children.Add(Plain(" ▾", text, 12, FontWeights.Bold, font));
            root = new Border
            {
                Margin = new Thickness(24),
                HorizontalAlignment = HorizontalAlignment.Center,
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(14, 7, 14, 7),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(dark ? Color.FromRgb(0x1C, 0x1C, 0x25) : Colors.White),
                BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(0x14, 255, 255, 255) : Color.FromRgb(0xE3, 0xE6, 0xEC)),
                Child = line,
            };
        }
        else
        {
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            System.Windows.Documents.TextElement.SetFontSize(title, 14.5);
            System.Windows.Documents.TextElement.SetFontWeight(title, FontWeights.Medium);
            System.Windows.Documents.TextElement.SetFontFamily(title, font ?? new FontFamily("Segoe UI"));
            title.Children.Add(label);
            var hint = Plain("TCP için yeni Colitu aktarımı; UDP her zamanki gibi gider.", muted, 12.5, FontWeights.Normal, font);
            hint.Margin = new Thickness(0, 3, 0, 0);
            root = new StackPanel { Margin = new Thickness(24), Width = 420 };
            ((StackPanel)root).Children.Add(title);
            ((StackPanel)root).Children.Add(hint);
        }
        var window = Host(root, dark ? Color.FromRgb(0x19, 0x19, 0x21) : Color.FromRgb(0xF4, 0xF7, 0xF7), 480);
        return (root, label, window);
    }

    // ── tests ────────────────────────────────────────────────────────────
    [Fact]
    public void Gradient_brush_is_only_on_the_gecko_word()
    {
        Sta(() =>
        {
            ColituGeckoLabel.ReducedMotionOverride = true;
            try
            {
                var (root, label, window) = Build(chip: false, dark: true);
                window.Show();
                var brush = Assert.IsType<LinearGradientBrush>(label.TextBrush);
                Assert.Equal(3, brush.GradientStops.Count);
                Assert.Equal(ColituGeckoLabel.Colors(true).Deep, brush.GradientStops[0].Color);
                Assert.Equal(ColituGeckoLabel.Colors(true).Light, brush.GradientStops[1].Color);
                Assert.Equal("ColituGecko", label.Word.Text);
                var others = ((StackPanel)root).Children.OfType<StackPanel>().SelectMany(p => p.Children.OfType<TextBlock>()).Concat(((StackPanel)root).Children.OfType<TextBlock>());
                Assert.All(others, t => Assert.IsNotType<LinearGradientBrush>(t.Foreground));
                window.Close();

                var light = new ColituGeckoLabel { Palette = ColituGeckoPalette.Light };
                var lightBrush = Assert.IsType<LinearGradientBrush>(light.TextBrush);
                Assert.Equal(Color.FromRgb(0x26, 0x22, 0x80), lightBrush.GradientStops[0].Color);
                Assert.Equal(Color.FromRgb(0x66, 0x50, 0xCC), lightBrush.GradientStops[1].Color);
                Assert.Equal(lightBrush.GradientStops[0].Color, lightBrush.GradientStops[2].Color);
            }
            finally
            {
                ColituGeckoLabel.ReducedMotionOverride = null;
            }
        });
    }

    [Fact]
    public void Reduced_motion_runs_no_storyboard_and_normal_motion_does()
    {
        Sta(() =>
        {
            try
            {
                ColituGeckoLabel.ReducedMotionOverride = true;
                var (_, still, w1) = Build(chip: true, dark: true);
                w1.Show();
                Pump(150);
                Assert.False(still.IsAnimating);
                w1.Close();

                ColituGeckoLabel.ReducedMotionOverride = false;
                var (_, moving, w2) = Build(chip: true, dark: true);
                w2.Show();
                Pump(150);
                Assert.True(moving.IsAnimating);
                w2.WindowState = WindowState.Minimized;
                Pump(150);
                Assert.False(moving.IsAnimating);
                w2.WindowState = WindowState.Normal;
                Pump(150);
                Assert.True(moving.IsAnimating);
                moving.Visibility = Visibility.Collapsed;
                Pump(150);
                Assert.False(moving.IsAnimating);
                w2.Close();
            }
            finally
            {
                ColituGeckoLabel.ReducedMotionOverride = null;
            }
        });
    }

    [Fact]
    public void Dark_palette_text_has_at_least_4_5_contrast_on_the_app_surfaces()
    {
        var (deep, light, _) = ColituGeckoLabel.Colors(true);
        foreach (var surface in new[] { Color.FromRgb(0x1C, 0x1C, 0x25), Color.FromRgb(0x19, 0x19, 0x21), Color.FromRgb(0x0A, 0x0A, 0x0E) })
        {
            Assert.True(Contrast(deep, surface) >= 4.5, $"deep stop on {surface}: {Contrast(deep, surface):0.00}");
            Assert.True(Contrast(light, surface) >= 4.5);
        }
        var (ldeep, llight, _) = ColituGeckoLabel.Colors(false);
        var paper = Color.FromRgb(0xF4, 0xF7, 0xF7);
        Assert.True(Contrast(ldeep, paper) >= 4.5);
        Assert.True(Contrast(llight, paper) >= 4.5);
    }

    [Fact]
    public void Render_proof_shots_light_and_dark_at_two_phases()
    {
        Sta(() =>
        {
            EnsureApp();
            try
            {
                ColituGeckoLabel.ReducedMotionOverride = false;
                foreach (var dark in new[] { true, false })
                {
                    foreach (var chip in new[] { false, true })
                    {
                        foreach (var phase in new[] { 0.0, 1.75 })
                        {
                            var (root, label, window) = Build(chip, dark);
                            window.Show();
                            Pump(200);
                            Assert.True(label.IsAnimating);
                            label.SeekTo(TimeSpan.FromSeconds(phase));
                            Pump(250);
                            var name = $"gecko-{(chip ? "chip" : "settings")}-{(dark ? "dark" : "light")}-{phase:0.00}s.png".Replace(',', '.');
                            Assert.True(Math.Abs(label.SlideOffset - phase / 3.5) < 0.02, $"slide={label.SlideOffset} dot={label.Children[0].Opacity} phase={phase}");
                            Save(window, name);
                            Assert.True(new FileInfo(Path.Combine(ShotDir, name)).Length > 500);
                            window.Close();
                        }
                    }
                }
            }
            finally
            {
                ColituGeckoLabel.ReducedMotionOverride = null;
            }
        });
    }
}
