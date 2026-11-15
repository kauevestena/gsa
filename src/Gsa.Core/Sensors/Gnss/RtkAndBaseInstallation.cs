using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Randomness;

namespace Gsa.Core.Sensors.Gnss;

public sealed record BaseInstallationOutcome(
    bool Succeeded,
    bool Installed,
    string MarkId,
    double AccumulatedOccupationSeconds,
    double RequiredOccupationSeconds,
    string ReasonKey,
    InstalledBaseStationState? BaseState = null);

public sealed class InstalledBaseStationState
{
    private readonly List<BaseStationCorrectionSample> _recordedSamples = [];

    public string MarkId { get; }
    public Vector3d KnownPositionEnu { get; }
    public double InstalledAtSeconds { get; }
    public double RemainingBatterySeconds { get; private set; }
    public bool IsActive { get; private set; }
    public IReadOnlyList<BaseStationCorrectionSample> RecordedSamples => _recordedSamples;

    public InstalledBaseStationState(
        string markId,
        Vector3d knownPositionEnu,
        double installedAtSeconds,
        double initialBatterySeconds = 7200.0,
        bool isActive = true,
        IEnumerable<BaseStationCorrectionSample>? restoredSamples = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markId);
        if (!double.IsFinite(initialBatterySeconds) || initialBatterySeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(initialBatterySeconds));

        MarkId = markId;
        KnownPositionEnu = knownPositionEnu;
        InstalledAtSeconds = installedAtSeconds;
        RemainingBatterySeconds = initialBatterySeconds;
        IsActive = isActive && initialBatterySeconds > 0.0;
        if (restoredSamples is not null)
            _recordedSamples.AddRange(restoredSamples);
    }

    public bool RecordEpochCorrection(double timestampSeconds, Vector3d commonErrorEnu, double baseOpenness, double dtSeconds = 1.0)
    {
        if (!IsActive)
            return false;
        if (!double.IsFinite(dtSeconds) || dtSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(dtSeconds));

        RemainingBatterySeconds = Math.Max(0.0, RemainingBatterySeconds - dtSeconds);
        if (RemainingBatterySeconds <= 0.0)
        {
            IsActive = false;
            return false;
        }

        // Base correction removes the common error experienced at that epoch
        _recordedSamples.Add(new BaseStationCorrectionSample(timestampSeconds, MarkId, -commonErrorEnu, baseOpenness));
        return true;
    }

    public void DeactivateOrRemove() => IsActive = false;

    public BaseStationLog ExportLog(double samplingIntervalSeconds = 0.2) =>
        new(MarkId, KnownPositionEnu, samplingIntervalSeconds, _recordedSamples);
}

/// <summary>Handles 5-second on-foot survey-mark base station setup, battery consumption, and removal preserving past logs.</summary>
public sealed class BaseStationInstaller
{
    public const double RequiredOccupationSeconds = 5.0;
    public const double MaxMarkDistanceMetres = 2.0;
    public const double MinimumMarkOpenness = 0.8;

    private string? _targetMarkId;
    private double _accumulatedOccupationSeconds;

    public BaseInstallationOutcome AdvanceInstallation(
        LocomotionMode playerMode,
        Vector3d playerPositionEnu,
        SurveyMarkBaseStationSpec markSpec,
        double dtSeconds,
        double currentTimeSeconds,
        double markOpenness,
        bool hasPowerAndLog = true,
        double initialBatterySeconds = 7200.0)
    {
        ArgumentNullException.ThrowIfNull(markSpec);
        if (!double.IsFinite(dtSeconds) || dtSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(dtSeconds));

        if (playerMode != LocomotionMode.OnFoot)
        {
            _accumulatedOccupationSeconds = 0.0;
            return new BaseInstallationOutcome(false, false, markSpec.MarkId, 0.0, RequiredOccupationSeconds, "gnss.base.must_be_on_foot");
        }

        var dist = (playerPositionEnu - markSpec.KnownPositionEnu).Length;
        if (dist > MaxMarkDistanceMetres)
        {
            _accumulatedOccupationSeconds = 0.0;
            return new BaseInstallationOutcome(false, false, markSpec.MarkId, 0.0, RequiredOccupationSeconds, "gnss.base.too_far_from_mark");
        }

        if (markOpenness < Math.Max(MinimumMarkOpenness, markSpec.MinimumOpenness))
        {
            _accumulatedOccupationSeconds = 0.0;
            return new BaseInstallationOutcome(false, false, markSpec.MarkId, 0.0, RequiredOccupationSeconds, "gnss.base.insufficient_sky_openness");
        }

        if (!hasPowerAndLog || initialBatterySeconds <= 0.0)
        {
            _accumulatedOccupationSeconds = 0.0;
            return new BaseInstallationOutcome(false, false, markSpec.MarkId, 0.0, RequiredOccupationSeconds, "gnss.base.missing_power_or_log");
        }

        if (!string.Equals(_targetMarkId, markSpec.MarkId, StringComparison.Ordinal))
        {
            _targetMarkId = markSpec.MarkId;
            _accumulatedOccupationSeconds = 0.0;
        }

        _accumulatedOccupationSeconds += dtSeconds;
        if (_accumulatedOccupationSeconds + 1e-9 < RequiredOccupationSeconds)
        {
            return new BaseInstallationOutcome(
                Succeeded: true,
                Installed: false,
                MarkId: markSpec.MarkId,
                AccumulatedOccupationSeconds: _accumulatedOccupationSeconds,
                RequiredOccupationSeconds: RequiredOccupationSeconds,
                ReasonKey: "gnss.base.occupying_mark");
        }

        var state = new InstalledBaseStationState(markSpec.MarkId, markSpec.KnownPositionEnu, currentTimeSeconds, initialBatterySeconds);
        return new BaseInstallationOutcome(
            Succeeded: true,
            Installed: true,
            MarkId: markSpec.MarkId,
            AccumulatedOccupationSeconds: _accumulatedOccupationSeconds,
            RequiredOccupationSeconds: RequiredOccupationSeconds,
            ReasonKey: "gnss.base.installed",
            BaseState: state);
    }
}

public sealed record CorrectableGnssObservationRecord(
    long Sequence,
    double TimestampSeconds,
    Vector3d UncorrectedRoverPositionEnu,
    Vector3d CommonErrorEnu,
    GnssSolutionKind LiveSolutionKind,
    double Openness,
    double BaselineMetres,
    string ProfileId,
    Vector3d? LiveMeasuredPositionEnu);

public sealed record RtkStateSnapshot(
    int SchemaVersion,
    GnssSolutionKind CurrentSolution,
    double HighOpennessStabilizationSeconds,
    long NextSequence,
    GaussianRandomState NoiseRngState,
    IReadOnlyList<CorrectableGnssObservationRecord> CorrectableLog);

/// <summary>RTK float/fix state machine enforcing 0.4 float threshold, 0.7 for 10 s fix stabilization, 2 km max baseline, and noise model.</summary>
public sealed class RtkSolutionStateMachine
{
    public const double MinimumOpennessForSignal = 0.1;
    public const double MinimumOpennessForRtkFloat = 0.4;
    public const double MinimumOpennessForRtkFix = 0.7;
    public const double FixStabilizationSeconds = 10.0;
    public const double MaxRtkBaselineMetres = 2000.0;
    public const double RtkBaselineDecorrelationMetresPerKm = 0.005;

    private readonly List<CorrectableGnssObservationRecord> _correctableLog = [];
    private GaussianRandom _rng;
    private long _nextSequence = 1;

    public GnssSolutionKind CurrentSolution { get; private set; } = GnssSolutionKind.Autonomous;
    public double HighOpennessStabilizationSeconds { get; private set; }
    public IReadOnlyList<CorrectableGnssObservationRecord> CorrectableLog => _correctableLog;

    public RtkSolutionStateMachine(ulong seed = 424242UL)
    {
        _rng = new GaussianRandom(seed);
    }

    private RtkSolutionStateMachine(RtkStateSnapshot snapshot)
    {
        CurrentSolution = snapshot.CurrentSolution;
        HighOpennessStabilizationSeconds = snapshot.HighOpennessStabilizationSeconds;
        _nextSequence = snapshot.NextSequence;
        _rng = new GaussianRandom(snapshot.NoiseRngState);
        _correctableLog.AddRange(snapshot.CorrectableLog);
    }

    public GnssObservation StepEpoch(
        double timestampSeconds,
        double dtSeconds,
        Vector3d truePositionEnu,
        Vector3d commonErrorEnu,
        double openness,
        int platformLevel,
        bool hasMultiFrequencyProfile,
        InstalledBaseStationState? activeBase,
        Vector3d? externalMultipathBiasEnu = null)
    {
        if (!double.IsFinite(dtSeconds) || dtSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(dtSeconds));

        var dop = GnssProfileSelector.ComputeDopForOpenness(openness, platformLevel);
        var multipath = externalMultipathBiasEnu ?? Vector3d.Zero;
        var baseAvailable = activeBase is not null && activeBase.IsActive;
        var baseline = activeBase is not null
            ? Math.Sqrt(Math.Pow(truePositionEnu.X - activeBase.KnownPositionEnu.X, 2) + Math.Pow(truePositionEnu.Y - activeBase.KnownPositionEnu.Y, 2))
            : double.PositiveInfinity;

        GnssProfile nominalProfile;
        Vector3d? measured = null;
        var seq = _nextSequence++;

        if (openness < MinimumOpennessForSignal)
        {
            CurrentSolution = GnssSolutionKind.Unavailable;
            HighOpennessStabilizationSeconds = 0.0;
            nominalProfile = hasMultiFrequencyProfile ? GnssProfile.RtkFloat : GnssProfile.Navigation;
        }
        else if (!baseAvailable || !hasMultiFrequencyProfile || openness < MinimumOpennessForRtkFloat || baseline > MaxRtkBaselineMetres)
        {
            // Below 0.4 openness or without active base within 2 km, fall back to Autonomous (never claim float/fix)
            HighOpennessStabilizationSeconds = 0.0;
            if (baseAvailable && !hasMultiFrequencyProfile && baseline <= DgnssRoverCorrector.DefaultMaxBaselineMetres)
            {
                CurrentSolution = GnssSolutionKind.Dgnss;
                nominalProfile = GnssProfile.Dgnss;
            }
            else
            {
                CurrentSolution = GnssSolutionKind.Autonomous;
                nominalProfile = platformLevel >= 1 ? GnssProfile.Navigation : GnssProfile.Cellphone;
            }
        }
        else
        {
            if (openness >= MinimumOpennessForRtkFix)
                HighOpennessStabilizationSeconds += dtSeconds;
            else
                HighOpennessStabilizationSeconds = 0.0;

            if (HighOpennessStabilizationSeconds + 1e-9 >= FixStabilizationSeconds)
            {
                CurrentSolution = GnssSolutionKind.RtkFix;
                nominalProfile = GnssProfile.RtkFix;
            }
            else
            {
                CurrentSolution = GnssSolutionKind.RtkFloat;
                nominalProfile = GnssProfile.RtkFloat;
            }
        }

        double? independentSigma = null;
        if (CurrentSolution != GnssSolutionKind.Unavailable)
        {
            var residualDecorrelationSigma = CurrentSolution is GnssSolutionKind.RtkFloat or GnssSolutionKind.RtkFix
                ? RtkBaselineDecorrelationMetresPerKm * (baseline / 1000.0)
                : 0.0;

            independentSigma = nominalProfile.BaseSigmaMetres * dop + residualDecorrelationSigma;
            var noise = new Vector3d(
                independentSigma.Value * _rng.NextGaussian(),
                independentSigma.Value * _rng.NextGaussian(),
                independentSigma.Value * _rng.NextGaussian());

            // Corrected modes (DGNSS / RTK float / RTK fix) cancel shared epoch common error; autonomous keeps commonErrorEnu
            var uncancelledCommon = CurrentSolution is GnssSolutionKind.Dgnss or GnssSolutionKind.RtkFloat or GnssSolutionKind.RtkFix
                ? Vector3d.Zero
                : commonErrorEnu;

            measured = truePositionEnu + uncancelledCommon + multipath + noise;
        }

        // Always record uncorrected raw rover + common error for PPK without leaking truePositionEnu
        var uncorrectedRaw = truePositionEnu + commonErrorEnu + multipath +
                             (independentSigma.HasValue
                                 ? new Vector3d(independentSigma.Value * 0.25, -independentSigma.Value * 0.25, 0.0)
                                 : Vector3d.Zero);

        _correctableLog.Add(new CorrectableGnssObservationRecord(
            seq, timestampSeconds, uncorrectedRaw, commonErrorEnu, CurrentSolution, openness,
            double.IsFinite(baseline) ? baseline : -1.0, nominalProfile.Id, measured));

        return new GnssObservation(seq, timestampSeconds, nominalProfile.Id, CurrentSolution, measured, dop, independentSigma);
    }

    public RtkStateSnapshot ExportSnapshot() =>
        new(1, CurrentSolution, HighOpennessStabilizationSeconds, _nextSequence, _rng.ExportState(), Array.AsReadOnly(_correctableLog.ToArray()));

    public static RtkSolutionStateMachine FromSnapshot(RtkStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(snapshot);
    }
}
