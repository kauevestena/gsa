using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Missions.Contracts;
using Gsa.Missions.Persistence;
using Gsa.Missions.Quality;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class VehicleAccidentRecoveryTests
{
    private static ContractDefinition LoadContract(string fixture) =>
        ContractDefinition.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)), fixture);

    private static SurveyBatchArtifact MakeArtifact(ContractDefinition def, string batchId)
    {
        var pose = new EstimatedBodyPose(10.0, new Vector3d(500010, 7400010, 101.5), Quaterniond.Identity);
        var quality = new CameraQualityMetrics(0.002, 0.2, 0.005, 0.0, 0.5, false, false, false, false, true);
        var thumb = CameraThumbnail.FromRgbBytes(
            CameraThumbnail.ExpectedWidth,
            CameraThumbnail.ExpectedHeight,
            new byte[CameraThumbnail.ExpectedWidth * CameraThumbnail.ExpectedHeight * 3]);
        var sample = new CameraObservationSample(
            $"{batchId}_s1", "cam_scooter_front", 0, 10.0, pose,
            CameraMountEstimate.ForwardFacing, CameraIntrinsics.Initial, quality, [], [], thumb);

        return new SurveyBatchArtifact(
            1, batchId, def.Id, 5.0, 15.0, 1, 0, CaptureStopReason.None,
            ["cam_scooter_front"],
            [SerializableCameraSample.FromDomain(sample)],
            [new SampleFootprintObservation($"{batchId}_s1", "commerce_facade_planes", new Bounds2DEnu(500000, 7400000, 500002, 7400002))],
            [], []);
    }

    [Fact]
    public void ScooterDestructionDiscardsPendingBatchesPreservesProtectedCustodyAndRepositionsAtOffice()
    {
        var def = LoadContract("facades.json");
        var contract = new ContractStateMachine(def);
        var custody = new BatchCustodyStore();
        Assert.True(contract.Accept(0.0).Succeeded);

        var protectedBatch = MakeArtifact(def, "batch_protected");
        var pendingBatch = MakeArtifact(def, "batch_pending");

        Assert.True(contract.RecordPendingBatch(10.0, "batch_protected").Succeeded);
        Assert.True(custody.RegisterBufferedBatch(protectedBatch, 10.0).Succeeded);
        Assert.True(custody.OffloadAtomic("batch_protected", 15.0).Succeeded);
        Assert.True(contract.ProtectBatchAtUnloadPoint(15.0, "batch_protected").Succeeded);

        Assert.True(contract.RecordPendingBatch(25.0, "batch_pending").Succeeded);
        Assert.True(custody.RegisterBufferedBatch(pendingBatch, 25.0).Succeeded);

        var sim = new LocomotionSimulator(new Vector3d(500300.0, 7400200.0, 100.0), initialMode: LocomotionMode.Scooter);
        var controller = new MvpVehicleMobilityController(sim);
        controller.RecordImpact(30.0, impactDeltaVMetresPerSecond: 4.0, explicitDamagePoints: 100.0);
        Assert.True(controller.IsDestroyed);

        var safeOfficePos = new Vector3d(500020.0, 7400010.0, 100.0);
        var recovery = VehicleAccidentRecoveryCoordinator.RecoverFromAccident(35.0, controller, safeOfficePos, custody, contract);

        Assert.True(recovery.Succeeded);
        Assert.Equal("mobility.recovery.safe_office_reposition", recovery.SummaryKey);
        Assert.Equal(safeOfficePos, recovery.SafeRespawnPositionEnu);
        Assert.Equal(LocomotionMode.Scooter, recovery.RecoveredMode);
        Assert.Equal(1, recovery.PendingBatchesLostCount);
        Assert.Equal(["batch_protected"], recovery.PreservedProtectedBatchIds);
        Assert.False(controller.IsDestroyed);
        Assert.Equal(0.0, controller.VehicleDamagePoints, 6);
        Assert.Single(contract.ProtectedBatches);
        Assert.Equal("batch_protected", contract.ProtectedBatches.Single().BatchId);
    }
}

