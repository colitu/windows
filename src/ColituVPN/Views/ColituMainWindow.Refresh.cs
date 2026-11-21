using System.Net;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using v2rayN.Services;

namespace v2rayN.Views;

/// <summary>
/// The 3.x interface refresh: connection panel with live speed and the leak test, per-row pings,
/// the animated plan badge, device cleanup / rename, the 30-day usage chart and the support chat's
/// typing indicator, read receipts, drag and drop and tray notification.
/// </summary>
public partial class ColituMainWindow
{
    // ── Connection panel ───────────────────────────────────────────────────
    private readonly ColituTrafficMeter _meter = new();
    private DispatcherTimer? _meterTimer;
    private string? _exitFor;
    private bool _leakBusy;

    /// <summary>Shows the panel while connected; a new server or protocol asks for the exit IP again.</summary>
    private void ApplyConnectionInfo()
    {
        if (ConnInfoCard == null)
        {
            return;
        }
        var on = _vpn.Status == ColituVpnStatus.Connected;
        ConnInfoCard.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on)
        {
            _meterTimer?.Stop();
            _meter.Reset();
            ConnGraph.Push([]);
            _exitFor = null;
            LeakResults.Children.Clear();
            LeakResults.Visibility = Visibility.Collapsed;
            return;
        }

        var server = _vpn.ConnectedServer;
        var protocol = _vpn.ConnectedProtocol;
        ConnProtocolText.Text = _vpn.WhitelistSelected
            ? Loc.I["transport.vless-ws"]
            : protocol is { Length: > 0 } ? ColituTransportNames.Of(protocol, Loc.I) : "—";
        var ping = server?.Id is { } id ? _vpn.DisplayPing(id) : null;
        ConnPingText.Text = ping is > 0 ? $"{ping} ms" : "— ms";

        var key = $"{server?.Id}|{protocol}|{_vpn.WhitelistSelected}";
        if (key != _exitFor)
        {
            _exitFor = key;
            ConnCountryText.Text = "…";
            LeakResults.Children.Clear();
            LeakResults.Visibility = Visibility.Collapsed;
            _ = LoadExitAsync(key);
        }

        if (_meterTimer == null)
        {
            _meterTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _meterTimer.Tick += (_, _) => TickMeter();
        }
        if (!_meterTimer.IsEnabled)
        {
            _meter.Reset();
            _meterTimer.Start();
        }
    }

    /// <summary>One second of the speed graph; reads the counters only while Home is on screen.</summary>
    private void TickMeter()
    {
        if (_vpn.Status != ColituVpnStatus.Connected)
        {
            _meterTimer?.Stop();
            return;
        }
        if (_page != "home" || !IsVisible || WindowState == WindowState.Minimized)
        {
            // Hidden: the next reading starts a fresh baseline instead of averaging the gap.
            _meter.Reset();
            return;
        }
        _meter.Tick();
        var language = Loc.I.Language;
        ConnDownText.Text = Loc.I.Format("conn.down", ("v", ColituTrafficMeter.Format(_meter.Latest.Down, language)));
        ConnUpText.Text = Loc.I.Format("conn.up", ("v", ColituTrafficMeter.Format(_meter.Latest.Up, language)));
        ConnGraphNote.Text = Loc.I[_meter.TunnelOnly ? "conn.speedTunnel" : "conn.speedPc"];
        ConnGraph.Push(_meter.Samples);
    }

    private async Task LoadExitAsync(string key)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var (port, credentials) = _vpn.TunnelProxy();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var exit = await ColituConnectionCheck.ExitAsync(port, credentials, cts.Token);
                if (key != _exitFor)
                {
                    return;
                }
                ShowExit(exit);
                return;
            }
            catch (Exception ex)
            {
                Logging.SaveLog($"ColituMainWindow.LoadExitAsync: {ex.GetType().Name}");
            }
            // Right after a connect the route may still settle: one more try a little later.
            await Task.Delay(TimeSpan.FromSeconds(4));
            if (key != _exitFor)
            {
                return;
            }
        }
        ConnCountryText.Text = Loc.I["conn.unknown"];
    }

    private void ShowExit(ColituExitInfo? exit)
    {
        // The exit IP itself is not shown (owner 2026-11-21): only where the tunnel leaves.
        ConnCountryText.Text = exit == null ? Loc.I["conn.unknown"] : CountryLabel(exit.CountryCode);
    }

    private static string CountryLabel(string? code) =>
        code == null ? "—" : ColituServerRow.CountryName(code) is { Length: > 0 } name ? $"{name} ({code})" : code;

    private async void LeakTest_Click(object sender, RoutedEventArgs e)
    {
        if (_leakBusy || _vpn.Status != ColituVpnStatus.Connected)
        {
            return;
        }
        _leakBusy = true;
        LeakTestButton.IsEnabled = false;
        LeakTestSpinner.Visibility = Visibility.Visible;
        LeakResults.Children.Clear();
        LeakResults.Visibility = Visibility.Visible;
        LeakResults.Children.Add(LeakLine(ColituLeakState.Unknown, Loc.I["conn.checking"]));
        var key = _exitFor;
        try
        {
            var (port, credentials) = _vpn.TunnelProxy();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            // The Beyaz Liste way in leaves through its own exit, not the node's country: no country check.
            var serverCountry = _vpn.WhitelistSelected ? null : _vpn.ConnectedServer?.CountryCode;
            var report = await ColituConnectionCheck.LeakTestAsync(port, credentials, serverCountry, cts.Token);
            if (key != _exitFor)
            {
                return;
            }
            LeakResults.Children.Clear();
            if (report.Exit == null)
            {
                LeakResults.Children.Add(LeakLine(ColituLeakState.Warn, Loc.I["conn.leak.failed"]));
                return;
            }
            ShowExit(report.Exit);
            var country = CountryLabel(report.Exit.CountryCode);
            LeakResults.Children.Add(LeakLine(report.IpState, Loc.I.Format(report.IpState == ColituLeakState.Warn ? "conn.leak.ipWarn" : "conn.leak.ipOk",
                ("ip", report.Exit.Ip), ("country", country))));
            var dns = report.DnsState switch
            {
                ColituLeakState.Ok => Loc.I.Format("conn.leak.dnsOk", ("ip", report.ResolverIp ?? "—"), ("country", CountryLabel(report.ResolverCountry))),
                ColituLeakState.Warn => Loc.I.Format("conn.leak.dnsWarn", ("ip", report.ResolverIp ?? "—"), ("country", CountryLabel(report.ResolverCountry))),
                _ => report.ResolverIp == null ? Loc.I["conn.leak.failed"] : Loc.I.Format("conn.leak.dnsUnknown", ("ip", report.ResolverIp))
            };
            LeakResults.Children.Add(LeakLine(report.DnsState, dns));
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"ColituMainWindow.LeakTest: {ex.GetType().Name}");
            LeakResults.Children.Clear();
            LeakResults.Children.Add(LeakLine(ColituLeakState.Warn, Loc.I["conn.leak.failed"]));
        }
        finally
        {
            _leakBusy = false;
            LeakTestButton.IsEnabled = true;
            LeakTestSpinner.Visibility = Visibility.Collapsed;
        }
    }

    private FrameworkElement LeakLine(ColituLeakState state, string text) => LeakLineCore(state, text, FindResource);

    /// <summary>One line of the leak test result: a round ✓ / ! / • mark and the sentence.</summary>
    internal static FrameworkElement LeakLineCore(ColituLeakState state, string text, Func<object, object> resource)
    {
        var (glyph, brushKey) = state switch
        {
            ColituLeakState.Ok => ("✓", "SuccessBrush"),
            ColituLeakState.Warn => ("!", "WarningBrush"),
            _ => ("•", "MutedBrush")
        };
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var brush = (Brush)resource(brushKey);
        var badge = new Border
        {
            Width = 20,
            Height = 20,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(10),
            BorderBrush = brush,
            BorderThickness = new Thickness(1.4),
            Child = new TextBlock { Text = glyph, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = brush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        var label = new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)resource("TextBrush") };
        Grid.SetColumn(label, 1);
        grid.Children.Add(badge);
        grid.Children.Add(label);
        System.Windows.Automation.AutomationProperties.SetName(grid, text);
        return grid;
    }

    // ── Keyboard ───────────────────────────────────────────────────────────
    /// <summary>
    /// Cards and chips that open something on click (location card, protocol chip, …) are
    /// focusable with Tab; Enter or Space does what the click does.
    /// </summary>
    private void ClickableBorder_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) || sender is not UIElement element)
        {
            return;
        }
        e.Handled = true;
        element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonUpEvent,
            Source = element
        });
    }

    // ── Locations: pings and the country click ─────────────────────────────
    private async void PingAll_Click(object sender, RoutedEventArgs e)
    {
        if (_measuringPings)
        {
            return;
        }
        PingAllButton.IsEnabled = false;
        PingAllIcon.Visibility = Visibility.Collapsed;
        PingAllSpinner.Visibility = Visibility.Visible;
        try
        {
            await MeasurePingsAsync(force: true);
        }
        finally
        {
            PingAllButton.IsEnabled = true;
            PingAllIcon.Visibility = Visibility.Visible;
            PingAllSpinner.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>The refresh button on a row: that server (a country: its cities) is measured again now.</summary>
    private async void RowPing_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { DataContext: ColituServerRow row } button)
        {
            return;
        }
        var targets = row.GroupServers.Count > 0 ? row.GroupServers.ToList() : row.Server is { } server ? [server] : [];
        if (targets.Count == 0 || _measuringPings)
        {
            return;
        }
        button.IsEnabled = false;
        _measuringPings = true;
        try
        {
            await _vpn.MeasurePingsAsync(targets);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ColituMainWindow.RowPing", ex);
        }
        finally
        {
            _measuringPings = false;
        }
        if (_page == "locations")
        {
            RenderServersKeepingScroll();
        }
        ApplyLocationCard();
    }

    /// <summary>
    /// A country header opens / closes on the press itself and the event stops there. Before, the
    /// ListBox selected the header and captured the mouse; the list was rebuilt under the pointer
    /// and the capture then selected the new header too, so a country with many cities (USA)
    /// opened and closed again in one click.
    /// </summary>
    private void ServerList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        for (var node = e.OriginalSource as DependencyObject; node != null && node != ServerList; node = ParentOf(node))
        {
            if (node is ButtonBase)
            {
                return;
            }
            if (node is ListBoxItem item)
            {
                if (item.DataContext is ColituServerRow { IsGroup: true } group)
                {
                    e.Handled = true;
                    ToggleGroup(group.GroupKey);
                }
                return;
            }
        }
    }

    private static DependencyObject? ParentOf(DependencyObject node) =>
        node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);

    // ── Plan badge ─────────────────────────────────────────────────────────
    private readonly Dictionary<Border, LinearGradientBrush> _activeBadges = [];

    /// <summary>
    /// The "ACTIVE" badge: its two stops slowly trade the brand violets (2.8 s, back and forth). Still
    /// when Windows' "Animation effects" are off. Dark text stays above 5:1 on every frame.
    /// </summary>
    private Brush ActiveBadgeBrush(Border badge)
    {
        if (_activeBadges.TryGetValue(badge, out var existing))
        {
            return existing;
        }
        var light = Color.FromRgb(0xC4, 0xB5, 0xFD);
        var mid = Color.FromRgb(0xB4, 0xA4, 0xFF);
        var deep = Color.FromRgb(0x8B, 0x78, 0xFF);
        var first = new GradientStop(mid, 0);
        var second = new GradientStop(deep, 1);
        var brush = new LinearGradientBrush(new GradientStopCollection { first, second }, 0);
        if (SystemParameters.ClientAreaAnimation)
        {
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            first.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(deep, TimeSpan.FromSeconds(2.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
            second.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(light, TimeSpan.FromSeconds(2.8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
        }
        _activeBadges[badge] = brush;
        return brush;
    }

    // ── Plan: 30-day usage ─────────────────────────────────────────────────
    private bool _usageBusy;

    private async Task LoadUsageHistoryAsync()
    {
        if (_usageBusy || !_auth.HasSession)
        {
            return;
        }
        _usageBusy = true;
        var epoch = _accountEpoch;
        try
        {
            var days = await ColituAccountApi.DailyUsageAsync(30);
            if (epoch != _accountEpoch)
            {
                return;
            }
            if (days == null || days.Count == 0)
            {
                // An older panel without the history: no card.
                UsageHistoryCard.Visibility = Visibility.Collapsed;
                return;
            }
            var culture = Loc.I.Culture;
            UsageHistoryTotal.Text = FormatBytes(days.Sum(day => day.Total));
            var peak = days.MaxBy(day => day.Total)!;
            UsageHistoryPeak.Text = peak.Total > 0 ? Loc.I.Format("usage.peak", ("day", ColituUsageBars.DayLabel(peak.Date, culture)), ("total", FormatBytes(peak.Total))) : "";
            UsageFirstDay.Text = ColituUsageBars.DayLabel(days[0].Date, culture);
            UsageBars.SetDays(days, day => Loc.I.Format("usage.day", ("day", ColituUsageBars.DayLabel(day.Date, culture)), ("total", FormatBytes(day.Total)),
                ("down", FormatBytes(day.DownloadBytes)), ("up", FormatBytes(day.UploadBytes))));
            System.Windows.Automation.AutomationProperties.SetName(UsageBars, $"{Loc.I["usage.title"]}: {UsageHistoryTotal.Text}. {UsageHistoryPeak.Text}");
            UsageHistoryCard.Visibility = Visibility.Visible;
        }
        catch (Exception ex) when (ColituVpnService.IsNetworkFailure(ex))
        {
            // Offline: the card keeps its last numbers.
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ColituMainWindow.LoadUsageHistoryAsync", ex);
        }
        finally
        {
            _usageBusy = false;
        }
    }

    // ── Account: old devices, rename ───────────────────────────────────────
    private const int StaleDeviceDays = 30;
    private List<ColituDeviceRow> _staleDevices = [];
    private ColituDeviceRow? _renaming;

    private void ApplyCleanupButton(List<ColituDeviceRow> rows)
    {
        var ids = ColituAccountApi.StaleDeviceIds(rows.Select(row => (row.Id, row.IsCurrent, row.LastSeen)), StaleDeviceDays, DateTimeOffset.UtcNow);
        _staleDevices = rows.Where(row => ids.Contains(row.Id)).ToList();
        CleanupDevicesButton.Visibility = _staleDevices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CleanupDevicesText.Text = Loc.I.Format("account.cleanupCount", ("n", _staleDevices.Count));
    }

    private async void CleanupDevices_Click(object sender, RoutedEventArgs e)
    {
        CleanupDevicesButton.IsEnabled = false;
        try
        {
            // The panel decides (it knows the last connection); an older panel: the list shown here.
            var ids = await ColituAccountApi.CleanupDevicesAsync(StaleDeviceDays, dryRun: true);
            var serverSide = ids != null;
            ids ??= _staleDevices.Select(device => device.Id).ToList();
            if (ids.Count == 0)
            {
                ShowToast(Loc.I["account.cleanupNone"]);
                return;
            }
            var names = ids.Select(id => _staleDevices.FirstOrDefault(device => device.Id == id)?.Name ?? id[..Math.Min(8, id.Length)]).ToList();
            var list = string.Join("\n", names.Take(8).Select(name => "• " + name)) + (names.Count > 8 ? "\n…" : "");
            var confirm = MessageBox.Show(this, Loc.I.Format("account.cleanupConfirm", ("n", ids.Count), ("names", list)), "Colitu VPN", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK)
            {
                return;
            }
            var removed = 0;
            if (serverSide)
            {
                removed = (await ColituAccountApi.CleanupDevicesAsync(StaleDeviceDays, dryRun: false))?.Count ?? 0;
            }
            else
            {
                foreach (var id in ids.Where(id => !string.Equals(id, _auth.RegisteredDeviceId, StringComparison.OrdinalIgnoreCase)))
                {
                    await _auth.RemoveDeviceAsync(id);
                    removed++;
                }
            }
            ShowToast(Loc.I.Format("account.cleanupDone", ("n", removed)));
        }
        catch (Exception ex)
        {
            ShowToast(ex is ColituApiException api ? api.Message : Loc.I["err.generic"], true);
        }
        finally
        {
            CleanupDevicesButton.IsEnabled = true;
        }
        await LoadDevicesAsync();
    }

    private void RenameDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ColituDeviceRow device })
        {
            return;
        }
        _renaming = device;
        RenameBox.Text = device.Name;
        RenameOverlay.Visibility = Visibility.Visible;
        FadeIn(RenameOverlay, 8);
        RenameBox.Focus();
        RenameBox.SelectAll();
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            RenameSave_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseRename();
        }
    }

    private void RenameCancel_Click(object sender, RoutedEventArgs e) => CloseRename();

    private void RenameBackdrop_Click(object sender, MouseButtonEventArgs e) => CloseRename();

    private void CloseRename()
    {
        RenameOverlay.Visibility = Visibility.Collapsed;
        _renaming = null;
    }

    private async void RenameSave_Click(object sender, RoutedEventArgs e)
    {
        var device = _renaming;
        var name = RenameBox.Text.Trim();
        if (device == null || name.Length == 0 || !RenameSaveButton.IsEnabled)
        {
            return;
        }
        if (name == device.Name)
        {
            CloseRename();
            return;
        }
        RenameSaveButton.IsEnabled = false;
        try
        {
            await ColituAccountApi.RenameDeviceAsync(device.Id, name);
            CloseRename();
            ShowToast(Loc.I["account.renamed"]);
            await LoadDevicesAsync();
        }
        catch (Exception ex)
        {
            ShowToast(ex is ColituApiException api ? api.Message : Loc.I["err.generic"], true);
        }
        finally
        {
            RenameSaveButton.IsEnabled = true;
        }
    }

    // ── Support: typing, drag and drop, tray notification ──────────────────
    private DateTimeOffset _typingSentAt = DateTimeOffset.MinValue;
    private bool _typingUnsupported;
    private bool _balloonOpensSupport;
    private bool _balloonHooked;

    private void ShowSupportTyping(bool typing)
    {
        if (SupportTypingText != null)
        {
            SupportTypingText.Visibility = typing ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>Tells support the user is typing, at most every 3 seconds; the answer says whether support is.</summary>
    private async void SupportReply_TextChanged(object sender, TextChangedEventArgs e)
    {
        var current = _supportCurrent;
        if (_typingUnsupported || current == null || SupportReplyBox.Text.Length == 0
            || DateTimeOffset.UtcNow - _typingSentAt < TimeSpan.FromSeconds(3))
        {
            return;
        }
        _typingSentAt = DateTimeOffset.UtcNow;
        try
        {
            var peerTyping = await ColituAccountApi.SupportTypingAsync(current.Id);
            if (peerTyping == null)
            {
                _typingUnsupported = true;
                return;
            }
            if (_supportCurrent?.Id == current.Id)
            {
                ShowSupportTyping(peerTyping.Value);
            }
        }
        catch (Exception ex) when (ColituVpnService.IsNetworkFailure(ex) || ex is ColituApiException)
        {
            // Best effort: the indicator is a nicety.
        }
    }

    private void SupportDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void SupportNewDrop(object sender, DragEventArgs e) => DropSupportFiles(e, _supportNewFiles, SupportNewFiles);

    private void SupportReplyDrop(object sender, DragEventArgs e)
    {
        if (SupportComposer.Visibility == Visibility.Visible)
        {
            DropSupportFiles(e, _supportReplyFiles, SupportReplyFiles);
        }
    }

    private void DropSupportFiles(DragEventArgs e, List<string> files, Panel chips)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
        {
            e.Handled = true;
            AddSupportFiles(files, chips, dropped.Where(System.IO.File.Exists));
        }
    }

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp"];

    /// <summary>A small preview of an image file for its chip; null for other files or one WPF cannot decode.</summary>
    private static BitmapImage? SupportThumbnail(string file)
    {
        if (!ImageExtensions.Contains(System.IO.Path.GetExtension(file).ToLowerInvariant()))
        {
            return null;
        }
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 96;
            image.UriSource = new Uri(file);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Window in the tray: a Windows notification with the reply's first words; clicking it opens the chat.</summary>
    private async Task NotifySupportReplyInTrayAsync()
    {
        var text = Loc.I["support.newReply"];
        try
        {
            var latest = (await _support.ListAsync()).Where(conversation => conversation.Unread > 0).MaxBy(conversation => conversation.LastMessageAt);
            if (latest?.LastMessage is { Length: > 0 } preview)
            {
                preview = preview.ReplaceLineEndings(" ").Trim();
                text = Loc.I.Format("support.replyFrom", ("text", preview.Length > 120 ? preview[..117] + "…" : preview));
            }
        }
        catch (Exception ex) when (ColituVpnService.IsNetworkFailure(ex) || ex is ColituApiException)
        {
            // The plain "new reply" text then.
        }
        if (!_balloonHooked)
        {
            _balloonHooked = true;
            TrayIcon.TrayBalloonTipClicked += (_, _) =>
            {
                if (_balloonOpensSupport)
                {
                    _balloonOpensSupport = false;
                    ShowFromTray();
                    Navigate("support");
                }
            };
        }
        _balloonOpensSupport = true;
        TrayIcon.ShowNotification("Colitu VPN", text);
    }
}
