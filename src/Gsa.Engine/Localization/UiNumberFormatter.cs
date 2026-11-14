using System.Globalization;

namespace Gsa.Engine.Localization;

/// <summary>Culture-aware display strings; persisted simulation numbers remain JSON numbers.</summary>
public static class UiNumberFormatter
{
    public static string Format(double value, string locale, int decimals = 2, bool groupThousands = true)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "UI numeric values must be finite.");
        if (decimals is < 0 or > 15)
            throw new ArgumentOutOfRangeException(nameof(decimals), "Use between zero and fifteen decimals.");

        var culture = CultureInfo.GetCultureInfo(LocalePolicy.Resolve(locale));
        return value.ToString((groupThousands ? "N" : "F") + decimals, culture);
    }

    /// <summary>Normative coordinate fixture: 1234,56 (PT-BR) and 1,234.56 (EN-US).</summary>
    public static string FormatCoordinate(double value, string locale, int decimals = 2) =>
        Format(value, locale, decimals, groupThousands: LocalePolicy.Resolve(locale) != LocalePolicy.Portuguese);
}
