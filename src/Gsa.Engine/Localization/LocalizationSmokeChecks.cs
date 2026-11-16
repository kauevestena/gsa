using System.Globalization;
using System.Text.Json;
using Godot;

namespace Gsa.Engine.Localization;

/// <summary>Headless integration fixture for real PO lookup, runtime switching and settings persistence.</summary>
public static class LocalizationSmokeChecks
{
    public static void Run(LocalizationService service)
    {
        var initialLocale = service.CurrentLocale;
        var initialCulture = CultureInfo.CurrentCulture;
        var initialUiCulture = CultureInfo.CurrentUICulture;
        var observations = new { east_m = 1234.56, north_m = 7400000.125 };
        var scientificJson = JsonSerializer.Serialize(observations);
        var settingsPath = "user://tests/locale-" + Guid.NewGuid().ToString("N") + ".cfg";

        try
        {
            using var fixture = new LocalizationService();
            fixture.Initialize("de-DE", settingsPath);
            Require(fixture.CurrentLocale == "en-US", "Unsupported system locale must fall back to en-US.");
            var notifications = 0;
            fixture.LocaleChanged += _ => notifications++;
            Require(fixture.SetLocale("pt-BR") == Error.Ok, "Locale settings must persist.");
            Require(notifications == 1, "Runtime language switch must notify presentation listeners.");
            Require(fixture.Translate("bootstrap.status") == "Em desenvolvimento · Treinamento de levantamento", "PT-BR PO lookup failed.");
            Require(fixture.Translate("training.title") == "Treinamento · Três fachadas", "PT-BR training PO lookup failed.");
            Require(fixture.TranslatePlural("fixture.sample_count", 0) == "0 amostras", "PT-BR zero plural failed.");
            Require(fixture.TranslatePlural("fixture.sample_count", 1) == "1 amostra", "PT-BR singular failed.");
            Require(fixture.TranslatePlural("fixture.sample_count", 2) == "2 amostras", "PT-BR plural failed.");
            Require(fixture.Translate("fixture.context_value", "quality") == "Aprovado", "Quality context failed.");
            Require(fixture.Translate("fixture.context_value", "quantity") == "Quantidade", "Quantity context failed.");
            Require(fixture.FormatNumber(1234.56) == "1234,56", "PT-BR coordinate formatting failed.");

            using var reloaded = new LocalizationService();
            reloaded.Initialize("en-US", settingsPath);
            Require(reloaded.CurrentLocale == "pt-BR", "Explicit choice must survive restart and override system language.");
            Require(reloaded.SetLocale("en-US", persist: false) == Error.Ok, "EN-US switch failed.");
            Require(reloaded.Translate("bootstrap.status") == "In development · Live survey training", "EN-US PO lookup failed.");
            Require(reloaded.Translate("training.title") == "Training · Three façades", "EN-US training PO lookup failed.");
            Require(reloaded.TranslatePlural("fixture.sample_count", 1) == "1 sample", "EN-US singular failed.");
            Require(reloaded.TranslatePlural("fixture.sample_count", 2) == "2 samples", "EN-US plural failed.");
            Require(reloaded.Translate("fixture.context_value", "quality") == "Approved", "EN-US context failed.");
            Require(reloaded.FormatNumber(1234.56) == "1,234.56", "EN-US coordinate formatting failed.");
            Require(reloaded.SetLocale("fr-FR", persist: false) == Error.InvalidParameter &&
                    reloaded.CurrentLocale == "en-US", "Unsupported explicit locale must be rejected without mutation.");

            using var stillPersisted = new LocalizationService();
            stillPersisted.Initialize("en-US", settingsPath);
            Require(stillPersisted.CurrentLocale == "pt-BR", "Nonpersistent switch must not overwrite saved choice.");
            Require(JsonSerializer.Serialize(observations) == scientificJson, "Language switch changed scientific JSON.");
            Require(ReferenceEquals(initialCulture, CultureInfo.CurrentCulture) &&
                    ReferenceEquals(initialUiCulture, CultureInfo.CurrentUICulture), "Language switch changed ambient .NET culture.");
            GD.Print("GSA_LOCALIZATION_SMOKE_OK locales=pt-BR,en-US plural=context=persistence=json");
        }
        finally
        {
            service.SetLocale(initialLocale, persist: false);
            if (Godot.FileAccess.FileExists(settingsPath))
                DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(settingsPath));
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
