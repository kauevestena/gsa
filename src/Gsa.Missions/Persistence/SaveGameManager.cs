using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    BlockedUnreadableCurrent = 2,
    InvalidState = 3,
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
    double? LastVehicleLossAtSeconds = null,
    SaveContentVersions? Content = null,
    SaveCitySessionState? City = null)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

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
    string? DiagnosticDetail = null,
    SaveCompatibilityInfo? Compatibility = null,
    string? PreservedOriginalPath = null)
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
    public const int CurrentSchemaVersion = 2;
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
        var previous = LoadFile(PreviousSavePath);
        _lastGeneration = Math.Max(current.State?.SaveGeneration ?? 0, previous.State?.SaveGeneration ?? 0);
    }

    public SaveOperationResult Save(SaveGameState state, SaveTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(state);

        var existing = LoadFile(CurrentSavePath);
        if (existing.Status != SaveLoadStatus.FileNotFound && !existing.Succeeded)
            return new SaveOperationResult(SaveOperationStatus.BlockedUnreadableCurrent, _lastGeneration,
                CurrentSavePath, string.Empty, "save.error.current_protected");

        try { state = SaveCompatibility.Normalize(state, state.SchemaVersion).State; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or
            FormatException or KeyNotFoundException or NullReferenceException)
        {
            return new SaveOperationResult(SaveOperationStatus.InvalidState, _lastGeneration,
                CurrentSavePath, string.Empty, "save.error.invalid_state");
        }

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

        var generation = Math.Max(Math.Max(_lastGeneration, state.SaveGeneration), existing.State?.SaveGeneration ?? 0);
        if (generation == long.MaxValue)
            return new SaveOperationResult(SaveOperationStatus.InvalidState, _lastGeneration,
                CurrentSavePath, string.Empty, "save.error.invalid_state");
        var nextGen = generation + 1;
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
            if (existing.Succeeded)
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

    /// <summary>Explicit recovery: preserve the rejected current file, keep the backup, then restore it atomically.</summary>
    public SaveLoadResult RestorePreviousGeneration()
    {
        var previous = LoadFile(PreviousSavePath);
        if (!previous.Succeeded)
            return previous;
        string? preserved = null;
        try
        {
            var backupBytes = File.ReadAllBytes(PreviousSavePath);
            if (File.Exists(CurrentSavePath))
            {
                var archivePath = Path.Combine(SlotDirectory, $"preserved-{Guid.NewGuid():N}.save.json");
                WriteAtomicBytes(archivePath, File.ReadAllBytes(CurrentSavePath));
                preserved = archivePath;
            }
            WriteAtomicBytes(CurrentSavePath, backupBytes);
            _lastGeneration = Math.Max(_lastGeneration, previous.State!.SaveGeneration);
            return LoadCurrent() with { PreservedOriginalPath = preserved };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SaveLoadResult(SaveLoadStatus.CorruptedPayload, null, true,
                "save.error.recovery_failed", ex.Message, PreservedOriginalPath: preserved);
        }
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
            return new(SaveLoadStatus.FileNotFound, null, false, "save.error.not_found");
        try
        {
            var rawJson = File.ReadAllText(path, Encoding.UTF8);
            using var document = JsonDocument.Parse(rawJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(nameof(SaveFileEnvelope.SchemaVersion), out var version) ||
                !version.TryGetInt32(out var sourceVersion))
                return new(SaveLoadStatus.CorruptedPayload, null, false, "save.error.missing_schema_version");
            if (sourceVersion > CurrentSchemaVersion)
                return new(SaveLoadStatus.UnsupportedFutureVersion, null, false, "save.error.future_version");
            if (sourceVersion < 1)
                return new(SaveLoadStatus.IncompatibleVersion, null, false, "save.error.incompatible_version");

            var envelope = JsonSerializer.Deserialize<SaveFileEnvelope>(rawJson);
            if (envelope is null || string.IsNullOrWhiteSpace(envelope.PayloadJson) || string.IsNullOrWhiteSpace(envelope.PayloadSha256))
                return new(SaveLoadStatus.CorruptedPayload, null, false, "save.error.empty_envelope");
            if (!string.Equals(ComputeSha256Hex(envelope.PayloadJson), envelope.PayloadSha256, StringComparison.OrdinalIgnoreCase))
                return new(SaveLoadStatus.ChecksumMismatch, null, false, "save.error.checksum_mismatch");

            using var payload = JsonDocument.Parse(envelope.PayloadJson);
            if (payload.RootElement.ValueKind != JsonValueKind.Object ||
                !payload.RootElement.TryGetProperty(nameof(SaveGameState.SchemaVersion), out var innerVersion) ||
                !innerVersion.TryGetInt32(out var inner))
                return new(SaveLoadStatus.CorruptedPayload, null, false, "save.error.missing_schema_version");
            if (inner > CurrentSchemaVersion)
                return new(SaveLoadStatus.UnsupportedFutureVersion, null, false, "save.error.future_version");
            if (inner != sourceVersion)
                return new(SaveLoadStatus.CorruptedPayload, null, false, "save.error.schema_mismatch");

            var state = JsonSerializer.Deserialize<SaveGameState>(envelope.PayloadJson);
            if (state is null || (sourceVersion == 2 && state.Content is null))
                return new(SaveLoadStatus.CorruptedPayload, null, false, "save.error.invalid_state");
            if (state.Content is { } content && (content.CatalogVersion is < 1 or > 4 || content.ModelVersion != 1 || content.WorldVersion != 1))
                return new(SaveLoadStatus.IncompatibleVersion, null, false, "save.error.content_version");
            var (normalized, info) = SaveCompatibility.Normalize(state, sourceVersion);
            return new(SaveLoadStatus.Loaded, normalized, false, Compatibility: info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(SaveLoadStatus.CorruptedPayload, null, false, "save.error.read_failed", ex.Message);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or
            FormatException or OverflowException or KeyNotFoundException or NullReferenceException or NotSupportedException)
        {
            return new(SaveLoadStatus.CorruptedPayload, null, false, "save.error.invalid_state", ex.Message);
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

