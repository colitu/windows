using System.Windows.Controls;
using System.Windows.Media;
using Path = System.Windows.Shapes.Path;
using v2rayN.Services;

namespace v2rayN.Views;

/// <summary>
/// Plan page: plans are bought in the app, as on colitu.com/checkout. Pick a
/// period (1 or 12 months), a tier (Personal, Plus, Family) and a payment
/// method: card, SBP or crypto through Platega (roubles), or Alipay / WeChat
/// Pay through Kyren Pay (yuan). Every method is offered in every language.
/// The provider's page opens in the browser; the app polls the order and
/// refreshes the plan as soon as the payment clears. The account on
/// app.colitu.com stays one click away.
/// </summary>
public partial class ColituMainWindow
{
    private const string AccountPortalUrl = "https://app.colitu.com";
    private static readonly int[] PlanPeriods = [1, 12];
    private readonly ColituBillingService _billing = ColituBillingService.Instance;
    private bool _refreshingPlan;
    private ColituTierCatalog? _catalog;
    private List<ColituPaymentMethod> _methods = [];
    private string _tier = "";
    private int _months = 12;
    private string _method = "";
    private ColituQuote? _quote;
    private int _quoteVersion;
    private ColituCheckout? _checkout;
    private DispatcherTimer? _paymentTimer;
    private string _paymentState = "";
    private bool _paying;

    private void ManagePlan_Click(object sender, RoutedEventArgs e) => OpenUrl(AccountPortalUrl);

    private async void PlanRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_refreshingPlan)
        {
            return;
        }
        _refreshingPlan = true;
        PlanRefreshButton.IsEnabled = false;
        PlanRefreshSpinner.Visibility = Visibility.Visible;
        try
        {
            if (await RefreshDataAsync())
            {
                ShowToast(Loc.I["plan.refreshed"]);
            }
            else
            {
                ShowToast(Loc.I["err.network"], true);
            }
        }
        finally
        {
            PlanRefreshSpinner.Visibility = Visibility.Collapsed;
            PlanRefreshButton.IsEnabled = true;
            _refreshingPlan = false;
        }
    }

    // ── Catalog ────────────────────────────────────────────────────────────
    private async Task LoadPricingAsync(bool force = false)
    {
        if (_paymentState == "pending")
        {
            return;
        }
        if (_catalog != null && _methods.Count > 0 && !force)
        {
            RenderPricing();
            await UpdateQuoteAsync();
            return;
        }

        PricingLoading.Visibility = Visibility.Visible;
        PricingError.Visibility = Visibility.Collapsed;
        PricingContent.Visibility = Visibility.Collapsed;
        try
        {
            var catalogTask = _billing.GetCatalogAsync();
            var methodsTask = _billing.GetPaymentMethodsAsync();
            _catalog = await catalogTask;
            _methods = await methodsTask;
            if (_catalog.Tiers.Count == 0 || _methods.Count == 0)
            {
                throw new InvalidOperationException("Empty catalog");
            }

            if (_methods.All(method => method.Key != _method))
            {
                _method = DefaultMethod(_methods);
            }
            if (_catalog.Tiers.All(tier => tier.Plan != _tier))
            {
                var current = _auth.CurrentSubscription?.PlanName;
                _tier = (_catalog.Tiers.FirstOrDefault(tier => string.Equals(tier.Plan, current, StringComparison.OrdinalIgnoreCase))
                    ?? _catalog.Tiers.FirstOrDefault(tier => tier.Plan == "plus")
                    ?? _catalog.Tiers[0]).Plan;
            }

            PricingLoading.Visibility = Visibility.Collapsed;
            PricingContent.Visibility = Visibility.Visible;
            RenderPricing();
            FadeIn(PricingContent, 12);
            await UpdateQuoteAsync();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ColituMainWindow.LoadPricingAsync", ex);
            PricingLoading.Visibility = Visibility.Collapsed;
            PricingError.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Alipay / WeChat Pay first in Chinese, SBP in Russian, otherwise the card.</summary>
    private static string DefaultMethod(List<ColituPaymentMethod> methods)
    {
        var preferred = Loc.I.Language switch
        {
            "zh" => "kyren",
            "ru" => "sbp",
            _ => "card"
        };
        return (methods.FirstOrDefault(method => method.Key == preferred) ?? methods[0]).Key;
    }

    private void PricingRetry_Click(object sender, RoutedEventArgs e) => _ = LoadPricingAsync(force: true);

    private ColituPaymentMethod? ChosenMethod => _methods.FirstOrDefault(method => method.Key == _method);

    private ColituTier? ChosenTier => _catalog?.Tiers.FirstOrDefault(tier => tier.Plan == _tier);

    private string ChargeCurrency => _catalog == null ? "RUB" : ColituBillingService.ChargeCurrency(ChosenMethod, _catalog);

    private string Money(long minor, string currency) => ColituBillingService.Money(minor, currency, Loc.I.Culture);

    private static string TierName(string plan) => Loc.Has("tier." + plan) ? Loc.I["tier." + plan] : plan;

    private string MethodName(ColituPaymentMethod method) =>
        Loc.Has("method." + method.Key) ? Loc.I["method." + method.Key] : method.DisplayName ?? method.Key;

    private static string MethodIcon(string key) => key switch
    {
        "card" => "M3 6h18v12H3z M3 10h18 M7 15h4",
        "crypto" => "M12 3l8 4.5v9L12 21l-8-4.5v-9z M12 12l8-4.5 M12 12v9 M12 12L4 7.5",
        "kyren" => "M4 4h16v16H4z M4 9h16 M9 4v16",
        _ => "M13 2L4.5 13.5H12L11 22l8.5-11.5H12z"
    };

    // ── Rendering ──────────────────────────────────────────────────────────
    private void RenderPricing()
    {
        if (_catalog == null || DurationList == null)
        {
            return;
        }

        DurationList.Children.Clear();
        foreach (var months in PlanPeriods)
        {
            if (_catalog.Tiers.All(tier => tier.Prices.All(price => price.IntervalMonths != months)))
            {
                continue;
            }
            var option = new RadioButton
            {
                GroupName = "PlanPeriod",
                Tag = months,
                Style = (Style)FindResource("SegmentOption"),
                IsChecked = months == _months,
                MinWidth = 120,
                Content = Loc.I[months == 1 ? "pricing.monthly" : "pricing.yearly"]
            };
            option.Checked += Period_Checked;
            DurationList.Children.Add(option);
        }

        RenderTiers();

        MethodList.Children.Clear();
        foreach (var method in _methods)
        {
            var option = new RadioButton
            {
                GroupName = "PlanMethod",
                Tag = method.Key,
                Style = (Style)FindResource("OptionCard"),
                IsChecked = method.Key == _method,
                Margin = new Thickness(0, 0, 8, 8)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = new Grid { Width = 32, Height = 32 };
            icon.Children.Add(new Border { Background = (Brush)FindResource("Glass06Brush"), CornerRadius = new CornerRadius(10) });
            icon.Children.Add(new Path
            {
                Data = Geometry.Parse(MethodIcon(method.Key)),
                Width = 16,
                Height = 16,
                Stretch = Stretch.Uniform,
                Stroke = (Brush)FindResource("LilacBrush"),
                StrokeThickness = 1.6,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
            grid.Children.Add(icon);
            var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = MethodName(method), FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            // Only the currency: whether a fee is added (or Colitu absorbs it) comes with the quote.
            var currency = ColituBillingService.ChargeCurrency(method, _catalog);
            var currencyKey = "currency." + currency.ToLowerInvariant();
            text.Children.Add(new TextBlock
            {
                Text = Loc.Has(currencyKey) ? Loc.I[currencyKey] : currency,
                FontSize = 12,
                Foreground = (Brush)FindResource("MutedBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            option.Content = grid;
            option.Checked += Method_Checked;
            MethodList.Children.Add(option);
        }

        RenderSummary();
    }

    /// <summary>Tier cards with the monthly price in the currency the chosen method charges.</summary>
    private void RenderTiers()
    {
        if (_catalog == null)
        {
            return;
        }
        TierList.Children.Clear();
        foreach (var tier in _catalog.Tiers)
        {
            var price = ColituBillingService.PriceIn(tier, _months, ChargeCurrency);
            var option = new RadioButton
            {
                GroupName = "PlanTier",
                Tag = tier.Plan,
                Style = (Style)FindResource("OptionCard"),
                IsChecked = tier.Plan == _tier,
                IsEnabled = price != null,
                Margin = new Thickness(0, 0, 8, 8)
            };
            var body = new StackPanel { Margin = new Thickness(2, 4, 2, 4) };
            body.Children.Add(new TextBlock { Text = TierName(tier.Plan), FontSize = 15, FontWeight = FontWeights.SemiBold });
            body.Children.Add(new TextBlock
            {
                Text = Loc.I.Count("device", tier.Devices),
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = (Brush)FindResource("MutedBrush")
            });
            var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            row.Children.Add(new TextBlock
            {
                Text = price == null ? "—" : Money(price.MonthlyMinor, price.Currency),
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Bottom
            });
            row.Children.Add(new TextBlock
            {
                Text = Loc.I["pricing.perMonth"],
                FontSize = 12,
                Margin = new Thickness(4, 0, 0, 3),
                VerticalAlignment = VerticalAlignment.Bottom,
                Foreground = (Brush)FindResource("MutedBrush")
            });
            body.Children.Add(row);
            if (price != null && _months > 1)
            {
                body.Children.Add(new TextBlock
                {
                    Text = Loc.I.Format("pricing.billedYearly", ("amount", Money(price.TotalMinor, price.Currency))),
                    FontSize = 11.5,
                    Margin = new Thickness(0, 4, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)FindResource("MutedBrush")
                });
                if (price.SavingPercent > 0)
                {
                    body.Children.Add(new TextBlock
                    {
                        Text = Loc.I.Format("pricing.off", ("n", price.SavingPercent)),
                        FontSize = 11.5,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 3, 0, 0),
                        Foreground = (Brush)FindResource("SuccessBrush")
                    });
                }
            }
            option.Content = body;
            option.Checked += Tier_Checked;
            TierList.Children.Add(option);
        }
    }

    private void RenderSummary()
    {
        if (ChosenTier is not { } tier)
        {
            return;
        }
        var currency = ChargeCurrency;
        var price = ColituBillingService.PriceIn(tier, _months, currency);
        SummaryTitle.Text = $"Colitu {TierName(tier.Plan)}";
        SummarySub.Text = $"{Loc.I.Count("device", tier.Devices)} · {Loc.I[_months == 1 ? "pricing.monthly" : "pricing.yearly"]}";
        var quote = _quote is { } q && q.Plan == _tier && q.DurationMonths == _months && q.Commission?.Method == _method ? q : null;
        var quoteCurrency = quote?.Currency ?? currency;
        var package = quote?.PackagePriceMinor ?? price?.TotalMinor;
        SummaryPackage.Text = package is { } amount ? Money(amount, quoteCurrency) : "…";
        // A fee row only when the customer pays one (not when Colitu absorbs it).
        var fee = quote?.Commission?.AmountMinor ?? 0;
        var rate = quote?.Commission?.RateBps ?? 0;
        SummaryFeeRow.Visibility = fee > 0 ? Visibility.Visible : Visibility.Collapsed;
        SummaryFeeLabel.Text = rate > 0
            ? $"{Loc.I["pricing.commission"]} ({(rate / 100m).ToString("0.##", Loc.I.Culture)}%)"
            : Loc.I["pricing.commission"];
        SummaryFee.Text = quote != null ? Money(fee, quoteCurrency) : "…";
        SummaryTotal.Text = quote != null ? Money(quote.CustomerTotalMinor, quoteCurrency) : "…";
        PayButton.IsEnabled = quote != null && !_paying;
    }

    private async Task UpdateQuoteAsync()
    {
        if (ChosenTier == null || _method.Length == 0)
        {
            return;
        }
        var version = ++_quoteVersion;
        RenderSummary();
        try
        {
            var quote = await _billing.QuoteAsync(_tier, _months, _method);
            if (version == _quoteVersion)
            {
                _quote = quote;
                RenderSummary();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ColituMainWindow.UpdateQuoteAsync", ex);
            if (version == _quoteVersion)
            {
                _quote = null;
                RenderSummary();
                SummaryTotal.Text = "—";
            }
        }
    }

    private async void Period_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: int months } && months != _months)
        {
            _months = months;
            RenderTiers();
            await UpdateQuoteAsync();
        }
    }

    private async void Tier_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string plan } && plan != _tier)
        {
            _tier = plan;
            await UpdateQuoteAsync();
        }
    }

    private async void Method_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string key } && key != _method)
        {
            var currencyBefore = ChargeCurrency;
            _method = key;
            // Kyren charges yuan, Platega roubles: the tier cards switch currency.
            if (ChargeCurrency != currencyBefore)
            {
                RenderTiers();
            }
            await UpdateQuoteAsync();
        }
    }

    // ── Checkout ───────────────────────────────────────────────────────────
    private async void Pay_Click(object sender, RoutedEventArgs e)
    {
        if (_paying || _quote == null)
        {
            return;
        }
        _paying = true;
        PayButton.IsEnabled = false;
        PaySpinner.Visibility = Visibility.Visible;
        try
        {
            _checkout = await _billing.CheckoutAsync(_tier, _months, _method);
            if (_checkout?.OrderId is not { Length: > 0 }
                || !Uri.TryCreate(_checkout.RedirectUrl, UriKind.Absolute, out var url)
                || url.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("Checkout response was incomplete.");
            }
            OpenUrl(url.AbsoluteUri);
            ShowPaymentState("pending");
            StartPaymentPolling();
        }
        catch (ColituApiException ex) when (ex.ErrorCode == "EMAIL_NOT_VERIFIED")
        {
            ShowVerify(codeJustSent: false);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("ColituMainWindow.Pay_Click", ex);
            ShowToast(ex is ColituApiException { ErrorCode: "RATE_LIMITED" } ? Loc.I["err.rateLimited"] : Loc.I["err.payment"], true);
        }
        finally
        {
            _paying = false;
            PaySpinner.Visibility = Visibility.Collapsed;
            PayButton.IsEnabled = _quote != null;
        }
    }

    private void StartPaymentPolling()
    {
        StopPaymentPolling();
        _paymentTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _paymentTimer.Tick += async (_, _) => await PollPaymentAsync();
        _paymentTimer.Start();
    }

    private void StopPaymentPolling()
    {
        _paymentTimer?.Stop();
        _paymentTimer = null;
    }

    private async Task PollPaymentAsync()
    {
        if (_checkout?.OrderId is not { } orderId)
        {
            StopPaymentPolling();
            return;
        }
        try
        {
            var order = await _billing.GetOrderAsync(orderId);
            if (_checkout?.OrderId != orderId)
            {
                return;
            }
            var status = order?.Status ?? "pending";
            if (status == "succeeded")
            {
                StopPaymentPolling();
                await RefreshDataAsync();
                ShowPaymentState("succeeded");
                ShowToast(Loc.I["pay.success"]);
            }
            else if (ColituBillingService.IsSettled(status))
            {
                StopPaymentPolling();
                ShowPaymentState("failed");
            }
        }
        catch (Exception ex)
        {
            // Keep polling through brief network trouble.
            Logging.SaveLog("ColituMainWindow.PollPaymentAsync", ex);
        }
    }

    private void ShowPaymentState(string state)
    {
        _paymentState = state;
        var pending = state == "pending";
        SummaryPanel.Visibility = state == "" ? Visibility.Visible : Visibility.Collapsed;
        PaymentPanel.Visibility = state == "" ? Visibility.Collapsed : Visibility.Visible;
        DurationList.IsEnabled = TierList.IsEnabled = MethodList.IsEnabled = !pending;
        if (state == "")
        {
            RenderSummary();
            return;
        }

        PaymentSpinner.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
        PaymentResultIcon.Visibility = pending ? Visibility.Collapsed : Visibility.Visible;
        var success = state == "succeeded";
        PaymentResultIcon.Data = Geometry.Parse(success ? "M5 12.5l4.5 4.5L19 7.5" : "M6 6l12 12 M18 6L6 18");
        PaymentResultIcon.Stroke = (Brush)FindResource(success ? "SuccessBrush" : "DangerBrush");
        PaymentTitle.Text = Loc.I[pending ? "pay.checking" : success ? "pay.success" : "pay.failed"];
        PaymentHint.Text = Loc.I[pending ? "pay.checkingHint" : success ? "pay.successHint" : "pay.failedHint"];
        PaymentPrimaryText.Text = Loc.I[pending ? "pay.reopen" : success ? "pay.connect" : "pay.back"];
        PaymentSecondaryText.Text = Loc.I["pay.cancel"];
        PaymentSecondary.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
        FadeIn(PaymentPanel, 8);
    }

    private async void PaymentPrimary_Click(object sender, RoutedEventArgs e)
    {
        switch (_paymentState)
        {
            case "pending":
                if (_checkout?.RedirectUrl is { } url)
                {
                    OpenUrl(url);
                }
                break;
            case "succeeded":
                _checkout = null;
                ShowPaymentState("");
                Navigate("home");
                if (_vpn.Status != ColituVpnStatus.Connected)
                {
                    await ToggleConnectionAsync();
                }
                break;
            default:
                _checkout = null;
                ShowPaymentState("");
                await UpdateQuoteAsync();
                break;
        }
    }

    private void PaymentSecondary_Click(object sender, RoutedEventArgs e)
    {
        StopPaymentPolling();
        _checkout = null;
        ShowPaymentState("");
    }
}
