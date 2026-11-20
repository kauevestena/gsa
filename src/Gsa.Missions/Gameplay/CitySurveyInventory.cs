using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.World;
using Gsa.Missions.Contracts;
using Gsa.Missions.Quality;

namespace Gsa.Missions.Gameplay;

public sealed record SidewalkSectionMeasurement(string SegmentId, double WidthMetres, double CurbHeightMetres);

/// <summary>Reconstructs requested features from image rays and optional published controls.</summary>
public static class CitySurveyInventory
{
    public const string SidewalkArea = "commerce_street_sidewalks";
    public const string SignsArea = "commerce_street_signs";

    private static bool IsSidewalkJob(ContractDefinition definition) => definition.AreaId == SidewalkArea;

    private static IEnumerable<string> Marks(string id) => [id + "_curb", id + "_back", id + "_foot"];

    private static IEnumerable<string> ControlIds(string id) => [id + "_control_a", id + "_control_b", id + "_control_c"];

    public static IReadOnlyDictionary<string, Vector3d> PublishedControls(
        ContractDefinition definition, IReadOnlyDictionary<string, WorldBoxObject> manifest) =>
        IsSidewalkJob(definition)
            ? definition.RequiredFeatureIds.SelectMany(ControlIds).ToDictionary(id => id, id => manifest[id].PositionEnu, StringComparer.Ordinal)
            : new Dictionary<string, Vector3d>(StringComparer.Ordinal);

    private static IEnumerable<string> CheckpointIds(ContractDefinition definition) =>
        IsSidewalkJob(definition) ? definition.RequiredFeatureIds.SelectMany(Marks) : definition.RequiredFeatureIds;

    public static bool HasReference(ContractDefinition definition, IReadOnlyDictionary<string, WorldBoxObject> manifest) =>
        CheckpointIds(definition).All(manifest.ContainsKey) &&
        (!IsSidewalkJob(definition) || definition.RequiredFeatureIds.SelectMany(ControlIds).All(manifest.ContainsKey));

    public static QualitySubmission Reconstruct(ContractDefinition definition, QualitySubmission submission,
        IReadOnlyDictionary<string, Vector3d> controls, out IReadOnlyList<SidewalkSectionMeasurement> widthsAndSteps,
        out int adjustedPhotoCount)
    {
        var sides = IsSidewalkJob(definition);
        var wanted = CheckpointIds(definition).ToHashSet(StringComparer.Ordinal);
        // Bound reconstruction to this contract's targets. Other buildings in the images are not
        // survey results, and the immutable originals remain in custody.
        var scopedSamples = new List<CameraObservationSample>();
        foreach (var sample in submission.Samples)
        {
            if (!sample.Quality.Passed) continue;
            var observations = sample.ObservedFeatures.Where(feature => wanted.Contains(feature.TargetId)).ToArray();
            if (observations.Length == 0) continue;
            var pose = sides ? CityLocalControlAdjustment.Adjust(sample, controls) : sample.EstimatedPose;
            if (pose is null) continue;
            scopedSamples.Add(new(sample.SampleId, sample.SensorId, sample.Sequence, sample.TimestampSeconds, pose,
                sample.MountEstimate, sample.Intrinsics, sample.Quality, observations, sample.PrivacyMasks, sample.Thumbnail));
        }
        adjustedPhotoCount = sides ? scopedSamples.Count : 0;

        var estimated = FeatureInventoryTriangulator.BuildInventory(scopedSamples, FeatureConfirmationMode.Arcade)
            .ResolvedConfirmedFeatures.ToDictionary(feature => feature.TargetId, StringComparer.Ordinal);
        var features = new List<TriangulatedFeatureEntry>();
        var measurements = new List<SidewalkSectionMeasurement>();
        var checkpoints = new List<SubmittedCheckpointEstimate>();

        foreach (var id in definition.RequiredFeatureIds)
        {
            if (!sides)
            {
                if (!estimated.TryGetValue(id, out var drain)) continue;
                features.Add(drain);
                checkpoints.Add(new(id, drain.EstimatedPositionEnu!.Value));
                continue;
            }

            if (!estimated.TryGetValue(id + "_curb", out var curb) ||
                !estimated.TryGetValue(id + "_back", out var back) ||
                !estimated.TryGetValue(id + "_foot", out var foot))
                continue;

            var curbPosition = curb.EstimatedPositionEnu!.Value;
            var width = back.EstimatedPositionEnu!.Value - curbPosition;
            var height = curbPosition.Z - foot.EstimatedPositionEnu!.Value.Z;
            var sourceSamples = curb.ContributingSampleIds.Concat(back.ContributingSampleIds).Concat(foot.ContributingSampleIds)
                .Distinct(StringComparer.Ordinal).ToArray();
            features.Add(curb with { TargetId = id, ObjectClass = "sidewalk_segment", ContributingSampleIds = sourceSamples });
            measurements.Add(new(id, Math.Sqrt(width.X * width.X + width.Y * width.Y), height));
            foreach (var mark in new[] { curb, back, foot })
                checkpoints.Add(new(mark.TargetId, mark.EstimatedPositionEnu!.Value));
        }

        widthsAndSteps = measurements;
        return submission with { Features = features, Checkpoints = checkpoints };
    }

    public static EvaluationReference BuildReference(ContractDefinition definition, IReadOnlyDictionary<string, WorldBoxObject> manifest)
    {
        var ids = definition.RequiredFeatureIds;
        var sides = IsSidewalkJob(definition);
        var refs = CheckpointIds(definition).Select(id => new ControlCheckpointReference(id, manifest[id].PositionEnu));
        var pairs = sides ? ids.SelectMany(id => new[]
        {
            new RelativeCheckpointPairReference(id + "_curb", id + "_back", RelativeCheckpointMeasure.HorizontalDistance),
            new RelativeCheckpointPairReference(id + "_foot", id + "_curb", RelativeCheckpointMeasure.HeightDifference),
        }) : definition.AreaId == SignsArea
            ? ids.Zip(ids.Skip(1), (first, second) => new RelativeCheckpointPairReference(first, second))
            : null;
        return new EvaluationReference(definition.Id, [], ids, refs, pairs);
    }
}
