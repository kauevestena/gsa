namespace Gsa.Engine.Localization;

/// <summary>Locale selection is a presentation policy; it never changes global .NET culture.</summary>
public static class LocalePolicy
{
    public const string Portuguese = "pt-BR";
    public const string English = "en-US";

    public static string Resolve(string? systemLocale, string? explicitLocale = null)
    {
        if (TryNormalize(explicitLocale, out var selected))
            return selected;
        return TryNormalize(systemLocale, out selected) ? selected : English;
    }

    public static bool TryNormalize(string? locale, out string normalized)
    {
        var value = locale?.Trim().Replace('_', '-');
        if (string.Equals(value, "pt", StringComparison.OrdinalIgnoreCase) ||
            value?.StartsWith("pt-", StringComparison.OrdinalIgnoreCase) == true)
        {
            normalized = Portuguese;
            return true;
        }
        if (string.Equals(value, "en", StringComparison.OrdinalIgnoreCase) ||
            value?.StartsWith("en-", StringComparison.OrdinalIgnoreCase) == true)
        {
            normalized = English;
            return true;
        }
        normalized = English;
        return false;
    }

    public static string ToGodotLocale(string locale) => Resolve(locale).Replace('-', '_');
}
