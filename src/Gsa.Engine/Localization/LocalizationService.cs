using Godot;

namespace Gsa.Engine.Localization;

/// <summary>Loads PO catalogs and stores only the explicit presentation-language preference.</summary>
public partial class LocalizationService : RefCounted
{
    [Signal]
    public delegate void LocaleChangedEventHandler(string locale);

    private static bool _catalogsLoaded;
    private string _settingsPath = "user://settings/locale.cfg";

    public string CurrentLocale { get; private set; } = LocalePolicy.English;

    public static string EnsureCatalogsLoaded(string? systemLocale = null, string settingsPath = "user://settings/locale.cfg")
    {
        if (!_catalogsLoaded)
        {
            foreach (var locale in new[] { "pt_BR", "en_US" })
            {
                var translation = ResourceLoader.Load<Translation>($"res://localization/{locale}.po");
                if (translation is null)
                    throw new InvalidOperationException($"Missing translation catalog: {locale}");
                TranslationServer.AddTranslation(translation);
            }
            _catalogsLoaded = true;
        }

        string? selected = null;
        if (settingsPath.StartsWith("user://", StringComparison.Ordinal))
        {
            using var settings = new ConfigFile();
            if (settings.Load(settingsPath) == Error.Ok && settings.HasSectionKey("presentation", "locale"))
            {
                var value = settings.GetValue("presentation", "locale");
                if (value.VariantType == Variant.Type.String)
                    selected = value.AsString();
            }
        }

        var resolved = LocalePolicy.Resolve(systemLocale ?? OS.GetLocale(), selected);
        TranslationServer.SetLocale(LocalePolicy.ToGodotLocale(resolved));
        return resolved;
    }

    public void Initialize(string? systemLocale = null, string settingsPath = "user://settings/locale.cfg")
    {
        if (!settingsPath.StartsWith("user://", StringComparison.Ordinal) ||
            settingsPath[7..].Split('/', '\\').Any(part =>
                string.IsNullOrWhiteSpace(part) || part is "." or ".." || part.Contains(':')))
            throw new ArgumentException("Locale settings must stay inside user://.", nameof(settingsPath));

        _settingsPath = settingsPath;
        ApplyLocale(EnsureCatalogsLoaded(systemLocale, settingsPath));
    }

    public Error SetLocale(string locale, bool persist = true)
    {
        if (!LocalePolicy.TryNormalize(locale, out var normalized))
            return Error.InvalidParameter;

        if (persist)
        {
            var parent = System.IO.Path.GetDirectoryName(ProjectSettings.GlobalizePath(_settingsPath));
            if (parent is null)
                return Error.InvalidParameter;
            var directory = DirAccess.MakeDirRecursiveAbsolute(parent);
            if (directory != Error.Ok)
                return directory;
            using var settings = new ConfigFile();
            settings.SetValue("presentation", "locale", normalized);
            var saved = settings.Save(_settingsPath);
            if (saved != Error.Ok)
                return saved;
        }
        ApplyLocale(normalized);
        return Error.Ok;
    }

    public string Translate(string key, string context = "") =>
        TranslationServer.Translate(key, context).ToString();

    public string TranslatePlural(string key, int count, string context = "")
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        var template = TranslationServer.TranslatePlural(key, key + ".plural", count, context).ToString();
        return string.Format(System.Globalization.CultureInfo.GetCultureInfo(CurrentLocale), template, count);
    }

    public string FormatNumber(double value, int decimals = 2) =>
        UiNumberFormatter.FormatCoordinate(value, CurrentLocale, decimals);

    private void ApplyLocale(string locale)
    {
        CurrentLocale = locale;
        TranslationServer.SetLocale(LocalePolicy.ToGodotLocale(locale));
        EmitSignal(SignalName.LocaleChanged, locale);
    }
}
