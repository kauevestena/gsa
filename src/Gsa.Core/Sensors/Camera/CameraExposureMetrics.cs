using Gsa.Core.Geometry;

namespace Gsa.Core.Sensors.Camera;

public sealed record CameraEnvironmentSnapshot
{
    public bool IsNight { get; }
    public double SunAngleRadians { get; }
    public double LensObstructionFraction { get; }
    public double BaseClippingFraction { get; }

    public CameraEnvironmentSnapshot(
        bool isNight = false,
        double sunAngleRadians = Math.PI / 2.0,
        double lensObstructionFraction = 0.0,
        double baseClippingFraction = 0.02)
    {
        if (!double.IsFinite(sunAngleRadians) || sunAngleRadians < 0 || sunAngleRadians > Math.PI)
            throw new ArgumentOutOfRangeException(nameof(sunAngleRadians), "Sun angle must be within [0, pi].");
        if (!double.IsFinite(lensObstructionFraction) || lensObstructionFraction < 0 || lensObstructionFraction > 1)
            throw new ArgumentOutOfRangeException(nameof(lensObstructionFraction), "Lens obstruction must be in [0, 1].");
        if (!double.IsFinite(baseClippingFraction) || baseClippingFraction < 0 || baseClippingFraction > 1)
            throw new ArgumentOutOfRangeException(nameof(baseClippingFraction), "Base clipping fraction must be in [0, 1].");
        IsNight = isNight;
        SunAngleRadians = sunAngleRadians;
        LensObstructionFraction = lensObstructionFraction;
        BaseClippingFraction = baseClippingFraction;
    }

    public static CameraEnvironmentSnapshot DryDay { get; } = new(isNight: false, sunAngleRadians: Math.PI / 3.0);
    public static CameraEnvironmentSnapshot Night { get; } = new(isNight: true, sunAngleRadians: Math.PI);
}

public sealed record CameraQualityMetrics(
    double ExposureSeconds,
    double MotionBlurPixels,
    double ClippingFraction,
    double LensObstructionFraction,
    double NoiseSigmaPixels,
    bool MotionBlurExceeded,
    bool ClippingExceeded,
    bool LensObstructionExceeded,
    bool InsufficientVisibleFeatures,
    bool Passed);

/// <summary>Computes analytical and two-instant motion blur, glare clipping, night noise, and quality flags.</summary>
public static class CameraExposureMetrics
{
    public const double GlareThresholdRadians = 10.0 * Math.PI / 180.0;

    public static double ComputeAnalyticalBlurPixels(
        double imagePlaneSpeedMetresPerSecond,
        double exposureSeconds,
        double targetDistanceMetres,
        double focalLengthPixels)
    {
        if (!double.IsFinite(imagePlaneSpeedMetresPerSecond) || imagePlaneSpeedMetresPerSecond < 0)
            throw new ArgumentOutOfRangeException(nameof(imagePlaneSpeedMetresPerSecond), "Speed must be finite and non-negative.");
        if (!double.IsFinite(exposureSeconds) || exposureSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(exposureSeconds), "Exposure must be finite and non-negative.");
        if (!double.IsFinite(targetDistanceMetres) || targetDistanceMetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetDistanceMetres), "Distance must be finite and positive.");
        if (!double.IsFinite(focalLengthPixels) || focalLengthPixels <= 0)
            throw new ArgumentOutOfRangeException(nameof(focalLengthPixels), "Focal length must be finite and positive.");
        var gsd = targetDistanceMetres / focalLengthPixels;
        return imagePlaneSpeedMetresPerSecond * exposureSeconds / gsd;
    }

    public static double ComputeTwoInstantBlurPixels(
        CameraProjector projector,
        Vector3d targetMapPoint,
        RigidTransform cameraToMapStart,
        RigidTransform cameraToMapExposureEnd)
    {
        ArgumentNullException.ThrowIfNull(projector);
        var pStart = cameraToMapStart.Inverse().Transform(targetMapPoint);
        var pEnd = cameraToMapExposureEnd.Inverse().Transform(targetMapPoint);
        var projStart = projector.ProjectCameraPoint(pStart);
        var projEnd = projector.ProjectCameraPoint(pEnd);
        if (!projStart.IsVisible || !projEnd.IsVisible ||
            projStart.Pixel is not { } first || projEnd.Pixel is not { } second)
            return 0.0;
        var du = second.U - first.U;
        var dv = second.V - first.V;
        return Math.Sqrt(du * du + dv * dv);
    }

    public static CameraQualityMetrics EvaluateFrameMetrics(
        CameraSensorProfile profile,
        CameraEnvironmentSnapshot environment,
        RigidTransform trueCameraToMapStart,
        RigidTransform trueCameraToMapExposureEnd,
        IEnumerable<Vector3d> referencePointsEnu,
        int visibleFeatureCount)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(referencePointsEnu);

        var exposureSeconds = environment.IsNight ? profile.NightExposureSeconds : profile.DayExposureSeconds;
        var projector = new CameraProjector(profile.Intrinsics);

        var maxBlur = 0.0;
        var evaluatedAny = false;
        foreach (var point in referencePointsEnu)
        {
            var blur = ComputeTwoInstantBlurPixels(projector, point, trueCameraToMapStart, trueCameraToMapExposureEnd);
            if (blur > maxBlur)
                maxBlur = blur;
            evaluatedAny = true;
        }
        if (!evaluatedAny)
        {
            var nominalPoint = trueCameraToMapStart.Transform(new Vector3d(0, 0, 10.0));
            maxBlur = ComputeTwoInstantBlurPixels(projector, nominalPoint, trueCameraToMapStart, trueCameraToMapExposureEnd);
        }

        var glarePenalty = environment.SunAngleRadians < GlareThresholdRadians
            ? 0.45 * (1.0 - environment.SunAngleRadians / GlareThresholdRadians)
            : 0.0;
        var clippingFraction = Math.Clamp(environment.BaseClippingFraction + glarePenalty, 0.0, 1.0);
        var noiseSigma = environment.IsNight ? 1.5 : 0.5;

        var blurExceeded = maxBlur > profile.MaxBlurPixels;
        var clippingExceeded = clippingFraction > profile.MaxClippingFraction;
        var lensExceeded = environment.LensObstructionFraction > profile.MaxLensObstructionFraction;
        var insufficientFeatures = visibleFeatureCount <= 0;
        var passed = !blurExceeded && !clippingExceeded && !lensExceeded && !insufficientFeatures;

        return new CameraQualityMetrics(
            ExposureSeconds: exposureSeconds,
            MotionBlurPixels: maxBlur,
            ClippingFraction: clippingFraction,
            LensObstructionFraction: environment.LensObstructionFraction,
            NoiseSigmaPixels: noiseSigma,
            MotionBlurExceeded: blurExceeded,
            ClippingExceeded: clippingExceeded,
            LensObstructionExceeded: lensExceeded,
            InsufficientVisibleFeatures: insufficientFeatures,
            Passed: passed);
    }
}

