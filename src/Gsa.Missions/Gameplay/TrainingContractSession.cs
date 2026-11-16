using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.World;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Office;
using Gsa.Missions.Persistence;
using Gsa.Missions.Quality;

namespace Gsa.Missions.Gameplay;

/// <summary>Player-driven training loop. Receives observations, never world target positions for reconstruction.</summary>
public sealed class TrainingContractSession
{
    public const string ContractId = "training_live_facades";
    public const int SampleLimit = 200;
    public static Vector3d SpawnPosition => new(500228, 7400000.7, 100);
    public static IReadOnlyList<string> TargetIds { get; } = Array.AsReadOnly(new[] { "facade_18", "facade_19", "facade_20" });
    private readonly Vector3d _office;
    private readonly Vector3d _unload;
    private readonly List<CameraObservationSample> _samples = [];
    private readonly HashSet<string> _sampleIds = new(StringComparer.Ordinal);
    private readonly EvaluationReference _reference;
    private readonly SaveGameManager _saves;
    public ContractStateMachine Contract { get; private set; }
    public BatchCustodyStore Custody { get; private set; } = new();
    public CareerLedger Ledger { get; private set; } = new();
    public CareerProgressionState Progression { get; private set; } = new();
    public QualityReport? Report { get; private set; }
    public double ClockSeconds { get; private set; }
    public bool SaveBlocked { get; private set; }
    public int SampleCount => _samples.Count;
    public int PendingCount => Contract.PendingFieldBatches.Count;
    public int ProtectedCount => Contract.ProtectedBatches.Count;
    public IReadOnlyList<CameraObservationSample> Samples => _samples.AsReadOnly();
    public IReadOnlyList<string> ResolvedTargetIds => FeatureInventoryTriangulator
        .BuildInventory(_samples, FeatureConfirmationMode.Arcade).ResolvedConfirmedFeatures
        .Where(f => TargetIds.Contains(f.TargetId)).Select(f => f.TargetId).ToArray();
    public bool CanCapture => !SaveBlocked && SampleCount < SampleLimit &&
        Contract.State is (ContractState.Aceito or ContractState.Coletando or ContractState.Retrabalho);

    public TrainingContractSession(WorldSceneManifest manifest, string saveDirectory)
    {
        _office = manifest.Objects.Single(o => o.Id == "office_entry").PositionEnu;
        _unload = manifest.Objects.Single(o => o.Id == "unload_point").PositionEnu;
        if (TargetIds.Any(id => !manifest.Objects.Any(o => o.Id == id)))
            throw new ArgumentException("Training scene is missing required facades.", nameof(manifest));
        var definition = new ContractDefinition(1, ContractId, "training.title", "training_office", "old_center",
            "training_facades", "feature_inventory", TargetIds,
            [new ThresholdRequirementSpec("feature_completeness", "ratio", "min", 1, 1),
             new PredicateRequirementSpec("privacy_redacted", true),
             new PredicateRequirementSpec("format_valid", true)],
            [], "low", true, new ContractDeadlineSpec(86400, 172800, 0, 0),
            new ContractRewardSpec(150, 25, 1, []), new ContractPartialPolicySpec("none", []),
            "gsa_bundle_v1", []);
        Contract = new ContractStateMachine(definition);
        _reference = new EvaluationReference(ContractId, [], TargetIds);
        _saves = new SaveGameManager(saveDirectory);
    }

    public static double HorizontalDistance(Vector3d a, Vector3d b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    public double OfficeDistance(Vector3d position) => HorizontalDistance(position, _office);
    public double UnloadDistance(Vector3d position) => HorizontalDistance(position, _unload);

    public void AdvanceClock(double delta)
    {
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        ClockSeconds += delta;
        Contract.AdvanceClock(ClockSeconds);
    }

    public bool Accept(Vector3d position)
    {
        if (SaveBlocked || OfficeDistance(position) > 8 || !Contract.Accept(ClockSeconds).Succeeded) return false;
        Save(SaveTrigger.ContractAccepted);
        return true;
    }

    public bool RecordObservation(CameraObservationSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (!CanCapture || _sampleIds.Contains(sample.SampleId)) return false;
        var batchId = $"live_{sample.SampleId}";
        var batch = SurveyBatchArtifact.FromObservationBatch(ContractId,
            new ObservationBatch(batchId, [sample.SensorId], [sample], 1, 0, CaptureStopReason.None));
        if (!Contract.RecordPendingBatch(ClockSeconds, batchId).Succeeded) return false;
        if (!Custody.RegisterBufferedBatch(batch, ClockSeconds).Succeeded)
            throw new InvalidOperationException("Could not register live observation.");
        _samples.Add(sample);
        _sampleIds.Add(sample.SampleId);
        return true;
    }

    public bool Offload(Vector3d position)
    {
        if (UnloadDistance(position) > 5 || PendingCount == 0) return false;
        foreach (var batch in Contract.PendingFieldBatches.ToArray())
        {
            if (!Custody.OffloadAtomic(batch.BatchId, ClockSeconds).Succeeded ||
                !Contract.ProtectBatchAtUnloadPoint(ClockSeconds, batch.BatchId).Succeeded)
                throw new InvalidOperationException("Could not protect live batch.");
        }
        Save(SaveTrigger.BatchOffloaded);
        return true;
    }

    // Processing is a deliberate office action with calendar time skipped, not an unattended background job.
    public bool Process(Vector3d position)
    {
        if (OfficeDistance(position) > 8 || PendingCount != 0 || ProtectedCount == 0 ||
            Contract.State is not (ContractState.Coletando or ContractState.Retrabalho)) return false;
        var queue = new OfficeProcessingQueue();
        var recipe = SimplifiedOfficeWorkstation.BuildArcadeAssistedRecipe(
            $"live_recipe_{Contract.Events.Count}", Contract, Custody, Ledger);
        // One training session is one office batch, regardless of how many capture frames were committed.
        recipe = recipe with { CalendarDurationSeconds = 600 };
        if (!Contract.MarkReadyToProcess(ClockSeconds).Succeeded) return false;
        var start = queue.StartOrResumeJob($"live_job_{Contract.Events.Count}", Contract, Custody, Ledger, recipe, ClockSeconds);
        if (!start.Succeeded) return false;
        var result = queue.AdvanceJob(Contract, Custody, recipe.CalendarDurationSeconds);
        ClockSeconds += recipe.CalendarDurationSeconds;
        if (!result.Succeeded || result.Product is null) return false;
        var submission = Reconstruct(result.Product.Submission);
        Report = QualityEvaluator.Evaluate(Contract.Snapshot!.Definition, _reference, submission);
        if (!Contract.RecordQualityEvaluation(ClockSeconds, Report.Passed, Report.PassedDeliverableIds,
                Report.FailureReasonKeys.FirstOrDefault()).Succeeded) return false;
        Save(SaveTrigger.Manual);
        return true;
    }

    private static QualitySubmission Reconstruct(QualitySubmission submission) => submission with
    {
        Features = FeatureInventoryTriangulator.BuildInventory(submission.Samples, FeatureConfirmationMode.Arcade).Entries,
    };

    public bool Deliver(Vector3d position)
    {
        if (OfficeDistance(position) > 8 || Report is null || !Report.Passed ||
            Contract.State is not (ContractState.ProntoEntregar or ContractState.Entregue)) return false;
        var result = ContractSettlementCoordinator.SettleDelivery(Contract, Ledger, Progression, Report, ClockSeconds);
        if (!result.Succeeded) return false;
        Save(SaveTrigger.ContractSettled);
        return true;
    }

    public SaveOperationResult Save(SaveTrigger trigger = SaveTrigger.Manual)
    {
        if (SaveBlocked) throw new IOException("Training save is unreadable or incompatible; refusing to overwrite it.");
        return _saves.Save(
        new SaveGameState(1, trigger, 0, new SaveClockState(ClockSeconds, ClockSeconds),
            new SaveRngState(1, 1, SampleCount), Ledger.ExportState(), Progression.ExportSnapshot(),
            new SaveEquipmentMountState("cart", ["camera_initial"], new Dictionary<string, string>()),
            Custody.ExportSnapshot(), [Contract.ExportSnapshot()]), trigger);
    }

    // Save data is isolated from career/HIST slots. Returning to the scene respawns at the training office.
    public SaveLoadResult Load()
    {
        var result = _saves.LoadCurrent();
        if (!result.Succeeded)
        {
            SaveBlocked = result.Status != SaveLoadStatus.FileNotFound;
            return result;
        }
        var state = result.State!;
        if (state.Contracts.Count != 1 || state.Contracts[0].CatalogDefinition.Id != ContractId)
        {
            SaveBlocked = true;
            return result with { Status = SaveLoadStatus.IncompatibleVersion, State = null };
        }
        SaveBlocked = false;
        var runtime = SaveGameManager.RestoreRuntime(state);
        Contract = runtime.Contracts.Single();
        Custody = runtime.CustodyStore;
        Ledger = runtime.Ledger;
        Progression = runtime.Progression;
        ClockSeconds = runtime.Clocks.SimulationTimeSeconds;
        _samples.Clear();
        _sampleIds.Clear();
        foreach (var entry in Custody.Entries.Where(e => e.Stage is BatchCustodyStage.Buffered or BatchCustodyStage.Protected))
        {
            var artifact = JsonSerializer.Deserialize<SurveyBatchArtifact>(entry.PayloadJson)!;
            foreach (var dto in artifact.Samples)
                if (_sampleIds.Add(dto.SampleId)) _samples.Add(dto.ToDomain());
        }
        Report = null;
        if (Contract.State is ContractState.Retrabalho or ContractState.ProntoEntregar or ContractState.Entregue)
        {
            var merged = ContractSettlementCoordinator.BuildMergedSubmission(Contract, Custody);
            var masked = PrivacyMaskingService.ApplyMasksToSamples(merged.Samples, ProcessingMode.Arcade,
                new Dictionary<string, IReadOnlyList<string>>(), out _);
            Report = QualityEvaluator.Evaluate(Contract.Snapshot!.Definition, _reference, Reconstruct(merged with { Samples = masked }));
        }
        return result;
    }
}
