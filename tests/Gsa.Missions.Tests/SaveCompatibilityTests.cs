using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Traffic;
using Gsa.Missions.Activities;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Gameplay;
using Gsa.Missions.Office;
using Gsa.Missions.Persistence;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class SaveCompatibilityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "gsa-compat-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private static SaveGameState LegacyState()
    {
        var definition = ContractDefinition.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/facades.json")));
        var contract = new ContractStateMachine(definition);
        contract.Accept(100);
        var custody = new BatchCustodyStore();
        foreach (var id in new[] { "protected", "pending" })
        {
            var artifact = new SurveyBatchArtifact(1, id, definition.Id, 110, 120, 0, 0, CaptureStopReason.None, [], [], [], [], []);
            custody.RegisterBufferedBatch(artifact, 120);
            contract.RecordPendingBatch(120, id);
        }
        custody.OffloadAtomic("protected", 121);
        contract.ProtectBatchAtUnloadPoint(121, "protected");
        var ledger = new CareerLedger(300, initialDebtCoords: 40);
        ledger.ApplyTrafficFine("fine", "speed", 123, 10, "fine.speeding");
        return new(1, SaveTrigger.Manual, 7, new(130, 520), new(1337, 123456789, 31),
            ledger.ExportState(), new CareerProgressionState().ExportSnapshot(),
            new("cart", ["cam_basic", "gnss_basic", "custom_sensor"], new Dictionary<string, string> { ["front"] = "camera_initial" }),
            custody.ExportSnapshot(), [contract.ExportSnapshot()]);
    }

    private static string Envelope(JsonNode state, int version)
    {
        var payload = state.ToJsonString();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return JsonSerializer.Serialize(new SaveFileEnvelope(version, hash, payload));
    }

    private static JsonNode LegacyNode(SaveGameState state)
    {
        var node = JsonSerializer.SerializeToNode(state)!;
        node.AsObject().Remove(nameof(SaveGameState.Content));
        node.AsObject().Remove(nameof(SaveGameState.City));
        return node;
    }

    private static SaveCitySessionState MinimalCity(SerializableCameraSample sample) => new(
        new LocomotionSimulator(new(500010, 7400020, 100)).ExportSaveSnapshot(), new FleetProgressionManager().ExportSnapshot(),
        new WorldCalendarAndWeatherController(45000).ExportSnapshot(), [sample], [], [], [], new(1, [], [], []),
        new([], [], [], [], [], new Dictionary<string, string>()), new TerritoryAttentionTracker().ExportSnapshot(),
        null, ProcessingMode.Technical, true, false, false, false, 1, 1, CameraNextSequence: sample.Sequence + 1);

    private static SerializableCameraSample Photo() => SerializableCameraSample.FromDomain(new CameraObservationSample(
        "city_photo", "city_cam", 0, 130, new(130, new(500010, 7400020, 101.4), Quaterniond.Identity),
        CameraMountEstimate.ForwardFacing, CameraIntrinsics.Initial,
        new(.002, 0, 0, 0, .5, false, false, false, false, true), [], [],
        CameraThumbnail.FromRgbBytes(256, 144, new byte[256 * 144 * 3])));

    [Fact]
    public void PreviousCatalogMigratesWithoutChangingRawPhotosOrTheSaveFile()
    {
        var manager = new SaveGameManager(_directory); var photo = Photo();
        var state = LegacyState() with { SchemaVersion = 2, Content = new(2, 1, 1, ["old_preview"]), City = MinimalCity(photo) };
        var original = Envelope(JsonSerializer.SerializeToNode(state)!, 2); File.WriteAllText(manager.CurrentSavePath, original);
        var result = manager.LoadCurrent(); Assert.True(result.Succeeded, result.DiagnosticDetail);
        Assert.True(result.Compatibility!.DerivedCachesInvalidated); Assert.Equal(SaveContentVersions.Current.CatalogVersion, result.State!.Content!.CatalogVersion);
        Assert.Equal(JsonSerializer.Serialize(photo), JsonSerializer.Serialize(result.State.City!.CameraSamples.Single()));
        Assert.Null(result.State.City.Hazards); Assert.Equal(original, File.ReadAllText(manager.CurrentSavePath));
    }

    [Fact]
    public void ProtectedCameraIdsAndSequencesCannotCollideWithPendingAcquisition()
    {
        var photo = Photo(); var custody = new BatchCustodyStore();
        custody.RegisterBufferedBatch(new(1, "city_batch", "job", 130, 130, 1, 0, CaptureStopReason.None, ["city_cam"], [photo], [], [], []), 130);
        custody.OffloadAtomic("city_batch", 131);
        var state = LegacyState() with { City = MinimalCity(photo), CustodyStore = custody.ExportSnapshot() };
        Assert.Throws<ArgumentException>(() => SaveCompatibility.Normalize(state, 1));
        var valid = state with { City = state.City! with { CameraSamples = [] } };
        Assert.Single(SaveCompatibility.Normalize(valid, 1).State.CustodyStore.Entries);
        Assert.Throws<ArgumentException>(() => SaveCompatibility.Normalize(valid with { City = valid.City! with { CameraNextSequence = 0 } }, 1));
    }

    [Fact]
    public void FutureNestedHazardsBlockOverwriteWhileKeepingTheOriginalSave()
    {
        var manager = new SaveGameManager(_directory);
        var state = LegacyState() with { SchemaVersion = 2, Content = SaveContentVersions.Current,
            City = MinimalCity(Photo()) with { Hazards = new CityHazardState().Export() with { SchemaVersion = 99 } } };
        var raw = Envelope(JsonSerializer.SerializeToNode(state)!, 2); File.WriteAllText(manager.CurrentSavePath, raw);
        Assert.False(manager.LoadCurrent().Succeeded); Assert.False(manager.Save(LegacyState(), SaveTrigger.Manual).Succeeded);
        Assert.Equal(raw, File.ReadAllText(manager.CurrentSavePath));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void OlderCatalogMigrationKeepsHazardDamageAndRawPhotosWithoutInventingAcquisition(int catalogVersion)
    {
        var manager = new SaveGameManager(_directory); var hazard = new CityHazardState(); hazard.Lens.ApplySplatter(.45);
        var photo = Photo(); var state = LegacyState() with { SchemaVersion = 2, Content = new(catalogVersion, 1, 1, ["preview"]),
            City = MinimalCity(photo) with { Hazards = hazard.Export() } };
        var raw = Envelope(JsonSerializer.SerializeToNode(state)!, 2); File.WriteAllText(manager.CurrentSavePath, raw);
        var result = manager.LoadCurrent(); Assert.True(result.Succeeded, result.DiagnosticDetail);
        Assert.Equal(SaveContentVersions.CurrentCatalogVersion, result.State!.Content!.CatalogVersion);
        Assert.True(result.Compatibility!.DerivedCachesInvalidated);
        Assert.Equal(.45, result.State.City!.Hazards!.LensObstruction); Assert.Null(result.State.City.Encounters); Assert.Null(result.State.City.Gimbalina);
        Assert.Null(result.State.City.LidarBuffer);
        Assert.Null(result.State.City.ViaductHeights);
        Assert.False(result.State.City.RainSurveyFilterEnabled);
        Assert.Null(result.State.City.BoundarySurvey);
        Assert.Null(result.State.City.WorksUpload);
        Assert.Equal(JsonSerializer.Serialize(photo), JsonSerializer.Serialize(result.State.City.CameraSamples.Single()));
        Assert.Equal(raw, File.ReadAllText(manager.CurrentSavePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsupportedEncounterOrChickenSnapshotCannotBeOverwritten(bool badChicken)
    {
        var manager = new SaveGameManager(_directory); var city = MinimalCity(Photo());
        city = badChicken ? city with { Gimbalina = new GimbalinaStabilizer().ExportSaveSnapshot() with { SchemaVersion = 99 } }
            : city with { Encounters = new CityEverydayEncounters().Export() with { SchemaVersion = 99 } };
        var state = LegacyState() with { SchemaVersion = 2, Content = SaveContentVersions.Current, City = city };
        var raw = Envelope(JsonSerializer.SerializeToNode(state)!, 2); File.WriteAllText(manager.CurrentSavePath, raw);
        Assert.False(manager.LoadCurrent().Succeeded); Assert.False(manager.Save(LegacyState(), SaveTrigger.Manual).Succeeded);
        Assert.Equal(raw, File.ReadAllText(manager.CurrentSavePath));
    }

    [Fact]
    public void V1MigrationPreservesDebtDeadlinesRngAndPendingCustodyWithoutChangingOriginal()
    {
        var manager = new SaveGameManager(_directory);
        var old = LegacyState();
        var node = LegacyNode(old);
        node["UnrecognizedLegacyData"] = new JsonObject { ["keep"] = "extra content metadata" };
        var original = Envelope(node, 1);
        File.WriteAllText(manager.CurrentSavePath, original);

        var result = manager.LoadCurrent();
        Assert.True(result.Succeeded, result.DiagnosticDetail);
        Assert.True(result.Compatibility!.Migrated);
        Assert.True(result.Compatibility.DerivedCachesInvalidated);
        Assert.Equal(1, result.Compatibility.SourceSchemaVersion);
        var state = result.State!;
        Assert.Equal(2, state.SchemaVersion);
        Assert.Equal("handcart", state.Equipment.ActivePlatformId);
        Assert.Equal("camera-initial", state.Equipment.MountedSlots["front"]);
        Assert.Contains("custom_sensor", state.Equipment.OwnedEquipmentIds);
        Assert.Equal(JsonSerializer.Serialize(old.Ledger), JsonSerializer.Serialize(state.Ledger));
        Assert.Equal(JsonSerializer.Serialize(old.Contracts), JsonSerializer.Serialize(state.Contracts));
        Assert.Equal(JsonSerializer.Serialize(old.CustodyStore), JsonSerializer.Serialize(state.CustodyStore));
        Assert.Equal(old.Clocks, state.Clocks);
        Assert.Equal(old.Rng, state.Rng);
        Assert.Equal(original, File.ReadAllText(manager.CurrentSavePath));
        Assert.False(File.Exists(manager.PreviousSavePath));
        var runtime = SaveGameManager.RestoreRuntime(state);
        Assert.Equal(40, runtime.Ledger.FineDebtCoords);
        Assert.Equal(BatchCustodyStage.Buffered, runtime.CustodyStore.Entries.Single(e => e.BatchId == "pending").Stage);
        Assert.Equal(BatchCustodyStage.Protected, runtime.CustodyStore.Entries.Single(e => e.BatchId == "protected").Stage);

        Assert.True(manager.Save(state, SaveTrigger.Manual).Succeeded);
        Assert.Equal(original, File.ReadAllText(manager.PreviousSavePath));
        Assert.Equal("extra content metadata", manager.LoadCurrent().State!.ExtensionData!["UnrecognizedLegacyData"].GetProperty("keep").GetString());
        Assert.False(manager.LoadCurrent().Compatibility!.Migrated);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"SchemaVersion\":\"2\"}")]
    [InlineData("{\"SchemaVersion\":2147483648}")]
    [InlineData("{\"SchemaVersion\":99}")]
    [InlineData("{\"SchemaVersion\":0}")]
    public void UnreadableSaveCannotBeOverwrittenByManualSaveOrAutosave(string original)
    {
        var manager = new SaveGameManager(_directory);
        Assert.True(manager.Save(LegacyState(), SaveTrigger.Manual).Succeeded);
        Assert.True(manager.Save(LegacyState(), SaveTrigger.Manual).Succeeded);
        var previous = File.ReadAllBytes(manager.PreviousSavePath);
        File.WriteAllText(manager.CurrentSavePath, original);
        Assert.False(manager.LoadCurrent().Succeeded);
        foreach (var trigger in Enum.GetValues<SaveTrigger>())
        {
            var result = new SaveGameManager(_directory).Save(LegacyState(), trigger);
            Assert.Equal(SaveOperationStatus.BlockedUnreadableCurrent, result.Status);
            Assert.Equal(original, File.ReadAllText(manager.CurrentSavePath));
            Assert.Equal(previous, File.ReadAllBytes(manager.PreviousSavePath));
        }
    }

    [Fact]
    public void ExplicitRecoveryArchivesOriginalAndKeepsBackupBeforeAllowingFurtherSaves()
    {
        var manager = new SaveGameManager(_directory);
        Assert.True(manager.Save(LegacyState(), SaveTrigger.Manual).Succeeded);
        Assert.True(manager.Save(LegacyState(), SaveTrigger.Manual).Succeeded);
        var previous = File.ReadAllBytes(manager.PreviousSavePath);
        const string future = "{\"SchemaVersion\":99,\"unreadable_future_data\":\"preserve me\"}";
        File.WriteAllText(manager.CurrentSavePath, future);
        var readonlyRecovery = manager.RecoverFromPreviousGeneration();
        Assert.True(readonlyRecovery.Succeeded);
        Assert.Equal(future, File.ReadAllText(manager.CurrentSavePath));
        Assert.False(manager.Save(readonlyRecovery.State!, SaveTrigger.Manual).Succeeded);

        var recovered = manager.RestorePreviousGeneration();
        Assert.True(recovered.Succeeded);
        Assert.NotNull(recovered.PreservedOriginalPath);
        Assert.Equal(future, File.ReadAllText(recovered.PreservedOriginalPath));
        Assert.Equal(previous, File.ReadAllBytes(manager.PreviousSavePath));
        Assert.Equal(previous, File.ReadAllBytes(manager.CurrentSavePath));
        Assert.True(manager.Save(recovered.State!, SaveTrigger.Manual).Succeeded);
        Assert.Equal(future, File.ReadAllText(recovered.PreservedOriginalPath));
    }

    [Fact]
    public void FailedRecoveryLeavesCurrentUntouched()
    {
        var manager = new SaveGameManager(_directory);
        const string original = "{\"SchemaVersion\":99}";
        File.WriteAllText(manager.CurrentSavePath, original);
        Assert.False(manager.RestorePreviousGeneration().Succeeded);
        Assert.Equal(original, File.ReadAllText(manager.CurrentSavePath));
        Assert.Empty(Directory.GetFiles(_directory, "preserved-*.save.json"));
    }

    [Fact]
    public void ContentChangeInvalidatesDerivedReferencesButNeverRewritesRawBatchPayloads()
    {
        var manager = new SaveGameManager(_directory);
        var old = LegacyState();
        var canonical = SaveCompatibility.Normalize(old, 1).State;
        var state = canonical with { Content = new(1, 1, 1, ["old_octree", "old_cq"]) };
        File.WriteAllText(manager.CurrentSavePath, Envelope(JsonSerializer.SerializeToNode(state)!, 2));
        var result = manager.LoadCurrent();
        Assert.True(result.Succeeded);
        Assert.True(result.Compatibility!.DerivedCachesInvalidated);
        Assert.Empty(result.State!.Content!.DerivedCacheIds);
        Assert.Equal(JsonSerializer.Serialize(old.CustodyStore), JsonSerializer.Serialize(result.State.CustodyStore));
        Assert.True(manager.Save(canonical with { Content = SaveContentVersions.Current with { DerivedCacheIds = ["current_octree"] } }, SaveTrigger.Manual).Succeeded);
        Assert.False(manager.LoadCurrent().Compatibility!.DerivedCachesInvalidated);
        Assert.Equal("current_octree", Assert.Single(manager.LoadCurrent().State!.Content!.DerivedCacheIds));
    }

    [Fact]
    public void SupportedEnvelopeWithWrongInnerSchemaOrFutureContentIsRejected()
    {
        var manager = new SaveGameManager(_directory);
        var canonical = SaveCompatibility.Normalize(LegacyState(), 1).State;
        var mixed = Envelope(JsonSerializer.SerializeToNode(canonical)!, 1);
        File.WriteAllText(manager.CurrentSavePath, mixed);
        Assert.Equal("save.error.schema_mismatch", manager.LoadCurrent().DiagnosticReasonKey);
        Assert.Equal(mixed, File.ReadAllText(manager.CurrentSavePath));
        var future = canonical with { Content = new(99, 1, 1, []) };
        var raw = Envelope(JsonSerializer.SerializeToNode(future)!, 2);
        File.WriteAllText(manager.CurrentSavePath, raw);
        Assert.Equal("save.error.content_version", manager.LoadCurrent().DiagnosticReasonKey);
        Assert.False(manager.Save(canonical, SaveTrigger.Manual).Succeeded);
        Assert.Equal(raw, File.ReadAllText(manager.CurrentSavePath));
    }

    [Fact]
    public void ChecksumAndRequiredFieldsAreValidatedBeforeMigration()
    {
        var manager = new SaveGameManager(_directory);
        var raw = JsonSerializer.Deserialize<SaveFileEnvelope>(Envelope(LegacyNode(LegacyState()), 1))!;
        File.WriteAllText(manager.CurrentSavePath, JsonSerializer.Serialize(raw with { PayloadSha256 = new string('0', 64) }));
        Assert.Equal(SaveLoadStatus.ChecksumMismatch, manager.LoadCurrent().Status);
        var node = LegacyNode(LegacyState()); node[nameof(SaveGameState.Equipment)] = null;
        var broken = Envelope(node, 1);
        File.WriteAllText(manager.CurrentSavePath, broken);
        Assert.Equal(SaveLoadStatus.CorruptedPayload, manager.LoadCurrent().Status);
        Assert.False(manager.Save(LegacyState(), SaveTrigger.Manual).Succeeded);
        Assert.Equal(broken, File.ReadAllText(manager.CurrentSavePath));
    }

    [Fact]
    public void CitySnapshotRoundTripKeepsOnFootParkingFleetRawPhotosAndProgress()
    {
        var manager = new SaveGameManager(_directory);
        var mobility = new LocomotionSimulator(new(500010, 7400020, 100), initialMode: LocomotionMode.Handcart);
        mobility.TryConsumeCaptureUnits(12, out _);
        var parked = mobility.ExportSaveSnapshot() with { Mode = LocomotionMode.OnFoot, PositionEnu = new(500018, 7400025, 100) };
        var pixelBytes = new byte[256 * 144 * 3]; pixelBytes[10] = 123;
        var sample = new CameraObservationSample("photo_1", "gnss_basic", 21, 130,
            new EstimatedBodyPose(130, parked.PositionEnu, Quaterniond.Identity), CameraMountEstimate.ForwardFacing, CameraIntrinsics.Initial,
            new CameraQualityMetrics(.002, .2, .005, 0, .5, false, false, false, false, true), [], [],
            CameraThumbnail.FromRgbBytes(256, 144, pixelBytes));
        var city = new SaveCitySessionState(parked, new FleetProgressionManager().ExportSnapshot(),
            new WorldCalendarAndWeatherController(45000, true, .7).ExportSnapshot(),
            [SerializableCameraSample.FromDomain(sample)], [parked.PositionEnu], ["facade_1"], ["geopedia_coverage"],
            new(1, ["egg_1"], [], ["card_utm"]), new(["cart"], ["old_center"], ["cam_initial"], ["intro"], [], new Dictionary<string, string> { ["HIST-01"] = "active" }),
            new TerritoryAttentionTracker().ExportSnapshot(), null, ProcessingMode.Technical, false, true, true, true, 177, 900, CameraNextSequence: 22);
        Assert.True(manager.Save(LegacyState() with { City = city }, SaveTrigger.Manual).Succeeded);
        var result = manager.LoadCurrent();
        Assert.True(result.Succeeded, result.DiagnosticDetail);
        var restored = result.State!.City!;
        Assert.Equal(JsonSerializer.Serialize(city), JsonSerializer.Serialize(restored));
        var player = LocomotionSimulator.FromSaveSnapshot(restored.Mobility);
        Assert.Equal(LocomotionMode.OnFoot, player.Mode);
        Assert.Equal(12, player.UsedVirtualStorageUnits);
        Assert.Equal(mobility.PositionEnu, player.HandcartPositionEnu);
        Assert.Equal(parked.PositionEnu, player.PositionEnu);
        var photo = restored.CameraSamples.Single().ToDomain();
        Assert.Equal("gnss_basic", photo.SensorId); // raw identifiers are never run through content aliases
        Assert.Equal(pixelBytes, photo.Thumbnail.CopyRgbBytes());
        Assert.Equal(0, player.ForwardSpeedMetresPerSecond);
    }
}
