using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Randomness;
using Gsa.Core.Sensors.Camera;

namespace Gsa.Core.Sensors.Calibration;

public enum RackSlotKind
{
    RoofCamera = 0,
    Panoramic360 = 1,
    GnssAntennaPrimary = 2,
    GnssAntennaSecondary = 3,
    ImuUnit = 4,
    WheelDmi = 5,
    LidarProfilerOrScanner = 6,
    OnboardComputer = 7,
    StorageBay = 8,
    PowerPack = 9,
    PavementCamera = 10,
}

public sealed record RackSlotDefinition(
    string SlotId,
    RackSlotKind Kind,
    int MinimumPlatformLevel,
    Vector3d NominalLeverArmBody,
    Quaterniond NominalSensorToBody,
    double MaxPowerWatts,
    int MaxWeightGrams);

/// <summary>Separated mounting transform revision (never mutated retroactively for past capture batches).</summary>
public sealed record MountCalibration(
    string CalibrationId,
    int Revision,
    string SlotId,
    string SensorId,
    Vector3d LeverArmBody,
    Quaterniond SensorToBody,
    double TimeOffsetSeconds,
    double EffectiveFromTimeSeconds)
{
    public CameraMountEstimate ToCameraMountEstimate() =>
        new(LeverArmBody, SensorToBody, TimeOffsetSeconds);
}

public sealed record SlotMountPair(
    string SlotId,
    string SensorId,
    RackSlotKind SensorKind,
    int Revision,
    double PowerWatts,
    int WeightGrams,
    MountCalibration PhysicalTruthMount,
    MountCalibration EstimatedCalibrationMount,
    bool EstimatedCalibrationInvalidated,
    bool NeedsWorkshopInspection);

public sealed record MountRevisionRecord(
    string SlotId,
    string SensorId,
    int Revision,
    double EffectiveFromTimeSeconds,
    MountCalibration PhysicalTruthMount,
    MountCalibration EstimatedCalibrationMount,
    bool EstimatedCalibrationInvalidated,
    string ReasonKey);

public sealed record RackMountAttemptResult(
    bool Succeeded,
    string SlotId,
    string SensorId,
    int NewRevision,
    string ReasonKey,
    SlotMountPair? SlotState = null);

/// <summary>Player-facing observable symptoms after impact; deliberately omits ground-truth misalignment deltas.</summary>
public sealed record ImpactPerturbationSymptom(
    string EventId,
    double TimestampSeconds,
    bool InspectionRequired,
    IReadOnlyList<string> AffectedSlotIds,
    string DiagnosticSummaryKey);

public sealed record ObservableDiscrepancySummary(
    string SlotId,
    bool NeedsWorkshopInspection,
    double LeverArmErrorMetres,
    double ProjectedBoresightDiscrepancyMetres,
    double LongitudinalTimingDiscrepancyMetres,
    double CombinedDiscrepancyMetres);

public static class RackCatalog
{
    public static IReadOnlyList<RackSlotDefinition> CreateDefaultSlotsForPlatform(PlatformProfile platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        var slots = new List<RackSlotDefinition>
        {
            new("slot_roof_cam_a", RackSlotKind.RoofCamera, 0, new Vector3d(0.25, 0.0, 1.55), CameraMountEstimate.ForwardFacing.CameraToBody, 25.0, 1200),
            new("slot_roof_cam_b", RackSlotKind.RoofCamera, 2, new Vector3d(-0.35, 0.0, 1.55), CameraMountEstimate.ForwardFacing.CameraToBody, 25.0, 1200),
            new("slot_gnss_primary", RackSlotKind.GnssAntennaPrimary, 0, new Vector3d(0.0, 0.0, 1.85), Quaterniond.Identity, 10.0, 500),
            new("slot_imu_core", RackSlotKind.ImuUnit, 3, new Vector3d(0.0, 0.0, 0.85), Quaterniond.Identity, 12.0, 600),
            new("slot_computer", RackSlotKind.OnboardComputer, 0, new Vector3d(-0.2, 0.0, 0.65), Quaterniond.Identity, 120.0, 4500),
            new("slot_storage", RackSlotKind.StorageBay, 0, new Vector3d(-0.25, 0.0, 0.60), Quaterniond.Identity, 25.0, 1500),
            new("slot_power", RackSlotKind.PowerPack, 0, new Vector3d(-0.30, 0.0, 0.50), Quaterniond.Identity, 500.0, 12000),
            new("slot_cam_360", RackSlotKind.Panoramic360, 3, new Vector3d(0.0, 0.0, 1.95), CameraMountEstimate.ForwardFacing.CameraToBody, 45.0, 2200),
            new("slot_wheel_dmi", RackSlotKind.WheelDmi, 4, new Vector3d(-0.95, 0.75, 0.35), Quaterniond.Identity, 8.0, 400),
            new("slot_lidar_a", RackSlotKind.LidarProfilerOrScanner, 4, new Vector3d(0.10, 0.0, 2.15), Quaterniond.Identity, 90.0, 4500),
            new("slot_lidar_b", RackSlotKind.LidarProfilerOrScanner, 5, new Vector3d(-0.15, 0.42, 2.08), Quaterniond.FromAxisAngle(Vector3d.UnitX, 0.12), 90.0, 4500),
            new("slot_gnss_secondary", RackSlotKind.GnssAntennaSecondary, 5, new Vector3d(-1.00, 0.0, 1.90), Quaterniond.Identity, 10.0, 500),
            new("slot_pavement_cam_a", RackSlotKind.PavementCamera, 6, new Vector3d(-1.50, -0.35, 0.90), Quaterniond.FromAxisAngle(Vector3d.UnitY, Math.PI / 2.0), 35.0, 1600),
            new("slot_pavement_cam_b", RackSlotKind.PavementCamera, 6, new Vector3d(-1.50, 0.35, 0.90), Quaterniond.FromAxisAngle(Vector3d.UnitY, Math.PI / 2.0), 35.0, 1600),
        };
        return Array.AsReadOnly(slots.ToArray());
    }
}

/// <summary>Manages modular sensor rack slots, physical vs estimated calibration state, and immutable historical revisions.</summary>
public sealed class ModularSensorRack
{
    private readonly Dictionary<string, RackSlotDefinition> _slots;
    private readonly Dictionary<string, SlotMountPair> _mountedBySlot = new(StringComparer.Ordinal);
    private readonly List<MountRevisionRecord> _revisionHistory = [];
    private int _revisionCounter;

    public PlatformProfile ActivePlatform { get; private set; }
    public double MaxPowerBudgetWatts { get; }
    public int MaxPayloadWeightGrams { get; }
    public IReadOnlyDictionary<string, RackSlotDefinition> Slots => _slots;
    public IReadOnlyDictionary<string, SlotMountPair> MountedSlots => _mountedBySlot;
    public IReadOnlyList<MountRevisionRecord> RevisionHistory => _revisionHistory;

    public double CurrentPowerLoadWatts => _mountedBySlot.Values.Sum(m => m.PowerWatts);
    public int CurrentWeightLoadGrams => _mountedBySlot.Values.Sum(m => m.WeightGrams);

    public ModularSensorRack(
        PlatformProfile platform,
        IEnumerable<RackSlotDefinition>? slots = null,
        double maxPowerBudgetWatts = 650.0,
        int maxPayloadWeightGrams = 35000)
    {
        ArgumentNullException.ThrowIfNull(platform);
        if (!double.IsFinite(maxPowerBudgetWatts) || maxPowerBudgetWatts <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPowerBudgetWatts));
        if (maxPayloadWeightGrams <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPayloadWeightGrams));

        ActivePlatform = platform;
        MaxPowerBudgetWatts = maxPowerBudgetWatts;
        MaxPayloadWeightGrams = maxPayloadWeightGrams;
        var slotList = (slots ?? RackCatalog.CreateDefaultSlotsForPlatform(platform)).ToArray();
        _slots = slotList.ToDictionary(s => s.SlotId, StringComparer.Ordinal);
    }

    public static bool IsWorkshopAccessible(int highestOwnedPlatformLevel) => highestOwnedPlatformLevel >= 3;

    public RackMountAttemptResult MountOrMoveSensor(
        string sensorId,
        RackSlotKind sensorKind,
        string targetSlotId,
        double effectiveTimeSeconds,
        double powerWatts = 15.0,
        int weightGrams = 600)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sensorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSlotId);
        if (!double.IsFinite(effectiveTimeSeconds) || effectiveTimeSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(effectiveTimeSeconds));
        if (!double.IsFinite(powerWatts) || powerWatts < 0.0)
            throw new ArgumentOutOfRangeException(nameof(powerWatts));
        if (weightGrams < 0)
            throw new ArgumentOutOfRangeException(nameof(weightGrams));

        if (!_slots.TryGetValue(targetSlotId, out var slot))
            return new RackMountAttemptResult(false, targetSlotId, sensorId, 0, "rack.error.unknown_slot");

        if (slot.Kind != sensorKind)
            return new RackMountAttemptResult(false, targetSlotId, sensorId, 0, "rack.error.slot_kind_incompatible");

        if (sensorKind == RackSlotKind.LidarProfilerOrScanner && !ActivePlatform.SupportsLidarSlots)
            return new RackMountAttemptResult(false, targetSlotId, sensorId, 0, "rack.error.lidar_requires_level_4");

        if (sensorKind == RackSlotKind.GnssAntennaSecondary && ActivePlatform.Level < 5)
            return new RackMountAttemptResult(false, targetSlotId, sensorId, 0, "rack.error.dual_antenna_requires_level_5");

        if (ActivePlatform.Level < slot.MinimumPlatformLevel)
            return new RackMountAttemptResult(false, targetSlotId, sensorId, 0, "rack.error.platform_level_insufficient");

        if (powerWatts > slot.MaxPowerWatts)
            return new RackMountAttemptResult(false, targetSlotId, sensorId, 0, "rack.error.slot_power_limit_exceeded");

        if (weightGrams > slot.MaxWeightGrams)
            return new RackMountAttemptResult(false, targetSlotId, sensorId, 0, "rack.error.slot_weight_limit_exceeded");

        // Compute net power and weight delta if replacing target slot or relocating from previous slot
        var existingInOtherSlot = _mountedBySlot.Values
            .FirstOrDefault(m => string.Equals(m.SensorId, sensorId, StringComparison.Ordinal) &&
                                 !string.Equals(m.SlotId, targetSlotId, StringComparison.Ordinal));
        var displacedTarget = _mountedBySlot.GetValueOrDefault(targetSlotId);

        var projectedPower = CurrentPowerLoadWatts
            - (existingInOtherSlot?.PowerWatts ?? 0.0)
            - (displacedTarget?.PowerWatts ?? 0.0)
            + powerWatts;
        if (projectedPower > MaxPowerBudgetWatts)
            return new RackMountAttemptResult(false, targetSlotId, sensorId, 0, "rack.error.power_capacity_exceeded");

        var projectedWeight = CurrentWeightLoadGrams
            - (existingInOtherSlot?.WeightGrams ?? 0)
            - (displacedTarget?.WeightGrams ?? 0)
            + weightGrams;
        if (projectedWeight > MaxPayloadWeightGrams)
            return new RackMountAttemptResult(false, targetSlotId, sensorId, 0, "rack.error.weight_capacity_exceeded");

        MountCalibration? previousEstimated = existingInOtherSlot?.EstimatedCalibrationMount ?? displacedTarget?.EstimatedCalibrationMount;
        if (existingInOtherSlot is not null)
            _mountedBySlot.Remove(existingInOtherSlot.SlotId);

        var rev = ++_revisionCounter;
        var physical = new MountCalibration(
            $"phys_{targetSlotId}_r{rev}",
            rev,
            targetSlotId,
            sensorId,
            slot.NominalLeverArmBody,
            slot.NominalSensorToBody,
            TimeOffsetSeconds: 0.0,
            EffectiveFromTimeSeconds: effectiveTimeSeconds);

        // Moving a sensor preserves the stale previous slot estimate so estimation error shows up until recalibrated
        var estimated = previousEstimated is not null && existingInOtherSlot is not null
            ? previousEstimated with
            {
                CalibrationId = $"est_{targetSlotId}_r{rev}",
                Revision = rev,
                SlotId = targetSlotId,
                SensorId = sensorId,
                EffectiveFromTimeSeconds = effectiveTimeSeconds,
            }
            : new MountCalibration(
                $"est_{targetSlotId}_r{rev}",
                rev,
                targetSlotId,
                sensorId,
                slot.NominalLeverArmBody,
                slot.NominalSensorToBody,
                TimeOffsetSeconds: 0.0,
                EffectiveFromTimeSeconds: effectiveTimeSeconds);

        var movedInvalidated = existingInOtherSlot is not null;
        var pair = new SlotMountPair(
            targetSlotId,
            sensorId,
            sensorKind,
            rev,
            powerWatts,
            weightGrams,
            physical,
            estimated,
            EstimatedCalibrationInvalidated: movedInvalidated,
            NeedsWorkshopInspection: movedInvalidated);

        _mountedBySlot[targetSlotId] = pair;
        _revisionHistory.Add(new MountRevisionRecord(
            targetSlotId,
            sensorId,
            rev,
            effectiveTimeSeconds,
            physical,
            estimated,
            movedInvalidated,
            movedInvalidated ? "rack.revision.sensor_moved" : "rack.revision.sensor_mounted"));

        return new RackMountAttemptResult(true, targetSlotId, sensorId, rev, pair.EstimatedCalibrationInvalidated ? "rack.mounted_needs_recalibration" : "rack.mounted", pair);
    }

    public SlotMountPair ApplyEstimatedCalibration(
        string slotId,
        Vector3d estimatedLeverArmBody,
        Quaterniond estimatedSensorToBody,
        double estimatedTimeOffsetSeconds,
        double effectiveTimeSeconds,
        string reasonKey = "rack.revision.workshop_calibrated")
    {
        if (!_mountedBySlot.TryGetValue(slotId, out var pair))
            throw new KeyNotFoundException($"No sensor mounted in slot '{slotId}'.");
        estimatedSensorToBody.EnsureValid();
        if (!double.IsFinite(estimatedTimeOffsetSeconds))
            throw new ArgumentOutOfRangeException(nameof(estimatedTimeOffsetSeconds));

        var rev = ++_revisionCounter;
        var updatedEstimated = new MountCalibration(
            $"est_{slotId}_r{rev}",
            rev,
            slotId,
            pair.SensorId,
            estimatedLeverArmBody,
            estimatedSensorToBody,
            estimatedTimeOffsetSeconds,
            effectiveTimeSeconds);

        var updated = pair with
        {
            Revision = rev,
            EstimatedCalibrationMount = updatedEstimated,
            EstimatedCalibrationInvalidated = false,
            NeedsWorkshopInspection = false,
        };
        _mountedBySlot[slotId] = updated;
        _revisionHistory.Add(new MountRevisionRecord(slotId, pair.SensorId, rev, effectiveTimeSeconds, pair.PhysicalTruthMount, updatedEstimated, false, reasonKey));
        return updated;
    }

    public MountRevisionRecord GetHistoricalRevision(int revision) =>
        _revisionHistory.FirstOrDefault(r => r.Revision == revision)
        ?? throw new KeyNotFoundException($"Rack revision {revision} not found.");

    public MountRevisionRecord GetMountAtTimestamp(string slotId, double timestampSeconds)
    {
        var match = _revisionHistory
            .Where(r => string.Equals(r.SlotId, slotId, StringComparison.Ordinal) && r.EffectiveFromTimeSeconds <= timestampSeconds)
            .OrderByDescending(r => r.Revision)
            .FirstOrDefault();

        return match ?? throw new KeyNotFoundException($"No historical mount revision for slot '{slotId}' at t={timestampSeconds}.");
    }

    public ObservableDiscrepancySummary ComputeObservableDiscrepancy(
        string slotId,
        double targetRangeMetres,
        double platformSpeedMetresPerSecond)
    {
        if (!_mountedBySlot.TryGetValue(slotId, out var pair))
            throw new KeyNotFoundException($"No sensor mounted in slot '{slotId}'.");
        if (!double.IsFinite(targetRangeMetres) || targetRangeMetres < 0.0)
            throw new ArgumentOutOfRangeException(nameof(targetRangeMetres));
        if (!double.IsFinite(platformSpeedMetresPerSecond) || platformSpeedMetresPerSecond < 0.0)
            throw new ArgumentOutOfRangeException(nameof(platformSpeedMetresPerSecond));

        var phys = pair.PhysicalTruthMount;
        var est = pair.EstimatedCalibrationMount;

        var leverArmError = (phys.LeverArmBody - est.LeverArmBody).Length;
        var relRot = phys.SensorToBody * est.SensorToBody.Inverse();
        var boresightAngleRad = 2.0 * Math.Acos(Math.Clamp(Math.Abs(relRot.W), 0.0, 1.0));
        var projectedBoresightErr = targetRangeMetres * 2.0 * Math.Sin(boresightAngleRad * 0.5);
        var timingErr = platformSpeedMetresPerSecond * Math.Abs(phys.TimeOffsetSeconds - est.TimeOffsetSeconds);
        var combined = Math.Sqrt(leverArmError * leverArmError + projectedBoresightErr * projectedBoresightErr + timingErr * timingErr);

        return new ObservableDiscrepancySummary(
            slotId,
            pair.NeedsWorkshopInspection,
            leverArmError,
            projectedBoresightErr,
            timingErr,
            combined);
    }

    internal SlotMountPair ApplyPhysicalPerturbationInternal(
        string slotId,
        Vector3d deltaLeverArmBody,
        Quaterniond deltaRotation,
        double deltaTimeOffsetSeconds,
        double effectiveTimeSeconds,
        string reasonKey)
    {
        if (!_mountedBySlot.TryGetValue(slotId, out var pair))
            throw new KeyNotFoundException($"No sensor mounted in slot '{slotId}'.");

        var rev = ++_revisionCounter;
        var prevPhys = pair.PhysicalTruthMount;
        var updatedPhysical = new MountCalibration(
            $"phys_{slotId}_r{rev}",
            rev,
            slotId,
            pair.SensorId,
            prevPhys.LeverArmBody + deltaLeverArmBody,
            deltaRotation * prevPhys.SensorToBody,
            prevPhys.TimeOffsetSeconds + deltaTimeOffsetSeconds,
            effectiveTimeSeconds);

        var updated = pair with
        {
            Revision = rev,
            PhysicalTruthMount = updatedPhysical,
            EstimatedCalibrationInvalidated = true,
            NeedsWorkshopInspection = true,
        };
        _mountedBySlot[slotId] = updated;
        _revisionHistory.Add(new MountRevisionRecord(slotId, pair.SensorId, rev, effectiveTimeSeconds, updatedPhysical, pair.EstimatedCalibrationMount, true, reasonKey));
        return updated;
    }
}

/// <summary>Perturbs physical rack transforms deterministically on high-delta-v impacts without exposing true offsets to UI.</summary>
public static class ImpactMisalignmentIntegrator
{
    public const double MaxLeverArmDeltaMetres = 0.03;
    public const double MaxBoresightDeltaRadians = 0.5 * Math.PI / 180.0;
    public const double MaxTimeOffsetDeltaSeconds = 0.005;

    public static ImpactPerturbationSymptom ApplyImpactToRack(
        ModularSensorRack rack,
        RackImpactEvent impactEvent,
        ulong seed)
    {
        ArgumentNullException.ThrowIfNull(rack);
        ArgumentNullException.ThrowIfNull(impactEvent);

        if (!impactEvent.TriggersRackMisalignment || rack.MountedSlots.Count == 0)
        {
            return new ImpactPerturbationSymptom(
                impactEvent.EventId,
                impactEvent.TimestampSeconds,
                InspectionRequired: false,
                AffectedSlotIds: [],
                DiagnosticSummaryKey: "rack.impact.below_threshold");
        }

        var rng = new GaussianRandom(seed);
        var affected = new List<string>();
        foreach (var slotId in rack.MountedSlots.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray())
        {
            // Map uniform random on (0,1) symmetrically into [-max, +max] within exact F13 bounds
            double SampleBounded(double maxAbs) => (2.0 * rng.NextUniformOpenUnit() - 1.0) * maxAbs;

            var dLever = new Vector3d(
                SampleBounded(MaxLeverArmDeltaMetres),
                SampleBounded(MaxLeverArmDeltaMetres),
                SampleBounded(MaxLeverArmDeltaMetres));

            var rx = Quaterniond.FromAxisAngle(Vector3d.UnitX, SampleBounded(MaxBoresightDeltaRadians));
            var ry = Quaterniond.FromAxisAngle(Vector3d.UnitY, SampleBounded(MaxBoresightDeltaRadians));
            var rz = Quaterniond.FromAxisAngle(Vector3d.UnitZ, SampleBounded(MaxBoresightDeltaRadians));
            var dRot = rz * ry * rx;

            var dTime = SampleBounded(MaxTimeOffsetDeltaSeconds);
            rack.ApplyPhysicalPerturbationInternal(
                slotId,
                dLever,
                dRot,
                dTime,
                impactEvent.TimestampSeconds,
                $"rack.impact:{impactEvent.EventId}");
            affected.Add(slotId);
        }

        return new ImpactPerturbationSymptom(
            impactEvent.EventId,
            impactEvent.TimestampSeconds,
            InspectionRequired: true,
            AffectedSlotIds: Array.AsReadOnly(affected.ToArray()),
            DiagnosticSummaryKey: "rack.impact.inspection_required");
    }
}

