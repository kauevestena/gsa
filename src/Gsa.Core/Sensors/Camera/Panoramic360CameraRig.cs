using Gsa.Core.Geometry;
using Gsa.Core.Mobility;

namespace Gsa.Core.Sensors.Camera;

public enum PanoramicFace
{
    Front = 0,
    Right = 1,
    Back = 2,
    Left = 3,
    Up = 4,
    Down = 5,
}

public sealed record PanoramicFaceObservation(
    PanoramicFace Face,
    string SampleId,
    int ResolutionPx,
    IReadOnlyList<string> VisibleFeatureIds,
    IReadOnlyList<string> OccludedFeatureIds,
    IReadOnlyList<string> SeamSharedFeatureIds,
    bool QualityPassed);

public sealed record PanoramicSweepCapture(
    string SweepId,
    double TimestampSeconds,
    EstimatedBodyPose HeadPose,
    int FaceResolutionPx,
    IReadOnlyList<PanoramicFaceObservation> Faces,
    IReadOnlyList<string> DeduplicatedVisibleFeatureIds,
    double EffectiveCoverageFraction,
    bool FalseSeamCoveragePrevented,
    bool FalseOcclusionCoveragePrevented);

/// <summary>Six-face panoramic 360° camera rig with joint mount, seam deduplication, and occlusion filtering.</summary>
public static class Panoramic360RigSimulator
{
    public const int Level3FaceResolutionPx = 512;
    public const int Level6FaceResolutionPx = 1024;
    public const double NominalPanoramicRateHz = 2.0;

    public static CameraMountEstimate GetJointRoofMount(LocomotionMode mode)
    {
        var height = mode switch
        {
            LocomotionMode.Van => 2.55,
            LocomotionMode.Kombi => 2.25,
            LocomotionMode.Pickup => 2.10,
            _ => 1.75,
        };
        return new CameraMountEstimate(new Vector3d(0.0, 0.0, height), Quaterniond.Identity);
    }

    public static CameraSensorProfile GetProfileForPlatform(LocomotionMode mode, PanoramicFace face)
    {
        var resPx = mode == LocomotionMode.Van ? Level6FaceResolutionPx : Level3FaceResolutionPx;
        var intrinsics = CameraIntrinsics.FromHorizontalFieldOfView(resPx, resPx, Math.PI / 2.0);
        return new CameraSensorProfile(
            id: $"camera-360-{face.ToString().ToLowerInvariant()}",
            rateHz: NominalPanoramicRateHz,
            rangeMetres: mode == LocomotionMode.Van ? 140.0 : 120.0,
            intrinsics: intrinsics,
            dayExposureSeconds: 1.0 / 500.0,
            nightExposureSeconds: 1.0 / 60.0,
            readoutTimeSeconds: 0.010,
            thumbnailWidth: 256,
            thumbnailHeight: 144);
    }

    public static CameraMountEstimate GetFaceMount(LocomotionMode mode, PanoramicFace face)
    {
        var baseMount = GetJointRoofMount(mode);
        var rel = face switch
        {
            PanoramicFace.Front => Quaterniond.Identity,
            PanoramicFace.Right => Quaterniond.FromAxisAngle(Vector3d.UnitZ, -Math.PI / 2.0),
            PanoramicFace.Back => Quaterniond.FromAxisAngle(Vector3d.UnitZ, Math.PI),
            PanoramicFace.Left => Quaterniond.FromAxisAngle(Vector3d.UnitZ, Math.PI / 2.0),
            PanoramicFace.Up => Quaterniond.FromAxisAngle(Vector3d.UnitY, -Math.PI / 2.0),
            PanoramicFace.Down => Quaterniond.FromAxisAngle(Vector3d.UnitY, Math.PI / 2.0),
            _ => throw new ArgumentOutOfRangeException(nameof(face), face, "Unknown panoramic face."),
        };
        return new CameraMountEstimate(baseMount.LeverArmBody, rel * CameraMountEstimate.ForwardFacing.CameraToBody);
    }

    public static PanoramicSweepCapture CaptureSixFaceSweep(
        string sweepId,
        double timestampSeconds,
        EstimatedBodyPose headPose,
        LocomotionMode mode,
        IReadOnlyDictionary<PanoramicFace, IReadOnlyList<string>> visibleFeaturesByFace,
        IReadOnlyDictionary<PanoramicFace, IReadOnlyList<string>>? occludedFeaturesByFace = null,
        int totalRequiredTargetCount = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sweepId);
        ArgumentNullException.ThrowIfNull(headPose);
        ArgumentNullException.ThrowIfNull(visibleFeaturesByFace);
        if (totalRequiredTargetCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalRequiredTargetCount));

        var resPx = mode == LocomotionMode.Van ? Level6FaceResolutionPx : Level3FaceResolutionPx;
        var countsByFeature = new Dictionary<string, int>(StringComparer.Ordinal);
        var occludedSet = new HashSet<string>(StringComparer.Ordinal);
        var faceObs = new List<PanoramicFaceObservation>(6);

        foreach (PanoramicFace face in Enum.GetValues<PanoramicFace>())
        {
            var rawVisible = visibleFeaturesByFace.TryGetValue(face, out var v) ? v : [];
            var occluded = occludedFeaturesByFace is not null && occludedFeaturesByFace.TryGetValue(face, out var o) ? o : [];
            foreach (var occId in occluded)
                occludedSet.Add(occId);

            var cleanVisible = rawVisible.Where(id => !occludedSet.Contains(id)).Distinct(StringComparer.Ordinal).ToArray();
            foreach (var fid in cleanVisible)
                countsByFeature[fid] = countsByFeature.TryGetValue(fid, out var cur) ? cur + 1 : 1;
        }

        var deduplicatedVisible = countsByFeature.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var seamSharedIds = countsByFeature.Where(kv => kv.Value > 1).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);

        foreach (PanoramicFace face in Enum.GetValues<PanoramicFace>())
        {
            var rawVisible = visibleFeaturesByFace.TryGetValue(face, out var v) ? v : [];
            var occluded = occludedFeaturesByFace is not null && occludedFeaturesByFace.TryGetValue(face, out var o) ? o : [];
            var cleanVisible = rawVisible.Where(id => !occluded.Contains(id, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToArray();
            var sharedOnFace = cleanVisible.Where(seamSharedIds.Contains).ToArray();

            faceObs.Add(new PanoramicFaceObservation(
                Face: face,
                SampleId: $"{sweepId}_{face.ToString().ToLowerInvariant()}",
                ResolutionPx: resPx,
                VisibleFeatureIds: Array.AsReadOnly(cleanVisible),
                OccludedFeatureIds: Array.AsReadOnly(occluded.ToArray()),
                SeamSharedFeatureIds: Array.AsReadOnly(sharedOnFace),
                QualityPassed: cleanVisible.Length > 0));
        }

        // Coverage counts unique non-occluded targets divided by required targets, preventing seam double-counting or occluded inflation.
        var effectiveCoverage = Math.Clamp((double)deduplicatedVisible.Length / totalRequiredTargetCount, 0.0, 1.0);
        return new PanoramicSweepCapture(
            SweepId: sweepId,
            TimestampSeconds: timestampSeconds,
            HeadPose: headPose,
            FaceResolutionPx: resPx,
            Faces: Array.AsReadOnly(faceObs.ToArray()),
            DeduplicatedVisibleFeatureIds: Array.AsReadOnly(deduplicatedVisible),
            EffectiveCoverageFraction: effectiveCoverage,
            FalseSeamCoveragePrevented: seamSharedIds.Count > 0,
            FalseOcclusionCoveragePrevented: occludedSet.Count > 0);
    }
}

