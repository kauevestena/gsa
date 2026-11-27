using Gsa.Core.Geometry;

namespace Gsa.Core.Sensors.Camera;

/// <summary>Engine-neutral automatic MMS camera shutter controller with manual toggle and stereo baseline gating.</summary>
public sealed class AutoCameraTriggerController
{
    public const double MinCoverageMoveMetres = 1.75;
    public const double MinCoverageTurnRadians = 0.24;
    public const double MinStereoBaselineMetres = 2.2;
    public const double FailedShotCooldownSeconds = 2.0;
    public const double RetryAfterFailureMoveMetres = 1.2;

    private readonly Dictionary<string, List<Vector3d>> _validViewpointsByTarget = new(StringComparer.Ordinal);
    private bool _hasLastAutoShot;
    private Vector3d _lastAutoPosEnu;
    private double _lastAutoHeadingRad;
    private double _cooldownSeconds;
    private bool _blockedByLensObstruction;

    /// <summary>When false (default), captures fire automatically when survey framing/baseline criteria are met.</summary>
    public bool ManualMode { get; set; }

    public bool IsBlockedByLensObstruction => _blockedByLensObstruction;

    public void Reset()
    {
        _validViewpointsByTarget.Clear();
        _hasLastAutoShot = false;
        _lastAutoPosEnu = Vector3d.Zero;
        _lastAutoHeadingRad = 0.0;
        _cooldownSeconds = 0.0;
        _blockedByLensObstruction = false;
    }

    public void StepCooldown(double dtSeconds, double currentLensObstruction, double maxLensObstruction = 0.20)
    {
        if (dtSeconds > 0.0 && _cooldownSeconds > 0.0)
            _cooldownSeconds = Math.Max(0.0, _cooldownSeconds - dtSeconds);

        if (_blockedByLensObstruction && currentLensObstruction <= maxLensObstruction)
            _blockedByLensObstruction = false;
    }

    public bool ShouldAutoTriggerCoverage(
        Vector3d posEnu,
        double headingRadians,
        bool hasIncompleteTargetInView,
        double currentLensObstruction,
        double maxLensObstruction = 0.20)
    {
        if (ManualMode || !hasIncompleteTargetInView || _cooldownSeconds > 0.0 || _blockedByLensObstruction)
            return false;

        if (!_hasLastAutoShot)
            return true;

        if (HorizontalDistance(posEnu, _lastAutoPosEnu) >= MinCoverageMoveMetres)
            return true;

        var yawDiff = Math.Abs(Math.IEEERemainder(headingRadians - _lastAutoHeadingRad, Math.Tau));
        return yawDiff >= MinCoverageTurnRadians;
    }

    public bool ShouldAutoTriggerStereoTarget(
        Vector3d posEnu,
        IEnumerable<string> visibleTargetIds,
        int requiredViewsPerTarget = 2,
        double minBaselineMetres = MinStereoBaselineMetres,
        double currentLensObstruction = 0.0,
        double maxLensObstruction = 0.20)
    {
        if (ManualMode || _cooldownSeconds > 0.0 || _blockedByLensObstruction)
            return false;

        foreach (var targetId in visibleTargetIds)
        {
            if (!_validViewpointsByTarget.TryGetValue(targetId, out var views) || views.Count == 0)
                return true;

            if (views.Count < requiredViewsPerTarget)
            {
                var separated = true;
                foreach (var existing in views)
                {
                    if (HorizontalDistance(posEnu, existing) < minBaselineMetres)
                    {
                        separated = false;
                        break;
                    }
                }
                if (separated)
                    return true;
            }
        }

        return false;
    }

    public int GetValidViewCount(string targetId) =>
        _validViewpointsByTarget.TryGetValue(targetId, out var views) ? views.Count : 0;

    public void RecordCaptureOutcome(
        Vector3d posEnu,
        double headingRadians,
        CameraObservationSample sample,
        IEnumerable<string>? relevantTargetIds = null,
        double minBaselineMetres = MinStereoBaselineMetres)
    {
        ArgumentNullException.ThrowIfNull(sample);

        _hasLastAutoShot = true;
        _lastAutoPosEnu = posEnu;
        _lastAutoHeadingRad = headingRadians;

        if (!sample.Quality.Passed)
        {
            _cooldownSeconds = FailedShotCooldownSeconds;
            if (sample.Quality.LensObstructionExceeded)
                _blockedByLensObstruction = true;
            return;
        }

        HashSet<string>? filter = relevantTargetIds is null
            ? null
            : new HashSet<string>(relevantTargetIds, StringComparer.Ordinal);

        foreach (var feature in sample.ObservedFeatures)
        {
            if (feature.TargetId.StartsWith("coverage/", StringComparison.Ordinal))
                continue;
            if (filter is not null && !filter.Contains(feature.TargetId))
                continue;

            RecordValidViewpoint(feature.TargetId, posEnu, minBaselineMetres);
        }
    }

    public void RebuildFromSamples(IEnumerable<CameraObservationSample> existingSamples, double minBaselineMetres = MinStereoBaselineMetres)
    {
        _validViewpointsByTarget.Clear();
        foreach (var sample in existingSamples)
        {
            if (!sample.Quality.Passed)
                continue;
            var pos = sample.EstimatedPose.PositionEnu;
            foreach (var feature in sample.ObservedFeatures)
            {
                if (feature.TargetId.StartsWith("coverage/", StringComparison.Ordinal))
                    continue;
                RecordValidViewpoint(feature.TargetId, pos, minBaselineMetres);
            }
        }
    }

    private void RecordValidViewpoint(string targetId, Vector3d posEnu, double minBaselineMetres)
    {
        if (!_validViewpointsByTarget.TryGetValue(targetId, out var views))
        {
            views = [];
            _validViewpointsByTarget[targetId] = views;
        }
        var threshold = minBaselineMetres * 0.75;
        foreach (var existing in views)
        {
            if (HorizontalDistance(posEnu, existing) < threshold)
                return;
        }
        views.Add(posEnu);
    }

    private static double HorizontalDistance(Vector3d a, Vector3d b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

