using System.Windows.Controls;
using v2rayN.Services;

namespace v2rayN.Views;

/// <summary>
/// "Invite a friend": the account's invite link, the results of its invites and the rules
/// (GET /referral). The page opens from the Account page and never shows invitee names.
/// </summary>
public partial class ColituMainWindow
{
    private ColituReferralInfo? _invite;
    private bool _inviteLoading;

    private void Invite_Click(object sender, RoutedEventArgs e) => Navigate("invite");

    private void InviteRetry_Click(object sender, RoutedEventArgs e) => _ = LoadInviteAsync();

    private void InviteLinkBox_Focus(object sender, KeyboardFocusChangedEventArgs e) => InviteLinkBox.SelectAll();

    /// <summary>Loads the card. An already shown card stays while it refreshes; a failure only replaces an empty page.</summary>
    private async Task LoadInviteAsync()
    {
        if (_inviteLoading || !_auth.HasSession)
        {
            return;
        }
        _inviteLoading = true;
        var epoch = _accountEpoch;
        if (_invite == null)
        {
            ShowInviteState(loading: true, error: false);
        }
        try
        {
            var info = await ColituReferralService.Instance.LoadAsync();
            if (epoch != _accountEpoch)
            {
                return;
            }
            _invite = info;
            ApplyInvite();
        }
        catch (ColituApiException ex) when (ex.ErrorCode is "SIGNED_OUT")
        {
            // Signed out while this ran; nothing to show.
        }
        catch (Exception ex)
        {
            if (epoch != _accountEpoch)
            {
                return;
            }
            Logging.SaveLog("ColituMainWindow.LoadInviteAsync", ex);
            if (_invite == null)
            {
                InviteErrorText.Text = Loc.I["invite.error"];
                ShowInviteState(loading: false, error: true);
            }
        }
        finally
        {
            _inviteLoading = false;
        }
    }

    private void ShowInviteState(bool loading, bool error)
    {
        InviteLoading.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        InviteError.Visibility = error ? Visibility.Visible : Visibility.Collapsed;
        InviteContent.Visibility = !loading && !error ? Visibility.Visible : Visibility.Collapsed;
        InviteLead.Visibility = !loading && !error ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Fills the page from the loaded card (also after a language change).</summary>
    private void ApplyInvite()
    {
        if (_invite is not { } info)
        {
            return;
        }
        var loc = Loc.I;
        InviteLead.Text = loc.Format("invite.lead", ("days", info.RewardDays), ("invitee", info.InviteeDays));
        InviteLinkBox.Text = info.Link;
        InviteCodeLine.Text = loc.Format("invite.codeLine", ("code", info.Code));
        InviteStatInvited.Text = info.Invited.ToString("N0", loc.Culture);
        InviteStatValid.Text = info.Rewarded.ToString("N0", loc.Culture);
        InviteStatPending.Text = loc.Format("invite.stat.pending", ("n", info.Pending));
        InviteStatEarned.Text = loc.Format("invite.stat.days", ("n", info.DaysEarned));
        InviteStatLeft.Text = $"{info.RewardsLeft} / {info.MonthlyCap}";
        InviteRule1.Text = loc.Format("invite.rule1", ("days", info.InviteeDays));
        InviteRule2.Text = loc.Format("invite.rule2", ("days", info.RewardDays));
        InviteRule3.Text = loc.Format("invite.rule3", ("n", info.MonthlyCap));
        InviteRule4.Text = loc["invite.rule4"];
        ShowInviteState(loading: false, error: false);
        if (_page == "invite")
        {
            FadeIn(InviteContent, 8);
        }
    }

    private void InviteCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_invite is not { } info)
        {
            return;
        }
        ShowCopyResult(CopyToClipboard(info.Link), "invite.copied");
    }

    /// <summary>Windows has no share sheet for desktop apps: the ready text (sentence and link) goes to the clipboard.</summary>
    private void InviteShare_Click(object sender, RoutedEventArgs e)
    {
        if (_invite is not { } info)
        {
            return;
        }
        ShowCopyResult(CopyToClipboard(ColituReferral.ShareText(info, Loc.I)), "invite.shareCopied");
    }

    private void ShowCopyResult(bool copied, string successKey)
    {
        if (copied)
        {
            ShowToast(Loc.I[successKey]);
            return;
        }
        // Another program holds the clipboard: let the user copy the selected link by hand.
        ShowToast(Loc.I["invite.copyFailed"], true);
        InviteLinkBox.Focus();
    }

    private static bool CopyToClipboard(string text)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetDataObject(text, true);
                return true;
            }
            catch (Exception ex)
            {
                // CLIPBRD_E_CANT_OPEN while another program has it open: try again shortly.
                if (attempt == 2)
                {
                    Logging.SaveLog("ColituMainWindow.CopyToClipboard", ex);
                }
                Thread.Sleep(40);
            }
        }
        return false;
    }

    /// <summary>Forgets the card when the account changes.</summary>
    private void ClearInvite()
    {
        _invite = null;
        InviteLinkBox.Clear();
        ShowInviteState(loading: true, error: false);
    }

    // ── Register view: optional invite code ────────────────────────────────
    private void InviteToggle_Click(object sender, RoutedEventArgs e)
    {
        InviteToggleButton.Visibility = Visibility.Collapsed;
        InviteFields.Visibility = Visibility.Visible;
        FadeIn(InviteFields, 6);
        InviteBox.Focus();
    }
}
