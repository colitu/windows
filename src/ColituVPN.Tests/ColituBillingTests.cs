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

    // The same catalog with the toman list on (Rialit enabled), plus a smaller tier.
    private const string TomanCatalogJson = """
        {"data":{"display_currency":"GBP","charge_currency":"RUB","tiers":[
          {"plan":"personal","devices":3,"prices":[
            {"interval_months":12,"currency":"GBP","price_minor":3999,"monthly_minor":333,"saving_percent":30,"charge_currency":"RUB","charge_minor":438900,"regional":{"IRT":{"price_minor":125000000,"monthly_minor":10416666,"saving_percent":30}}}]},
          {"plan":"plus","devices":10,"prices":[
            {"interval_months":12,"currency":"GBP","price_minor":5999,"monthly_minor":499,"saving_percent":28,"charge_currency":"RUB","charge_minor":658400}]}]}}
        """;

    private static ColituTierCatalog TomanCatalog() =>
        JsonSerializer.Deserialize<ColituEnvelope<ColituTierCatalog>>(TomanCatalogJson)!.Data!;

    [Fact]
    public void Rialit_ChargesToman_NotTheCatalogsRoubles()
    {
        var catalog = TomanCatalog();
        // As GET /billing/payment-methods sends it (migration 000130).
        var rialit = new ColituPaymentMethod { Key = "rialit", DisplayName = "Shetab card (Rialit)", Provider = "rialit" };
        ColituBillingService.ChargeCurrency(rialit, catalog).Should().Be("IRT");
        // An older panel without the provider field: the key tells.
        ColituBillingService.ChargeCurrency(new ColituPaymentMethod { Key = "rialit" }, catalog).Should().Be("IRT");
        ColituBillingService.ChargeCurrency(new ColituPaymentMethod { Key = "card", Provider = "platega" }, catalog).Should().Be("RUB");
        ColituBillingService.ChargeCurrency(null, catalog).Should().Be("RUB");

        var price = ColituBillingService.PriceIn(catalog.Tiers[0], 12, "IRT")!;
        price.TotalMinor.Should().Be(125000000);
        // Whole toman a month (104,167), not 104,166.66.
        price.MonthlyMinor.Should().Be(10416700);
        ColituBillingService.PriceIn(catalog.Tiers[1], 12, "IRT").Should().BeNull();
    }

    [Fact]
    public void Rialit_IsHidden_WithoutATomanPriceList()
    {
        var rialit = new ColituPaymentMethod { Key = "rialit", Provider = "rialit" };
        ColituBillingService.IsSellable(rialit, TomanCatalog()).Should().BeTrue();
        ColituBillingService.IsSellable(rialit, Catalog()).Should().BeFalse();
        ColituBillingService.IsSellable(new ColituPaymentMethod { Key = "sbp", Provider = "platega" }, Catalog()).Should().BeTrue();
        ColituBillingService.IsSellable(new ColituPaymentMethod { Key = "kyren", Provider = "kyren" }, Catalog()).Should().BeTrue();
    }

    [Fact]
    public void Money_FormatsToman()
    {
        ColituBillingService.Money(125000000, "IRT", CultureInfo.InvariantCulture).Should().Be("1,250,000 toman");
        ColituBillingService.Money(125000000, "IRT", CultureInfo.GetCultureInfo("ru-RU")).Should().EndWith(" томан");
    }

    [Theory]
    [InlineData(0, "personal")]
    [InlineData(1, "personal")]
    [InlineData(3, "personal")]
    [InlineData(4, "plus")]
    [InlineData(25, "plus")]
    public void SuggestTier_IsTheSmallestPlanWithRoomForTheDevices(int devicesUsed, string expected)
    {
        ColituBillingService.SuggestTier(TomanCatalog(), devicesUsed).Should().Be(expected);
    }

    [Fact]
    public void IsLifetime_OnlyBeyondTenYears()
    {
        var now = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
        ColituBillingService.IsLifetime(now.AddDays(36493), now).Should().BeTrue();
        ColituBillingService.IsLifetime(now.AddYears(10).AddDays(2), now).Should().BeTrue();
        ColituBillingService.IsLifetime(now.AddYears(1), now).Should().BeFalse();
        ColituBillingService.IsLifetime(now.AddYears(9), now).Should().BeFalse();
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
