using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;

namespace Gsa.Missions.Persistence;

public enum SaveTrigger
{
    Manual = 0,
    ContractAccepted = 1,
    BatchOffloaded = 2,
    ContractSettled = 3,
    EquipmentPurchased = 4,
    MissionEnded = 5,
    ConfirmedVehicleLoss = 6,
}

public enum SaveOperationStatus
{
    Executed = 0,
    BlockedDuringAtomicTransition = 1,
}

public enum SaveLoadStatus
{
    Loaded = 0,
    FileNotFound = 1,
    CorruptedPayload = 2,
    ChecksumMismatch = 3,
    UnsupportedFutureVersion = 4,
    IncompatibleVersion = 5,
}

public sealed record SaveClockState(
    double SimulationTimeSeconds,
    double CareerElapsedSeconds);

public sealed record SaveRngState(
    ulong WorldSeed,
    ulong StreamState,
    long StepIndex);

public sealed record SaveEquipmentMountState(
    string ActivePlatformId,
    IReadOnlyList<string> OwnedEquipmentIds,
    IReadOnlyDictionary<string, string> MountedSlots);

public sealed record SaveGameState(
    int SchemaVersion,
    SaveTrigger Trigger,
    long SaveGeneration,
    SaveClockState Clocks,
    SaveRngState Rng,
    CareerLedgerState Ledger,
    CareerProgressionSnapshot Progression,
    SaveEquipmentMountState Equipment,
    CustodyStoreSnapshot CustodyStore,
    IReadOnlyList<ContractInstanceSnapshot> Contracts,
    int ConfirmedVehicleLossCount = 0,
    double? LastVehicleLossAtSeconds = null);

public sealed record SaveFileEnvelope(
    int SchemaVersion,
    string PayloadSha256,
    string PayloadJson);

public sealed record SaveOperationResult(
    SaveOperationStatus Status,
    long SaveGeneration,
    string FilePath,
    string PayloadSha256,
    string? ReasonKey = null)
{
    public bool Succeeded => Status == SaveOperationStatus.Executed;
}

public sealed record SaveLoadResult(
    SaveLoadStatus Status,
    SaveGameState? State,
    bool PreviousGenerationAvailable,
    string? DiagnosticReasonKey = null,
    string? DiagnosticDetail = null)
{
    public bool Succeeded => Status == SaveLoadStatus.Loaded && State is not null;
}

public sealed record RestoredCareerRuntime(
    SaveClockState Clocks,
    SaveRngState Rng,
    CareerLedger Ledger,
    CareerProgressionState Progression,
    SaveEquipmentMountState Equipment,
    BatchCustodyStore CustodyStore,
    IReadOnlyList<ContractStateMachine> Contracts,
    int ConfirmedVehicleLossCount,
    double? LastVehicleLossAtSeconds);

/// <summary>Atomic generation-rotated career save manager preserving clocks, RNG, ledger, custody, and corrupt files.</summary>
public sealed class SaveGameManager
{
    public const int CurrentSchemaVersion = 1;
    public const string CurrentFileName = "current.save.json";
    public const string PreviousFileName = "previous.save.json";

    private long _lastGeneration;

    public string SlotDirectory { get; }
    public string CurrentSavePath => Path.Combine(SlotDirectory, CurrentFileName);
    public string PreviousSavePath => Path.Combine(SlotDirectory, PreviousFileName);

    public SaveGameManager(string slotDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotDirectory);
        SlotDirectory = Path.GetFullPath(slotDirectory);
        Directory.CreateDirectory(SlotDirectory);

        var current = LoadFile(CurrentSavePath);
        if (current.Succeeded && current.State is not null)
            _lastGeneration = current.State.SaveGeneration;
    }

    public SaveOperationResult Save(SaveGameState state, SaveTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (trigger == SaveTrigger.Manual &&
            state.CustodyStore.Entries.Any(e => e.Stage == BatchCustodyStage.Offloading))
        {
            return new SaveOperationResult(
                SaveOperationStatus.BlockedDuringAtomicTransition,
                _lastGeneration,
                CurrentSavePath,
                string.Empty,
                "save.error.manual_blocked_during_offload");
        }

        Directory.CreateDirectory(SlotDirectory);

        var nextGen = Math.Max(_lastGeneration + 1, state.SaveGeneration + 1);
        var normalized = state with
        {
            SchemaVersion = CurrentSchemaVersion,
            Trigger = trigger,
            SaveGeneration = nextGen,
        };

        var payloadJson = JsonSerializer.Serialize(normalized);
        var payloadSha = ComputeSha256Hex(payloadJson);
        var envelope = new SaveFileEnvelope(CurrentSchemaVersion, payloadSha, payloadJson);
        var envelopeJson = JsonSerializer.Serialize(envelope);

        // Rotate valid current.save.json into previous.save.json without overwriting backup with garbage.
        if (File.Exists(CurrentSavePath))
        {
            var existingValid = LoadFile(CurrentSavePath);
            if (existingValid.Succeeded)
            {
                var currentBytes = File.ReadAllBytes(CurrentSavePath);
                WriteAtomicBytes(PreviousSavePath, currentBytes);
            }
        }

        WriteAtomicBytes(CurrentSavePath, Encoding.UTF8.GetBytes(envelopeJson));
        _lastGeneration = nextGen;

        return new SaveOperationResult(SaveOperationStatus.Executed, nextGen, CurrentSavePath, payloadSha);
    }

    public SaveOperationResult RecordConfirmedVehicleLossAndAutosave(
        SaveGameState currentState,
        IReadOnlyList<ContractStateMachine> contracts,
        BatchCustodyStore custodyStore,
        CareerLedger ledger,
        CareerProgressionState progression,
        double nowSeconds)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(progression);

        custodyStore.HandleVehicleLoss(nowSeconds);
        foreach (var contract in contracts)
        {
            if (contract.IsActiveState)
                contract.HandleFieldIncident(nowSeconds, FieldIncidentKind.VehicleDestroyed);
        }

        var updated = currentState with
        {
            Clocks = currentState.Clocks with { SimulationTimeSeconds = Math.Max(currentState.Clocks.SimulationTimeSeconds, nowSeconds) },
            Ledger = ledger.ExportState(),
            Progression = progression.ExportSnapshot(),
            CustodyStore = custodyStore.ExportSnapshot(),
            Contracts = contracts.Select(c => c.ExportSnapshot()).ToArray(),
            ConfirmedVehicleLossCount = currentState.ConfirmedVehicleLossCount + 1,
            LastVehicleLossAtSeconds = nowSeconds,
        };

        return Save(updated, SaveTrigger.ConfirmedVehicleLoss);
    }

    public SaveLoadResult LoadCurrent()
    {
        var previousValid = File.Exists(PreviousSavePath) && LoadFile(PreviousSavePath).Succeeded;
        var primary = LoadFile(CurrentSavePath);
        return primary with { PreviousGenerationAvailable = previousValid };
    }

    public SaveLoadResult RecoverFromPreviousGeneration()
    {
        var previous = LoadFile(PreviousSavePath);
        return previous with { PreviousGenerationAvailable = previous.Succeeded };
    }

    public static RestoredCareerRuntime RestoreRuntime(SaveGameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var custodyStore = BatchCustodyStore.RecoverAfterCrash(state.CustodyStore);
        var ledger = new CareerLedger(state.Ledger);
        var progression = CareerProgressionState.FromSnapshot(state.Progression);
        var contracts = state.Contracts.Select(ContractStateMachine.FromSnapshot).ToArray();

        return new RestoredCareerRuntime(
            state.Clocks,
            state.Rng,
            ledger,
            progression,
            state.Equipment,
            custodyStore,
            contracts,
            state.ConfirmedVehicleLossCount,
            state.LastVehicleLossAtSeconds);
    }

    private static SaveLoadResult LoadFile(string path)
    {
        if (!File.Exists(path))
            return new SaveLoadResult(SaveLoadStatus.FileNotFound, null, false, "save.error.not_found", $"Missing '{Path.GetFileName(path)}'.");

        string rawJson;
        try
        {
            rawJson = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SaveLoadResult(SaveLoadStatus.CorruptedPayload, null, false, "save.error.read_failed", ex.Message);
        }

        SaveFileEnvelope? envelope;
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (!doc.RootElement.TryGetProperty(nameof(SaveFileEnvelope.SchemaVersion), out var versionEl) ||
                versionEl.ValueKind != JsonValueKind.Number)
            {
                return new SaveLoadResult(SaveLoadStatus.CorruptedPayload, null, false, "save.error.missing_schema_version", "Missing numeric SchemaVersion.");
            }

            var schemaVersion = versionEl.GetInt32();
            if (schemaVersion > CurrentSchemaVersion)
            {
                return new SaveLoadResult(
                    SaveLoadStatus.UnsupportedFutureVersion,
                    null,
                    false,
                    "save.error.future_version",
                    $"Save schema_version {schemaVersion} exceeds supported version {CurrentSchemaVersion}.");
            }

            if (schemaVersion < CurrentSchemaVersion)
            {
                return new SaveLoadResult(
                    SaveLoadStatus.IncompatibleVersion,
                    null,
                    false,
                    "save.error.incompatible_version",
                    $"Save schema_version {schemaVersion} is older than {CurrentSchemaVersion}.");
            }

            envelope = JsonSerializer.Deserialize<SaveFileEnvelope>(rawJson);
        }
        catch (JsonException ex)
        {
            return new SaveLoadResult(SaveLoadStatus.CorruptedPayload, null, false, "save.error.corrupted_json", ex.Message);
        }

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.PayloadJson) || string.IsNullOrWhiteSpace(envelope.PayloadSha256))
        {
            return new SaveLoadResult(SaveLoadStatus.CorruptedPayload, null, false, "save.error.empty_envelope", "Save envelope is missing payload fields.");
        }

        var actualSha = ComputeSha256Hex(envelope.PayloadJson);
        if (!string.Equals(actualSha, envelope.PayloadSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new SaveLoadResult(
                SaveLoadStatus.ChecksumMismatch,
                null,
                false,
                "save.error.checksum_mismatch",
                $"Expected SHA-256 '{envelope.PayloadSha256}' but observed '{actualSha}'.");
        }

        try
        {
            using var payloadDoc = JsonDocument.Parse(envelope.PayloadJson);
            if (payloadDoc.RootElement.TryGetProperty(nameof(SaveGameState.SchemaVersion), out var innerVerEl) &&
                innerVerEl.ValueKind == JsonValueKind.Number &&
                innerVerEl.GetInt32() > CurrentSchemaVersion)
            {
                return new SaveLoadResult(
                    SaveLoadStatus.UnsupportedFutureVersion,
                    null,
                    false,
                    "save.error.future_version",
                    $"Inner state schema_version {innerVerEl.GetInt32()} exceeds {CurrentSchemaVersion}.");
            }

            var state = JsonSerializer.Deserialize<SaveGameState>(envelope.PayloadJson);
            if (state is null)
                return new SaveLoadResult(SaveLoadStatus.CorruptedPayload, null, false, "save.error.null_state", "Deserialized save state was null.");

            return new SaveLoadResult(SaveLoadStatus.Loaded, state, false);
        }
        catch (JsonException ex)
        {
            return new SaveLoadResult(SaveLoadStatus.CorruptedPayload, null, false, "save.error.corrupted_payload", ex.Message);
        }
    }

    private static void WriteAtomicBytes(string destinationPath, byte[] bytes)
    {
        var tempPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }

            File.Move(tempPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static string ComputeSha256Hex(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}

