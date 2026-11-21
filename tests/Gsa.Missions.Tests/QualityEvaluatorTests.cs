using System.Reflection;
using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Gnss;
using Gsa.Missions.Contracts;
using Xunit;

namespace Gsa.Missions.Quality;

public sealed class QualityEvaluatorTests
{
    private static ContractDefinition LoadContract(string fixture) =>
        ContractDefinition.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)), fixture);

    private static CameraObservationSample MakeSample(
        string sampleId,
        double timestamp,
        bool qualityPassed = true,
        bool privacyUnredacted = false)
    {
        var pose = new EstimatedBodyPose(timestamp, new Vector3d(500000, 7400000, 101.5), Quaterniond.Identity);
        var quality = new CameraQualityMetrics(0.002, qualityPassed ? 0.5 : 8.0, 0.01, 0.0, 0.5, !qualityPassed, false, false, false, qualityPassed);
        var masks = privacyUnredacted
            ? new[] { new PrivacyMaskRegion("plate_01", 10, 10, 40, 20, Applied: false) }
            : new[] { new PrivacyMaskRegion("plate_01", 10, 10, 40, 20, Applied: true) };
        var thumb = CameraThumbnail.FromRgbBytes(256, 144, new byte[256 * 144 * 3]);
        return new CameraObservationSample(
            sampleId, "cam_front", 0, timestamp, pose,
            CameraMountEstimate.ForwardFacing, CameraIntrinsics.Initial,
            quality, [], masks, thumb);
    }

    private static TriangulatedFeatureEntry ResolvedFeature(string targetId, Vector3d pos) =>
        new(targetId, "facade", TriangulationStatus.Resolved, FeatureReconstructionMethod.TwoRayTriangulation,
            GeometryFailureReason.None, pos, 12.0, null, 2, Confirmed: true, ["s1", "s2"]);

    [Fact]
    public void PartialBoundaryCellClippingWeightsCoverageByUsefulAreaAndDeduplicatesSamples()
    {
        // Surface [0..3] x [0..2]: 2x2 m grid -> cell (0,0)=[0..2,0..2] (area 4 m²), cell (1,0)=[2..3,0..2] (clipped area 2 m²).
        // Total useful area = 6 m². Covering only cell (0,0) must yield 4/6 = 2/3, not 1/2!
        var contract = LoadContract("facades.json");
        var surface = new SurveySurfaceReference("commerce_facade_planes", new Bounds2DEnu(500000, 7400000, 500003, 7400002));
        var reference = new EvaluationReference(contract.Id, [surface], contract.RequiredFeatureIds);

        var samples = new[]
        {
            MakeSample("s1", 1.0, qualityPassed: true),
            MakeSample("s1", 1.0, qualityPassed: true), // duplicate sample_id
            MakeSample("s_bad", 2.0, qualityPassed: false), // invalid quality must not contribute coverage
        };
        var footprints = new[]
        {
            new SampleFootprintObservation("s1", "commerce_facade_planes", new Bounds2DEnu(500000.1, 7400000.1, 500001.9, 7400001.9)),
            new SampleFootprintObservation("s1", "commerce_facade_planes", new Bounds2DEnu(500000.1, 7400000.1, 500001.9, 7400001.9)),
            new SampleFootprintObservation("s_bad", "commerce_facade_planes", new Bounds2DEnu(500002.1, 7400000.1, 500002.9, 7400001.9)),
        };
        var resolved = contract.RequiredFeatureIds.Select((id, idx) => ResolvedFeature(id, new Vector3d(500000 + idx, 7400000, 100))).ToArray();

        var report = QualityEvaluator.Evaluate(contract, reference, new QualitySubmission(
            contract.Id, ["b1"], "gsa_bundle_v1", 1, false, samples, footprints, resolved, []));

        Assert.Equal(2, report.UniqueSampleCount);
        Assert.Equal(1, report.ValidSampleCount);
        Assert.Equal(4.0 / 6.0, report.GetMetric("area_coverage")!.Value!.Value, 9);
        Assert.Equal(0.5, report.GetMetric("valid_image_fraction")!.Value!.Value, 9);
        Assert.False(report.Passed);
        Assert.Contains("cq.failure.area_coverage", report.FailureReasonKeys);
        Assert.Contains(report.RecollectionAreas, r => r.AreaId == "commerce_facade_planes:1:0");
    }

    [Fact]
    public void ImageQualityScopeStillChecksPrivacyOnEveryFace()
    {
        var contract = LoadContract("inventory.json");
        var reference = new EvaluationReference(contract.Id, [], contract.RequiredFeatureIds);
        var submission = new QualitySubmission(contract.Id, ["b1"], "gsa_bundle_v1", 1, false,
            [MakeSample("target_face", 1), MakeSample("other_face", 1, qualityPassed: false, privacyUnredacted: true)], [], [], [])
        {
            ImageQualitySampleIds = new HashSet<string> { "target_face" },
        };

        var report = QualityEvaluator.Evaluate(contract, reference, submission);

        Assert.Equal(2, report.UniqueSampleCount);
        Assert.Equal(1.0, report.GetMetric("valid_image_fraction")!.Value);
        Assert.Contains("cq.failure.privacy_redacted", report.FailureReasonKeys);
    }

    [Fact]
    public void ExclusionZonesClipUsefulCellAreaAccuratelyAndRejectEmptyUsefulSurface()
    {
        var emptySurface = new SurveySurfaceReference(
            "empty",
            new Bounds2DEnu(0, 0, 2, 2),
            exclusionZones: [new Bounds2DEnu(0, 0, 2, 2)]);
        Assert.Throws<ArgumentException>(() => emptySurface.RasterizeUsefulCells(2.0));

        // 2x2 cell with 1x2 building exclusion leaves 1x2 = 2 m² useful area; second 2x2 cell has 4 m². Total = 6 m².
        var surface = new SurveySurfaceReference(
            "block",
            new Bounds2DEnu(0, 0, 4, 2),
            exclusionZones: [new Bounds2DEnu(0, 0, 1, 2)]);
        var cells = surface.RasterizeUsefulCells(2.0);
        Assert.Equal(2, cells.Count);
        Assert.Equal(2.0, cells[0].UsefulAreaM2, 9);
        Assert.Equal(4.0, cells[1].UsefulAreaM2, 9);
    }

    [Fact]
    public void EmptyDatasetReturnsZeroMetricsAndNeverNaNOrPasses()
    {
        var contract = LoadContract("inventory.json");
        var surface = new SurveySurfaceReference("facade_grid", new Bounds2DEnu(0, 0, 4, 4));
        var reference = new EvaluationReference(contract.Id, [surface], contract.RequiredFeatureIds);

        var report = QualityEvaluator.Evaluate(contract, reference, new QualitySubmission(
            contract.Id, [], "gsa_bundle_v1", 1, false, [], [], [], []));

        Assert.False(report.Passed);
        Assert.Equal(0.0, report.GetMetric("feature_completeness")!.Value);
        Assert.Equal(0.0, report.GetMetric("valid_image_fraction")!.Value);
        Assert.Equal(MetricEvaluationStatus.InsufficientData, report.GetMetric("absolute_rmse")!.Status);
        Assert.Contains("cq.failure.empty_submission", report.FailureReasonKeys);
    }

    [Fact]
    public void AbsoluteRmseRequiresAtLeastThreeCheckpointsAndMatchesAnalyticalValue()
    {
        var contract = LoadContract("inventory.json");
        var checkpoints = new[]
        {
            new ControlCheckpointReference("cp1", new Vector3d(10, 0, 100)),
            new ControlCheckpointReference("cp2", new Vector3d(20, 0, 100)),
            new ControlCheckpointReference("cp3", new Vector3d(30, 0, 100)),
        };
        var reference = new EvaluationReference(contract.Id, [], contract.RequiredFeatureIds, checkpoints);
        var samples = new[] { MakeSample("s1", 1.0) };
        var resolved = contract.RequiredFeatureIds.Select(id => ResolvedFeature(id, Vector3d.Zero)).ToArray();

        // Only 2 checkpoints -> insufficient_data, must fail.
        var tooFew = QualityEvaluator.Evaluate(contract, reference, new QualitySubmission(
            contract.Id, ["b1"], "gsa_bundle_v1", 1, false, samples, [], resolved,
            [
                new SubmittedCheckpointEstimate("cp1", new Vector3d(10.0, 0, 100)),
                new SubmittedCheckpointEstimate("cp2", new Vector3d(20.0, 0, 100)),
            ]));

        var fewRmse = tooFew.GetMetric("absolute_rmse")!;
        Assert.Equal(MetricEvaluationStatus.InsufficientData, fewRmse.Status);
        Assert.Null(fewRmse.Value);
        Assert.False(tooFew.Passed);
        Assert.Contains("cq.failure.insufficient_checkpoints", tooFew.FailureReasonKeys);

        // 3 checkpoints with errors (1, 0, 0), (0, 2, 0), (0, 0, 2) -> squared sum = 1+4+4=9 -> RMSE = sqrt(9/3) = sqrt(3) ~ 1.73205 <= 2.0 m.
        var passing = QualityEvaluator.Evaluate(contract, reference, new QualitySubmission(
            contract.Id, ["b1"], "gsa_bundle_v1", 1, false, samples, [], resolved,
            [
                new SubmittedCheckpointEstimate("cp1", new Vector3d(11.0, 0.0, 100.0)),
                new SubmittedCheckpointEstimate("cp2", new Vector3d(20.0, 2.0, 100.0)),
                new SubmittedCheckpointEstimate("cp3", new Vector3d(30.0, 0.0, 102.0)),
            ]));

        var okRmse = passing.GetMetric("absolute_rmse")!;
        Assert.Equal(MetricEvaluationStatus.Passed, okRmse.Status);
        Assert.Equal(Math.Sqrt(3.0), okRmse.Value!.Value, 9);
        Assert.True(passing.Passed);
    }

    [Fact]
    public void UnredactedPrivacyFailsPredicateAndDeliverablesEvaluatePartialEligibility()
    {
        var contract = LoadContract("inventory.json");
        var checkpoints = new[]
        {
            new ControlCheckpointReference("cp1", new Vector3d(10, 0, 100)),
            new ControlCheckpointReference("cp2", new Vector3d(20, 0, 100)),
            new ControlCheckpointReference("cp3", new Vector3d(30, 0, 100)),
        };
        var reference = new EvaluationReference(contract.Id, [], contract.RequiredFeatureIds, checkpoints);

        // Deliver all of block_a (facade_01..10) and none of block_b (facade_11..20), plus unredacted mask on s1.
        var blockAFeatures = contract.PartialPolicy.Deliverables[0].RequiredFeatureIds
            .Select(id => ResolvedFeature(id, Vector3d.Zero)).ToArray();

        var report = QualityEvaluator.Evaluate(contract, reference, new QualitySubmission(
            contract.Id,
            ["b1"],
            "gsa_bundle_v1",
            1,
            false,
            [MakeSample("s1", 1.0, qualityPassed: true, privacyUnredacted: true)],
            [],
            blockAFeatures,
            [
                new SubmittedCheckpointEstimate("cp1", new Vector3d(10.1, 0, 100)),
                new SubmittedCheckpointEstimate("cp2", new Vector3d(20.1, 0, 100)),
                new SubmittedCheckpointEstimate("cp3", new Vector3d(30.1, 0, 100)),
            ]));

        Assert.False(report.Passed);
        Assert.Equal(MetricEvaluationStatus.Failed, report.GetMetric("privacy_redacted")!.Status);
        Assert.Contains("cq.failure.privacy_redacted", report.FailureReasonKeys);
        // block_a only requires feature_completeness & absolute_rmse, so it qualifies as a partial deliverable!
        Assert.Equal(["block_a"], report.PassedDeliverableIds);

        using var json = JsonDocument.Parse(report.ToJson());
        Assert.Equal(1, json.RootElement.GetProperty("schema_version").GetInt32());
        Assert.False(json.RootElement.GetProperty("passed").GetBoolean());
    }

    [Fact]
    public void CoreAssemblyNeverReferencesEvaluationReference()
    {
        var coreAssembly = typeof(GnssSkyEnvironmentResolver).Assembly;
        var coreTypes = coreAssembly.GetTypes();
        Assert.DoesNotContain(coreTypes, t => t.Name.Contains("EvaluationReference", StringComparison.Ordinal));
        foreach (var type in coreTypes)
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                Assert.DoesNotContain("EvaluationReference", prop.PropertyType.Name, StringComparison.Ordinal);
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType.Name.Contains("EvaluationReference", StringComparison.Ordinal));
        }
    }
}
