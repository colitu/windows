using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AwesomeAssertions;
using v2rayN.Services;
using v2rayN.Views;
using Xunit;

namespace v2rayN.Tests;

/// <summary>
/// Draws the signed-in window from the real ColituMainWindow.xaml with sample data (no services,
/// no network) and checks that every page builds. With COLITU_RENDER_DIR set it saves one PNG per
/// page: Home (connection panel), Locations, Plan (30-day chart), Account (devices), Live support.
/// All addresses are documentation ranges (RFC 5737), never real servers.
/// </summary>
public class ColituUiPreviewTests
{
    private const int W = 1140;
    private const int H = 760;

    [Fact]
    public void SignedInPages_BuildFromTheRealXaml()
    {
        var dir = Environment.GetEnvironmentVariable("COLITU_RENDER_DIR");
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RenderAll(dir);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure.Should().BeNull();
    }

    private static void RenderAll(string? dir)
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // "pack://application:,,,/flags/de.svg" (set by the rows' code) resolves in the app's assembly,
        // not the test host's: point WPF's application resources at ColituVPN for this process.
        // The svgc: namespace (round flags) is only known once SharpVectors is loaded.
        Assembly.Load("SharpVectors.Converters.Wpf");
        typeof(Application).GetField("_resourceAssembly", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, typeof(ColituMainWindow).Assembly);
        if (!app.Resources.MergedDictionaries.Any(dictionary => dictionary.Source?.OriginalString.Contains("ColituTheme") == true))
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/ColituVPN;component/Styles/ColituTheme.xaml", UriKind.Absolute)
            });
        }
        if (!app.Resources.MergedDictionaries.Any(dictionary => dictionary.Source?.OriginalString.Contains("ColituPassport") == true))
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/ColituVPN;component/Styles/ColituPassport.xaml", UriKind.Absolute)
            });
        }

        var language = Environment.GetEnvironmentVariable("COLITU_RENDER_LANG") is { Length: > 0 } lang ? lang : "tr";
        Loc.I.SetLanguage(language);
        try
        {
            var text = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ColituVPN", "Views", "ColituMainWindow.xaml")));
            var handlers = typeof(ColituMainWindow).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(method => method.GetParameters() is [{ ParameterType: var sender }, { ParameterType: var args }] && sender == typeof(object) && typeof(EventArgs).IsAssignableFrom(args))
                .Select(method => method.Name)
                .ToHashSet();

            string Clean(string xaml)
            {
                xaml = Regex.Replace(xaml, @"\{s:T ([\w.]+)\}", match => System.Security.SecurityElement.Escape(Loc.I[match.Groups[1].Value]));
                return Regex.Replace(xaml, @"\s[A-Za-z.]+=""([A-Za-z_]+)""", match => handlers.Contains(match.Groups[1].Value) ? "" : match.Value);
            }
            const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" "
                + "xmlns:local=\"clr-namespace:v2rayN.Views;assembly=ColituVPN\" xmlns:s=\"clr-namespace:v2rayN.Services;assembly=ColituVPN\" "
                + "xmlns:shell=\"clr-namespace:System.Windows.Shell;assembly=PresentationFramework\" xmlns:svgc=\"http://sharpvectors.codeplex.com/svgc/\" ";

            var resStart = text.IndexOf("<ResourceDictionary>", StringComparison.Ordinal);
            var resEnd = text.IndexOf("</Window.Resources>", StringComparison.Ordinal);
            var resources = (ResourceDictionary)XamlReader.Parse(Clean(text[resStart..resEnd].TrimEnd()).Replace("<ResourceDictionary>", $"<ResourceDictionary {Ns}>"));
            // The window's own resources (icons, row templates) must be known before the frame is parsed.
            app.Resources.MergedDictionaries.Add(resources);

            var frameStart = text.IndexOf("<Border x:Name=\"WindowFrame\"", StringComparison.Ordinal);
            var frameEnd = text.LastIndexOf("</Window>", StringComparison.Ordinal);
            var frameXaml = text[frameStart..frameEnd];
            frameXaml = Regex.Replace(frameXaml, @"<tb:TaskbarIcon[\s\S]*?</tb:TaskbarIcon>", "");
            frameXaml = Clean(frameXaml).Replace("\"/Resources/", "\"pack://application:,,,/ColituVPN;component/Resources/").Replace("<Border x:Name=\"WindowFrame\"", $"<Border {Ns} x:Name=\"WindowFrame\"");

            foreach (var page in new[] { "home", "locations", "plan", "account", "support" })
            {
                // Relative image paths ("/Resources/home-bg.jpg") resolve against the app's assembly.
                var frame = (Border)XamlReader.Parse(frameXaml, new ParserContext { BaseUri = new Uri("pack://application:,,,/ColituVPN;component/Views/ColituMainWindow.xaml") });
                frame.FlowDirection = Loc.I.FlowDirection;
                // What the Window sets for everything inside it.
                TextElement.SetForeground(frame, new SolidColorBrush(Color.FromRgb(0xF4, 0xF3, 0xFA)));
                TextElement.SetFontFamily(frame, (FontFamily)Res("ColituFont"));
                Fill(frame, page);
                frame.Measure(new Size(W, H));
                frame.Arrange(new Rect(0, 0, W, H));
                frame.UpdateLayout();
                PlaceNavThumb(frame, page);
                var logo = Find<Image>(frame, "BrandWordmark");
                logo.ActualWidth.Should().BeGreaterThan(40, "the brand wordmark (Resources/logo-wordmark.png) is packed and drawn");
                frame.UpdateLayout();
                if (page == "support")
                {
                    ((ScrollViewer)frame.FindName("SupportScroll")).ScrollToEnd();
                    frame.UpdateLayout();
                }
                if (dir == null)
                {
                    continue;
                }
                var bitmap = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(frame);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(dir, $"windows-{page}-{language}.png"));
                encoder.Save(stream);
            }
        }
        finally
        {
            Loc.I.SetLanguage("tr");
        }
    }

    private static T Find<T>(FrameworkElement root, string name) where T : class => (root.FindName(name) as T)!;

    /// <summary>
    /// The rows' flags are "pack://application:,,,/flags/xx.svg", the app's own resources. A test
    /// process may already have tied "application" to the test host, so name the assembly.
    /// </summary>
    private static ColituServerRow Pinned(ColituServerRow row)
    {
        foreach (var name in new[] { "FlagUri", "EntryFlagUri", "ExitFlagUri" })
        {
            var property = typeof(ColituServerRow).GetProperty(name)!;
            if (property.GetValue(row) is Uri uri)
            {
                property.SetValue(row, new Uri(uri.OriginalString.Replace("application:,,,/flags/", "application:,,,/ColituVPN;component/flags/")));
            }
        }
        return row;
    }

    private static void Show(FrameworkElement root, string name, bool visible = true) =>
        Find<UIElement>(root, name).Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private static void Text(FrameworkElement root, string name, string value) => Find<TextBlock>(root, name).Text = value;

    private static object Res(object key) => Application.Current.TryFindResource(key) ?? throw new KeyNotFoundException(key.ToString());

    private static void Fill(Border frame, string page)
    {
        var loc = Loc.I;
        Show(frame, "LoadingView", false);
        Show(frame, "AppView");
        var pages = Find<Grid>(frame, "Pages");
        var target = page switch { "locations" => "LocationsPage", "plan" => "PlanPage", "account" => "AccountPage", "support" => "SupportPage", _ => "HomePage" };
        foreach (FrameworkElement child in pages.Children)
        {
            child.Visibility = child.Name == target ? Visibility.Visible : Visibility.Collapsed;
        }
        Show(frame, "HomeBackdrop", page == "home");
        Show(frame, "NavSupport");
        Show(frame, "NavSupportDot");
        Text(frame, "NavSupportCount", "2");
        Text(frame, "StatusChipText", loc["status.protected"]);
        Find<System.Windows.Shapes.Ellipse>(frame, "StatusDot").Fill = (Brush)Res("SuccessBrush");

        var frankfurt = new ColituVpnServer { Id = "de1", Name = "Almanya", CountryCode = "DE", City = "Frankfurt", Available = true, Online = true, Protocols = ["hysteria2", "vless-reality"] };
        switch (page)
        {
            case "home":
                FillHome(frame, frankfurt);
                break;
            case "locations":
                FillLocations(frame, frankfurt);
                break;
            case "plan":
                FillPlan(frame);
                break;
            case "account":
                FillAccount(frame);
                break;
            case "support":
                FillSupport(frame);
                break;
        }
    }

    private static void FillHome(Border frame, ColituVpnServer server)
    {
        var loc = Loc.I;
        Text(frame, "ConnectLabel", loc["home.tapOff"]);
        Text(frame, "HomeTitle", loc["home.title.on"]);
        Text(frame, "HomeSubtitle", loc.Format("home.sub.on", ("server", "Almanya · Frankfurt")));
        Show(frame, "PowerIcon", false);
        Show(frame, "SessionTimer");
        Text(frame, "SessionTimer", "00:12:47");
        Show(frame, "ProtocolChip");
        Text(frame, "ProtocolChipText", $"{loc["home.protocol"]} · {ColituTransportNames.Of("hysteria2", loc)} ▾".ToUpper(loc.Culture));
        var row = Pinned(ColituServerRow.From(server, 38));
        Find<ContentPresenter>(frame, "HomeFlag").Content = row;
        Text(frame, "HomeServerTitle", row.Title);
        Text(frame, "HomeServerSubtitle", row.Subtitle);

        Show(frame, "ConnInfoCard");
        Text(frame, "ConnCountryText", $"{ColituServerRow.CountryName("DE")} (DE)");
        Text(frame, "ConnProtocolText", ColituTransportNames.Of("hysteria2", loc));
        Text(frame, "ConnPingText", "38 ms");
        var samples = Enumerable.Range(0, 60).Select(i => new ColituSpeedSample(
            40_000 + 25_000 * Math.Sin(i / 4.0) + (i % 7) * 3000,
            900_000 + 650_000 * Math.Sin(i / 6.0 + 1) + (i % 5) * 60_000)).ToList();
        Find<ColituSpeedGraph>(frame, "ConnGraph").Push(samples);
        Text(frame, "ConnDownText", loc.Format("conn.down", ("v", ColituTrafficMeter.Format(samples[^1].Down, loc.Language))));
        Text(frame, "ConnUpText", loc.Format("conn.up", ("v", ColituTrafficMeter.Format(samples[^1].Up, loc.Language))));
        Text(frame, "ConnGraphNote", loc["conn.speedTunnel"]);
        var leak = Find<StackPanel>(frame, "LeakResults");
        leak.Visibility = Visibility.Visible;
        var country = $"{ColituServerRow.CountryName("DE")} (DE)";
        leak.Children.Add(ColituMainWindow.LeakLineCore(ColituLeakState.Ok, loc.Format("conn.leak.ipOk", ("ip", "203.0.113.24"), ("country", country)), Res));
        leak.Children.Add(ColituMainWindow.LeakLineCore(ColituLeakState.Ok, loc.Format("conn.leak.dnsOk", ("ip", "203.0.113.53"), ("country", country)), Res));

        Text(frame, "PlanName", "Premium");
        Text(frame, "PlanDetail", "5 cihaz · sınırsız trafik");
        Text(frame, "PlanBadgeText", loc["plan.status.active"]);
        Find<TextBlock>(frame, "PlanBadgeText").Foreground = (Brush)Res("OnAccentBrush");
        Show(frame, "TrafficBlock", false);
        Text(frame, "DevicesText", "2 / 5");
        Show(frame, "PassportHomeCard", false);
    }

    private static void FillLocations(Border frame, ColituVpnServer frankfurt)
    {
        var loc = Loc.I;
        Text(frame, "LocationsSubtitle", loc.Format("locations.sub", ("n", 37)));
        Show(frame, "CategoryHost", false);
        var whitelist = new ColituVpnServer { Id = "fi-wl", Name = "Finlandiya", CountryCode = "FI", City = "Helsinki", Available = true, WhitelistOk = true, Protocols = ["vless-ws"] };
        var cities = new[] { ("New York", 96), ("Ashburn", 104), ("Dallas", 131), ("Los Angeles", 162) }
            .Select((city, i) => new ColituVpnServer { Id = $"us{i}", Name = "ABD", CountryCode = "US", City = city.Item1, Available = true, Online = true, Protocols = ["vless-reality"] })
            .ToList();
        var rows = new List<ColituServerRow>
        {
            ColituServerRow.WhitelistWithPing(whitelist, 57),
            ColituServerRow.Auto("Almanya · Frankfurt"),
            ColituServerRow.Header(loc["locations.favorites"]),
            ColituServerRow.From(frankfurt, 38),
            ColituServerRow.Header(loc["locations.all"]),
            ColituServerRow.From(new ColituVpnServer { Id = "nl", Name = "Hollanda", CountryCode = "NL", City = "Amsterdam", Available = true, Online = true, Protocols = ["trojan"] }, 44),
            ColituServerRow.Group(new ColituServerGroup { Key = "US", Name = "ABD", CountryCode = "US", Servers = cities }, 96, true),
        };
        rows.AddRange(cities.Select((city, i) => ColituServerRow.From(city, new[] { 96, 104, 131, 162 }[i], city: true)));
        rows.Add(ColituServerRow.From(new ColituVpnServer { Id = "se", Name = "İsveç", CountryCode = "SE", City = "Stockholm", Available = true, Online = true, Protocols = ["hysteria2"] }, 61));
        rows[3].MarkState(true, true);
        rows[3].IsFavorite = true;
        rows[1].MarkState(false, false);
        Find<ListBox>(frame, "ServerList").ItemsSource = rows.Select(Pinned).ToList();
    }

    private static void FillPlan(Border frame)
    {
        var loc = Loc.I;
        Text(frame, "CurrentPlanText", "Premium · 5 cihaz");
        Text(frame, "CurrentPlanBadgeText", loc["plan.status.active"]);
        Find<TextBlock>(frame, "CurrentPlanBadgeText").Foreground = (Brush)Res("OnAccentBrush");
        Find<Border>(frame, "CurrentPlanBadge").Background = new LinearGradientBrush(Color.FromRgb(0xB4, 0xA4, 0xFF), Color.FromRgb(0x9F, 0x8C, 0xFF), 0);
        var start = new DateTime(2026, 10, 23);
        var days = Enumerable.Range(0, 30).Select(i => new ColituDailyUsage
        {
            Date = start.AddDays(i).ToString("yyyy-MM-dd"),
            DownloadBytes = i is 4 or 11 ? 0 : (long)((1.2 + Math.Abs(Math.Sin(i * 0.7)) * 3.4 + (i % 6 == 5 ? 4 : 0)) * 1024 * 1024 * 1024),
            UploadBytes = i is 4 or 11 ? 0 : (long)((0.08 + Math.Abs(Math.Cos(i)) * 0.2) * 1024 * 1024 * 1024)
        }).ToList();
        string Gb(long bytes) => $"{bytes / 1024d / 1024 / 1024:0.0} GB";
        var culture = loc.Culture;
        Show(frame, "UsageHistoryCard");
        Text(frame, "UsageHistoryTotal", Gb(days.Sum(day => day.Total)));
        var peak = days.MaxBy(day => day.Total)!;
        Text(frame, "UsageHistoryPeak", loc.Format("usage.peak", ("day", ColituUsageBars.DayLabel(peak.Date, culture)), ("total", Gb(peak.Total))));
        Text(frame, "UsageFirstDay", ColituUsageBars.DayLabel(days[0].Date, culture));
        Find<ColituUsageBars>(frame, "UsageBars").SetDays(days, day => Gb(day.Total));
        Show(frame, "PricingLoading", false);
    }

    private static void FillAccount(Border frame)
    {
        var loc = Loc.I;
        Text(frame, "AvatarText", "Ö");
        Text(frame, "AccountEmail", "ornek@example.com");
        Text(frame, "AccountId", "ID · 7f3a…c21e");
        Text(frame, "AccountPlan", "Premium");
        Text(frame, "AccountPlanBadgeText", loc["plan.status.active"]);
        Find<TextBlock>(frame, "AccountPlanBadgeText").Foreground = (Brush)Res("OnAccentBrush");
        Text(frame, "AccountUntil", "21 Aralık 2026");
        Text(frame, "DevicesTitle", loc.Format("account.devicesTitle", ("used", 4), ("limit", 5)));
        Show(frame, "CleanupDevicesButton");
        Text(frame, "CleanupDevicesText", loc.Format("account.cleanupCount", ("n", 2)));
        Find<ItemsControl>(frame, "DeviceList").ItemsSource = new[]
        {
            new ColituDeviceRow { Id = "1", Name = "Ofis bilgisayarı", IsCurrent = true, Detail = "Windows · bugün" },
            new ColituDeviceRow { Id = "2", Name = "Pixel 8", Detail = "Android · 2 gün önce" },
            new ColituDeviceRow { Id = "3", Name = "Eski dizüstü", Detail = "Windows · 12 Eylül 2026" },
            new ColituDeviceRow { Id = "4", Name = "iPad", Detail = "iOS · 3 Ağustos 2026" },
        };
    }

    private static void FillSupport(Border frame)
    {
        var loc = Loc.I;
        Find<ListBox>(frame, "SupportList").ItemsSource = new[]
        {
            new ColituSupportRow { Id = "a", Title = "Bağlantı sabah kopuyor", Preview = "Günlükleri aldık, Frankfurt…", TimeText = "14:06", Unread = 2, StatusText = "Açık" },
            new ColituSupportRow { Id = "b", Title = "Fatura sorusu", Preview = "Teşekkürler, çözüldü.", TimeText = "Dün", StatusText = "Çözüldü" },
        };
        foreach (var name in new[] { "SupportPlaceholder", "SupportNewForm", "SupportChat" })
        {
            if (frame.FindName(name) is UIElement pane)
            {
                pane.Visibility = Visibility.Collapsed;
            }
        }
        Show(frame, "SupportThread");
        Text(frame, "SupportThreadTitle", "Bağlantı sabah kopuyor");
        Text(frame, "SupportThreadStatusText", "Açık");
        var today = new DateTimeOffset(2026, 11, 21, 13, 52, 0, TimeSpan.FromHours(3));
        var messages = new[]
        {
            new ColituSupportMessage { Id = "1", Sender = "user", Body = "Merhaba, her sabah 9 civarında bağlantı kopuyor, tekrar bağlanınca düzeliyor.", CreatedAt = today, ReadAt = today.AddMinutes(3) },
            new ColituSupportMessage { Id = "2", Sender = "admin", AdminName = "Deniz", Body = "Merhaba! Günlükleri aldık. Frankfurt sunucusunda sabah bakım penceresi var; bu saatlerde Amsterdam'ı deneyebilir misiniz?", CreatedAt = today.AddMinutes(9) },
            new ColituSupportMessage { Id = "3", Sender = "user", Body = "Tamam, yarın sabah deneyip yazarım. Ekran görüntüsünü de ekledim.", CreatedAt = today.AddMinutes(14) },
        };
        var host = Find<StackPanel>(frame, "SupportMessages");
        host.Children.Add(new TextBlock { Text = today.ToString("D", loc.Culture), Margin = new Thickness(0, 10, 0, 12), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12, Foreground = (Brush)Res("DimBrush") });
        foreach (var message in messages)
        {
            host.Children.Add(ColituMainWindow.BuildBubbleCore(message, message.CreatedAt, Res, null));
        }
        Show(frame, "SupportTypingText");
    }

    /// <summary>The lavender pill behind the active tab (MoveNavThumb in the window).</summary>
    private static void PlaceNavThumb(Border frame, string page)
    {
        var name = page switch { "locations" => "NavLocations", "plan" => "NavPlan", "account" => "NavAccount", "support" => "NavSupport", _ => "NavHome" };
        // FitNav narrows the tabs when all six do not fit next to the logo and the status chip.
        foreach (var tab in Find<StackPanel>(frame, "NavList").Children.OfType<RadioButton>())
        {
            tab.Padding = new Thickness(15, 0, 15, 0);
        }
        frame.UpdateLayout();
        var nav = Find<RadioButton>(frame, name);
        nav.IsChecked = true;
        var list = Find<StackPanel>(frame, "NavList");
        var thumb = Find<Border>(frame, "NavThumb");
        thumb.Width = nav.ActualWidth;
        thumb.Height = nav.ActualHeight;
        ((TranslateTransform)thumb.RenderTransform).X = nav.TranslatePoint(new Point(0, 0), list).X;
    }
}
