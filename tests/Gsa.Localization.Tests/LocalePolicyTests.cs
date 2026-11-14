using System.Globalization;
using System.Text.Json;
using Gsa.Engine.Localization;
using Xunit;

namespace Gsa.Localization.Tests;

public class LocalePolicyTests
{
    [Theory]
    [InlineData("pt", "pt-BR")]
    [InlineData("pt_PT", "pt-BR")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("en_GB", "en-US")]
    [InlineData("ja_JP", "en-US")]
    [InlineData("", "en-US")]
    [InlineData(null, "en-US")]
    public void SystemLanguageUsesSupportedFallback(string? system, string expected) =>
        Assert.Equal(expected, LocalePolicy.Resolve(system));

    [Theory]
    [InlineData("pt-BR", "en-US", "en-US")]
    [InlineData("en-US", "pt_BR", "pt-BR")]
    [InlineData("pt-BR", "de-DE", "pt-BR")]
    public void ExplicitSupportedChoiceOverridesSystem(string system, string selected, string expected) =>
        Assert.Equal(expected, LocalePolicy.Resolve(system, selected));

    [Theory]
    [InlineData("pt-BR", "1234,56")]
    [InlineData("en-US", "1,234.56")]
    public void CoordinatesUseNormativeCultureFixture(string locale, string expected) =>
        Assert.Equal(expected, UiNumberFormatter.FormatCoordinate(1234.56, locale));

    [Fact]
    public void LanguageChangesDoNotMutateAmbientCultureOrScientificJson()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var data = new { east_m = 1234.56, north_m = 7400000.125 };
        var originalJson = JsonSerializer.Serialize(data);

        UiNumberFormatter.FormatCoordinate(data.east_m, "pt-BR");
        UiNumberFormatter.FormatCoordinate(data.east_m, "en-US");

        Assert.Same(originalCulture, CultureInfo.CurrentCulture);
        Assert.Same(originalUiCulture, CultureInfo.CurrentUICulture);
        Assert.Equal(originalJson, JsonSerializer.Serialize(data));
        Assert.Contains("1234.56", originalJson);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonfiniteCoordinatesAreRejected(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => UiNumberFormatter.FormatCoordinate(value, "pt-BR"));

    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    public void InvalidPrecisionIsRejected(int decimals) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => UiNumberFormatter.Format(1, "en-US", decimals));
}
