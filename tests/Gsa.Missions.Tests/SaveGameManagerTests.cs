using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Persistence;
using Gsa.Missions.Quality;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class SaveGameManagerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"gsa_save_tests_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static ContractDefinition LoadContract(string fixture) =>
        ContractDefinition.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)), fixture);

    private static SurveyBatchArtifact MakeArtifact(string batchId, string contractId)
    {
        var pose = new EstimatedBodyPose(12.0, new Vector3d(500000, 7400000, 101.5), Quaterniond.Identity);
        var quality = new CameraQualityMetrics(0.002, 0.2, 0.005, 0.0, 0.5, false, false, false, false, true);
        var thumb = CameraThumbnail.FromRgbBytes(CameraThumbnail.ExpectedWidth, CameraThumbnail.ExpectedHeight, new byte[CameraThumbnail.ExpectedWidth * CameraThumbnail.ExpectedHeight * 3]);
        var sample = new CameraObservationSample(
            $"{batchId}_s1", "cam_front", 0, 12.0, pose,
            CameraMountEstimate.ForwardFacing, CameraIntrinsics.Initial,
            quality, [], [new PrivacyMaskRegion("p1", 10, 10, 20, 20, true)], thumb);

        return new SurveyBatchArtifact(
            1, batchId, contractId, 10.0, 15.0, 1, 0, CaptureStopReason.None,
            ["cam_front"], [SerializableCameraSample.FromDomain(sample)], [], [], []);
    }

    [Fact]
    public void SaveAndReloadPreservesClocksDeadlinesRngLedgerCustodyWithoutPromotingPendingBatches()
    {
        var slotDir = Path.Combine(_tempDir, "slot1");
        var manager = new SaveGameManager(slotDir);

        var contractDef = LoadContract("facades.json");
        var contract = new ContractStateMachine(contractDef);
        Assert.True(contract.Accept(100.0).Succeeded);
        Assert.True(contract.RecordPendingBatch(120.0, "b_protected").Succeeded);

        var store = new BatchCustodyStore();
        Assert.True(store.RegisterBufferedBatch(MakeArtifact("b_protected", contractDef.Id), 120.0).Succeeded);
        Assert.True(store.OffloadAtomic("b_protected", 130.0).Succeeded);
        Assert.True(contract.ProtectBatchAtUnloadPoint(130.0, "b_protected").Succeeded);

        // Add vehicle-bound pending batch before saving
        Assert.True(contract.RecordPendingBatch(140.0, "b_pending").Succeeded);
        Assert.True(store.RegisterBufferedBatch(MakeArtifact("b_pending", contractDef.Id), 140.0).Succeeded);

        var ledger = new CareerLedger(300, initialDebtCoords: 40);
        Assert.True(ledger.ApplyTrafficFine("fine_1", "speeding", 110.0, 25, "fine.speeding").Succeeded);

        var progression = new CareerProgressionState();
        var equipment = new SaveEquipmentMountState(
            "handcart",
            ["cam_basic", "gnss_basic"],
            new Dictionary<string, string> { ["front"] = "cam_basic", ["mast"] = "gnss_basic" });

        var state = new SaveGameState(
            1,
            SaveTrigger.BatchOffloaded,
            0,
            new SaveClockState(140.0, 500.0),
            new SaveRngState(1337UL, 987654321UL, 42L),
            ledger.ExportState(),
            progression.ExportSnapshot(),
            equipment,
            store.ExportSnapshot(),
            [contract.ExportSnapshot()]);

        var saveResult = manager.Save(state, SaveTrigger.BatchOffloaded);
        Assert.True(saveResult.Succeeded);
        Assert.Equal(1L, saveResult.SaveGeneration);

        var loadResult = manager.LoadCurrent();
        Assert.True(loadResult.Succeeded);
        Assert.NotNull(loadResult.State);

        var runtime = SaveGameManager.RestoreRuntime(loadResult.State!);
        Assert.Equal(140.0, runtime.Clocks.SimulationTimeSeconds);
        Assert.Equal(500.0, runtime.Clocks.CareerElapsedSeconds);
        Assert.Equal(1337UL, runtime.Rng.WorldSeed);
        Assert.Equal(987654321UL, runtime.Rng.StreamState);
        Assert.Equal(42L, runtime.Rng.StepIndex);
        Assert.Equal(275, runtime.Ledger.BalanceCoords);
        Assert.Equal(40, runtime.Ledger.FineDebtCoords);
        Assert.Equal("handcart", runtime.Equipment.ActivePlatformId);
        Assert.Equal("camera-initial", runtime.Equipment.MountedSlots["front"]);

        // Contract deadline snapshot and batch custody preserved
        var restoredContract = Assert.Single(runtime.Contracts);
        Assert.Equal(100.0 + contractDef.Deadline.DueAfterSeconds, restoredContract.Snapshot!.DueAtSeconds);
        Assert.Equal(100.0 + contractDef.Deadline.ExpiresAfterSeconds, restoredContract.Snapshot!.ExpiresAtSeconds);
        Assert.Single(restoredContract.ProtectedBatches);
        Assert.Single(restoredContract.PendingFieldBatches);

        // Reload NEVER converts vehicle-bound Buffered batches into Protected
        var entries = runtime.CustodyStore.Entries.ToDictionary(e => e.BatchId);
        Assert.Equal(BatchCustodyStage.Protected, entries["b_protected"].Stage);
        Assert.Equal(BatchCustodyStage.Buffered, entries["b_pending"].Stage);
    }

    [Fact]
    public void CorruptAndFutureSavesPreserveOriginalFileUntouchedAndAllowPreviousGenerationRecovery()
    {
        var slotDir = Path.Combine(_tempDir, "slot_corrupt");
        var manager = new SaveGameManager(slotDir);

        var ledger = new CareerLedger(500);
        var stateGen1 = new SaveGameState(
            1, SaveTrigger.Manual, 0,
            new SaveClockState(10.0, 10.0),
            new SaveRngState(11UL, 22UL, 1L),
            ledger.ExportState(),
            new CareerProgressionState().ExportSnapshot(),
            new SaveEquipmentMountState("on_foot", ["cam_basic"], new Dictionary<string, string>()),
            new BatchCustodyStore().ExportSnapshot(),
            []);
        Assert.True(manager.Save(stateGen1, SaveTrigger.Manual).Succeeded);

        ledger.TryDebitPurchaseOrService("buy_cart", "shop", 20.0, 150, "equipment", "shop.cart");
        var stateGen2 = stateGen1 with
        {
            Clocks = new SaveClockState(25.0, 25.0),
            Ledger = ledger.ExportState(),
            Equipment = new SaveEquipmentMountState("handcart", ["cam_basic", "handcart"], new Dictionary<string, string>()),
        };
        Assert.True(manager.Save(stateGen2, SaveTrigger.EquipmentPurchased).Succeeded);
        Assert.True(File.Exists(manager.PreviousSavePath));

        // 1) Corrupt JSON in current.save.json
        const string badJson = "{ \"SchemaVersion\": 1, \"broken\": ";
        File.WriteAllText(manager.CurrentSavePath, badJson);

        var corruptLoad = manager.LoadCurrent();
        Assert.Equal(SaveLoadStatus.CorruptedPayload, corruptLoad.Status);
        Assert.True(corruptLoad.PreviousGenerationAvailable);
        Assert.Equal(badJson, File.ReadAllText(manager.CurrentSavePath)); // Original left untouched!

        // 2) Checksum mismatch
        var gen1Raw = File.ReadAllText(manager.PreviousSavePath);
        var parsed = JsonSerializer.Deserialize<SaveFileEnvelope>(gen1Raw)!;
        var tampered = JsonSerializer.Serialize(parsed with { PayloadSha256 = new string('0', 64) });
        File.WriteAllText(manager.CurrentSavePath, tampered);

        var checksumLoad = manager.LoadCurrent();
        Assert.Equal(SaveLoadStatus.ChecksumMismatch, checksumLoad.Status);
        Assert.True(checksumLoad.PreviousGenerationAvailable);
        Assert.Equal(tampered, File.ReadAllText(manager.CurrentSavePath));

        // 3) Future schema version
        var futureText = JsonSerializer.Serialize(new SaveFileEnvelope(99, parsed.PayloadSha256, parsed.PayloadJson));
        File.WriteAllText(manager.CurrentSavePath, futureText);

        var futureLoad = manager.LoadCurrent();
        Assert.Equal(SaveLoadStatus.UnsupportedFutureVersion, futureLoad.Status);
        Assert.True(futureLoad.PreviousGenerationAvailable);
        Assert.Equal(futureText, File.ReadAllText(manager.CurrentSavePath));

        // Previous generation recovers clean Gen 1 without modifying future current.save.json
        var recovered = manager.RecoverFromPreviousGeneration();
        Assert.True(recovered.Succeeded);
        Assert.Equal(1L, recovered.State!.SaveGeneration);
        Assert.Equal(500, recovered.State!.Ledger.BalanceCoords);
        Assert.Equal(futureText, File.ReadAllText(manager.CurrentSavePath));
    }

    [Fact]
    public void ManualSaveBlockedDuringAtomicOffloadAndConfirmedVehicleLossPersistsLostCustody()
    {
        var slotDir = Path.Combine(_tempDir, "slot_loss");
        var manager = new SaveGameManager(slotDir);

        var contractDef = LoadContract("facades.json");
        var contract = new ContractStateMachine(contractDef);
        Assert.True(contract.Accept(1.0).Succeeded);
        Assert.True(contract.RecordPendingBatch(5.0, "b_safe").Succeeded);
        Assert.True(contract.RecordPendingBatch(8.0, "b_inflight").Succeeded);

        var store = new BatchCustodyStore();
        Assert.True(store.RegisterBufferedBatch(MakeArtifact("b_safe", contractDef.Id), 5.0).Succeeded);
        Assert.True(store.OffloadAtomic("b_safe", 6.0).Succeeded);
        Assert.True(contract.ProtectBatchAtUnloadPoint(6.0, "b_safe").Succeeded);

        Assert.True(store.RegisterBufferedBatch(MakeArtifact("b_inflight", contractDef.Id), 8.0).Succeeded);
        Assert.True(store.BeginOffload("b_inflight", 9.0).Succeeded);

        var ledger = new CareerLedger(250);
        var prog = new CareerProgressionState();
        var baseState = new SaveGameState(
            1, SaveTrigger.Manual, 0,
            new SaveClockState(9.0, 9.0),
            new SaveRngState(1UL, 2UL, 3L),
            ledger.ExportState(),
            prog.ExportSnapshot(),
            new SaveEquipmentMountState("handcart", [], new Dictionary<string, string>()),
            store.ExportSnapshot(),
            [contract.ExportSnapshot()]);

        // Manual save during active Offloading is rejected
        var blockedManual = manager.Save(baseState, SaveTrigger.Manual);
        Assert.False(blockedManual.Succeeded);
        Assert.Equal(SaveOperationStatus.BlockedDuringAtomicTransition, blockedManual.Status);

        // Confirmed vehicle loss converts b_inflight to Lost/Discarded, keeps b_safe Protected, and persists atomically
        var lossSave = manager.RecordConfirmedVehicleLossAndAutosave(baseState, [contract], store, ledger, prog, 15.0);
        Assert.True(lossSave.Succeeded);

        var reloaded = manager.LoadCurrent();
        Assert.True(reloaded.Succeeded);
        Assert.Equal(SaveTrigger.ConfirmedVehicleLoss, reloaded.State!.Trigger);
        Assert.Equal(1, reloaded.State!.ConfirmedVehicleLossCount);
        Assert.Equal(15.0, reloaded.State!.LastVehicleLossAtSeconds);

        var runtime = SaveGameManager.RestoreRuntime(reloaded.State!);
        var entries = runtime.CustodyStore.Entries.ToDictionary(e => e.BatchId);
        Assert.Equal(BatchCustodyStage.Protected, entries["b_safe"].Stage);
        Assert.Equal(BatchCustodyStage.Lost, entries["b_inflight"].Stage);
        var restoredContract = Assert.Single(runtime.Contracts);
        Assert.Single(restoredContract.ProtectedBatches);
        Assert.Single(restoredContract.DiscardedBatches);
    }
}
