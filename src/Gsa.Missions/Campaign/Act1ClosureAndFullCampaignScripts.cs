using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Missions.Content;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Office;
using Gsa.Missions.Persistence;

namespace Gsa.Missions.Campaign;

public sealed record NarrativeBeatSpec(
    MissionBeatKind BeatKind,
    string BeatId,
    string SpeakerId,
    string DialogueCueId,
    string TechnicalContextKey);

public sealed record CutsceneSpec(
    string CutsceneId,
    string MissionId,
    bool Skippable,
    double DurationSeconds,
    IReadOnlyList<string> GrantUnlockedPlatforms,
    IReadOnlyList<string> GrantUnlockedRegions,
    IReadOnlyList<string> GrantUnlockedEquipment,
    string ObjectiveStateTransition);

public sealed record CalmVariantSpec(
    string VariantMissionId,
    string BaseMissionId,
    string ProductType,
    int RewardCoords,
    int ReputationDelta,
    IReadOnlyList<string> UnlockedPlatforms,
    IReadOnlyList<string> UnlockedRegions,
    IReadOnlyList<string> UnlockedEquipment,
    bool HostileHazardsDisabled,
    bool RequiresPlayerWeapons);

public sealed record CampaignMissionFullSpec(
    string MissionId,
    int Act,
    string TitleKey,
    IReadOnlyList<string> RegionIds,
    int MinimumPlatformLevel,
    string MinimumPlatformId,
    IReadOnlyList<string> Preconditions,
    string ProductType,
    int RewardCoords,
    int ReputationDelta,
    IReadOnlyList<string> UnlockedPlatforms,
    IReadOnlyList<string> UnlockedRegions,
    IReadOnlyList<string> UnlockedEquipment,
    IReadOnlyList<NarrativeBeatSpec> Beats,
    string OnFailureCueId,
    string OnReworkCueId,
    CutsceneSpec Cutscene,
    bool HasHostileOrDynamicHazards,
    CalmVariantSpec? CalmVariant,
    bool DependsOnAudiovisualCue,
    bool RequiresPlayerWeapons);

public sealed record CampaignGraphValidationResult(
    bool Valid,
    int MissionCount,
    int CalmVariantCount,
    IReadOnlyList<string> Errors);

/// <summary>Snapshot-safe save progression state for validating cutscene full-watch vs skip equivalence.</summary>
public sealed class CampaignSaveProgressState : IEquatable<CampaignSaveProgressState>
{
    public HashSet<string> UnlockedPlatforms { get; }
    public HashSet<string> UnlockedRegions { get; }
    public HashSet<string> UnlockedEquipment { get; }
    public HashSet<string> AppliedCutsceneIds { get; }
    public HashSet<string> SkippedCutsceneIds { get; }
    public Dictionary<string, string> ObjectiveStates { get; }

    public CampaignSaveProgressState(
        IEnumerable<string>? platforms = null,
        IEnumerable<string>? regions = null,
        IEnumerable<string>? equipment = null)
    {
        UnlockedPlatforms = new HashSet<string>(platforms ?? ["cart"], StringComparer.Ordinal);
        UnlockedRegions = new HashSet<string>(regions ?? ["old_center"], StringComparer.Ordinal);
        UnlockedEquipment = new HashSet<string>(equipment ?? ["cam_initial", "gnss_basic"], StringComparer.Ordinal);
        AppliedCutsceneIds = new HashSet<string>(StringComparer.Ordinal);
        SkippedCutsceneIds = new HashSet<string>(StringComparer.Ordinal);
        ObjectiveStates = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public bool Equals(CampaignSaveProgressState? other)
    {
        if (other is null)
            return false;

        return UnlockedPlatforms.SetEquals(other.UnlockedPlatforms) &&
               UnlockedRegions.SetEquals(other.UnlockedRegions) &&
               UnlockedEquipment.SetEquals(other.UnlockedEquipment) &&
               AppliedCutsceneIds.SetEquals(other.AppliedCutsceneIds) &&
               ObjectiveStates.Count == other.ObjectiveStates.Count &&
               ObjectiveStates.All(kv => other.ObjectiveStates.TryGetValue(kv.Key, out var val) && string.Equals(kv.Value, val, StringComparison.Ordinal));
    }

    public override bool Equals(object? obj) => Equals(obj as CampaignSaveProgressState);
    public override int GetHashCode() => UnlockedPlatforms.Count ^ (UnlockedRegions.Count << 4) ^ (ObjectiveStates.Count << 8);
}

/// <summary>Applies skippable cutscenes idempotently so watching vs skipping guarantees identical gameplay/unlock state.</summary>
public static class CutsceneSkipAndStateCoordinator
{
    public static bool ExecuteFull(CutsceneSpec cutscene, CampaignSaveProgressState state)
    {
        ArgumentNullException.ThrowIfNull(cutscene);
        ArgumentNullException.ThrowIfNull(state);
        return ApplyInternal(cutscene, state, skipped: false);
    }

    public static bool SkipCutscene(CutsceneSpec cutscene, CampaignSaveProgressState state)
    {
        ArgumentNullException.ThrowIfNull(cutscene);
        ArgumentNullException.ThrowIfNull(state);
        if (!cutscene.Skippable)
            throw new InvalidOperationException($"Cutscene '{cutscene.CutsceneId}' must be skippable.");

        return ApplyInternal(cutscene, state, skipped: true);
    }

    private static bool ApplyInternal(CutsceneSpec cutscene, CampaignSaveProgressState state, bool skipped)
    {
        if (skipped)
            state.SkippedCutsceneIds.Add(cutscene.CutsceneId);

        if (!state.AppliedCutsceneIds.Add(cutscene.CutsceneId))
            return false;

        foreach (var p in cutscene.GrantUnlockedPlatforms)
            state.UnlockedPlatforms.Add(p);
        foreach (var r in cutscene.GrantUnlockedRegions)
            state.UnlockedRegions.Add(r);
        foreach (var e in cutscene.GrantUnlockedEquipment)
            state.UnlockedEquipment.Add(e);

        state.ObjectiveStates[cutscene.MissionId] = cutscene.ObjectiveStateTransition;
        return true;
    }
}

/// <summary>Full 3-act narrative campaign catalog (HIST-01..HIST-11) with 5 beats, calm variants, and Act 1 closure rules (T-F10-03).</summary>
public static class FullCampaignCatalog
{
    public static IReadOnlyList<CampaignMissionFullSpec> AllMissions { get; } = BuildCatalog();

    public static CampaignMissionFullSpec GetMission(string missionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        return AllMissions.FirstOrDefault(m => string.Equals(m.MissionId, missionId, StringComparison.Ordinal))
               ?? throw new KeyNotFoundException($"Campaign mission '{missionId}' not found.");
    }

    public static CampaignGraphValidationResult ValidateCampaignGraphAndCalmParity()
    {
        var errors = new List<string>();
        if (AllMissions.Count != 11)
            errors.Add($"Expected 11 story missions (HIST-01..HIST-11), found {AllMissions.Count}.");

        var byId = AllMissions.ToDictionary(m => m.MissionId, StringComparer.Ordinal);
        for (var i = 1; i <= 11; i++)
        {
            var expectedId = $"HIST-{i:00}";
            if (!byId.ContainsKey(expectedId))
                errors.Add($"Missing expected mission '{expectedId}'.");
        }

        foreach (var m in AllMissions)
        {
            if (m.Beats.Count < 5)
                errors.Add($"{m.MissionId}: requires at least 5 narrative beats.");

            var beatKinds = m.Beats.Select(b => b.BeatKind).ToHashSet();
            foreach (MissionBeatKind k in Enum.GetValues<MissionBeatKind>())
            {
                if (!beatKinds.Contains(k))
                    errors.Add($"{m.MissionId}: missing mandatory beat '{k}'.");
            }

            if (string.IsNullOrWhiteSpace(m.OnFailureCueId) || string.IsNullOrWhiteSpace(m.OnReworkCueId))
                errors.Add($"{m.MissionId}: missing failure or rework dialogue cue.");

            if (!m.Cutscene.Skippable)
                errors.Add($"{m.MissionId}: cutscene must be skippable.");

            if (m.DependsOnAudiovisualCue)
                errors.Add($"{m.MissionId}: cannot gate objectives behind audiovisual-only cues.");

            if (m.RequiresPlayerWeapons)
                errors.Add($"{m.MissionId}: player weapons are forbidden.");

            foreach (var pre in m.Preconditions)
            {
                if (!byId.ContainsKey(pre))
                    errors.Add($"{m.MissionId}: precondition '{pre}' does not exist.");
            }

            if (m.HasHostileOrDynamicHazards)
            {
                if (m.CalmVariant is null)
                {
                    errors.Add($"{m.MissionId}: hazardous mission missing CalmVariant.");
                }
                else
                {
                    var cv = m.CalmVariant;
                    if (!cv.HostileHazardsDisabled || cv.RequiresPlayerWeapons)
                        errors.Add($"{m.MissionId}: calm variant must disable hostile hazards and forbid player weapons.");
                    if (!string.Equals(cv.ProductType, m.ProductType, StringComparison.Ordinal) ||
                        cv.RewardCoords != m.RewardCoords ||
                        cv.ReputationDelta != m.ReputationDelta ||
                        !cv.UnlockedPlatforms.SequenceEqual(m.UnlockedPlatforms) ||
                        !cv.UnlockedRegions.SequenceEqual(m.UnlockedRegions) ||
                        !cv.UnlockedEquipment.SequenceEqual(m.UnlockedEquipment))
                    {
                        errors.Add($"{m.MissionId}: calm variant rewards/product/unlocks diverge from standard mission.");
                    }
                }
            }
        }

        // Acyclic graph check + Act 1 closure verification on HIST-04
        var hist04 = byId["HIST-04"];
        if (hist04.Act != 1 ||
            !hist04.UnlockedPlatforms.Contains("car", StringComparer.Ordinal) ||
            !hist04.UnlockedRegions.Contains("port", StringComparer.Ordinal) ||
            !hist04.UnlockedRegions.Contains("waterfront", StringComparer.Ordinal))
        {
            errors.Add("HIST-04 must close Act 1 by unlocking car platform plus port and waterfront regions.");
        }

        var calmCount = AllMissions.Count(m => m.CalmVariant is not null);
        return new CampaignGraphValidationResult(errors.Count == 0, AllMissions.Count, calmCount, Array.AsReadOnly(errors.ToArray()));
    }

    private static IReadOnlyList<CampaignMissionFullSpec> BuildCatalog() =>
    [
        CreateSpec("HIST-01", 1, "mission.hist_01.title", ["old_center"], 0, "cart", [], "images", 300, 2,
            [], [], [], "dona_clara", "on_fail_hist_01", "on_rework_hist_01", false, 30.0),
        CreateSpec("HIST-02", 1, "mission.hist_02.title", ["old_center"], 0, "cart", ["HIST-01"], "feature_inventory", 500, 3,
            ["bicycle"], [], ["rack_bike_clip"], "eng_arnaldo", "on_fail_hist_02", "on_rework_hist_02", false, 35.0),
        CreateSpec("HIST-03", 1, "mission.hist_03.title", ["old_center"], 1, "bicycle", ["HIST-02"], "feature_inventory", 700, 2,
            ["scooter"], [], ["gimbal_scooter_mount"], "inspetora_bia", "on_fail_hist_03", "on_rework_hist_03", false, 35.0),
        CreateSpec("HIST-04", 1, "mission.hist_04.title", ["old_center"], 2, "scooter", ["HIST-03"], "feature_inventory", 900, 10,
            ["car"], ["port", "waterfront"], ["rack_car_basic", "imu_tactical_m4"], "eng_arnaldo", "on_fail_hist_04", "on_rework_hist_04", false, 45.0),
        CreateSpec("HIST-05", 2, "mission.hist_05.title", ["port"], 3, "car", ["HIST-04"], "point_cloud", 1450, 8,
            [], [], ["rtk_radio_link"], "mestre_bastos", "on_fail_hist_05", "on_rework_hist_05", true, 50.0),
        CreateSpec("HIST-06", 2, "mission.hist_06.title", ["waterfront"], 3, "car", ["HIST-05"], "point_cloud", 1650, 8,
            ["kombi"], [], ["camera_360_m4"], "dona_clara", "on_fail_hist_06", "on_rework_hist_06", true, 50.0),
        CreateSpec("HIST-07", 2, "mission.hist_07.title", ["port"], 4, "kombi", ["HIST-06"], "point_cloud", 2200, 10,
            [], [], ["lidar_single_beam"], "eng_arnaldo", "on_fail_hist_07", "on_rework_hist_07", true, 55.0),
        CreateSpec("HIST-08", 2, "mission.hist_08.title", ["port", "waterfront"], 4, "kombi", ["HIST-07"], "images", 2600, 12,
            ["pickup"], ["hills"], ["imu_nav_grade", "dmi_wheel_encoder"], "inspetora_bia", "on_fail_hist_08", "on_rework_hist_08", true, 60.0),
        CreateSpec("HIST-09", 3, "mission.hist_09.title", ["hills"], 5, "pickup", ["HIST-08"], "pavement_profile", 3400, 15,
            [], [], ["lidar_multi_beam"], "mestre_bastos", "on_fail_hist_09", "on_rework_hist_09", true, 65.0),
        CreateSpec("HIST-10", 3, "mission.hist_10.title", ["old_center", "port"], 5, "pickup", ["HIST-09"], "point_cloud", 4200, 18,
            ["van"], [], ["van_workstation_rack"], "dona_clara", "on_fail_hist_10", "on_rework_hist_10", true, 70.0),
        CreateSpec("HIST-11", 3, "mission.hist_11.title", ["old_center", "port", "waterfront", "hills"], 6, "van", ["HIST-10"], "pavement_profile", 6000, 25,
            [], [], ["master_geodesist_seal"], "eng_arnaldo", "on_fail_hist_11", "on_rework_hist_11", true, 90.0),
    ];

    private static CampaignMissionFullSpec CreateSpec(
        string id,
        int act,
        string titleKey,
        IReadOnlyList<string> regions,
        int minLevel,
        string minPlatform,
        IReadOnlyList<string> preconditions,
        string productType,
        int rewardCoords,
        int repDelta,
        IReadOnlyList<string> unlockedPlatforms,
        IReadOnlyList<string> unlockedRegions,
        IReadOnlyList<string> unlockedEquipment,
        string primarySpeaker,
        string failCue,
        string reworkCue,
        bool hasHazards,
        double cutsceneSeconds)
    {
        var slug = id.ToLowerInvariant().Replace('-', '_');
        NarrativeBeatSpec[] beats =
        [
            new(MissionBeatKind.Proposal, $"{slug}_proposal", primarySpeaker, $"{slug}_dlg_proposal", $"ctx.{slug}.proposal"),
            new(MissionBeatKind.Preparation, $"{slug}_preparation", "dona_clara", $"{slug}_dlg_preparation", $"ctx.{slug}.preparation"),
            new(MissionBeatKind.TechnicalDiscovery, $"{slug}_technical_discovery", "eng_arnaldo", $"{slug}_dlg_discovery", $"ctx.{slug}.discovery"),
            new(MissionBeatKind.SetbackOrCorrection, $"{slug}_setback_or_correction", "inspetora_bia", $"{slug}_dlg_setback", $"ctx.{slug}.setback"),
            new(MissionBeatKind.DeliveryAndConsequence, $"{slug}_delivery_and_consequence", primarySpeaker, $"{slug}_dlg_delivery", $"ctx.{slug}.delivery"),
        ];

        var cutscene = new CutsceneSpec(
            $"cut_{slug}",
            id,
            Skippable: true,
            cutsceneSeconds,
            unlockedPlatforms,
            unlockedRegions,
            unlockedEquipment,
            $"completed:{id}");

        CalmVariantSpec? calmVariant = hasHazards
            ? new CalmVariantSpec(
                $"{id}-CALM",
                id,
                productType,
                rewardCoords,
                repDelta,
                unlockedPlatforms,
                unlockedRegions,
                unlockedEquipment,
                HostileHazardsDisabled: true,
                RequiresPlayerWeapons: false)
            : null;

        return new CampaignMissionFullSpec(
            id,
            act,
            titleKey,
            regions,
            minLevel,
            minPlatform,
            preconditions,
            productType,
            rewardCoords,
            repDelta,
            unlockedPlatforms,
            unlockedRegions,
            unlockedEquipment,
            beats,
            $"{slug}_{failCue}",
            $"{slug}_{reworkCue}",
            cutscene,
            hasHazards,
            calmVariant,
            DependsOnAudiovisualCue: false,
            RequiresPlayerWeapons: false);
    }
}

public sealed record Act1ClosureReport(
    bool Passed,
    MissionExecutionSummary Hist04Summary,
    bool CarUnlockedInFleet,
    bool PortUnlocked,
    bool WaterfrontUnlocked,
    int FinalLedgerCoords,
    int GlobalReputation);

/// <summary>Executes Act 1 through HIST-04 closure, verifying scooter acquisition, 12 signs CQ, car unlock, and Port/Waterfront opening.</summary>
public static class Act1ClosureRunner
{
    public static Act1ClosureReport RunAct1Closure(string contentDirectory, string worldManifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldManifestPath);

        var mvpReport = MvpCampaignRunner.RunMvpThreeMissionPlaythrough(contentDirectory, worldManifestPath);
        if (!mvpReport.AllThreeMissionsPassed)
            throw new InvalidOperationException("MVP missions HIST-01..03 must pass before HIST-04 closure.");

        var missions = Directory.GetFiles(Path.Combine(contentDirectory, "missions"), "*.json")
            .Order(StringComparer.Ordinal)
            .Select(p => MissionDefinition.Parse(File.ReadAllText(p), Path.GetFileName(p)))
            .ToDictionary(m => m.Id, StringComparer.Ordinal);
        var contracts = Directory.GetFiles(Path.Combine(contentDirectory, "contracts"), "*.json")
            .Order(StringComparer.Ordinal)
            .Select(p => ContractDefinition.Parse(File.ReadAllText(p), Path.GetFileName(p)))
            .ToDictionary(c => c.Id, StringComparer.Ordinal);

        var hist04Def = missions["HIST-04"];
        var hist04Contract = contracts[hist04Def.Objectives.Single().ContractId];

        var regionDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(contentDirectory, "regions", "old-center.json")));
        var featuresById = regionDoc.RootElement.GetProperty("features").EnumerateArray()
            .ToDictionary(
                el => el.GetProperty("id").GetString()!,
                el =>
                {
                    var p = el.GetProperty("position_enu");
                    return new Vector3d(p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble());
                },
                StringComparer.Ordinal);

        var coordinator = new StoryCampaignCoordinator(missions.Values);
        coordinator.CompleteMission("HIST-01", 0, skipAllCutscenes: true);
        coordinator.CompleteMission("HIST-02", 0, skipAllCutscenes: true);
        coordinator.CompleteMission("HIST-03", 1, skipAllCutscenes: true);

        if (!coordinator.CanStartMission("HIST-04", activePlatformLevel: 2))
            throw new InvalidOperationException("HIST-04 must be unlockable once HIST-03 completes and Scooter (level 2) is active.");

        var ledger = new CareerLedger(mvpReport.FinalBalanceCoords);
        var progression = CareerProgressionState.FromSnapshot(new CareerProgressionSnapshot(
            600,
            mvpReport.FinalBalanceCoords,
            new Dictionary<string, int>(StringComparer.Ordinal) { ["city_hall"] = 5, ["university"] = 2 },
            new Dictionary<string, int>(StringComparer.Ordinal) { ["old_center"] = 7 },
            ["HIST-02", "HIST-03", "HIST-04"],
            ["contract_hist_01", "contract_hist_02", "contract_hist_03"]));

        var fleet = new FleetProgressionManager();
        var custody = new BatchCustodyStore();
        var queue = new OfficeProcessingQueue();

        var machine = new ContractStateMachine(hist04Contract);
        machine.Accept(2500.0);
        foreach (var ev in hist04Def.Events.Take(2))
            coordinator.TriggerOrSkipBeatEvent("HIST-04", ev.Id, skipCutscene: true);

        var hist04Summary = MvpCampaignRunner.ExecuteInventoryContractMission(
            "HIST-04",
            "traffic_sign",
            "commerce_sign_planes",
            102.5,
            hist04Contract,
            machine,
            hist04Def,
            featuresById,
            ["hist04_chk_01", "hist04_chk_02", "hist04_chk_03"],
            LocomotionMode.Scooter,
            startTimeSeconds: 2520.0,
            custody,
            queue,
            ledger,
            progression,
            coordinator);

        var saveState = new CampaignSaveProgressState();
        var spec04 = FullCampaignCatalog.GetMission("HIST-04");
        CutsceneSkipAndStateCoordinator.SkipCutscene(spec04.Cutscene, saveState);

        var carQuote = fleet.QuotePlatformPurchase(LocomotionMode.Car, progression, new HashSet<string>(coordinator.State.CompletedMissions, StringComparer.Ordinal));

        return new Act1ClosureReport(
            Passed: hist04Summary.Passed && coordinator.State.IsVehicleUnlocked("car") && carQuote.Unlocked,
            Hist04Summary: hist04Summary,
            CarUnlockedInFleet: coordinator.State.IsVehicleUnlocked("car") && carQuote.Unlocked,
            PortUnlocked: saveState.UnlockedRegions.Contains("port"),
            WaterfrontUnlocked: saveState.UnlockedRegions.Contains("waterfront"),
            FinalLedgerCoords: ledger.BalanceCoords,
            GlobalReputation: progression.GlobalReputation);
    }
}

public enum ScriptCategory
{
    MissionDialogue = 0,
    MissionCalmDialogue = 1,
    MissionFailureCue = 2,
    MissionReworkCue = 3,
    Cutscene = 4,
    NpcBark = 5,
    ActivityDispatch = 6,
    RadioTechnical = 7,
    RadioLocalNews = 8,
    RadioMusicAndCulture = 9,
}

public sealed record RecordingCueEntry(
    string CueId,
    int Version,
    ScriptCategory Category,
    string MissionOrProgramId,
    string SpeakerId,
    string ContextDescription,
    string PtBrText,
    string EnUsText,
    double FallbackDurationSeconds);

public sealed record RadioStationProgramSpec(
    string StationId,
    string NamePtBr,
    string NameEnUs,
    ScriptCategory Category,
    int ProgramCount);

public sealed record SubtitlePlaybackFrame(
    string CueId,
    string SpeakerId,
    string Locale,
    string Text,
    double DurationSeconds,
    bool AudioFileRequired);

public sealed record ScriptManifestValidationResult(
    bool Valid,
    int TotalCues,
    int RadioProgramCount,
    bool SubtitlesPlayWithoutAudioFiles,
    IReadOnlyList<string> Errors);

/// <summary>Bilingual recording-ready manifest of all story missions, failure/rework/calm variants, cutscenes, NPCs, activities, and 3 radio stations (T-F10-06).</summary>
public static class RecordingScriptCatalog
{
    public static IReadOnlyList<RadioStationProgramSpec> RadioStations { get; } =
    [
        new("radio_geodesia_fm", "Rádio Geodésia FM", "Geodesy FM", ScriptCategory.RadioTechnical, 3),
        new("radio_sao_datum_news", "Notícias São Datum", "São Datum News", ScriptCategory.RadioLocalNews, 3),
        new("radio_orla_tropica", "Orla Trópica FM", "Tropica Waterfront FM", ScriptCategory.RadioMusicAndCulture, 3),
    ];

    public static IReadOnlyList<RecordingCueEntry> AllCues { get; } = BuildCues();

    public static SubtitlePlaybackFrame PlaybackSubtitlesWithoutAudio(string cueId, string locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cueId);
        var cue = AllCues.FirstOrDefault(c => string.Equals(c.CueId, cueId, StringComparison.Ordinal))
                  ?? throw new KeyNotFoundException($"Recording cue '{cueId}' not found.");

        var isPortuguese = string.Equals(locale, "pt-BR", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(locale, "pt_BR", StringComparison.OrdinalIgnoreCase);

        return new SubtitlePlaybackFrame(
            cue.CueId,
            cue.SpeakerId,
            isPortuguese ? "pt-BR" : "en-US",
            isPortuguese ? cue.PtBrText : cue.EnUsText,
            cue.FallbackDurationSeconds,
            AudioFileRequired: false);
    }

    public static ScriptManifestValidationResult ValidateCatalogCompleteness()
    {
        var errors = new List<string>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var cue in AllCues)
        {
            if (!seenIds.Add(cue.CueId))
                errors.Add($"Duplicate cue ID '{cue.CueId}'.");
            if (cue.Version < 1)
                errors.Add($"{cue.CueId}: invalid script version {cue.Version}.");
            if (string.IsNullOrWhiteSpace(cue.SpeakerId) || string.IsNullOrWhiteSpace(cue.ContextDescription))
                errors.Add($"{cue.CueId}: speaker and context are required for studio recording.");
            if (string.IsNullOrWhiteSpace(cue.PtBrText) || string.IsNullOrWhiteSpace(cue.EnUsText))
                errors.Add($"{cue.CueId}: missing pt-BR or en-US dialogue line.");
            if (cue.FallbackDurationSeconds <= 0.0)
                errors.Add($"{cue.CueId}: fallback duration must be positive.");
        }

        foreach (var mission in FullCampaignCatalog.AllMissions)
        {
            foreach (var beat in mission.Beats)
            {
                if (!seenIds.Contains(beat.DialogueCueId))
                    errors.Add($"Missing dialogue cue '{beat.DialogueCueId}' for {mission.MissionId}.");
            }
            if (!seenIds.Contains(mission.OnFailureCueId))
                errors.Add($"Missing failure cue '{mission.OnFailureCueId}'.");
            if (!seenIds.Contains(mission.OnReworkCueId))
                errors.Add($"Missing rework cue '{mission.OnReworkCueId}'.");
            if (!seenIds.Contains(mission.Cutscene.CutsceneId))
                errors.Add($"Missing cutscene cue '{mission.Cutscene.CutsceneId}'.");
            if (mission.CalmVariant is not null && !seenIds.Contains($"{mission.CalmVariant.VariantMissionId.ToLowerInvariant().Replace('-', '_')}_calm_intro"))
                errors.Add($"Missing calm variant script for {mission.MissionId}.");
        }

        if (AllCues.Count(c => c.Category == ScriptCategory.NpcBark) < 5)
            errors.Add("Expected at least 5 recurring NPC script cues.");
        if (AllCues.Count(c => c.Category == ScriptCategory.ActivityDispatch) < 5)
            errors.Add("Expected at least 5 activity dispatch cues.");

        var radioPrograms = 0;
        foreach (var station in RadioStations)
        {
            var count = AllCues.Count(c => string.Equals(c.MissionOrProgramId, station.StationId, StringComparison.Ordinal));
            if (count < station.ProgramCount)
                errors.Add($"Radio station '{station.StationId}' has {count} scripts; expected >= {station.ProgramCount}.");
            radioPrograms += count;
        }

        return new ScriptManifestValidationResult(
            errors.Count == 0,
            AllCues.Count,
            radioPrograms,
            SubtitlesPlayWithoutAudioFiles: true,
            Array.AsReadOnly(errors.ToArray()));
    }

    private static IReadOnlyList<RecordingCueEntry> BuildCues()
    {
        var cues = new List<RecordingCueEntry>();

        foreach (var mission in FullCampaignCatalog.AllMissions)
        {
            var slug = mission.MissionId.ToLowerInvariant().Replace('-', '_');
            foreach (var beat in mission.Beats)
            {
                cues.Add(new RecordingCueEntry(
                    beat.DialogueCueId,
                    Version: 1,
                    ScriptCategory.MissionDialogue,
                    mission.MissionId,
                    beat.SpeakerId,
                    $"Mission {mission.MissionId} — {beat.BeatKind} beat in {string.Join('/', mission.RegionIds)}",
                    $"[PT-BR] {mission.MissionId} ({beat.BeatKind}): coordenacao geodetica em {string.Join(", ", mission.RegionIds)}.",
                    $"[EN-US] {mission.MissionId} ({beat.BeatKind}): geodetic survey coordination in {string.Join(", ", mission.RegionIds)}.",
                    FallbackDurationSeconds: 4.5));
            }

            cues.Add(new RecordingCueEntry(
                mission.OnFailureCueId,
                Version: 1,
                ScriptCategory.MissionFailureCue,
                mission.MissionId,
                "inspetora_bia",
                $"Failure debrief when CQ thresholds fail in {mission.MissionId}",
                $"Auditoria do CQ reprovou o lote de {mission.MissionId}; verifique RMSE, cobertura ou privacidade antes do reenvio.",
                $"QC audit rejected the {mission.MissionId} batch; check RMSE, coverage, or privacy redaction before resubmitting.",
                FallbackDurationSeconds: 4.0));

            cues.Add(new RecordingCueEntry(
                mission.OnReworkCueId,
                Version: 1,
                ScriptCategory.MissionReworkCue,
                mission.MissionId,
                "eng_arnaldo",
                $"Rework guidance pointing player to missing pass or calibration in {mission.MissionId}",
                $"Retrabalho {mission.MissionId}: preserve o lote anterior e capture somente os trechos com lacuna.",
                $"Rework {mission.MissionId}: keep prior custody intact and recapture only the gapped segments.",
                FallbackDurationSeconds: 4.0));

            cues.Add(new RecordingCueEntry(
                mission.Cutscene.CutsceneId,
                Version: 1,
                ScriptCategory.Cutscene,
                mission.MissionId,
                "narrator",
                $"Skippable chapter cutscene ({mission.Cutscene.DurationSeconds:F0}s) after {mission.MissionId}",
                $"Cena do capítulo {mission.MissionId}: transição técnica e liberação de equipamentos e setores.",
                $"Chapter {mission.MissionId} cutscene: technical transition unlocking approved equipment and districts.",
                FallbackDurationSeconds: mission.Cutscene.DurationSeconds));

            if (mission.CalmVariant is not null)
            {
                cues.Add(new RecordingCueEntry(
                    $"{mission.CalmVariant.VariantMissionId.ToLowerInvariant().Replace('-', '_')}_calm_intro",
                    Version: 1,
                    ScriptCategory.MissionCalmDialogue,
                    mission.CalmVariant.VariantMissionId,
                    "dona_clara",
                    $"Calm variant briefing for {mission.MissionId} guaranteeing zero hostile interference",
                    $"Rota Calma de {mission.MissionId}: corredor protegido pela prefeitura com o mesmo pagamento e metas de CQ.",
                    $"Calm Route for {mission.MissionId}: municipality-protected corridor with identical payout and QC goals.",
                    FallbackDurationSeconds: 4.0));
            }
        }

        cues.AddRange(
        [
            new("npc_dona_clara_office_greeting", 1, ScriptCategory.NpcBark, "npc_hub", "dona_clara", "Office hub greeting on morning arrival", "Café passado e fila de contratos aberta; confira as calibrações antes de sair.", "Fresh coffee is brewed and contracts are posted; verify your rack calibration before rollout.", 3.5),
            new("npc_eng_arnaldo_workshop", 1, ScriptCategory.NpcBark, "npc_workshop", "eng_arnaldo", "Workshop tip on temperature and boresight drift", "Pancada forte no meio-fio muda o extrínseco sem avisar; rode a calibração na oficina.", "A hard curb bump shifts your extrinsics silently; run a bench calibration in the workshop.", 3.8),
            new("npc_mestre_bastos_dock", 1, ScriptCategory.NpcBark, "npc_port", "mestre_bastos", "Port foreman bark about faction roadblocks", "Se o pessoal do Cimento fechar o pátio, acione a rota calma ou volte depois do alerta.", "If the Cement Cartel blocks the yard, switch to Calm City or return once heat cools off.", 3.5),
            new("npc_inspetora_bia_qc", 1, ScriptCategory.NpcBark, "npc_qc", "inspetora_bia", "Quality inspector warning on checkpoint hygiene", "Checkpoint é gabarito cego — nunca misture no ajustamento da trajetória.", "Checkpoints are blind references — never feed them into trajectory optimization.", 3.5),
            new("npc_gimbalina_calm_cluck", 1, ScriptCategory.NpcBark, "npc_gimbalina", "gimbalina", "Gimbalina mascot reaction after corn treat", "Cocoricó estável! Gimbalina alinhou o cardã e zerou o tremor da câmera.", "Steady cluck! Gimbalina stabilized the gimbal and settled optical jitter.", 2.5),

            new("act_timetrial_dispatch", 1, ScriptCategory.ActivityDispatch, "activity_time_trial", "radio_dispatch", "Field time-trial checkpoint dispatch", "Cronômetro ativo: mantenha velocidade segura de captura sem perder sobreposição.", "Clock running: keep a steady capture pace without sacrificing frame overlap.", 3.5),
            new("act_workshop_arcade_assist", 1, ScriptCategory.ActivityDispatch, "activity_calibration_arcade", "eng_arnaldo", "Arcade workshop calibration execution prompt", "Modo Arcade: o mesmo solver de mínimos quadrados está ajustando o lote por você.", "Arcade Mode: the exact same damped least-squares solver is tuning your batch.", 3.5),
            new("act_workshop_technical_tune", 1, ScriptCategory.ActivityDispatch, "activity_calibration_tech", "eng_arnaldo", "Technical calibration manual residual prompt", "Modo Técnico: observe os resíduos nos três eixos e aplique o passo sugerido.", "Technical Mode: inspect three-axis residuals and apply the recommended solver step.", 3.5),
            new("act_test_track_drift", 1, ScriptCategory.ActivityDispatch, "activity_test_track", "eng_arnaldo", "Photogrammetry test track pass feedback", "Pista de testes: faça a passada de retorno invertida para fechar o vetor de braço.", "Test track: perform a reversed return pass to resolve the lever-arm offset.", 3.5),
            new("act_landmark_occupy", 1, ScriptCategory.ActivityDispatch, "activity_landmark", "dona_clara", "Geodetic landmark 5s stationary occupation prompt", "Permaneça imóvel por cinco segundos sobre o pino para registrar o marco na Geopédia.", "Hold position for five seconds over the brass cap to log the landmark in Geopedia.", 3.5),

            new("radio_geo_01_rtk_multipath", 1, ScriptCategory.RadioTechnical, "radio_geodesia_fm", "host_prof_helena", "Technical broadcast explaining GNSS multipath in urban canyons", "Geodésia FM: entre prédios espelhados, desconfie de saltos de ambiguidade por multipercurso.", "Geodesy FM: inside reflective urban canyons, watch out for multipath ambiguity jumps.", 6.0),
            new("radio_geo_02_lever_arm", 1, ScriptCategory.RadioTechnical, "radio_geodesia_fm", "host_prof_helena", "Technical broadcast explaining sensor lever-arm offsets", "Geodésia FM: todo centímetro entre antena e câmera vira erro na curva sem braço calibrado.", "Geodesy FM: every centimeter between antenna and camera turns into curve drift without lever-arm calibration.", 6.0),
            new("radio_geo_03_imu_zupt", 1, ScriptCategory.RadioTechnical, "radio_geodesia_fm", "host_prof_helena", "Technical broadcast introducing IMU bias and stationary ZUPT", "Geodésia FM: parar no semáforo não é tempo perdido — é observação de velocidade zero para o filtro.", "Geodesy FM: pausing at red lights is not wasted time — it gives your filter clean zero-velocity updates.", 6.0),

            new("radio_news_01_calm_city_act", 1, ScriptCategory.RadioLocalNews, "radio_sao_datum_news", "anchor_lucas", "Local bulletin announcing Municipal Calm Surveying Policy", "Notícias São Datum: decreto municipal garante corredores calmos de levantamento sem cortes de receita.", "São Datum News: municipal decree guarantees Calm Surveying corridors with zero revenue cuts.", 5.5),
            new("radio_news_02_port_benchmarks", 1, ScriptCategory.RadioLocalNews, "radio_sao_datum_news", "anchor_lucas", "Local news investigating shifted brass survey benchmarks at the port", "Notícias São Datum: equipe apura desaparecimento de marcos de bronze próximos às docas do Porto.", "São Datum News: inspectors probe tampered bronze survey pins near the Port wharves.", 5.5),
            new("radio_news_03_open_cadastre", 1, ScriptCategory.RadioLocalNews, "radio_sao_datum_news", "anchor_lucas", "Local bulletin celebrating Open Municipal Cadastre milestone", "Notícias São Datum: atualização do cadastro municipal reduz disputas de calçada e drenagem urbana.", "São Datum News: municipal cadastre update cuts sidewalk and storm-drain disputes across downtown.", 5.5),

            new("radio_orla_01_morning_breeze", 1, ScriptCategory.RadioMusicAndCulture, "radio_orla_tropica", "dj_sol", "Upbeat coastal music show & dry-day field forecast", "Orla Trópica FM: céu aberto e brisa leve na Orla — luz difusa ideal para fotogrametria de fachadas.", "Tropica Waterfront FM: clear skies and steady light along the bay — prime conditions for facade capture.", 5.0),
            new("radio_orla_02_rain_alert", 1, ScriptCategory.RadioMusicAndCulture, "radio_orla_tropica", "dj_sol", "Afternoon weather bulletin warning of coastal showers", "Orla Trópica FM: frente úmida prevista às 16h; ajuste a exposição antes da chuva chegar.", "Tropica Waterfront FM: coastal shower arrives at 4 PM; adjust shutter exposure before raindrops hit.", 5.0),
            new("radio_orla_03_sunset_session", 1, ScriptCategory.RadioMusicAndCulture, "radio_orla_tropica", "dj_sol", "Evening acoustic set highlighting downtown landmarks", "Orla Trópica FM: som acústico enquanto as equipes recolhem os últimos dados do crepúsculo.", "Tropica Waterfront FM: acoustic sunset session as field crews wrap up twilight collections.", 5.0),
        ]);

        return Array.AsReadOnly(cues.ToArray());
    }
}
