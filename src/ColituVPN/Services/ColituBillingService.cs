using System.Text.Json.Serialization;

namespace v2rayN.Services;

/// <summary>
/// Plan purchase in the app, the same flow as colitu.com/checkout: tier catalog
/// and payment methods → quote → checkout (the provider's page opens in the
/// browser) → order status polling until the payment settles. Platega charges
/// roubles (card, SBP, crypto), Kyren Pay charges yuan (Alipay / WeChat Pay).
/// Unlike the site, every method is offered in every app language.
/// </summary>
public sealed class ColituBillingService
{
    public const string ProviderKyren = "kyren";

    public static ColituBillingService Instance { get; } = new();

    private ColituBillingService() { }

    public async Task<ColituTierCatalog> GetCatalogAsync()
    {
        var envelope = await ColituAuthService.Instance.GetAuthorizedJsonAsync<ColituEnvelope<ColituTierCatalog>>("/billing/catalog");
        var catalog = envelope?.Data ?? new ColituTierCatalog();
        catalog.Tiers = catalog.Tiers
            .Where(tier => tier.Prices.Count > 0 && !string.IsNullOrWhiteSpace(tier.Plan))
            .OrderBy(tier => tier.Devices)
            .ToList();
        return catalog;
    }

    public async Task<List<ColituPaymentMethod>> GetPaymentMethodsAsync()
    {
        var envelope = await ColituAuthService.Instance.GetAuthorizedJsonAsync<ColituEnvelope<List<ColituPaymentMethod>>>("/billing/payment-methods");
        return (envelope?.Data ?? [])
            .Where(method => method.Enabled && method.SupportsOneTime && method.Key.Length > 0)
            .OrderBy(method => method.SortOrder)
            .ToList();
    }

    public async Task<ColituQuote?> QuoteAsync(string plan, int durationMonths, string method)
    {
        var envelope = await ColituAuthService.Instance.PostAuthorizedJsonAsync<ColituEnvelope<ColituQuote>>("/billing/quote", Request(plan, durationMonths, method));
        return envelope?.Data;
    }

    public async Task<ColituCheckout?> CheckoutAsync(string plan, int durationMonths, string method)
    {
        var envelope = await ColituAuthService.Instance.PostAuthorizedJsonAsync<ColituEnvelope<ColituCheckout>>("/billing/checkout", Request(plan, durationMonths, method));
        return envelope?.Data;
    }

    public async Task<ColituOrder?> GetOrderAsync(string orderId)
    {
        var envelope = await ColituAuthService.Instance.GetAuthorizedJsonAsync<ColituEnvelope<ColituOrder>>($"/billing/orders/{Uri.EscapeDataString(orderId)}");
        return envelope?.Data;
    }

    private static object Request(string plan, int durationMonths, string method) => new
    {
        plan,
        duration_months = durationMonths,
        payment_method = method
    };

    public static bool IsSettled(string? status) => status is "succeeded" or "failed" or "canceled" or "cancelled" or "expired" or "refunded" or "chargeback";

    /// <summary>The currency a method charges in: Kyren Pay yuan, Platega the catalog's charge currency.</summary>
    public static string ChargeCurrency(ColituPaymentMethod? method, ColituTierCatalog catalog) =>
        method?.Provider == ProviderKyren ? "CNY" : catalog.ChargeCurrency;

    /// <summary>
    /// A tier's price for a period in the currency the chosen method charges:
    /// the fixed regional price (CNY) or the converted charge price (RUB).
    /// Null when the tier is not sold in that currency for that period.
    /// </summary>
    public static ColituShownPrice? PriceIn(ColituTier tier, int months, string currency)
    {
        var price = tier.Prices.FirstOrDefault(item => item.IntervalMonths == months);
        if (price == null)
        {
            return null;
        }
        if (price.Regional != null && price.Regional.TryGetValue(currency, out var regional) && regional.PriceMinor > 0)
        {
            return new ColituShownPrice(currency, regional.PriceMinor, regional.MonthlyMinor, regional.SavingPercent);
        }
        if (string.Equals(price.ChargeCurrency, currency, StringComparison.OrdinalIgnoreCase) && price.ChargeMinor > 0)
        {
            // Converted prices: the monthly figure in whole units (548 ₽, not 548,66 ₽).
            var monthly = (long)Math.Round(price.ChargeMinor / (decimal)Math.Max(1, months) / 100m, MidpointRounding.AwayFromZero) * 100;
            return new ColituShownPrice(currency, price.ChargeMinor, monthly, price.SavingPercent);
        }
        return null;
    }

    /// <summary>Formats minor units: "548 ₽", "¥38", "¥24.83".</summary>
    public static string Money(long minor, string currency, IFormatProvider culture)
    {
        var amount = minor / 100m;
        var number = amount.ToString(amount % 1 == 0 ? "#,0" : "#,0.00", culture);
        return currency.ToUpperInvariant() switch
        {
            "RUB" => $"{number} ₽",
            "CNY" => $"¥{number}",
            "GBP" => $"£{number}",
            "USD" => $"${number}",
            "EUR" => $"€{number}",
            var other => $"{number} {other}"
        };
    }
}

public sealed record ColituShownPrice(string Currency, long TotalMinor, long MonthlyMinor, int SavingPercent);

public sealed class ColituEnvelope<T>
{
    [JsonPropertyName("data")] public T? Data { get; set; }
}

public sealed class ColituTierCatalog
{
    [JsonPropertyName("display_currency")] public string DisplayCurrency { get; set; } = "GBP";
    [JsonPropertyName("charge_currency")] public string ChargeCurrency { get; set; } = "RUB";
    [JsonPropertyName("tiers")] public List<ColituTier> Tiers { get; set; } = [];
}

public sealed class ColituTier
{
    [JsonPropertyName("plan")] public string Plan { get; set; } = "";
    [JsonPropertyName("devices")] public int Devices { get; set; }
    [JsonPropertyName("prices")] public List<ColituTierPrice> Prices { get; set; } = [];
}

public sealed class ColituTierPrice
{
    [JsonPropertyName("interval_months")] public int IntervalMonths { get; set; }
    [JsonPropertyName("currency")] public string Currency { get; set; } = "GBP";
    [JsonPropertyName("price_minor")] public long PriceMinor { get; set; }
    [JsonPropertyName("monthly_minor")] public long MonthlyMinor { get; set; }
    [JsonPropertyName("saving_percent")] public int SavingPercent { get; set; }
    [JsonPropertyName("charge_currency")] public string? ChargeCurrency { get; set; }
    [JsonPropertyName("charge_minor")] public long ChargeMinor { get; set; }
    [JsonPropertyName("regional")] public Dictionary<string, ColituRegionalPrice>? Regional { get; set; }
}

public sealed class ColituRegionalPrice
{
    [JsonPropertyName("price_minor")] public long PriceMinor { get; set; }
    [JsonPropertyName("monthly_minor")] public long MonthlyMinor { get; set; }
    [JsonPropertyName("saving_percent")] public int SavingPercent { get; set; }
}

public sealed class ColituPaymentMethod
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    [JsonPropertyName("commission_bps")] public int CommissionBps { get; set; }
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("supports_one_time")] public bool SupportsOneTime { get; set; }
    [JsonPropertyName("sort_order")] public int SortOrder { get; set; }
    [JsonPropertyName("provider")] public string? Provider { get; set; }
}

public sealed class ColituQuote
{
    [JsonPropertyName("plan")] public string? Plan { get; set; }
    [JsonPropertyName("currency")] public string Currency { get; set; } = "RUB";
    [JsonPropertyName("duration_months")] public int DurationMonths { get; set; }
    [JsonPropertyName("device_count")] public int DeviceCount { get; set; }
    [JsonPropertyName("regular_price_minor")] public long RegularPriceMinor { get; set; }
    [JsonPropertyName("discount_percent")] public int DiscountPercent { get; set; }
    [JsonPropertyName("discount_minor")] public long DiscountMinor { get; set; }
    [JsonPropertyName("package_price_minor")] public long PackagePriceMinor { get; set; }
    [JsonPropertyName("commission")] public ColituQuoteCommission? Commission { get; set; }
    [JsonPropertyName("customer_total_minor")] public long CustomerTotalMinor { get; set; }
}

public sealed class ColituQuoteCommission
{
    [JsonPropertyName("method")] public string? Method { get; set; }
    [JsonPropertyName("rate_bps")] public int RateBps { get; set; }
    [JsonPropertyName("amount_minor")] public long AmountMinor { get; set; }
}

public sealed class ColituCheckout
{
    [JsonPropertyName("order_id")] public string? OrderId { get; set; }
    [JsonPropertyName("provider")] public string? Provider { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("redirect_url")] public string? RedirectUrl { get; set; }
    [JsonPropertyName("expires_at")] public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class ColituOrder
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
}
