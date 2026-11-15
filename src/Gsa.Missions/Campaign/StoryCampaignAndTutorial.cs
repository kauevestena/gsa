using System.Security.Cryptography;
using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.World;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Office;
using Gsa.Missions.Persistence;
using Gsa.Missions.Presentation;
using Gsa.Missions.Quality;

namespace Gsa.Missions.Campaign;

public enum MissionBeatKind
{
    Proposal = 0,
    Preparation = 1,
    TechnicalDiscovery = 2,
    SetbackOrCorrection = 3,
    DeliveryAndConsequence = 4,
}

public sealed record SceneCue(
    string CueId,
    string AudioCueId,
    bool Skippable);

public sealed record DialogueEntry(
    string DialogueKey,
    string SpeakerId,
    MissionBeatKind Beat);

public sealed record MissionBeatEvent(
    string Id,
    MissionBeatKind Beat,
    string Trigger,
    DialogueEntry Dialogue,
    SceneCue Cue,
    string IdempotentEffect);

public sealed record MissionObjectiveDefinition(
    string Id,
    string ContractId);

/// <summary>Explicit boundary keeping estimation control targets separate from independent QC checkpoints.</summary>
public sealed record ControlVsCheckpointReference
{
    public IReadOnlyList<string> ControlTargetIds { get; }
    public IReadOnlyList<string> CheckpointTargetIds { get; }
    public bool ControlsUsedInEstimation { get; }
    public bool CheckpointsReservedForQualityEvaluationOnly { get; }

    public ControlVsCheckpointReference(
        IEnumerable<string> controlTargetIds,
        IEnumerable<string> checkpointTargetIds,
        bool controlsUsedInEstimation,
        bool checkpointsReservedForQualityEvaluationOnly)
    {
        ArgumentNullException.ThrowIfNull(controlTargetIds);
        ArgumentNullException.ThrowIfNull(checkpointTargetIds);

        var controls = controlTargetIds.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToArray();
        var checkpoints = checkpointTargetIds.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToArray();

        if (controls.Length == 0)
            throw new ArgumentException("At least one control target is required.", nameof(controlTargetIds));
        if (checkpoints.Length == 0)
            throw new ArgumentException("At least one independent checkpoint is required.", nameof(checkpointTargetIds));
        if (controls.Intersect(checkpoints, StringComparer.Ordinal).Any())
            throw new ArgumentException("Control targets and CQ checkpoints must be strictly disjoint.");
        if (!checkpointsReservedForQualityEvaluationOnly)
            throw new ArgumentException("Checkpoints must remain isolated from trajectory/filter estimation.", nameof(checkpointsReservedForQualityEvaluationOnly));

        ControlTargetIds = Array.AsReadOnly(controls);
        CheckpointTargetIds = Array.AsReadOnly(checkpoints);
        ControlsUsedInEstimation = controlsUsedInEstimation;
        CheckpointsReservedForQualityEvaluationOnly = checkpointsReservedForQualityEvaluationOnly;
    }
}

public sealed record MissionDefinition(
    int SchemaVersion,
    string Id,
    int Act,
    string TitleKey,
    string SummaryKey,
    int MinimumPlatformLevel,
    string MinimumPlatformId,
    IReadOnlyList<string> VehicleUnlocks,
    string WeatherMode,
    string ControlMode,
    IReadOnlyList<MissionObjectiveDefinition> Objectives,
    IReadOnlyList<MissionBeatEvent> Events,
    IReadOnlyList<string> Preconditions,
    bool Calm,
    string? CalmVariant,
    IReadOnlyList<string> DialogueKeys,
    IReadOnlyList<string> Unlocks)
{
    public static MissionDefinition Parse(string json, string sourceName = "mission.json")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException($"Mission '{sourceName}' root must be an object.");

        if (!root.TryGetProperty("schema_version", out var versionEl) || !versionEl.TryGetInt32(out var version) || version != 1)
            throw new FormatException($"Mission '{sourceName}' only supports schema_version 1.");

        var id = ReadRequiredString(root, "id", sourceName);
        if (!root.TryGetProperty("act", out var actEl) || !actEl.TryGetInt32(out var act) || act is < 1 or > 3)
            throw new FormatException($"Mission '{sourceName}' act must be between 1 and 3.");

        var titleKey = root.TryGetProperty("title_key", out var tk) && tk.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(tk.GetString())
            ? tk.GetString()!
            : $"mission.{id.ToLowerInvariant().Replace('-', '_')}.title";
        var summaryKey = root.TryGetProperty("summary_key", out var sk) && sk.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(sk.GetString())
            ? sk.GetString()!
            : $"mission.{id.ToLowerInvariant().Replace('-', '_')}.summary";

        var minLevel = root.TryGetProperty("minimum_platform_level", out var lvlEl) && lvlEl.TryGetInt32(out var lvl)
            ? lvl
            : 0;
        if (minLevel < 0 || minLevel > 6)
            throw new FormatException($"Mission '{sourceName}' minimum_platform_level must be within [0, 6].");

        var minPlatformId = root.TryGetProperty("minimum_platform_id", out var pidEl) && pidEl.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(pidEl.GetString())
            ? pidEl.GetString()!
            : "cart";

        var vehicleUnlocks = ReadOptionalStringArray(root, "vehicle_unlocks");
        var weatherMode = root.TryGetProperty("weather_mode", out var wmEl) && wmEl.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(wmEl.GetString())
            ? wmEl.GetString()!
            : "dry_day";
        var controlMode = root.TryGetProperty("control_mode", out var cmEl) && cmEl.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(cmEl.GetString())
            ? cmEl.GetString()!
            : "gnss_plus_checkpoints";

        var objectives = ParseObjectives(root, sourceName);
        var events = ParseEvents(root, sourceName);
        var preconditions = ReadOptionalStringArray(root, "preconditions");
        var dialogueKeys = ReadOptionalStringArray(root, "dialogue_keys");
        var unlocks = ReadOptionalStringArray(root, "unlocks");

        if (!root.TryGetProperty("calm", out var calmEl) || calmEl.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new FormatException($"Mission '{sourceName}' missing boolean 'calm'.");
        var calm = calmEl.GetBoolean();

        string? calmVariant = null;
        if (root.TryGetProperty("calm_variant", out var cvEl) && cvEl.ValueKind != JsonValueKind.Null)
        {
            if (cvEl.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(cvEl.GetString()))
                throw new FormatException($"Mission '{sourceName}' calm_variant must be null or a non-empty string.");
            calmVariant = cvEl.GetString()!;
        }
        if (!calm && calmVariant is null)
            throw new FormatException($"Mission '{sourceName}' is not calm and requires a calm_variant.");

        if (events.Count < 5)
            throw new FormatException($"Mission '{sourceName}' must define at least five narrative beat events.");

        var coveredBeats = events.Select(e => e.Beat).ToHashSet();
        foreach (MissionBeatKind requiredBeat in Enum.GetValues<MissionBeatKind>())
        {
            if (!coveredBeats.Contains(requiredBeat))
                throw new FormatException($"Mission '{sourceName}' is missing mandatory narrative beat '{requiredBeat}'.");
        }

        var dialogueSet = new HashSet<string>(dialogueKeys, StringComparer.Ordinal);
        foreach (var ev in events)
        {
            if (!dialogueSet.Contains(ev.Dialogue.DialogueKey))
                throw new FormatException($"Mission '{sourceName}' event '{ev.Id}' dialogue_key '{ev.Dialogue.DialogueKey}' must be declared in dialogue_keys.");
        }

        return new MissionDefinition(
            version,
            id,
            act,
            titleKey,
            summaryKey,
            minLevel,
            minPlatformId,
            vehicleUnlocks,
            weatherMode,
            controlMode,
            objectives,
            events,
            preconditions,
            calm,
            calmVariant,
            dialogueKeys,
            unlocks);
    }

    private static string ReadRequiredString(JsonElement parent, string name, string sourceName)
    {
        if (!parent.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(el.GetString()))
            throw new FormatException($"Mission '{sourceName}' missing non-empty string '{name}'.");
        return el.GetString()!;
    }

    private static IReadOnlyList<string> ReadOptionalStringArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<string>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                throw new FormatException($"Array '{name}' must contain non-empty strings.");
            list.Add(item.GetString()!);
        }
        return Array.AsReadOnly(list.ToArray());
    }

    private static IReadOnlyList<MissionObjectiveDefinition> ParseObjectives(JsonElement root, string sourceName)
    {
        if (!root.TryGetProperty("objectives", out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
            throw new FormatException($"Mission '{sourceName}' must define at least one objective.");

        var list = new List<MissionObjectiveDefinition>();
        foreach (var item in arr.EnumerateArray())
        {
            var objId = ReadRequiredString(item, "id", sourceName);
            var contractId = ReadRequiredString(item, "contract_id", sourceName);
            list.Add(new MissionObjectiveDefinition(objId, contractId));
        }
        return Array.AsReadOnly(list.ToArray());
    }

    private static IReadOnlyList<MissionBeatEvent> ParseEvents(JsonElement root, string sourceName)
    {
        if (!root.TryGetProperty("events", out var arr) || arr.ValueKind != JsonValueKind.Array)
            throw new FormatException($"Mission '{sourceName}' must define 'events' array.");

        var list = new List<MissionBeatEvent>();
        foreach (var item in arr.EnumerateArray())
        {
            var eventId = ReadRequiredString(item, "id", sourceName);
            var beatStr = ReadRequiredString(item, "beat", sourceName);
            var beat = ParseBeatKind(beatStr, sourceName);
            var trigger = ReadRequiredString(item, "trigger", sourceName);
            var dialogueKey = ReadRequiredString(item, "dialogue_key", sourceName);
            var sceneCueId = ReadRequiredString(item, "scene_cue_id", sourceName);
            var audioCueId = ReadRequiredString(item, "audio_cue_id", sourceName);
            var skippable = !item.TryGetProperty("skippable", out var skEl) || skEl.GetBoolean();
            var effect = ReadRequiredString(item, "idempotent_effect", sourceName);
            var speaker = item.TryGetProperty("speaker_id", out var spEl) && spEl.ValueKind == JsonValueKind.String
                ? spEl.GetString()!
                : "coordinator";

            list.Add(new MissionBeatEvent(
                eventId,
                beat,
                trigger,
                new DialogueEntry(dialogueKey, speaker, beat),
                new SceneCue(sceneCueId, audioCueId, skippable),
                effect));
        }
        return Array.AsReadOnly(list.ToArray());
    }

    private static MissionBeatKind ParseBeatKind(string value, string sourceName) => value switch
    {
        "proposal" => MissionBeatKind.Proposal,
        "preparation" => MissionBeatKind.Preparation,
        "technical_discovery" => MissionBeatKind.TechnicalDiscovery,
        "setback_or_correction" => MissionBeatKind.SetbackOrCorrection,
        "delivery_and_consequence" => MissionBeatKind.DeliveryAndConsequence,
        _ => throw new FormatException($"Mission '{sourceName}' contains unknown narrative beat '{value}'."),
    };
}

public sealed record CampaignEventResult(
    bool Applied,
    bool WasIdempotentNoOp,
    bool CutsceneSkipped,
    string EventId,
    string? ReasonKey = null);

public sealed record RainQualityObstacleResult(
    bool IsRainActive,
    bool InstantFailureTriggered,
    double QualityPenaltyFraction,
    bool CorrectionRequired,
    string GuidanceKey);

public sealed record StoryCatalogValidationResult(
    bool Valid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> OrderedMissionIds,
    IReadOnlyList<string> UnlockedVehicleProgression,
    bool RequiresFutureSensors);

/// <summary>Tracks deterministic campaign mission unlocks, vehicle unlocks, and idempotent story event application.</summary>
public sealed class CampaignState
{
    private readonly List<string> _unlockedMissions = ["HIST-01"];
    private readonly HashSet<string> _unlockedMissionSet = new(["HIST-01"], StringComparer.Ordinal);
    private readonly List<string> _completedMissions = [];
    private readonly HashSet<string> _completedMissionSet = new(StringComparer.Ordinal);
    private readonly List<string> _unlockedVehicles = ["cart"];
    private readonly HashSet<string> _unlockedVehicleSet = new(["cart"], StringComparer.Ordinal);
    private readonly HashSet<string> _appliedEventIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _skippedCutsceneIds = new(StringComparer.Ordinal);

    public IReadOnlyList<string> UnlockedMissions => _unlockedMissions;
    public IReadOnlyList<string> CompletedMissions => _completedMissions;
    public IReadOnlyList<string> UnlockedVehicles => _unlockedVehicles;
    public IReadOnlySet<string> AppliedEventIds => _appliedEventIds;
    public IReadOnlySet<string> SkippedCutsceneIds => _skippedCutsceneIds;

    public bool IsMissionUnlocked(string missionId) => _unlockedMissionSet.Contains(missionId);
    public bool IsMissionCompleted(string missionId) => _completedMissionSet.Contains(missionId);
    public bool IsVehicleUnlocked(string vehicleId) => _unlockedVehicleSet.Contains(vehicleId);

    internal bool TryAddAppliedEvent(string eventId) => _appliedEventIds.Add(eventId);
    internal void RecordSkippedCutscene(string cueId) => _skippedCutsceneIds.Add(cueId);

    internal void UnlockMission(string missionId)
    {
        if (!string.IsNullOrWhiteSpace(missionId) && _unlockedMissionSet.Add(missionId))
            _unlockedMissions.Add(missionId);
    }

    internal void UnlockVehicle(string vehicleId)
    {
        if (!string.IsNullOrWhiteSpace(vehicleId) && _unlockedVehicleSet.Add(vehicleId))
            _unlockedVehicles.Add(vehicleId);
    }

    internal void MarkCompleted(string missionId)
    {
        if (!string.IsNullOrWhiteSpace(missionId) && _completedMissionSet.Add(missionId))
            _completedMissions.Add(missionId);
    }
}

public sealed class StoryCampaignCoordinator
{
    private static readonly HashSet<string> FutureOnlyCapabilities =
        new(["lidar", "imu", "dmi", "rtk", "ppk"], StringComparer.Ordinal);

    private readonly Dictionary<string, MissionDefinition> _missionsById;
    public CampaignState State { get; }

    public StoryCampaignCoordinator(IEnumerable<MissionDefinition> missions, CampaignState? initialState = null)
    {
        ArgumentNullException.ThrowIfNull(missions);
        _missionsById = missions.ToDictionary(m => m.Id, StringComparer.Ordinal);
        State = initialState ?? new CampaignState();
    }

    public bool CanStartMission(string missionId, int activePlatformLevel)
    {
        if (!_missionsById.TryGetValue(missionId, out var mission))
            return false;

        if (!State.IsMissionUnlocked(missionId))
            return false;

        if (activePlatformLevel < mission.MinimumPlatformLevel)
            return false;

        return mission.Preconditions.All(State.IsMissionCompleted);
    }

    public CampaignEventResult TriggerOrSkipBeatEvent(string missionId, string eventId, bool skipCutscene)
    {
        if (!_missionsById.TryGetValue(missionId, out var mission))
            return new CampaignEventResult(false, false, false, eventId, "campaign.error.unknown_mission");

        var ev = mission.Events.FirstOrDefault(e => string.Equals(e.Id, eventId, StringComparison.Ordinal));
        if (ev is null)
            return new CampaignEventResult(false, false, false, eventId, "campaign.error.unknown_event");

        if (!State.TryAddAppliedEvent(ev.Id))
            return new CampaignEventResult(true, true, skipCutscene && ev.Cue.Skippable, ev.Id);

        var skipped = skipCutscene && ev.Cue.Skippable;
        if (skipped)
            State.RecordSkippedCutscene(ev.Cue.CueId);

        ApplyEffectString(ev.IdempotentEffect);
        return new CampaignEventResult(true, false, skipped, ev.Id);
    }

    public bool CompleteMission(string missionId, int activePlatformLevel, bool skipAllCutscenes = false)
    {
        if (!CanStartMission(missionId, activePlatformLevel))
            return false;

        var mission = _missionsById[missionId];
        foreach (var ev in mission.Events)
            TriggerOrSkipBeatEvent(missionId, ev.Id, skipAllCutscenes);

        State.MarkCompleted(mission.Id);
        foreach (var vehicle in mission.VehicleUnlocks)
            State.UnlockVehicle(vehicle);
        foreach (var nextMission in mission.Unlocks)
            State.UnlockMission(nextMission);

        return true;
    }

    private void ApplyEffectString(string effect)
    {
        if (string.IsNullOrWhiteSpace(effect))
            return;

        foreach (var token in effect.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.StartsWith("unlock_mission:", StringComparison.Ordinal))
                State.UnlockMission(token["unlock_mission:".Length..]);
            else if (token.StartsWith("unlock_vehicle:", StringComparison.Ordinal))
                State.UnlockVehicle(token["unlock_vehicle:".Length..]);
        }
    }

    public static RainQualityObstacleResult EvaluateAnnouncedRainObstacle(
        MissionDefinition mission,
        double rainIntensity,
        bool exposureAdjusted)
    {
        ArgumentNullException.ThrowIfNull(mission);
        if (!double.IsFinite(rainIntensity) || rainIntensity < 0.0 || rainIntensity > 1.0)
            throw new ArgumentOutOfRangeException(nameof(rainIntensity), "Rain intensity must be in [0, 1].");

        if (rainIntensity <= 0.0 || !string.Equals(mission.WeatherMode, "announced_rain_quality_obstacle", StringComparison.Ordinal))
        {
            return new RainQualityObstacleResult(
                rainIntensity > 0.0,
                InstantFailureTriggered: false,
                QualityPenaltyFraction: 0.0,
                CorrectionRequired: false,
                GuidanceKey: "mission.weather.clear");
        }

        // Normative rule: announced rain in HIST-02 acts as a quality/exposure obstacle, never an instant mission failure.
        var penalty = exposureAdjusted ? 0.05 * rainIntensity : 0.25 * rainIntensity;
        var correctionRequired = !exposureAdjusted && rainIntensity >= 0.25;
        return new RainQualityObstacleResult(
            IsRainActive: true,
            InstantFailureTriggered: false,
            QualityPenaltyFraction: penalty,
            CorrectionRequired: correctionRequired,
            GuidanceKey: correctionRequired ? "mission.hist_02.beat.technical_discovery" : "mission.hist_02.beat.setback_or_correction");
    }

    public static StoryCatalogValidationResult ValidateInitialStoryCatalog(
        IReadOnlyDictionary<string, MissionDefinition> missions,
        IReadOnlyDictionary<string, ContractDefinition> contracts)
    {
        ArgumentNullException.ThrowIfNull(missions);
        ArgumentNullException.ThrowIfNull(contracts);

        var errors = new List<string>();
        var requiredOrder = new[] { "HIST-01", "HIST-02", "HIST-03" };
        var vehicleProgression = new List<string> { "cart" };
        var requiresFutureSensors = false;

        foreach (var missionId in requiredOrder)
        {
            if (!missions.TryGetValue(missionId, out var mission))
            {
                errors.Add($"Missing required MVP story mission '{missionId}'.");
                continue;
            }

            if (mission.Act != 1)
                errors.Add($"Mission '{missionId}' must belong to Act 1.");

            if (mission.Objectives.Count == 0)
            {
                errors.Add($"Mission '{missionId}' has no objectives.");
                continue;
            }

            foreach (var obj in mission.Objectives)
            {
                if (!contracts.TryGetValue(obj.ContractId, out var contract))
                {
                    errors.Add($"Mission '{missionId}' references missing contract '{obj.ContractId}'.");
                    continue;
                }

                if (!string.Equals(contract.PartialPolicy.Mode, "disabled", StringComparison.Ordinal))
                    errors.Add($"Mission '{missionId}' contract '{contract.Id}' must disable partial delivery.");

                if (contract.RequiredCapabilities.Any(FutureOnlyCapabilities.Contains))
                {
                    requiresFutureSensors = true;
                    errors.Add($"Mission '{missionId}' contract '{contract.Id}' cannot require future sensors.");
                }
            }
        }

        if (errors.Count == 0)
        {
            // Simulate step-by-step progression to prove acyclic playability with available vehicles.
            var coordinator = new StoryCampaignCoordinator(missions.Values);
            var simulatedPlatformLevel = 0;

            foreach (var missionId in requiredOrder)
            {
                var mission = missions[missionId];
                if (!coordinator.CanStartMission(missionId, simulatedPlatformLevel))
                {
                    errors.Add($"Mission '{missionId}' is unreachable at simulated platform level {simulatedPlatformLevel}.");
                    break;
                }

                coordinator.CompleteMission(missionId, simulatedPlatformLevel, skipAllCutscenes: true);
                if (coordinator.State.IsVehicleUnlocked("bicycle") && simulatedPlatformLevel < 1)
                {
                    simulatedPlatformLevel = 1;
                    vehicleProgression.Add("bicycle");
                }
                if (coordinator.State.IsVehicleUnlocked("scooter") && !vehicleProgression.Contains("scooter", StringComparer.Ordinal))
                    vehicleProgression.Add("scooter");
            }
        }

        return new StoryCatalogValidationResult(
            errors.Count == 0,
            Array.AsReadOnly(errors.ToArray()),
            requiredOrder,
            Array.AsReadOnly(vehicleProgression.ToArray()),
            requiresFutureSensors);
    }
}

public sealed record TutorialPhaseRecord(
    int PhaseNumber,
    string PhaseId,
    string LocalizationKey,
    string ContractStateAfter,
    bool Passed,
    string Detail);

public sealed record TutorialPlaythroughReport(
    int SchemaVersion,
    string MissionId,
    string ContractId,
    int MinimumPlatformLevel,
    IReadOnlyList<string> RequiredCapabilities,
    bool RequiresFutureSensors,
    bool ControlsUsedInEstimation,
    bool CheckpointsReservedForQualityEvaluationOnly,
    ControlVsCheckpointReference ControlVsCheckpointReference,
    bool InitialReportPassed,
    int InitialStars,
    IReadOnlyList<string> InitialFailureReasons,
    bool FinalReportPassed,
    int FinalStars,
    int NetPayoutCoords,
    int FinalLedgerCoords,
    bool IdempotentReplayConfirmed,
    string RawBatchSha256BeforeProcessing,
    string RawBatchSha256AfterProcessing,
    bool InitialPrivacyCompliant,
    bool FinalPrivacyCompliant,
    bool SaveRoundTripVerified,
    IReadOnlyList<string> UnlockedMissions,
    IReadOnlyList<string> UnlockedVehicles,
    int AppliedStoryEventCount,
    StoryCatalogValidationResult CatalogValidation,
    IReadOnlyList<TutorialPhaseRecord> Phases);

/// <summary>Executes the normative HIST-01 tutorial loop end-to-end against neutral world and contract assets.</summary>
public static class TutorialMissionRunner
{
    public static TutorialPlaythroughReport RunHist01Playthrough(
        string contentDirectory,
        string worldManifestPath,
        string? saveSlotDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldManifestPath);

        var fullContent = Path.GetFullPath(contentDirectory);
        var fullWorld = Path.GetFullPath(worldManifestPath);

        if (!Directory.Exists(fullContent))
            throw new DirectoryNotFoundException($"Content directory not found: {fullContent}");
        if (!File.Exists(fullWorld))
            throw new FileNotFoundException($"Tutorial world manifest not found: {fullWorld}", fullWorld);

        var missionJsons = Directory.GetFiles(Path.Combine(fullContent, "missions"), "*.json")
            .Order(StringComparer.Ordinal)
            .Select(path => MissionDefinition.Parse(File.ReadAllText(path), Path.GetFileName(path)))
            .ToDictionary(m => m.Id, StringComparer.Ordinal);

        var contractJsons = Directory.GetFiles(Path.Combine(fullContent, "contracts"), "*.json")
            .Order(StringComparer.Ordinal)
            .Select(path => ContractDefinition.Parse(File.ReadAllText(path), Path.GetFileName(path)))
            .ToDictionary(c => c.Id, StringComparer.Ordinal);

        var worldManifest = WorldSceneManifest.Parse(File.ReadAllText(fullWorld));
        return ExecuteHist01Playthrough(missionJsons, contractJsons, worldManifest, saveSlotDirectory);
    }

    public static TutorialPlaythroughReport RunDefaultHist01InMemoryPlaythrough(WorldSceneManifest worldManifest)
    {
        ArgumentNullException.ThrowIfNull(worldManifest);

        var missions = new[]
        {
            BuildEmbeddedHistMission("HIST-01", 0, "cart", [], "dry_day", "tutorial_control_vs_checkpoint", "contract_hist_01", "facades", [], ["HIST-02"]),
            BuildEmbeddedHistMission("HIST-02", 0, "cart", ["bicycle"], "announced_rain_quality_obstacle", "gnss_plus_checkpoints", "contract_hist_02", "drainage", ["HIST-01"], ["HIST-03"]),
            BuildEmbeddedHistMission("HIST-03", 1, "bicycle", ["scooter"], "dry_day", "local_control_planes_and_targets", "contract_hist_03", "sidewalks", ["HIST-02"], []),
        }.ToDictionary(m => m.Id, StringComparer.Ordinal);

        var facades = worldManifest.Objects.Where(o => o.ObjectClass == "building_facade").Select(o => o.Id).Order(StringComparer.Ordinal).ToArray();
        var hist01Contract = BuildEmbeddedHist01Contract(facades);
        var hist02Contract = BuildEmbeddedFollowUpContract("contract_hist_02", "mission.hist_02.title", "commerce_street_drainage",
            Enumerable.Range(1, 12).Select(i => $"manhole_{i:00}").ToArray(), "absolute_rmse", 2.0, 0.5, 400, 100, ["HIST-01"], ["HIST-03"]);
        var hist03Contract = BuildEmbeddedFollowUpContract("contract_hist_03", "mission.hist_03.title", "commerce_street_sidewalks",
            Enumerable.Range(1, 8).Select(i => $"sidewalk_seg_{i:00}").ToArray(), "relative_rmse", 0.2, 0.05, 700, 125, ["HIST-02"], []);

        var contracts = new Dictionary<string, ContractDefinition>(StringComparer.Ordinal)
        {
            [hist01Contract.Id] = hist01Contract,
            [hist02Contract.Id] = hist02Contract,
            [hist03Contract.Id] = hist03Contract,
        };

        return ExecuteHist01Playthrough(missions, contracts, worldManifest, saveSlotDirectory: null);
    }

    private static TutorialPlaythroughReport ExecuteHist01Playthrough(
        IReadOnlyDictionary<string, MissionDefinition> missions,
        IReadOnlyDictionary<string, ContractDefinition> contracts,
        WorldSceneManifest worldManifest,
        string? saveSlotDirectory)
    {
        var catalogValidation = StoryCampaignCoordinator.ValidateInitialStoryCatalog(missions, contracts);
        if (!catalogValidation.Valid)
            throw new InvalidOperationException("Initial story catalog failed validation: " + string.Join("; ", catalogValidation.Errors));

        var hist01 = missions["HIST-01"];
        var def = contracts[hist01.Objectives[0].ContractId];

        var facades = worldManifest.Objects
            .Where(o => o.ObjectClass == "building_facade")
            .OrderBy(o => o.Id, StringComparer.Ordinal)
            .ToArray();
        if (facades.Length < 20)
            throw new InvalidOperationException($"Tutorial block requires 20 facades; found {facades.Length}.");

        var controlIds = worldManifest.Objects
            .Where(o => o.ObjectClass == "survey_control")
            .OrderBy(o => o.Id, StringComparer.Ordinal)
            .Select(o => o.Id)
            .ToArray();
        if (controlIds.Length == 0)
            controlIds = ["survey_control_east", "survey_control_west"];

        var unloadExists = worldManifest.Objects.Any(o => o.Id == "unload_point" || o.ObjectClass == "unload_point");
        if (!unloadExists)
            throw new InvalidOperationException("Tutorial block requires an unload_point object.");

        var checkpoints = new[]
        {
            new ControlCheckpointReference("tutorial_checkpoint_01", new Vector3d(500040.0, 7400005.0, 101.0)),
            new ControlCheckpointReference("tutorial_checkpoint_02", new Vector3d(500160.0, 7400005.0, 101.0)),
        };
        var controlVsCheck = new ControlVsCheckpointReference(
            controlIds,
            checkpoints.Select(c => c.CheckpointId),
            controlsUsedInEstimation: true,
            checkpointsReservedForQualityEvaluationOnly: true);

        var minE = facades.Min(f => f.PositionEnu.X) - 2.0;
        var minN = facades.Min(f => f.PositionEnu.Y) - 2.0;
        var maxE = facades.Max(f => f.PositionEnu.X) + 2.0;
        var maxN = facades.Max(f => f.PositionEnu.Y) + 2.0;
        var surfaceBounds = new Bounds2DEnu(minE, minN, maxE, maxN);
        var surfaceRef = new SurveySurfaceReference("commerce_facade_planes", surfaceBounds, 101.0);
        var evalRef = new EvaluationReference(
            def.Id,
            [surfaceRef],
            def.RequiredFeatureIds,
            checkpoints,
            featureLocationsById: facades.ToDictionary(f => f.Id, f => f.PositionEnu, StringComparer.Ordinal));

        var campaign = new StoryCampaignCoordinator(missions.Values);
        var contract = new ContractStateMachine(def);
        var store = new BatchCustodyStore();
        var ledger = new CareerLedger(CareerLedger.DefaultInitialBalanceCoords);
        var progression = new CareerProgressionState();
        var queue = new OfficeProcessingQueue();
        var phases = new List<TutorialPhaseRecord>(4);

        // Beat 1 & 2: Proposal & Preparation
        campaign.TriggerOrSkipBeatEvent("HIST-01", hist01.Events[0].Id, skipCutscene: false);
        AssertOk(contract.Accept(0.0).Succeeded, "Contract accept failed.");
        campaign.TriggerOrSkipBeatEvent("HIST-01", hist01.Events[1].Id, skipCutscene: false);

        // Phase 1: Initial field capture with Handcart + Camera/GNSS (first 15 facades clean, 5 blurred/missing, 1 unmasked sensitive face)
        campaign.TriggerOrSkipBeatEvent("HIST-01", hist01.Events[2].Id, skipCutscene: false);

        var initialSamples = new List<SerializableCameraSample>();
        var initialFootprints = new List<SampleFootprintObservation>();
        var initialFeatures = new List<TriangulatedFeatureEntry>();
        var cellWidth = surfaceBounds.Width / 20.0;

        for (var i = 0; i < 20; i++)
        {
            var facade = facades[i];
            var sampleId = $"tut_s_{i + 1:00}";
            var isFlawed = i >= 15;
            var hasPrivacy = i == 0;
            var sample = CreateTutorialSample(sampleId, 20.0 + i * 5.0, facade.PositionEnu, blurred: isFlawed, hasPendingPrivacyMask: hasPrivacy);
            initialSamples.Add(SerializableCameraSample.FromDomain(sample));

            var cellMinE = minE + i * cellWidth;
            var cellMaxE = i == 19 ? maxE : minE + (i + 1) * cellWidth;
            initialFootprints.Add(new SampleFootprintObservation(sampleId, "commerce_facade_planes", new Bounds2DEnu(cellMinE, minN, cellMaxE, maxN)));

            if (!isFlawed)
            {
                initialFeatures.Add(new TriangulatedFeatureEntry(
                    facade.Id,
                    "building_facade",
                    TriangulationStatus.Resolved,
                    FeatureReconstructionMethod.TwoRayTriangulation,
                    GeometryFailureReason.None,
                    facade.PositionEnu,
                    10.0,
                    null,
                    2,
                    true,
                    [sampleId]));
            }
        }

        var batch1 = new SurveyBatchArtifact(
            1,
            "batch_tut_01",
            def.Id,
            15.0,
            120.0,
            20,
            0,
            CaptureStopReason.None,
            ["cam_initial"],
            initialSamples,
            initialFootprints,
            initialFeatures,
            []);

        AssertOk(contract.RecordPendingBatch(120.0, batch1.BatchId).Succeeded, "Record pending batch 1 failed.");
        AssertOk(store.RegisterBufferedBatch(batch1, 120.0).Succeeded, "Register batch 1 failed.");
        AssertOk(store.OffloadAtomic(batch1.BatchId, 130.0).Succeeded, "Offload batch 1 failed.");
        AssertOk(contract.ProtectBatchAtUnloadPoint(130.0, batch1.BatchId).Succeeded, "Protect batch 1 failed.");
        var rawSha1Before = store.AuthoritativeManifest.CommittedBatches.Single(b => b.BatchId == batch1.BatchId).PayloadSha256;

        phases.Add(new TutorialPhaseRecord(
            1,
            "collect",
            "tutorial.step.collect",
            contract.State.ToString(),
            true,
            $"Collected initial cart batch '{batch1.BatchId}' across 20 facade slots (15 valid, 5 blurred/omitted, 1 sensitive region)."));

        // Phase 2: Unload & initial Technical office processing without confirming privacy mask -> triggers CQ rework
        var recipeInitial = new ProcessingRecipe(
            1,
            "rec_tut_initial",
            def.Id,
            ProcessingMode.Technical,
            [batch1.BatchId],
            initialSamples.Select(s => s.SampleId).ToArray(),
            initialFeatures.Select(f => f.TargetId).ToArray(),
            new Dictionary<string, IReadOnlyList<string>>(),
            CalendarDurationSeconds: 300.0,
            ServiceFeeCoords: 20);

        var startInitial = queue.StartOrResumeJob("job_tut_1", contract, store, ledger, recipeInitial, 140.0);
        AssertOk(startInitial.Succeeded, "Start initial office job failed.");
        var doneInitial = queue.AdvanceJob(contract, store, 300.0);
        AssertOk(doneInitial.Succeeded && doneInitial.Product is not null, "Complete initial office job failed.");
        var initialPrivacyCompliant = doneInitial.Product!.PrivacyManifest.IsCompliant;
        AssertOk(!initialPrivacyCompliant, "Expected pending privacy mask in Technical mode before operator confirmation.");

        var initialReport = QualityEvaluator.Evaluate(def, evalRef, doneInitial.Product.Submission);
        var initialReportView = BoardOfficeAndReportPresenter.BuildQualityReportView(def, initialReport, null, ProcessingMode.Technical, "en-US", doneInitial.Product.RecipeHash);
        var initialEvalTransition = contract.RecordQualityEvaluation(450.0, initialReport.Passed, initialReport.PassedDeliverableIds, initialReport.FailureReasonKeys.FirstOrDefault());
        AssertOk(initialEvalTransition.Succeeded && contract.State == ContractState.Retrabalho, "Initial failed CQ must enter Retrabalho.");
        AssertOk(!initialReport.Passed, "Initial tutorial report must require rework.");
        campaign.TriggerOrSkipBeatEvent("HIST-01", hist01.Events[3].Id, skipCutscene: false);

        phases.Add(new TutorialPhaseRecord(
            2,
            "unload_and_process",
            "tutorial.step.unload_and_process",
            contract.State.ToString(),
            !initialReport.Passed && initialReportView.CorrectiveActionCards.Count > 0,
            $"Initial office revision {doneInitial.Product.Revision} failed CQ ({string.Join(", ", initialReport.FailureReasonKeys)}), entering Retrabalho with {initialReportView.CorrectiveActionCards.Count} corrective action card(s)."));

        // Phase 3: Recollect 5 missed facades + confirm/apply privacy mask in office -> CQ approved 5 stars
        var reworkSamples = new List<SerializableCameraSample>();
        var reworkFootprints = new List<SampleFootprintObservation>();
        var reworkFeatures = new List<TriangulatedFeatureEntry>();
        for (var i = 15; i < 20; i++)
        {
            var facade = facades[i];
            var sampleId = $"tut_rework_s_{i + 1:00}";
            var sample = CreateTutorialSample(sampleId, 470.0 + (i - 15) * 5.0, facade.PositionEnu, blurred: false, hasPendingPrivacyMask: false);
            reworkSamples.Add(SerializableCameraSample.FromDomain(sample));

            var cellMinE = minE + i * cellWidth;
            var cellMaxE = i == 19 ? maxE : minE + (i + 1) * cellWidth;
            reworkFootprints.Add(new SampleFootprintObservation(sampleId, "commerce_facade_planes", new Bounds2DEnu(cellMinE, minN, cellMaxE, maxN)));
            reworkFeatures.Add(new TriangulatedFeatureEntry(
                facade.Id,
                "building_facade",
                TriangulationStatus.Resolved,
                FeatureReconstructionMethod.TwoRayTriangulation,
                GeometryFailureReason.None,
                facade.PositionEnu,
                10.0,
                null,
                2,
                true,
                [sampleId]));
        }

        var batch2 = new SurveyBatchArtifact(
            1,
            "batch_tut_02",
            def.Id,
            465.0,
            495.0,
            5,
            0,
            CaptureStopReason.None,
            ["cam_initial"],
            reworkSamples,
            reworkFootprints,
            reworkFeatures,
            []);

        AssertOk(contract.RecordPendingBatch(495.0, batch2.BatchId).Succeeded, "Record rework batch failed.");
        AssertOk(store.RegisterBufferedBatch(batch2, 495.0).Succeeded, "Register rework batch failed.");
        AssertOk(store.OffloadAtomic(batch2.BatchId, 500.0).Succeeded, "Offload rework batch failed.");
        AssertOk(contract.ProtectBatchAtUnloadPoint(500.0, batch2.BatchId).Succeeded, "Protect rework batch failed.");

        var selectedCleanSamples = initialSamples.Take(15).Select(s => s.SampleId).Concat(reworkSamples.Select(s => s.SampleId)).ToArray();
        var confirmedMasks = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["tut_s_01"] = ["privacy_face_01"],
        };
        var recipeRework = new ProcessingRecipe(
            1,
            "rec_tut_corrected",
            def.Id,
            ProcessingMode.Technical,
            [batch1.BatchId, batch2.BatchId],
            selectedCleanSamples,
            def.RequiredFeatureIds,
            confirmedMasks,
            CalendarDurationSeconds: 300.0,
            ServiceFeeCoords: 0);

        var startRework = queue.StartOrResumeJob("job_tut_2", contract, store, ledger, recipeRework, 510.0);
        AssertOk(startRework.Succeeded, "Start rework office job failed.");
        var doneRework = queue.AdvanceJob(contract, store, 300.0);
        AssertOk(doneRework.Succeeded && doneRework.Product is not null, "Complete rework office job failed.");

        var rawSha1After = store.AuthoritativeManifest.CommittedBatches.Single(b => b.BatchId == batch1.BatchId).PayloadSha256;
        AssertOk(string.Equals(rawSha1Before, rawSha1After, StringComparison.Ordinal), "Office processing mutated raw batch hash.");
        AssertOk(doneRework.Product!.PrivacyManifest.IsCompliant, "Final privacy manifest must be compliant.");

        var finalReport = QualityEvaluator.Evaluate(def, evalRef, doneRework.Product.Submission);
        var finalEvalTransition = contract.RecordQualityEvaluation(820.0, finalReport.Passed, finalReport.PassedDeliverableIds, finalReport.FailureReasonKeys.FirstOrDefault());
        AssertOk(finalEvalTransition.Succeeded && finalReport.Passed && contract.State == ContractState.ProntoEntregar, "Corrected CQ must pass and transition to ProntoEntregar.");

        var finalQuote = DeliveryQuoteCalculator.Calculate(def, finalReport, contract.Snapshot!.AcceptedAtSeconds, 830.0);
        AssertOk(finalQuote.Eligible && finalQuote.Stars == 5, "Final tutorial quote must qualify for 5 stars.");

        phases.Add(new TutorialPhaseRecord(
            3,
            "correct_rework",
            "tutorial.step.correct_rework",
            contract.State.ToString(),
            true,
            $"Recollected 5 missing facades, applied RGB(32,32,32) mask on derived thumbnail without mutating raw SHA-256, and passed CQ ({finalQuote.Stars} stars)."));

        // Phase 4: Delivery, idempotent payout, save round-trip, and mission completion
        var settle1 = ContractSettlementCoordinator.SettleDelivery(contract, ledger, progression, finalReport, 840.0);
        var settle2 = ContractSettlementCoordinator.SettleDelivery(contract, ledger, progression, finalReport, 845.0);
        AssertOk(settle1.Succeeded && !settle1.WasIdempotentReplay, "First tutorial settlement must succeed.");
        AssertOk(settle2.Succeeded && settle2.WasIdempotentReplay, "Duplicate tutorial settlement must replay idempotently.");
        AssertOk(contract.State == ContractState.Entregue, "Contract must reach Entregue.");

        AssertOk(campaign.CompleteMission("HIST-01", activePlatformLevel: 0, skipAllCutscenes: false), "Completing HIST-01 must unlock HIST-02.");

        // Verify SaveGameManager round-trip preserves progression & custody integrity
        var tempSlot = saveSlotDirectory ?? Path.Combine(Path.GetTempPath(), "gsa-tut-save-" + Guid.NewGuid().ToString("N"));
        bool saveVerified;
        try
        {
            var saveMgr = new SaveGameManager(tempSlot);
            var saveState = new SaveGameState(
                1,
                SaveTrigger.ContractSettled,
                0,
                new SaveClockState(840.0, 840.0),
                new SaveRngState(101UL, 7UL, 42),
                ledger.ExportState(),
                progression.ExportSnapshot(),
                new SaveEquipmentMountState("cart", ["cart", "cam_initial", "gnss_basic"], new Dictionary<string, string> { ["handheld"] = "cam_initial" }),
                store.ExportSnapshot(),
                [contract.ExportSnapshot()]);
            var saveRes = saveMgr.Save(saveState, SaveTrigger.ContractSettled);
            var loadRes = saveMgr.LoadCurrent();
            var restored = loadRes.Succeeded && loadRes.State is not null ? SaveGameManager.RestoreRuntime(loadRes.State) : null;
            saveVerified = saveRes.Succeeded
                && loadRes.Succeeded
                && restored is not null
                && restored.Ledger.BalanceCoords == ledger.BalanceCoords
                && restored.Progression.HasStoryUnlock("HIST-02")
                && restored.Contracts.Single().State == ContractState.Entregue;
        }
        finally
        {
            if (saveSlotDirectory is null && Directory.Exists(tempSlot))
                Directory.Delete(tempSlot, recursive: true);
        }
        AssertOk(saveVerified, "SaveGameManager round-trip verification failed.");

        phases.Add(new TutorialPhaseRecord(
            4,
            "deliver_and_pay",
            "tutorial.step.deliver_and_pay",
            contract.State.ToString(),
            true,
            $"Settled {settle1.Quote.NetCoords} Coords once (duplicate replay safe), balance {ledger.BalanceCoords} Coords, unlocked HIST-02, and verified save round-trip."));

        return new TutorialPlaythroughReport(
            1,
            hist01.Id,
            def.Id,
            hist01.MinimumPlatformLevel,
            def.RequiredCapabilities,
            catalogValidation.RequiresFutureSensors,
            controlVsCheck.ControlsUsedInEstimation,
            controlVsCheck.CheckpointsReservedForQualityEvaluationOnly,
            controlVsCheck,
            initialReport.Passed,
            0,
            initialReport.FailureReasonKeys,
            finalReport.Passed,
            finalQuote.Stars,
            settle1.Quote.NetCoords,
            ledger.BalanceCoords,
            settle2.WasIdempotentReplay,
            rawSha1Before,
            rawSha1After,
            initialPrivacyCompliant,
            doneRework.Product.PrivacyManifest.IsCompliant,
            saveVerified,
            campaign.State.UnlockedMissions,
            campaign.State.UnlockedVehicles,
            campaign.State.AppliedEventIds.Count,
            catalogValidation,
            Array.AsReadOnly(phases.ToArray()));
    }

    private static CameraObservationSample CreateTutorialSample(
        string sampleId,
        double timestamp,
        Vector3d facadePositionEnu,
        bool blurred,
        bool hasPendingPrivacyMask)
    {
        var pose = new EstimatedBodyPose(timestamp, new Vector3d(facadePositionEnu.X, facadePositionEnu.Y + 4.0, 101.5), Quaterniond.Identity);
        var quality = new CameraQualityMetrics(
            ExposureSeconds: 0.002,
            MotionBlurPixels: blurred ? 6.5 : 0.2,
            ClippingFraction: 0.005,
            LensObstructionFraction: 0.0,
            NoiseSigmaPixels: 0.5,
            MotionBlurExceeded: blurred,
            ClippingExceeded: false,
            LensObstructionExceeded: false,
            InsufficientVisibleFeatures: false,
            Passed: !blurred);

        var rgb = new byte[CameraThumbnail.ExpectedWidth * CameraThumbnail.ExpectedHeight * 3];
        for (var i = 0; i < rgb.Length; i += 3)
        {
            rgb[i] = 140;
            rgb[i + 1] = 150;
            rgb[i + 2] = 160;
        }
        var thumb = CameraThumbnail.FromRgbBytes(CameraThumbnail.ExpectedWidth, CameraThumbnail.ExpectedHeight, rgb);

        var masks = hasPendingPrivacyMask
            ? new[] { new PrivacyMaskRegion("privacy_face_01", 64.0, 48.0, 192.0, 128.0, Applied: false) }
            : [];

        return new CameraObservationSample(
            sampleId,
            "cam_initial",
            0,
            timestamp,
            pose,
            CameraMountEstimate.ForwardFacing,
            CameraIntrinsics.Initial,
            quality,
            [],
            masks,
            thumb);
    }

    private static MissionDefinition BuildEmbeddedHistMission(
        string id,
        int minLevel,
        string minPlatformId,
        string[] vehicleUnlocks,
        string weatherMode,
        string controlMode,
        string contractId,
        string objectiveId,
        string[] preconditions,
        string[] unlocks)
    {
        var lower = id.ToLowerInvariant().Replace('-', '_');
        var keys = new[]
        {
            $"mission.{lower}.beat.proposal",
            $"mission.{lower}.beat.preparation",
            $"mission.{lower}.beat.technical_discovery",
            $"mission.{lower}.beat.setback_or_correction",
            $"mission.{lower}.beat.delivery_and_consequence",
        };
        var beatNames = new[]
        {
            MissionBeatKind.Proposal,
            MissionBeatKind.Preparation,
            MissionBeatKind.TechnicalDiscovery,
            MissionBeatKind.SetbackOrCorrection,
            MissionBeatKind.DeliveryAndConsequence,
        };
        var events = beatNames.Select((b, idx) => new MissionBeatEvent(
            $"{lower}_{b}",
            b,
            $"trigger_{idx}",
            new DialogueEntry(keys[idx], "coordinator", b),
            new SceneCue($"cue_{lower}_{idx}", $"sfx_{idx}", true),
            idx == 4 ? string.Join("|", unlocks.Select(u => $"unlock_mission:{u}").Concat(vehicleUnlocks.Select(v => $"unlock_vehicle:{v}"))) : $"step_{idx}")).ToArray();

        return new MissionDefinition(
            1, id, 1, $"mission.{lower}.title", $"mission.{lower}.summary",
            minLevel, minPlatformId, vehicleUnlocks, weatherMode, controlMode,
            [new MissionObjectiveDefinition(objectiveId, contractId)],
            events, preconditions, true, null, keys, unlocks);
    }

    private static ContractDefinition BuildEmbeddedHist01Contract(IReadOnlyList<string> facadeIds) =>
        new(
            1,
            "contract_hist_01",
            "mission.hist_01.title",
            "city_hall",
            "old_center",
            "commerce_street_facades",
            "images",
            facadeIds,
            [
                new ThresholdRequirementSpec("area_coverage", "ratio", "min", 0.9, 0.98, "commerce_facade_planes", 2.0),
                new PredicateRequirementSpec("all_required_features", true),
                new ThresholdRequirementSpec("valid_image_fraction", "ratio", "min", 0.8, 0.98),
                new PredicateRequirementSpec("privacy_redacted", true),
                new PredicateRequirementSpec("format_valid", true),
            ],
            ["camera", "gnss_basic"],
            "easy",
            true,
            new ContractDeadlineSpec(21600, 27000, 0.05, 0.5),
            new ContractRewardSpec(400, 100, 2, ["HIST-02"]),
            new ContractPartialPolicySpec("disabled", []),
            "gsa_bundle_v1",
            []);

    private static ContractDefinition BuildEmbeddedFollowUpContract(
        string id,
        string titleKey,
        string areaId,
        IReadOnlyList<string> featureIds,
        string accuracyMetric,
        double limit,
        double excellent,
        int baseCoords,
        int baseXp,
        IReadOnlyList<string> prereqs,
        IReadOnlyList<string> unlocks) =>
        new(
            1,
            id,
            titleKey,
            id == "contract_hist_03" ? "university" : "city_hall",
            "old_center",
            areaId,
            "feature_inventory",
            featureIds,
            [
                new ThresholdRequirementSpec("feature_completeness", "ratio", "min", 0.9, 1.0),
                new ThresholdRequirementSpec(accuracyMetric, "m", "max", limit, excellent),
                new ThresholdRequirementSpec("valid_image_fraction", "ratio", "min", 0.8, 0.98),
                new PredicateRequirementSpec("privacy_redacted", true),
                new PredicateRequirementSpec("format_valid", true),
            ],
            ["camera", "gnss_basic"],
            "easy",
            true,
            new ContractDeadlineSpec(21600, 27000, 0.05, 0.5),
            new ContractRewardSpec(baseCoords, baseXp, 2, unlocks),
            new ContractPartialPolicySpec("disabled", []),
            "gsa_bundle_v1",
            prereqs);

    private static void AssertOk(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
