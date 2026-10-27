using System.Windows.Media;
using v2rayN.Services;

namespace v2rayN.Views;

/// <summary>
/// Premium encouragement for free-plan accounts on Home: one compact line normally, a stronger card when
/// the month's traffic runs low or is used up (connecting is refused then) and right after Premium ended.
/// The decisions are in <see cref="ColituUpsell"/>; every number comes from the panel.
/// </summary>
public partial class ColituMainWindow
{
    private ColituUpsellState _upsell = ColituUpsellState.None;

    private void ApplyUpsell()
    {
        if (UpsellBanner == null)
        {
            return;
        }
        var subscription = _auth.CurrentSubscription;
        var now = DateTimeOffset.UtcNow;
        var saved = _vpn.Preferences.PremiumSeenUntil;
        var seen = ColituUpsell.RememberPremium(subscription, saved);
        if (seen != saved)
        {
            try
            {
                _vpn.SetPremiumSeenUntil(seen);
            }
            catch (Exception ex)
            {
                Logging.SaveLog("ColituMainWindow.ApplyUpsell", ex);
            }
        }

        var state = _auth.HasSession
            ? ColituUpsell.Decide(subscription, _usage?.Stats.TotalUsedBytes, now, ColituUpsell.PremiumEndedAt(seen, now))
            : ColituUpsellState.None;
        var changed = state.Kind != _upsell.Kind;
        _upsell = state;
        RenderUpsell();
        if (changed)
        {
            // The panel's own usage notices repeat what the banner says; the home title follows a used-up allowance.
            ShowNextNotice();
            ApplyStatus();
        }
    }

    private void RenderUpsell()
    {
        var state = _upsell;
        var loc = Loc.I;
        // The plain free-plan line repeated the plan card (allowance, devices) above it, which now has
        // its own "Get Premium" button: only the stronger states (running low, used up, Premium ended) show.
        if (state.Kind is ColituUpsellKind.None or ColituUpsellKind.Free || state.LimitBytes <= 0)
        {
            UpsellBanner.Visibility = Visibility.Collapsed;
            return;
        }

        var limit = FormatBytes(state.LimitBytes);
        var devices = loc.Count("device", state.DeviceLimit);
        var strong = state.Kind != ColituUpsellKind.Free;
        UpsellCompact.Visibility = strong ? Visibility.Collapsed : Visibility.Visible;
        UpsellStrong.Visibility = strong ? Visibility.Visible : Visibility.Collapsed;
        UpsellLaterButton.Visibility = state.Kind == ColituUpsellKind.PremiumEnded ? Visibility.Visible : Visibility.Collapsed;
        // Calm violet like the trial banner when there is something to act on; plain glass for the compact line.
        UpsellBanner.Background = strong ? new SolidColorBrush(Color.FromArgb(0x14, 0x9F, 0x8C, 0xFF)) : new SolidColorBrush(Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF));
        UpsellBanner.BorderBrush = strong ? new SolidColorBrush(Color.FromArgb(0x59, 0x9F, 0x8C, 0xFF)) : (Brush)FindResource("LineStrongBrush");
        UpsellBanner.Padding = strong ? new Thickness(18, 16, 18, 16) : new Thickness(18, 12, 12, 12);

        switch (state.Kind)
        {
            case ColituUpsellKind.Free:
                UpsellCompactText.Text = loc.Format("upsell.free", ("left", FormatBytes(state.LeftBytes)), ("limit", limit), ("devices", devices));
                break;
            case ColituUpsellKind.Low:
                UpsellTitle.Text = loc.Format("upsell.low.title", ("percent", state.Percent));
                UpsellBody.Text = (state.RenewsAt is { } until
                    ? loc.Format("upsell.leftUntil", ("left", FormatBytes(state.LeftBytes)), ("date", FormatDate(until)))
                    : loc.Format("upsell.left", ("left", FormatBytes(state.LeftBytes)))) + " " + loc["upsell.unlimited"];
                break;
            case ColituUpsellKind.UsedUp:
                UpsellTitle.Text = loc["upsell.out.title"];
                UpsellBody.Text = UsedUpRenewText() + " " + loc["upsell.out.premium"];
                break;
            case ColituUpsellKind.PremiumEnded:
                UpsellTitle.Text = loc["upsell.ended.title"];
                UpsellBody.Text = loc.Format("upsell.ended.body", ("limit", limit), ("devices", devices));
                break;
        }
        UpsellBanner.Visibility = Visibility.Visible;
    }

    /// <summary>"Free traffic comes back on 1 November 2026." (or "at the start of next month" without a date).</summary>
    private string UsedUpRenewText() => _upsell.RenewsAt is { } renews
        ? Loc.I.Format("upsell.out.renews", ("date", FormatDate(renews)))
        : Loc.I["upsell.out.renewsSoon"];

    /// <summary>Connecting was refused (or would be) because the free allowance is used up: say when it returns and offer Premium.</summary>
    private void ShowQuotaUsedUp()
    {
        ShowToast($"{Loc.I["upsell.out.title"]}. {UsedUpRenewText()}", true);
        Navigate("plan");
    }

    /// <summary>The in-app plans and payment (Platega, Kyren Pay, Rialit), the fitting plan preselected.</summary>
    private void UpsellPremium_Click(object sender, RoutedEventArgs e) => OpenPlans(suggest: true);

    /// <summary>"Not now" on the Premium-ended card: back to the compact line for good.</summary>
    private void UpsellLater_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _vpn.SetPremiumSeenUntil(null);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ColituMainWindow.UpsellLater_Click", ex);
        }
        ApplyUpsell();
    }
}
