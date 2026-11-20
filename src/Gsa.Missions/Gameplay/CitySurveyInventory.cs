using Gsa.Core.Sensors.Camera;
using Gsa.Core.World;
using Gsa.Missions.Contracts;
using Gsa.Missions.Quality;

namespace Gsa.Missions.Gameplay;

public sealed record SidewalkSectionMeasurement(string SegmentId, double WidthMetres, double CurbHeightMetres);

/// <summary>Office reconstruction uses only the calibrated image observations. The manifest is used later by CQ.</summary>
public static class CitySurveyInventory
{
    private static bool IsSidewalkJob(ContractDefinition definition) => definition.Id == "HIST-03";

    private static IEnumerable<string> Marks(string id) => [id + "_curb", id + "_back", id + "_foot"];

    private static IEnumerable<string> CheckpointIds(ContractDefinition definition) =>
        IsSidewalkJob(definition) ? definition.RequiredFeatureIds.SelectMany(Marks) : definition.RequiredFeatureIds;

    public static bool HasReference(ContractDefinition definition, IReadOnlyDictionary<string, WorldBoxObject> manifest) =>
        CheckpointIds(definition).All(manifest.ContainsKey);

    public static QualitySubmission Reconstruct(ContractDefinition definition, QualitySubmission submission,
        out IReadOnlyList<SidewalkSectionMeasurement> widthsAndSteps)
    {
        var sides = IsSidewalkJob(definition);
        var wanted = CheckpointIds(definition).ToHashSet(StringComparer.Ordinal);
        // Bound reconstruction to this contract's targets. Other buildings in the images are not
        // survey results, and the immutable originals remain in custody.
        var scopedSamples = submission.Samples.Select(sample =>
            new CameraObservationSample(sample.SampleId, sample.SensorId, sample.Sequence,
                sample.TimestampSeconds, sample.EstimatedPose, sample.MountEstimate, sample.Intrinsics, sample.Quality,
                sample.ObservedFeatures.Where(feature => wanted.Contains(feature.TargetId)).ToArray(),
                sample.PrivacyMasks, sample.Thumbnail));

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
        }) : null;
        return new EvaluationReference(definition.Id, [], ids, refs, pairs);
    }
}
