using Gsa.Core.Sensors.Camera;

namespace Gsa.Missions.Office;

public enum ProcessingMode
{
    Arcade = 0,
    Technical = 1,
}

public sealed record SyntheticSensitiveRegion(
    string SampleId,
    string TargetId,
    string TagKind,
    double MinU,
    double MinV,
    double MaxU,
    double MaxV,
    bool ConfirmedByOperator,
    bool AppliedToProduct);

public sealed record PrivacyComplianceManifest(
    int SchemaVersion,
    int TotalSensitiveRegions,
    int AppliedMaskCount,
    IReadOnlyList<string> PendingUnmaskedTargetIds,
    IReadOnlyDictionary<string, string> BeforeThumbnailSha256BySampleId,
    IReadOnlyDictionary<string, string> AfterThumbnailSha256BySampleId)
{
    public bool IsCompliant => PendingUnmaskedTargetIds.Count == 0;
}

/// <summary>Synthesizes, confirms, and applies irreversible thumbnail privacy redactions without mutating raw captures.</summary>
public static class PrivacyMaskingService
{
    public static IReadOnlyList<SyntheticSensitiveRegion> DetectSensitiveRegions(CameraObservationSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        var detected = new List<SyntheticSensitiveRegion>();
        var seenTargetIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var mask in sample.PrivacyMasks)
        {
            seenTargetIds.Add(mask.TargetId);
            detected.Add(new SyntheticSensitiveRegion(
                sample.SampleId,
                mask.TargetId,
                ClassifyTargetTag(mask.TargetId, null),
                mask.MinU,
                mask.MinV,
                mask.MaxU,
                mask.MaxV,
                mask.Applied,
                mask.Applied));
        }

        foreach (var feature in sample.ObservedFeatures)
        {
            if (!feature.RequiresPrivacyReview || !seenTargetIds.Add(feature.TargetId))
                continue;

            detected.Add(new SyntheticSensitiveRegion(
                sample.SampleId,
                feature.TargetId,
                ClassifyTargetTag(feature.TargetId, feature.ObjectClass),
                feature.MinU,
                feature.MinV,
                feature.MaxU,
                feature.MaxV,
                false,
                false));
        }

        return Array.AsReadOnly(detected.ToArray());
    }

    public static CameraObservationSample ApplyMasks(
        CameraObservationSample sample,
        IReadOnlySet<string>? confirmedTargetIds,
        ProcessingMode mode,
        out IReadOnlyList<SyntheticSensitiveRegion> regionStates,
        out string beforeThumbSha256,
        out string afterThumbSha256)
    {
        ArgumentNullException.ThrowIfNull(sample);

        beforeThumbSha256 = sample.Thumbnail.Sha256;
        var detected = DetectSensitiveRegions(sample);
        if (detected.Count == 0)
        {
            regionStates = [];
            afterThumbSha256 = beforeThumbSha256;
            return sample;
        }

        var rgb = sample.Thumbnail.CopyRgbBytes();
        var updatedMasks = new List<PrivacyMaskRegion>(detected.Count);
        var states = new List<SyntheticSensitiveRegion>(detected.Count);

        var thumbW = CameraThumbnail.ExpectedWidth;
        var thumbH = CameraThumbnail.ExpectedHeight;
        var imgW = Math.Max(1, sample.Intrinsics.Width);
        var imgH = Math.Max(1, sample.Intrinsics.Height);
        var scaleX = (double)thumbW / imgW;
        var scaleY = (double)thumbH / imgH;

        foreach (var region in detected)
        {
            var confirmed = mode == ProcessingMode.Arcade ||
                            region.ConfirmedByOperator ||
                            (confirmedTargetIds is not null && confirmedTargetIds.Contains(region.TargetId));
            var applied = confirmed;

            if (applied)
            {
                var x0 = Math.Clamp((int)Math.Floor(region.MinU * scaleX), 0, thumbW - 1);
                var x1 = Math.Clamp((int)Math.Ceiling(region.MaxU * scaleX), 0, thumbW - 1);
                var y0 = Math.Clamp((int)Math.Floor(region.MinV * scaleY), 0, thumbH - 1);
                var y1 = Math.Clamp((int)Math.Ceiling(region.MaxV * scaleY), 0, thumbH - 1);
                for (var y = y0; y <= y1; y++)
                {
                    for (var x = x0; x <= x1; x++)
                    {
                        var idx = (y * thumbW + x) * CameraThumbnail.ChannelsRgb;
                        rgb[idx] = 32;
                        rgb[idx + 1] = 32;
                        rgb[idx + 2] = 32;
                    }
                }
            }

            updatedMasks.Add(new PrivacyMaskRegion(
                region.TargetId,
                region.MinU,
                region.MinV,
                region.MaxU,
                region.MaxV,
                Applied: applied));

            states.Add(region with
            {
                ConfirmedByOperator = confirmed,
                AppliedToProduct = applied,
            });
        }

        var redactedThumb = CameraThumbnail.FromRgbBytes(thumbW, thumbH, rgb);
        afterThumbSha256 = redactedThumb.Sha256;
        regionStates = Array.AsReadOnly(states.ToArray());

        return new CameraObservationSample(
            sample.SampleId,
            sample.SensorId,
            sample.Sequence,
            sample.TimestampSeconds,
            sample.EstimatedPose,
            sample.MountEstimate,
            sample.Intrinsics,
            sample.Quality,
            sample.ObservedFeatures,
            updatedMasks,
            redactedThumb);
    }

    public static IReadOnlyList<CameraObservationSample> ApplyMasksToSamples(
        IReadOnlyList<CameraObservationSample> samples,
        ProcessingMode mode,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? confirmedMasksBySampleId,
        out PrivacyComplianceManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var output = new List<CameraObservationSample>(samples.Count);
        var pendingTargets = new List<string>();
        var beforeShas = new Dictionary<string, string>(StringComparer.Ordinal);
        var afterShas = new Dictionary<string, string>(StringComparer.Ordinal);
        var totalRegions = 0;
        var appliedCount = 0;

        foreach (var sample in samples)
        {
            HashSet<string>? confirmedSet = null;
            if (confirmedMasksBySampleId is not null &&
                confirmedMasksBySampleId.TryGetValue(sample.SampleId, out var confirmedIds))
            {
                confirmedSet = new HashSet<string>(confirmedIds, StringComparer.Ordinal);
            }

            var processed = ApplyMasks(sample, confirmedSet, mode, out var regionStates, out var beforeSha, out var afterSha);
            output.Add(processed);
            beforeShas[sample.SampleId] = beforeSha;
            afterShas[sample.SampleId] = afterSha;

            foreach (var region in regionStates)
            {
                totalRegions++;
                if (region.AppliedToProduct)
                    appliedCount++;
                else
                    pendingTargets.Add($"{sample.SampleId}:{region.TargetId}");
            }
        }

        manifest = new PrivacyComplianceManifest(
            1,
            totalRegions,
            appliedCount,
            Array.AsReadOnly(pendingTargets.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray()),
            beforeShas,
            afterShas);

        return Array.AsReadOnly(output.ToArray());
    }

    private static string ClassifyTargetTag(string targetId, string? objectClass)
    {
        var combined = $"{objectClass ?? string.Empty} {targetId}".ToLowerInvariant();
        if (combined.Contains("plate", StringComparison.Ordinal) || combined.Contains("placa", StringComparison.Ordinal))
            return "license_plate";
        if (combined.Contains("face", StringComparison.Ordinal) || combined.Contains("rosto", StringComparison.Ordinal) || combined.Contains("person", StringComparison.Ordinal))
            return "face";
        return "sensitive_region";
    }
}

