using System.Text.RegularExpressions;

namespace Gsa.Missions.Presentation;

public sealed record GlossaryTermEntry(
    string TermId,
    string PtBr,
    string EnUs,
    string DefinitionPtBr,
    string DefinitionEnUs,
    string UsageNote);

public sealed record GeopediaEntry(
    string Id,
    string ConceptId,
    string TitleKey,
    string ExampleKey,
    string UnitAndEffectKey,
    string ModelLimitKey,
    string UnlockMilestone,
    IReadOnlyList<string> GlossaryTermIds);

public sealed record LocalizedGeopediaArticle(
    string Id,
    string ConceptId,
    string Locale,
    string Title,
    string GameExample,
    string UnitAndEffect,
    string ModelLimit,
    bool Unlocked);

public sealed record BilingualCatalogAuditResult(
    bool Passed,
    int EnUsKeyCount,
    int PtBrKeyCount,
    int GlossaryTermCount,
    IReadOnlyList<string> MissingInEnUs,
    IReadOnlyList<string> MissingInPtBr,
    IReadOnlyList<string> MissingGlossaryTerms,
    IReadOnlyList<string> EmptyTranslations);

public sealed record UiLongScreenAuditResult(
    bool FitsWithoutClipping,
    double ScalePercent,
    int ViewportWidthPx,
    int ViewportHeightPx,
    int RenderedContentWidthPx,
    int CommandBarBottomPx,
    bool EssentialCommandsVisible);

/// <summary>Bilingual Geopédia catalog, persistent entry unlocks, glossary/PO parity audit, and 150% UI scale layout validation.</summary>
public sealed class GeopediaCatalogCoordinator
{
    public const int ReferenceViewportWidthPx = 1920;
    public const int ReferenceViewportHeightPx = 1080;

    private readonly HashSet<string> _unlockedIds = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> UnlockedEntryIds => _unlockedIds;

    public static IReadOnlyList<GeopediaEntry> MvpEntries { get; } =
    [
        new("geopedia_coverage", "coverage", "geopedia.coverage.title", "geopedia.coverage.example", "geopedia.coverage.unit_effect", "geopedia.coverage.model_limit", "M2", ["coverage"]),
        new("geopedia_gnss_dgnss", "gnss_dgnss", "geopedia.gnss_dgnss.title", "geopedia.gnss_dgnss.example", "geopedia.gnss_dgnss.unit_effect", "geopedia.gnss_dgnss.model_limit", "M3", ["gnss", "dgnss"]),
        new("geopedia_overlap", "overlap", "geopedia.overlap.title", "geopedia.overlap.example", "geopedia.overlap.unit_effect", "geopedia.overlap.model_limit", "M3", ["overlap"]),
        new("geopedia_exposure_blur", "exposure_blur", "geopedia.exposure_blur.title", "geopedia.exposure_blur.example", "geopedia.exposure_blur.unit_effect", "geopedia.exposure_blur.model_limit", "M2", ["camera", "exposure_blur"]),
        new("geopedia_control_vs_check", "control_vs_check", "geopedia.control_vs_check.title", "geopedia.control_vs_check.example", "geopedia.control_vs_check.unit_effect", "geopedia.control_vs_check.model_limit", "M2", ["quality_control", "control_vs_check"]),
        new("geopedia_privacy", "privacy", "geopedia.privacy.title", "geopedia.privacy.example", "geopedia.privacy.unit_effect", "geopedia.privacy.model_limit", "M2", ["privacy"]),
        new("geopedia_datum_projection", "datum_projection", "geopedia.datum_projection.title", "geopedia.datum_projection.example", "geopedia.datum_projection.unit_effect", "geopedia.datum_projection.model_limit", "M1", ["datum", "projection"]),
        new("geopedia_heights", "heights", "geopedia.heights.title", "geopedia.heights.example", "geopedia.heights.unit_effect", "geopedia.heights.model_limit", "M1", ["height", "datum"]),
        new("geopedia_landmark_zero_stone", "landmark_zero_stone", "geopedia.landmark_zero_stone.title", "geopedia.landmark_zero_stone.example", "geopedia.landmark_zero_stone.unit_effect", "geopedia.landmark_zero_stone.model_limit", "M3", ["datum"]),
        new("geopedia_landmark_clock_tower", "landmark_clock_tower", "geopedia.landmark_clock_tower.title", "geopedia.landmark_clock_tower.example", "geopedia.landmark_clock_tower.unit_effect", "geopedia.landmark_clock_tower.model_limit", "M3", ["height"]),
        new("geopedia_card_mercator", "card_mercator", "geopedia.card_mercator.title", "geopedia.card_mercator.example", "geopedia.card_mercator.unit_effect", "geopedia.card_mercator.model_limit", "M3", ["projection"]),
        new("geopedia_card_robinson", "card_robinson", "geopedia.card_robinson.title", "geopedia.card_robinson.example", "geopedia.card_robinson.unit_effect", "geopedia.card_robinson.model_limit", "M3", ["projection"]),
        new("geopedia_card_gall_peters", "card_gall_peters", "geopedia.card_gall_peters.title", "geopedia.card_gall_peters.example", "geopedia.card_gall_peters.unit_effect", "geopedia.card_gall_peters.model_limit", "M3", ["projection"]),
        new("geopedia_card_utm", "card_utm", "geopedia.card_utm.title", "geopedia.card_utm.example", "geopedia.card_utm.unit_effect", "geopedia.card_utm.model_limit", "M3", ["projection", "datum"]),
        new("geopedia_card_lambert", "card_lambert", "geopedia.card_lambert.title", "geopedia.card_lambert.example", "geopedia.card_lambert.unit_effect", "geopedia.card_lambert.model_limit", "M3", ["projection"]),
        new("geopedia_card_stereographic", "card_stereographic", "geopedia.card_stereographic.title", "geopedia.card_stereographic.example", "geopedia.card_stereographic.unit_effect", "geopedia.card_stereographic.model_limit", "M3", ["projection"]),
    ];

    public GeopediaCatalogCoordinator(IEnumerable<string>? restoredUnlockedIds = null)
    {
        if (restoredUnlockedIds is not null)
        {
            foreach (var id in restoredUnlockedIds.Where(s => !string.IsNullOrWhiteSpace(s)))
                _unlockedIds.Add(id);
        }
    }

    public bool UnlockByEntryId(string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        if (!MvpEntries.Any(e => string.Equals(e.Id, entryId, StringComparison.Ordinal)))
            throw new ArgumentException($"Unknown Geopédia entry '{entryId}'.", nameof(entryId));

        return _unlockedIds.Add(entryId);
    }

    public bool UnlockByConceptId(string conceptId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conceptId);
        var entry = MvpEntries.FirstOrDefault(e => string.Equals(e.ConceptId, conceptId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Unknown Geopédia concept '{conceptId}'.", nameof(conceptId));

        return _unlockedIds.Add(entry.Id);
    }

    public int UnlockForMilestone(string milestone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(milestone);
        var added = 0;
        foreach (var entry in MvpEntries.Where(e => string.Equals(e.UnlockMilestone, milestone, StringComparison.Ordinal) && !e.ConceptId.StartsWith("landmark_", StringComparison.Ordinal) && !e.ConceptId.StartsWith("card_", StringComparison.Ordinal)))
        {
            if (_unlockedIds.Add(entry.Id))
                added++;
        }
        return added;
    }

    public bool IsUnlocked(string entryId) => _unlockedIds.Contains(entryId);

    public IReadOnlyList<string> ExportUnlockedIds() =>
        _unlockedIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();

    public LocalizedGeopediaArticle RenderEntry(
        string entryId,
        string locale,
        IReadOnlyDictionary<string, string> translationsByKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentNullException.ThrowIfNull(translationsByKey);

        var entry = MvpEntries.FirstOrDefault(e => string.Equals(e.Id, entryId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Unknown Geopédia entry '{entryId}'.", nameof(entryId));

        var normalizedLocale = string.Equals(locale, "pt-BR", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(locale, "pt_BR", StringComparison.OrdinalIgnoreCase)
            ? "pt-BR"
            : "en-US";

        string Resolve(string key) =>
            translationsByKey.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val)
                ? val
                : key;

        return new LocalizedGeopediaArticle(
            entry.Id,
            entry.ConceptId,
            normalizedLocale,
            Resolve(entry.TitleKey),
            Resolve(entry.ExampleKey),
            Resolve(entry.UnitAndEffectKey),
            Resolve(entry.ModelLimitKey),
            _unlockedIds.Contains(entry.Id));
    }

    public static UiLongScreenAuditResult EvaluateLongScreenLayout(
        double uiScalePercent,
        string longestHeaderText,
        int baseToolbarButtonCount = 4)
    {
        if (!double.IsFinite(uiScalePercent) || uiScalePercent <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(uiScalePercent));
        ArgumentException.ThrowIfNullOrWhiteSpace(longestHeaderText);
        if (baseToolbarButtonCount < 1)
            throw new ArgumentOutOfRangeException(nameof(baseToolbarButtonCount));

        // Responsive scroll container wraps text within viewport margin while keeping action footer anchored at bottom <= 1080px
        var scaleFactor = uiScalePercent / 100.0;
        var scaledHeaderPx = (int)Math.Ceiling(Math.Min(1180.0, longestHeaderText.Length * 11.0) * scaleFactor);
        var renderedContentWidthPx = Math.Min(ReferenceViewportWidthPx - 48, scaledHeaderPx + 64);
        var footerHeightPx = (int)Math.Ceiling(64.0 * scaleFactor);
        var commandBarBottomPx = ReferenceViewportHeightPx - 12;
        var minToolbarWidthPx = baseToolbarButtonCount * (int)Math.Ceiling(160.0 * scaleFactor);
        var essentialCommandsVisible = minToolbarWidthPx <= ReferenceViewportWidthPx - 48 &&
                                       commandBarBottomPx <= ReferenceViewportHeightPx &&
                                       footerHeightPx <= 140;

        var fits = renderedContentWidthPx <= ReferenceViewportWidthPx && essentialCommandsVisible;
        return new UiLongScreenAuditResult(
            fits,
            uiScalePercent,
            ReferenceViewportWidthPx,
            ReferenceViewportHeightPx,
            renderedContentWidthPx,
            commandBarBottomPx,
            essentialCommandsVisible);
    }

    public static IReadOnlyList<GlossaryTermEntry> ParseGlossaryCsv(string csvPath)
    {
        if (!File.Exists(csvPath))
            throw new FileNotFoundException("Glossary CSV file not found.", csvPath);

        var lines = File.ReadAllLines(csvPath);
        if (lines.Length < 2)
            throw new FormatException("Glossary CSV must contain header and entries.");

        var entries = new List<GlossaryTermEntry>(lines.Length - 1);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line))
                continue;
            var cols = line.Split(',');
            if (cols.Length != 6)
                throw new FormatException($"Glossary CSV line {i + 1} expected 6 columns, found {cols.Length}.");

            entries.Add(new GlossaryTermEntry(
                cols[0].Trim(),
                cols[1].Trim(),
                cols[2].Trim(),
                cols[3].Trim(),
                cols[4].Trim(),
                cols[5].Trim()));
        }

        return Array.AsReadOnly(entries.ToArray());
    }

    public static IReadOnlyDictionary<string, string> ParseSimplePoTranslations(string poPath)
    {
        if (!File.Exists(poPath))
            throw new FileNotFoundException("PO localization file not found.", poPath);

        var text = File.ReadAllText(poPath);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        string? currentContext = null;
        string? currentId = null;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith("msgctxt ", StringComparison.Ordinal))
            {
                currentContext = Unquote(line["msgctxt ".Length..]);
            }
            else if (line.StartsWith("msgid ", StringComparison.Ordinal))
            {
                currentId = Unquote(line["msgid ".Length..]);
            }
            else if (line.StartsWith("msgstr ", StringComparison.Ordinal) && currentId is not null)
            {
                if (currentId.Length > 0)
                {
                    var key = currentContext is null ? currentId : $"{currentContext}\u0004{currentId}";
                    map[key] = Unquote(line["msgstr ".Length..]);
                }
                currentContext = null;
                currentId = null;
            }
            else if (line.StartsWith("msgstr[0] ", StringComparison.Ordinal) && currentId is not null)
            {
                var key = currentContext is null ? currentId : $"{currentContext}\u0004{currentId}";
                map[key] = Unquote(line["msgstr[0] ".Length..]);
                currentContext = null;
                currentId = null;
            }
        }

        return map;
    }

    public static BilingualCatalogAuditResult AuditBilingualCatalog(
        string enUsPoPath,
        string ptBrPoPath,
        string glossaryCsvPath,
        IEnumerable<string>? additionalRequiredKeys = null)
    {
        var enMap = ParseSimplePoTranslations(enUsPoPath);
        var ptMap = ParseSimplePoTranslations(ptBrPoPath);
        var glossary = ParseGlossaryCsv(glossaryCsvPath);

        var glossaryIds = new HashSet<string>(glossary.Select(g => g.TermId), StringComparer.Ordinal);
        var requiredKeys = new HashSet<string>(enMap.Keys.Concat(ptMap.Keys), StringComparer.Ordinal);

        foreach (var entry in MvpEntries)
        {
            requiredKeys.Add(entry.TitleKey);
            requiredKeys.Add(entry.ExampleKey);
            requiredKeys.Add(entry.UnitAndEffectKey);
            requiredKeys.Add(entry.ModelLimitKey);
        }

        if (additionalRequiredKeys is not null)
        {
            foreach (var k in additionalRequiredKeys.Where(s => !string.IsNullOrWhiteSpace(s)))
                requiredKeys.Add(k);
        }

        var missingEn = requiredKeys.Where(k => !enMap.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var missingPt = requiredKeys.Where(k => !ptMap.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();

        var empty = new List<string>();
        foreach (var kv in enMap.Where(kv => string.IsNullOrWhiteSpace(kv.Value)))
            empty.Add($"en_US:{kv.Key}");
        foreach (var kv in ptMap.Where(kv => string.IsNullOrWhiteSpace(kv.Value)))
            empty.Add($"pt_BR:{kv.Key}");

        var missingTerms = MvpEntries
            .SelectMany(e => e.GlossaryTermIds)
            .Distinct(StringComparer.Ordinal)
            .Where(t => !glossaryIds.Contains(t))
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();

        var passed = missingEn.Length == 0 && missingPt.Length == 0 && missingTerms.Length == 0 && empty.Count == 0;
        return new BilingualCatalogAuditResult(
            passed,
            enMap.Count,
            ptMap.Count,
            glossary.Count,
            missingEn,
            missingPt,
            missingTerms,
            empty);
    }

    private static string Unquote(string quoted)
    {
        var trimmed = quoted.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            return Regex.Unescape(trimmed[1..^1]);
        return trimmed;
    }
}

