namespace Gsa.Core.Sensors.Camera;

/// <summary>Camera sensor configuration aligned with content/profiles/camera-initial.json and parameters baseline.</summary>
public sealed class CameraSensorProfile
{
    public string Id { get; }
    public double RateHz { get; }
    public double RangeMetres { get; }
    public CameraIntrinsics Intrinsics { get; }
    public double DayExposureSeconds { get; }
    public double NightExposureSeconds { get; }
    public double ReadoutTimeSeconds { get; }
    public int ThumbnailWidth { get; }
    public int ThumbnailHeight { get; }
    public double MaxBlurPixels { get; }
    public double MaxClippingFraction { get; }
    public double MaxLensObstructionFraction { get; }
    public double MinProjectedFeaturePixels { get; }
    public int StorageUnitsPerSample { get; }

    public CameraSensorProfile(
        string id,
        double rateHz,
        double rangeMetres,
        CameraIntrinsics intrinsics,
        double dayExposureSeconds,
        double nightExposureSeconds,
        double readoutTimeSeconds,
        int thumbnailWidth = 256,
        int thumbnailHeight = 144,
        double maxBlurPixels = 3.0,
        double maxClippingFraction = 0.20,
        double maxLensObstructionFraction = 0.20,
        double minProjectedFeaturePixels = 16.0,
        int storageUnitsPerSample = 1)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Profile ID is required.", nameof(id));
        if (!double.IsFinite(rateHz) || rateHz <= 0)
            throw new ArgumentOutOfRangeException(nameof(rateHz), "Rate must be finite and positive.");
        if (!double.IsFinite(rangeMetres) || rangeMetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(rangeMetres), "Range must be finite and positive.");
        ArgumentNullException.ThrowIfNull(intrinsics);
        if (!double.IsFinite(dayExposureSeconds) || dayExposureSeconds <= 0 ||
            !double.IsFinite(nightExposureSeconds) || nightExposureSeconds <= 0 ||
            !double.IsFinite(readoutTimeSeconds) || readoutTimeSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(dayExposureSeconds), "Exposure and readout times must be finite.");
        if (thumbnailWidth <= 0 || thumbnailHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(thumbnailWidth), "Thumbnail dimensions must be positive.");
        if (!double.IsFinite(maxBlurPixels) || maxBlurPixels <= 0 ||
            !double.IsFinite(maxClippingFraction) || maxClippingFraction is <= 0 or > 1 ||
            !double.IsFinite(maxLensObstructionFraction) || maxLensObstructionFraction is <= 0 or > 1 ||
            !double.IsFinite(minProjectedFeaturePixels) || minProjectedFeaturePixels <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBlurPixels), "Quality limits must be finite and in range.");
        if (storageUnitsPerSample <= 0)
            throw new ArgumentOutOfRangeException(nameof(storageUnitsPerSample), "Storage cost must be positive.");

        Id = id;
        RateHz = rateHz;
        RangeMetres = rangeMetres;
        Intrinsics = intrinsics;
        DayExposureSeconds = dayExposureSeconds;
        NightExposureSeconds = nightExposureSeconds;
        ReadoutTimeSeconds = readoutTimeSeconds;
        ThumbnailWidth = thumbnailWidth;
        ThumbnailHeight = thumbnailHeight;
        MaxBlurPixels = maxBlurPixels;
        MaxClippingFraction = maxClippingFraction;
        MaxLensObstructionFraction = maxLensObstructionFraction;
        MinProjectedFeaturePixels = minProjectedFeaturePixels;
        StorageUnitsPerSample = storageUnitsPerSample;
    }

    public static CameraSensorProfile Initial { get; } = new(
        id: "camera-initial",
        rateHz: 2.0,
        rangeMetres: 100.0,
        intrinsics: CameraIntrinsics.Initial,
        dayExposureSeconds: 1.0 / 500.0,
        nightExposureSeconds: 1.0 / 60.0,
        readoutTimeSeconds: 0.010);
}

