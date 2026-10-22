using System.Globalization;
using System.Text.Json;
using AwesomeAssertions;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

public class ColituBillingTests
{
    // Trimmed from the live GET /api/v1/billing/catalog (2026-10-22).
    private const string CatalogJson = """
        {"data":{"currency":"RUB","display_currency":"GBP","charge_currency":"RUB","tiers":[
          {"plan":"plus","devices":10,"prices":[
            {"interval_months":1,"currency":"GBP","price_minor":699,"monthly_minor":699,"saving_percent":0,"charge_currency":"RUB","charge_minor":76800,"regional":{"CNY":{"price_minor":4800,"monthly_minor":4800,"saving_percent":0}}},
            {"interval_months":12,"currency":"GBP","price_minor":5999,"monthly_minor":499,"saving_percent":28,"charge_currency":"RUB","charge_minor":658400,"regional":{"CNY":{"price_minor":38800,"monthly_minor":3233,"saving_percent":32}}}]}]}}
        """;

    private static ColituTierCatalog Catalog() =>
        JsonSerializer.Deserialize<ColituEnvelope<ColituTierCatalog>>(CatalogJson)!.Data!;

    [Fact]
    public void Kyren_ChargesTheFixedYuanPrice()
    {
        var catalog = Catalog();
        var kyren = new ColituPaymentMethod { Key = "kyren", Provider = "kyren" };
        var currency = ColituBillingService.ChargeCurrency(kyren, catalog);
        currency.Should().Be("CNY");

        var price = ColituBillingService.PriceIn(catalog.Tiers[0], 12, currency)!;
        price.TotalMinor.Should().Be(38800);
        price.MonthlyMinor.Should().Be(3233);
        price.SavingPercent.Should().Be(32);
    }

    [Fact]
    public void Platega_ChargesRoubles()
    {
        var catalog = Catalog();
        var sbp = new ColituPaymentMethod { Key = "sbp", Provider = "platega" };
        var currency = ColituBillingService.ChargeCurrency(sbp, catalog);
        currency.Should().Be("RUB");

        var price = ColituBillingService.PriceIn(catalog.Tiers[0], 12, currency)!;
        price.TotalMinor.Should().Be(658400);
        price.MonthlyMinor.Should().Be(54900);
        price.SavingPercent.Should().Be(28);
        ColituBillingService.PriceIn(catalog.Tiers[0], 1, currency)!.MonthlyMinor.Should().Be(76800);
    }

    [Fact]
    public void UnknownPeriodOrCurrency_HasNoPrice()
    {
        var tier = Catalog().Tiers[0];
        ColituBillingService.PriceIn(tier, 24, "RUB").Should().BeNull();
        ColituBillingService.PriceIn(tier, 12, "USD").Should().BeNull();
    }

    [Theory]
    [InlineData(54800, "RUB", "548 ₽")]
    [InlineData(3800, "CNY", "¥38")]
    [InlineData(3233, "CNY", "¥32.33")]
    [InlineData(1000, "XYZ", "10 XYZ")]
    public void Money_FormatsMinorUnits(long minor, string currency, string expected)
    {
        ColituBillingService.Money(minor, currency, CultureInfo.InvariantCulture).Should().Be(expected);
    }

    [Theory]
    [InlineData("succeeded", true)]
    [InlineData("expired", true)]
    [InlineData("pending", false)]
    [InlineData(null, false)]
    public void IsSettled(string? status, bool expected)
    {
        ColituBillingService.IsSettled(status).Should().Be(expected);
    }
}
