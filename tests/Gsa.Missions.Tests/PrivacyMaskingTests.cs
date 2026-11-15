using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;
using Gsa.Missions.Contracts;
using Gsa.Missions.Office;
using Gsa.Missions.Quality;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class PrivacyMaskingTests
{
    private static ContractDefinition LoadContract(string fixture) =>
        ContractDefinition.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)), fixture);

    private static CameraObservationSample MakeSampleWithSensitiveTags()
    {
        var pose = new EstimatedBodyPose(10.0, new Vector3d(500000, 7400000, 101.5), Quaterniond.Identity);
        var quality = new CameraQualityMetrics(0.002, 0.2, 0.005, 0.0, 0.5, false, false, false, false, true);
        var intrinsics = CameraIntrinsics.Initial;

        var features = new[]
        {
            new ObservedFeature2D("face_pedestrian_01", "person_face", 640, 360, 600, 320, 680, 400, 80, 9, 9, 0.0, RequiresPrivacyReview: true),
            new ObservedFeature2D("plate_car_01", "license_plate", 320, 540, 280, 520, 360, 560, 80, 9, 9, 0.0, RequiresPrivacyReview: true),
        };

        var profile = new CameraSensorProfile(
            "cam_front", 1.0 / 3.0, 60.0, intrinsics, 0.002, 0.01, 0.005);

        var thumb = CameraThumbnail.CreateSynthetic(profile, features, CameraEnvironmentSnapshot.DryDay);

        return new CameraObservationSample(
            "s_priv_01", "cam_front", 0, 10.0, pose,
            CameraMountEstimate.ForwardFacing, intrinsics,
            quality, features, [], thumb);
    }

    [Fact]
    public void TechnicalUnmaskedRegionFailsCqAndBlocksEvenPartialUntilAppliedToThumbnail()
    {
        var contract = LoadContract("inventory.json");
        var surface = new SurveySurfaceReference("commerce_street_facades", new Bounds2DEnu(500000, 7400000, 500002, 7400002));
        var cps = new[]
        {
            new ControlCheckpointReference("cp1", new Vector3d(0, 0, 0)),
            new ControlCheckpointReference("cp2", new Vector3d(10, 0, 0)),
            new ControlCheckpointReference("cp3", new Vector3d(0, 10, 0)),
        };
        var reference = new EvaluationReference(contract.Id, [surface], contract.RequiredFeatureIds, cps);
        var blockA = contract.RequiredFeatureIds.Take(10)
            .Select((id, i) => new TriangulatedFeatureEntry(id, "facade", TriangulationStatus.Resolved, FeatureReconstructionMethod.TwoRayTriangulation, GeometryFailureReason.None, new Vector3d(i, 0, 0), 10.0, null, 2, true, ["s_priv_01", "s2"]))
            .ToArray();
        var cpEst = cps.Select(c => new SubmittedCheckpointEstimate(c.CheckpointId, c.TruePositionEnu)).ToArray();

        var rawSample = MakeSampleWithSensitiveTags();
        var originalSha = rawSample.Thumbnail.Sha256;
        var detected = PrivacyMaskingService.DetectSensitiveRegions(rawSample);
        Assert.Equal(2, detected.Count);

        // Operator confirms ONLY face, forgets license plate -> non-compliant & partial quote must be blocked!
        var partialConfirm = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["s_priv_01"] = ["face_pedestrian_01"],
        };
        var partialMasked = PrivacyMaskingService.ApplyMasksToSamples([rawSample], ProcessingMode.Technical, partialConfirm, out var incompleteManifest);
        Assert.False(incompleteManifest.IsCompliant);
        Assert.Equal(["s_priv_01:plate_car_01"], incompleteManifest.PendingUnmaskedTargetIds);

        var badReport = QualityEvaluator.Evaluate(contract, reference, new QualitySubmission(
            contract.Id, ["b1"], contract.DeliveryFormat, 1, false, partialMasked, [], blockA, cpEst));
        var blockedQuote = DeliveryQuoteCalculator.Calculate(contract, badReport, 0.0, 100.0, DeliveryMode.Partial);
        Assert.False(blockedQuote.Eligible);
        Assert.Equal("quote.rejected.compliance", blockedQuote.ReasonKey);

        // Confirm ALL regions (or use Arcade) -> thumbnail bytes mutated deterministically to (32, 32, 32), raw sample untouched, partial now allowed
        var arcadeMasked = PrivacyMaskingService.ApplyMasksToSamples([rawSample], ProcessingMode.Arcade, null, out var cleanManifest);
        Assert.True(cleanManifest.IsCompliant);
        Assert.Equal(2, cleanManifest.AppliedMaskCount);
        Assert.Equal(originalSha, rawSample.Thumbnail.Sha256); // original unmutated
        Assert.NotEqual(originalSha, cleanManifest.AfterThumbnailSha256BySampleId["s_priv_01"]);

        // Check actual redacted pixels inside face bounding box [600..680, 320..400] scaled to 256x144 (center pixel at x=128, y=72 for 1280x720)
        var redactedBytes = arcadeMasked[0].Thumbnail.CopyRgbBytes();
        var centerIdx = ((CameraThumbnail.ExpectedHeight / 2) * CameraThumbnail.ExpectedWidth + (CameraThumbnail.ExpectedWidth / 2)) * 3;
        Assert.Equal((byte)32, redactedBytes[centerIdx]);
        Assert.Equal((byte)32, redactedBytes[centerIdx + 1]);
        Assert.Equal((byte)32, redactedBytes[centerIdx + 2]);

        var okReport = QualityEvaluator.Evaluate(contract, reference, new QualitySubmission(
            contract.Id, ["b1"], contract.DeliveryFormat, 1, false, arcadeMasked, [], blockA, cpEst));
        var allowedPartial = DeliveryQuoteCalculator.Calculate(contract, okReport, 0.0, 100.0, DeliveryMode.Partial);
        Assert.True(allowedPartial.Eligible);
        Assert.True(allowedPartial.IsPartial);
    }
}
