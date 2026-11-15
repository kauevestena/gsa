using Gsa.Core.Geometry;
using Gsa.Core.Mobility;

namespace Gsa.Core.Sensors.Camera;

public readonly record struct SurfaceFootprint2DEnu(
    double MinEast,
    double MinNorth,
    double MaxEast,
    double MaxNorth)
{
    public double Width => MaxEast - MinEast;
    public double Height => MaxNorth - MinNorth;
    public double Area => Width > 0.0 && Height > 0.0 ? Width * Height : 0.0;

    public double IntersectionArea(SurfaceFootprint2DEnu other)
    {
        var dx = Math.Min(MaxEast, other.MaxEast) - Math.Max(MinEast, other.MinEast);
        var dy = Math.Min(MaxNorth, other.MaxNorth) - Math.Max(MinNorth, other.MinNorth);
        return dx > 0.0 && dy > 0.0 ? dx * dy : 0.0;
    }
}

/// <summary>Accumulates lens droplets/paint contamination and clears contamination only after >= 3 s stopped.</summary>
public sealed class LensContaminationTracker
{
    public const double RequiredStoppedCleaningSeconds = 3.0;
    public const double MaxStoppedSpeedMetresPerSecond = 0.1;

    public double ObstructionFraction { get; private set; }
    public double AccumulatedStopCleaningSeconds { get; private set; }

    public LensContaminationTracker(double initialObstructionFraction = 0.0)
    {
        if (!double.IsFinite(initialObstructionFraction) || initialObstructionFraction < 0.0 || initialObstructionFraction > 1.0)
            throw new ArgumentOutOfRangeException(nameof(initialObstructionFraction));
        ObstructionFraction = initialObstructionFraction;
    }

    public void AccumulateRain(double dtSeconds, double rainIntensity)
    {
        if (!double.IsFinite(dtSeconds) || dtSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(dtSeconds));
        if (!double.IsFinite(rainIntensity) || rainIntensity < 0.0 || rainIntensity > 1.0)
            throw new ArgumentOutOfRangeException(nameof(rainIntensity));

        if (dtSeconds > 0.0 && rainIntensity > 0.0)
        {
            AccumulatedStopCleaningSeconds = 0.0;
            ObstructionFraction = Math.Clamp(ObstructionFraction + 0.08 * rainIntensity * dtSeconds, 0.0, 1.0);
        }
    }

    public void ApplySplatter(double obstructionDelta)
    {
        if (!double.IsFinite(obstructionDelta) || obstructionDelta < 0.0)
            throw new ArgumentOutOfRangeException(nameof(obstructionDelta));

        AccumulatedStopCleaningSeconds = 0.0;
        ObstructionFraction = Math.Clamp(ObstructionFraction + obstructionDelta, 0.0, 1.0);
    }

    public bool StepStoppedCleaning(double dtSeconds, double speedMetresPerSecond)
    {
        if (!double.IsFinite(dtSeconds) || dtSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(dtSeconds));
        if (!double.IsFinite(speedMetresPerSecond))
            throw new ArgumentOutOfRangeException(nameof(speedMetresPerSecond));

        if (Math.Abs(speedMetresPerSecond) >= MaxStoppedSpeedMetresPerSecond)
        {
            AccumulatedStopCleaningSeconds = 0.0;
            return false;
        }

        AccumulatedStopCleaningSeconds += dtSeconds;
        if (AccumulatedStopCleaningSeconds + 1e-9 >= RequiredStoppedCleaningSeconds)
        {
            ObstructionFraction = 0.0;
            AccumulatedStopCleaningSeconds = RequiredStoppedCleaningSeconds;
            return true;
        }

        return false;
    }
}

public static class RollingShutterMetrics
{
    public static double ComputeRollingShutterSkewPixels(
        double imagePlaneSpeedPixelsPerSecond,
        CameraSensorProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!double.IsFinite(imagePlaneSpeedPixelsPerSecond))
            throw new ArgumentOutOfRangeException(nameof(imagePlaneSpeedPixelsPerSecond));

        return Math.Abs(imagePlaneSpeedPixelsPerSecond) * profile.ReadoutTimeSeconds;
    }
}

public sealed record ConsecutiveOverlapEvaluation(
    bool HasSufficientGeometry,
    double OverlapFraction,
    bool MeetsMinimumRequirement,
    SurfaceFootprint2DEnu? FirstFootprint,
    SurfaceFootprint2DEnu? SecondFootprint,
    string? DiagnosticReasonKey = null);

/// <summary>Estimates support-surface footprints and consecutive frame overlap without treating missing geometry as zero error.</summary>
public static class SurfaceFootprintOverlapCalculator
{
    public const double DefaultMinimumOverlapFraction = 0.60;

    public static bool TryEstimateSupportFootprint(
        EstimatedBodyPose pose,
        CameraMountEstimate mount,
        CameraSensorProfile profile,
        double nominalSupportDistanceMetres,
        out SurfaceFootprint2DEnu footprint)
    {
        ArgumentNullException.ThrowIfNull(pose);
        ArgumentNullException.ThrowIfNull(mount);
        ArgumentNullException.ThrowIfNull(profile);

        if (!double.IsFinite(nominalSupportDistanceMetres) || nominalSupportDistanceMetres <= 0.0)
        {
            footprint = default;
            return false;
        }

        var camToMap = CameraGeometry.CameraToMap(pose, mount);
        var nearDepth = Math.Max(0.5, nominalSupportDistanceMetres * 0.5);
        var farDepth = nominalSupportDistanceMetres * 1.5;
        var halfWidthScale = (profile.Intrinsics.Width * 0.5) / profile.Intrinsics.Fx;

        var nearHalfW = nearDepth * halfWidthScale;
        var farHalfW = farDepth * halfWidthScale;

        Vector3d[] corners =
        [
            camToMap.Transform(new Vector3d(-nearHalfW, 0.0, nearDepth)),
            camToMap.Transform(new Vector3d(nearHalfW, 0.0, nearDepth)),
            camToMap.Transform(new Vector3d(-farHalfW, 0.0, farDepth)),
            camToMap.Transform(new Vector3d(farHalfW, 0.0, farDepth)),
        ];

        var minE = corners.Min(c => c.X);
        var maxE = corners.Max(c => c.X);
        var minN = corners.Min(c => c.Y);
        var maxN = corners.Max(c => c.Y);

        if (maxE - minE <= 1e-6)
        {
            minE -= 0.05;
            maxE += 0.05;
        }
        if (maxN - minN <= 1e-6)
        {
            minN -= 0.05;
            maxN += 0.05;
        }

        footprint = new SurfaceFootprint2DEnu(minE, minN, maxE, maxN);
        return true;
    }

    public static ConsecutiveOverlapEvaluation EvaluateConsecutiveOverlap(
        SurfaceFootprint2DEnu? first,
        SurfaceFootprint2DEnu? second,
        double requiredMinimumOverlap = DefaultMinimumOverlapFraction)
    {
        if (!double.IsFinite(requiredMinimumOverlap) || requiredMinimumOverlap <= 0.0 || requiredMinimumOverlap > 1.0)
            throw new ArgumentOutOfRangeException(nameof(requiredMinimumOverlap));

        if (first is not { } f1 || second is not { } f2 || f1.Area <= 0.0 || f2.Area <= 0.0)
        {
            return new ConsecutiveOverlapEvaluation(
                HasSufficientGeometry: false,
                OverlapFraction: 0.0,
                MeetsMinimumRequirement: false,
                FirstFootprint: first,
                SecondFootprint: second,
                DiagnosticReasonKey: "camera.overlap.insufficient_geometry");
        }

        var denom = Math.Max(f1.Area, f2.Area);
        var overlap = Math.Clamp(f1.IntersectionArea(f2) / denom, 0.0, 1.0);
        return new ConsecutiveOverlapEvaluation(
            HasSufficientGeometry: true,
            OverlapFraction: overlap,
            MeetsMinimumRequirement: overlap >= requiredMinimumOverlap,
            FirstFootprint: f1,
            SecondFootprint: f2,
            DiagnosticReasonKey: null);
    }
}

