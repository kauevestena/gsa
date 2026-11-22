using Gsa.Core.Geometry;
using Gsa.Core.World;
using Gsa.Missions.Contracts;

namespace Gsa.Missions.Quality;

public sealed record BoundaryMonumentResult(string MonumentId, bool HasStationA, bool HasStationB, Vector3d? PositionEnu,
    double? RepeatErrorMetres, double? HorizontalDisplacementMetres)
{
    public bool Resolved => RepeatErrorMetres is <= SerraBoundarySurvey.RepeatLimit;
    public bool Displaced => Resolved && HorizontalDisplacementMetres is >= SerraBoundarySurvey.DetectionLimit;
}

public sealed record SerraBoundaryReview(IReadOnlyList<BoundaryMonumentResult> Monuments, QualityReport Delivery);

public static class SerraBoundaryQuality
{
    private const string Incomplete = "city.boundary.recollect";
    private const string Unconfirmed = "city.boundary.unconfirmed";

    public static IReadOnlyList<ContractRequirementSpec> Requirements { get; } = Array.AsReadOnly<ContractRequirementSpec>(
    [
        new PredicateRequirementSpec("all_required_features", true),
        new ThresholdRequirementSpec("boundary_repeat_error", "m", "max", SerraBoundarySurvey.RepeatLimit, .02),
        new PredicateRequirementSpec("boundary_displacements_confirmed", true),
        new PredicateRequirementSpec("format_valid", true),
    ]);

    public static SerraBoundaryReview Evaluate(string contractId, IReadOnlyList<string> batchIds, IReadOnlyList<BoundarySurveyShot> shots)
    {
        var latest = shots.GroupBy(shot => (shot.MonumentId, shot.StationIndex))
            .ToDictionary(group => group.Key, group => group.MaxBy(shot => shot.PhysicsTick)!);
        var monuments = SerraBoundarySurvey.Sites.Select(site =>
        {
            var hasA = latest.TryGetValue((site.Id, 0), out var shotA);
            var hasB = latest.TryGetValue((site.Id, 1), out var shotB);
            if (!hasA || !hasB) return new BoundaryMonumentResult(site.Id, hasA, hasB, null, null, null);
            var a = shotA!.RecomputePoint(site);
            var b = shotB!.RecomputePoint(site);
            var position = (a + b) / 2;
            var delta = position - site.PublishedPositionEnu;
            return new BoundaryMonumentResult(site.Id, true, true, position, (a - b).Length,
                Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y));
        }).ToArray();

        var resolved = monuments.Count(mark => mark.Resolved);
        var displaced = monuments.Count(mark => mark.Displaced);
        var pairs = monuments.Where(mark => mark.RepeatErrorMetres.HasValue).Select(mark => mark.RepeatErrorMetres!.Value).ToArray();
        var repeatPasses = pairs.Length == monuments.Length && resolved == monuments.Length;
        var formatValid = batchIds.Count > 0 && shots.Count > 0;
        MetricEvaluationResult[] metrics =
        [
            Predicate("all_required_features", pairs.Length == monuments.Length, pairs.Length, Incomplete),
            new("boundary_repeat_error", "accuracy", repeatPasses ? MetricEvaluationStatus.Passed :
                pairs.Length < monuments.Length ? MetricEvaluationStatus.InsufficientData : MetricEvaluationStatus.Failed,
                pairs.Length == 0 ? null : pairs.Max(), null, SerraBoundarySurvey.RepeatLimit, .02, false, pairs.Length, repeatPasses ? null : Incomplete),
            Predicate("boundary_displacements_confirmed", displaced == monuments.Length, displaced, Unconfirmed),
            Predicate("format_valid", formatValid, batchIds.Count, "cq.failure.format_valid"),
        ];
        var gaps = monuments.Zip(SerraBoundarySurvey.Sites).Where(pair => !pair.First.Displaced).Select(pair =>
            new RecollectionArea(pair.Second.Id, pair.First.Resolved ? Unconfirmed : Incomplete, pair.Second.PublishedPositionEnu,
                new(8, 12, 3), [pair.Second.Id])).ToArray();
        var failures = metrics.Select(metric => metric.FailureReasonKey).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        var report = new QualityReport(contractId, batchIds, failures.Length == 0, metrics, failures, gaps, [],
            shots.Count, shots.Count, resolved, displaced);
        return new(monuments, report);
    }

    private static MetricEvaluationResult Predicate(string metric, bool passed, int count, string failure) =>
        new(metric, "predicate", passed ? MetricEvaluationStatus.Passed : MetricEvaluationStatus.Failed,
            null, passed, null, null, true, count, passed ? null : failure);
}
