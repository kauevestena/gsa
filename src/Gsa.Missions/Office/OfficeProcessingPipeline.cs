using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gsa.Core.Sensors.Camera;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Persistence;
using Gsa.Missions.Quality;

namespace Gsa.Missions.Office;

public enum ProcessingJobState
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Cancelled = 3,
    FailedExpired = 4,
}

public enum ProcessingCommandStatus
{
    Executed = 0,
    IdempotentNoOp = 1,
    RejectedBusy = 2,
    RejectedInvalidState = 3,
    RejectedPrecondition = 4,
    RejectedInsufficientFunds = 5,
    RejectedExpired = 6,
}

public sealed record ProcessingRecipe(
    int SchemaVersion,
    string RecipeId,
    string ContractId,
    ProcessingMode Mode,
    IReadOnlyList<string> SourceBatchIds,
    IReadOnlyList<string> SelectedSampleIds,
    IReadOnlyList<string> ConfirmedFeatureTargetIds,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ConfirmedMasksBySampleId,
    double CalendarDurationSeconds,
    int ServiceFeeCoords)
{
    public string ComputeRecipeHash()
    {
        var canonical = JsonSerializer.Serialize(this);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

public sealed record ProcessingQueueQuote(
    string ContractId,
    string RecipeId,
    string RecipeHash,
    IReadOnlyList<string> SourceBatchIds,
    int SelectedSampleCount,
    double CalendarDurationSeconds,
    int ServiceFeeCoords,
    double ProjectedCompletionTimeSeconds,
    bool WouldExpireBeforeCompletion);

public sealed record ProcessedProduct(
    int SchemaVersion,
    string ProductId,
    int Revision,
    string ContractId,
    string DeliveryFormat,
    string RecipeId,
    string RecipeHash,
    IReadOnlyDictionary<string, string> SourceBatchSha256ById,
    double CreatedAtSeconds,
    QualitySubmission Submission,
    PrivacyComplianceManifest PrivacyManifest);

public sealed record ProcessingJob(
    string JobId,
    ProcessingRecipe Recipe,
    ProcessingJobState State,
    double StartedAtSeconds,
    double LastClockSeconds,
    double ElapsedCalendarSeconds,
    double TotalCalendarSeconds,
    int ChargedFeeCoords,
    IReadOnlyDictionary<string, string> SourceBatchSha256ById,
    ProcessedProduct? Product = null,
    string? FailureReasonKey = null)
{
    public double ProgressFraction => TotalCalendarSeconds <= 0.0
        ? 1.0
        : Math.Clamp(ElapsedCalendarSeconds / TotalCalendarSeconds, 0.0, 1.0);
}

public sealed record ProcessingJobResult(
    ProcessingCommandStatus Status,
    ProcessingJob? Job,
    ProcessedProduct? Product = null,
    string? ReasonKey = null)
{
    public bool Succeeded => Status is ProcessingCommandStatus.Executed or ProcessingCommandStatus.IdempotentNoOp;
}

/// <summary>Single-slot deterministic office processing queue producing versioned products without mutating raw batches.</summary>
public sealed class OfficeProcessingQueue
{
    private readonly Dictionary<string, int> _revisionsByContract = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProcessingJob> _jobsById = new(StringComparer.Ordinal);
    private readonly List<ProcessedProduct> _completedProducts = [];

    public ProcessingJob? ActiveJob { get; private set; }
    public IReadOnlyList<ProcessedProduct> CompletedProducts => _completedProducts;

    public ProcessingQueueQuote PreviewJob(
        ContractStateMachine contract,
        BatchCustodyStore custodyStore,
        ProcessingRecipe recipe,
        double nowSeconds)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(recipe);
        ValidateNonNegativeFinite(nowSeconds, nameof(nowSeconds));
        ValidateRecipe(recipe, contract);

        var protectedArtifacts = ResolveProtectedArtifacts(contract, custodyStore, recipe.SourceBatchIds);
        var selectedCount = CountSelectedUniqueSamples(protectedArtifacts, recipe.SelectedSampleIds);
        var projectedDone = nowSeconds + recipe.CalendarDurationSeconds;
        var expiresAt = contract.Snapshot?.ExpiresAtSeconds ?? double.PositiveInfinity;

        return new ProcessingQueueQuote(
            contract.ContractId,
            recipe.RecipeId,
            recipe.ComputeRecipeHash(),
            protectedArtifacts.Select(a => a.BatchId).ToArray(),
            selectedCount,
            recipe.CalendarDurationSeconds,
            recipe.ServiceFeeCoords,
            projectedDone,
            projectedDone > expiresAt);
    }

    public ProcessingJobResult StartOrResumeJob(
        string jobId,
        ContractStateMachine contract,
        BatchCustodyStore custodyStore,
        CareerLedger ledger,
        ProcessingRecipe recipe,
        double nowSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(recipe);
        ValidateNonNegativeFinite(nowSeconds, nameof(nowSeconds));
        ValidateRecipe(recipe, contract);

        if (_jobsById.TryGetValue(jobId, out var existingJob))
        {
            if (existingJob.State == ProcessingJobState.Completed)
                return new ProcessingJobResult(ProcessingCommandStatus.IdempotentNoOp, existingJob, existingJob.Product);
            if (existingJob.State == ProcessingJobState.Running)
                return new ProcessingJobResult(ProcessingCommandStatus.IdempotentNoOp, existingJob, null);
        }

        if (ActiveJob is not null && ActiveJob.State == ProcessingJobState.Running &&
            !string.Equals(ActiveJob.JobId, jobId, StringComparison.Ordinal))
        {
            return new ProcessingJobResult(
                ProcessingCommandStatus.RejectedBusy,
                ActiveJob,
                null,
                "office.error.queue_busy");
        }

        var clockCheck = contract.AdvanceClock(nowSeconds);
        if (contract.State == ContractState.Expirado)
            return new ProcessingJobResult(ProcessingCommandStatus.RejectedExpired, null, null, "contract.error.expired");

        var protectedArtifacts = ResolveProtectedArtifacts(contract, custodyStore, recipe.SourceBatchIds);
        if (protectedArtifacts.Count == 0)
            return new ProcessingJobResult(ProcessingCommandStatus.RejectedPrecondition, null, null, "office.error.no_protected_batches");

        var batchShaById = custodyStore.AuthoritativeManifest.CommittedBatches
            .Where(b => string.Equals(b.ContractId, contract.ContractId, StringComparison.Ordinal))
            .GroupBy(b => b.BatchId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().PayloadSha256, StringComparer.Ordinal);

        if (contract.State is ContractState.Coletando or ContractState.Retrabalho)
        {
            var queued = contract.MarkReadyToProcess(nowSeconds);
            if (!queued.Succeeded)
                return new ProcessingJobResult(ProcessingCommandStatus.RejectedInvalidState, null, null, queued.ReasonKey);
        }

        if (contract.State != ContractState.ProntoProcessar)
            return new ProcessingJobResult(ProcessingCommandStatus.RejectedInvalidState, null, null, "office.error.invalid_contract_state");

        var isCalm = contract.Snapshot?.Definition.Calm ?? false;
        var effectiveFee = isCalm && ledger.BalanceCoords < CareerLedger.LowBalanceSubsidyThresholdCoords
            ? 0
            : recipe.ServiceFeeCoords;

        if (effectiveFee > 0)
        {
            var debit = ledger.TryDebitPurchaseOrService(
                $"office_job:{jobId}",
                contract.ContractId,
                nowSeconds,
                effectiveFee,
                "office_processing",
                "ledger.office.processing");

            if (!debit.Succeeded)
                return new ProcessingJobResult(ProcessingCommandStatus.RejectedInsufficientFunds, null, null, debit.ReasonKey);
        }

        var startCmd = contract.StartProcessing(nowSeconds);
        if (!startCmd.Succeeded)
        {
            var status = startCmd.Status == ContractCommandStatus.RejectedExpired
                ? ProcessingCommandStatus.RejectedExpired
                : ProcessingCommandStatus.RejectedInvalidState;
            return new ProcessingJobResult(status, null, null, startCmd.ReasonKey);
        }

        var job = new ProcessingJob(
            jobId,
            recipe,
            ProcessingJobState.Running,
            nowSeconds,
            nowSeconds,
            0.0,
            recipe.CalendarDurationSeconds,
            effectiveFee,
            batchShaById);

        _jobsById[jobId] = job;
        ActiveJob = job;

        if (recipe.CalendarDurationSeconds <= 0.0)
            return CompleteRunningJob(job, contract, custodyStore, nowSeconds);

        return new ProcessingJobResult(ProcessingCommandStatus.Executed, job);
    }

    public ProcessingJobResult AdvanceJob(
        ContractStateMachine contract,
        BatchCustodyStore custodyStore,
        double deltaCalendarSeconds)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(custodyStore);
        ValidateNonNegativeFinite(deltaCalendarSeconds, nameof(deltaCalendarSeconds));

        if (ActiveJob is null || ActiveJob.State != ProcessingJobState.Running)
            return new ProcessingJobResult(ProcessingCommandStatus.RejectedInvalidState, ActiveJob, null, "office.error.no_active_job");

        var nowSeconds = ActiveJob.LastClockSeconds + deltaCalendarSeconds;
        contract.AdvanceClock(nowSeconds);
        if (contract.State == ContractState.Expirado)
        {
            var expiredJob = ActiveJob with
            {
                State = ProcessingJobState.FailedExpired,
                LastClockSeconds = nowSeconds,
                ElapsedCalendarSeconds = ActiveJob.ElapsedCalendarSeconds + deltaCalendarSeconds,
                Product = null,
                FailureReasonKey = "office.error.contract_expired",
            };
            _jobsById[expiredJob.JobId] = expiredJob;
            ActiveJob = null;
            return new ProcessingJobResult(ProcessingCommandStatus.RejectedExpired, expiredJob, null, "office.error.contract_expired");
        }

        var updated = ActiveJob with
        {
            LastClockSeconds = nowSeconds,
            ElapsedCalendarSeconds = ActiveJob.ElapsedCalendarSeconds + deltaCalendarSeconds,
        };
        _jobsById[updated.JobId] = updated;
        ActiveJob = updated;

        if (updated.ElapsedCalendarSeconds + 1e-9 >= updated.TotalCalendarSeconds)
            return CompleteRunningJob(updated, contract, custodyStore, nowSeconds);

        return new ProcessingJobResult(ProcessingCommandStatus.Executed, updated);
    }

    public ProcessingJobResult CancelActiveJob(
        ContractStateMachine contract,
        double nowSeconds,
        string? reasonKey = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ValidateNonNegativeFinite(nowSeconds, nameof(nowSeconds));

        if (ActiveJob is null || ActiveJob.State != ProcessingJobState.Running)
            return new ProcessingJobResult(ProcessingCommandStatus.RejectedInvalidState, ActiveJob, null, "office.error.no_active_job");

        if (contract.State == ContractState.Processando)
            contract.CancelProcessing(nowSeconds, reasonKey);

        var cancelled = ActiveJob with
        {
            State = ProcessingJobState.Cancelled,
            LastClockSeconds = nowSeconds,
            Product = null,
            FailureReasonKey = reasonKey ?? "office.job.cancelled",
        };
        _jobsById[cancelled.JobId] = cancelled;
        ActiveJob = null;

        return new ProcessingJobResult(ProcessingCommandStatus.Executed, cancelled, null, cancelled.FailureReasonKey);
    }

    private ProcessingJobResult CompleteRunningJob(
        ProcessingJob job,
        ContractStateMachine contract,
        BatchCustodyStore custodyStore,
        double nowSeconds)
    {
        var completeCmd = contract.CompleteProcessing(nowSeconds);
        if (!completeCmd.Succeeded)
        {
            var failedState = completeCmd.Status == ContractCommandStatus.RejectedExpired
                ? ProcessingJobState.FailedExpired
                : ProcessingJobState.Cancelled;
            var failed = job with
            {
                State = failedState,
                LastClockSeconds = nowSeconds,
                Product = null,
                FailureReasonKey = completeCmd.ReasonKey,
            };
            _jobsById[failed.JobId] = failed;
            ActiveJob = null;
            return new ProcessingJobResult(
                completeCmd.Status == ContractCommandStatus.RejectedExpired ? ProcessingCommandStatus.RejectedExpired : ProcessingCommandStatus.RejectedInvalidState,
                failed,
                null,
                completeCmd.ReasonKey);
        }

        var revision = _revisionsByContract.TryGetValue(contract.ContractId, out var prevRev) ? prevRev + 1 : 1;
        _revisionsByContract[contract.ContractId] = revision;

        var artifacts = ResolveProtectedArtifacts(contract, custodyStore, job.Recipe.SourceBatchIds);
        var product = BuildProcessedProduct(
            contract,
            artifacts,
            job.Recipe,
            job.SourceBatchSha256ById,
            revision,
            nowSeconds);

        _completedProducts.Add(product);
        var completed = job with
        {
            State = ProcessingJobState.Completed,
            LastClockSeconds = nowSeconds,
            ElapsedCalendarSeconds = job.TotalCalendarSeconds,
            Product = product,
        };
        _jobsById[completed.JobId] = completed;
        ActiveJob = null;

        return new ProcessingJobResult(ProcessingCommandStatus.Executed, completed, product);
    }

    private static ProcessedProduct BuildProcessedProduct(
        ContractStateMachine contract,
        IReadOnlyList<SurveyBatchArtifact> artifacts,
        ProcessingRecipe recipe,
        IReadOnlyDictionary<string, string> batchShaById,
        int revision,
        double nowSeconds)
    {
        var definition = contract.Snapshot!.Definition;
        var selectedSet = new HashSet<string>(recipe.SelectedSampleIds, StringComparer.Ordinal);
        var rawSamples = new List<CameraObservationSample>();
        var seenSamples = new HashSet<string>(StringComparer.Ordinal);
        var footprints = new List<SampleFootprintObservation>();
        var seenFootprints = new HashSet<SampleFootprintObservation>();
        var featuresByTarget = new Dictionary<string, TriangulatedFeatureEntry>(StringComparer.Ordinal);
        var checkpointsById = new Dictionary<string, SubmittedCheckpointEstimate>(StringComparer.Ordinal);

        var confirmedFeatureSet = new HashSet<string>(recipe.ConfirmedFeatureTargetIds, StringComparer.Ordinal);

        foreach (var artifact in artifacts)
        {
            foreach (var sampleDto in artifact.Samples)
            {
                if (!selectedSet.Contains(sampleDto.SampleId))
                    continue;
                if (seenSamples.Add(sampleDto.SampleId))
                    rawSamples.Add(sampleDto.ToDomain());
            }

            foreach (var fp in artifact.Footprints)
            {
                if (!selectedSet.Contains(fp.SampleId))
                    continue;
                if (seenFootprints.Add(fp))
                    footprints.Add(fp);
            }

            foreach (var feat in artifact.Features)
            {
                var entry = recipe.Mode == ProcessingMode.Technical && confirmedFeatureSet.Count > 0
                    ? feat with { Confirmed = feat.Confirmed && confirmedFeatureSet.Contains(feat.TargetId) }
                    : feat;
                featuresByTarget[entry.TargetId] = entry;
            }

            foreach (var cp in artifact.Checkpoints)
                checkpointsById[cp.CheckpointId] = cp;
        }

        var maskedSamples = PrivacyMaskingService.ApplyMasksToSamples(
            rawSamples,
            recipe.Mode,
            recipe.ConfirmedMasksBySampleId,
            out var privacyManifest);

        var submission = new QualitySubmission(
            contract.ContractId,
            artifacts.Select(a => a.BatchId).ToArray(),
            definition.DeliveryFormat,
            1,
            false,
            maskedSamples,
            footprints,
            featuresByTarget.Values.OrderBy(f => f.TargetId, StringComparer.Ordinal).ToArray(),
            checkpointsById.Values.OrderBy(c => c.CheckpointId, StringComparer.Ordinal).ToArray());

        return new ProcessedProduct(
            1,
            $"{contract.ContractId}:rev{revision}",
            revision,
            contract.ContractId,
            definition.DeliveryFormat,
            recipe.RecipeId,
            recipe.ComputeRecipeHash(),
            new Dictionary<string, string>(batchShaById, StringComparer.Ordinal),
            nowSeconds,
            submission,
            privacyManifest);
    }

    private static IReadOnlyList<SurveyBatchArtifact> ResolveProtectedArtifacts(
        ContractStateMachine contract,
        BatchCustodyStore custodyStore,
        IReadOnlyList<string> sourceBatchIds)
    {
        var requestedIds = new HashSet<string>(sourceBatchIds, StringComparer.Ordinal);
        var contractProtectedIds = contract.ProtectedBatches
            .Select(b => b.BatchId)
            .ToHashSet(StringComparer.Ordinal);

        return custodyStore.GetProtectedArtifactsForContract(contract.ContractId)
            .Where(a => contractProtectedIds.Contains(a.BatchId) && requestedIds.Contains(a.BatchId))
            .OrderBy(a => a.StartTimeSeconds)
            .ThenBy(a => a.BatchId, StringComparer.Ordinal)
            .ToArray();
    }

    private static int CountSelectedUniqueSamples(
        IReadOnlyList<SurveyBatchArtifact> artifacts,
        IReadOnlyList<string> selectedSampleIds)
    {
        var selectedSet = new HashSet<string>(selectedSampleIds, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in artifacts)
        {
            foreach (var sample in artifact.Samples)
            {
                if (selectedSet.Contains(sample.SampleId))
                    seen.Add(sample.SampleId);
            }
        }
        return seen.Count;
    }

    private static void ValidateRecipe(ProcessingRecipe recipe, ContractStateMachine contract)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipe.RecipeId);
        if (!string.Equals(recipe.ContractId, contract.ContractId, StringComparison.Ordinal))
            throw new ArgumentException("ProcessingRecipe contract ID does not match ContractStateMachine.");
        if (!double.IsFinite(recipe.CalendarDurationSeconds) || recipe.CalendarDurationSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(recipe), "Calendar duration must be finite and non-negative.");
        if (recipe.ServiceFeeCoords < 0)
            throw new ArgumentOutOfRangeException(nameof(recipe), "Service fee cannot be negative.");
    }

    private static void ValidateNonNegativeFinite(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0.0)
            throw new ArgumentOutOfRangeException(name, "Value must be finite and non-negative.");
    }
}

public sealed record OfficeJobPreview(
    ProcessingMode Mode,
    double DurationSeconds,
    int BaseServiceFeeCoords,
    int EffectiveServiceFeeCoords,
    bool LowBalanceSubsidized,
    int ProtectedBatchCount,
    int SuggestedSampleCount,
    int SensitiveRegionCount,
    string PreviewKey);

/// <summary>Builds Arcade (assisted/automated) and Technical (manual) simplified processing workflows over custody batches.</summary>
public static class SimplifiedOfficeWorkstation
{
    public const double BasicBatchDurationSeconds = 600.0;
    public const double ArcadeFeeFraction = 0.05;

    public static OfficeJobPreview BuildCostAndTimePreview(
        ContractStateMachine contract,
        BatchCustodyStore custodyStore,
        CareerLedger ledger,
        ProcessingMode mode,
        int processingSkillRank = 0,
        IReadOnlyList<string>? sourceBatchIds = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(ledger);

        var definition = contract.Snapshot?.Definition
            ?? throw new InvalidOperationException("Contract must be accepted before previewing office processing.");

        var artifacts = ResolveArtifacts(contract, custodyStore, sourceBatchIds);
        var clampedRank = Math.Clamp(processingSkillRank, 0, FleetShopCatalog.MaxSkillRank);
        var perBatchDuration = BasicBatchDurationSeconds * (1.0 - 0.10 * clampedRank);
        var durationSeconds = artifacts.Count * perBatchDuration;

        var baseFee = mode == ProcessingMode.Arcade
            ? (int)Math.Round(definition.Reward.BaseCoords * ArcadeFeeFraction, MidpointRounding.AwayFromZero)
            : 0;
        var subsidized = mode == ProcessingMode.Arcade
            && definition.Calm
            && ledger.BalanceCoords < CareerLedger.LowBalanceSubsidyThresholdCoords;
        var effectiveFee = subsidized ? 0 : baseFee;

        var seenSamples = new HashSet<string>(StringComparer.Ordinal);
        var sensitiveCount = 0;
        foreach (var artifact in artifacts)
        {
            foreach (var sampleDto in artifact.Samples)
            {
                if (seenSamples.Add(sampleDto.SampleId))
                    sensitiveCount += PrivacyMaskingService.DetectSensitiveRegions(sampleDto.ToDomain()).Count;
            }
        }

        var previewKey = mode == ProcessingMode.Arcade
            ? "office.mode.arcade.preview"
            : "office.mode.technical.preview";

        return new OfficeJobPreview(
            Mode: mode,
            DurationSeconds: durationSeconds,
            BaseServiceFeeCoords: baseFee,
            EffectiveServiceFeeCoords: effectiveFee,
            LowBalanceSubsidized: subsidized,
            ProtectedBatchCount: artifacts.Count,
            SuggestedSampleCount: seenSamples.Count,
            SensitiveRegionCount: sensitiveCount,
            PreviewKey: previewKey);
    }

    public static ProcessingRecipe BuildArcadeAssistedRecipe(
        string recipeId,
        ContractStateMachine contract,
        BatchCustodyStore custodyStore,
        CareerLedger ledger,
        int processingSkillRank = 0,
        IReadOnlyList<string>? sourceBatchIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipeId);
        var artifacts = ResolveArtifacts(contract, custodyStore, sourceBatchIds);
        var preview = BuildCostAndTimePreview(contract, custodyStore, ledger, ProcessingMode.Arcade, processingSkillRank, sourceBatchIds);

        var batchIds = artifacts.Select(a => a.BatchId).ToArray();
        var sampleIds = new List<string>();
        var seenSamples = new HashSet<string>(StringComparer.Ordinal);
        var confirmedFeatureIds = new List<string>();
        var seenFeatures = new HashSet<string>(StringComparer.Ordinal);
        var confirmedMasksBySample = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var artifact in artifacts)
        {
            foreach (var sampleDto in artifact.Samples)
            {
                if (!seenSamples.Add(sampleDto.SampleId))
                    continue;
                sampleIds.Add(sampleDto.SampleId);
                var regions = PrivacyMaskingService.DetectSensitiveRegions(sampleDto.ToDomain());
                if (regions.Count > 0)
                    confirmedMasksBySample[sampleDto.SampleId] = regions.Select(r => r.TargetId).ToArray();
            }

            foreach (var feat in artifact.Features)
            {
                if (feat.Confirmed && feat.Status == TriangulationStatus.Resolved && seenFeatures.Add(feat.TargetId))
                    confirmedFeatureIds.Add(feat.TargetId);
            }
        }

        return new ProcessingRecipe(
            SchemaVersion: 1,
            RecipeId: recipeId,
            ContractId: contract.ContractId,
            Mode: ProcessingMode.Arcade,
            SourceBatchIds: batchIds,
            SelectedSampleIds: sampleIds,
            ConfirmedFeatureTargetIds: confirmedFeatureIds,
            ConfirmedMasksBySampleId: confirmedMasksBySample,
            CalendarDurationSeconds: preview.DurationSeconds,
            ServiceFeeCoords: preview.EffectiveServiceFeeCoords);
    }

    public static ProcessingRecipe BuildTechnicalManualRecipe(
        string recipeId,
        ContractStateMachine contract,
        BatchCustodyStore custodyStore,
        CareerLedger ledger,
        IReadOnlyList<string> sourceBatchIds,
        IReadOnlyList<string> selectedSampleIds,
        IReadOnlyList<string> confirmedFeatureTargetIds,
        IReadOnlyDictionary<string, IReadOnlyList<string>> confirmedMasksBySampleId,
        int processingSkillRank = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipeId);
        ArgumentNullException.ThrowIfNull(sourceBatchIds);
        ArgumentNullException.ThrowIfNull(selectedSampleIds);
        ArgumentNullException.ThrowIfNull(confirmedFeatureTargetIds);
        ArgumentNullException.ThrowIfNull(confirmedMasksBySampleId);

        var artifacts = ResolveArtifacts(contract, custodyStore, sourceBatchIds);
        var preview = BuildCostAndTimePreview(contract, custodyStore, ledger, ProcessingMode.Technical, processingSkillRank, sourceBatchIds);

        return new ProcessingRecipe(
            SchemaVersion: 1,
            RecipeId: recipeId,
            ContractId: contract.ContractId,
            Mode: ProcessingMode.Technical,
            SourceBatchIds: artifacts.Select(a => a.BatchId).ToArray(),
            SelectedSampleIds: selectedSampleIds,
            ConfirmedFeatureTargetIds: confirmedFeatureTargetIds,
            ConfirmedMasksBySampleId: confirmedMasksBySampleId,
            CalendarDurationSeconds: preview.DurationSeconds,
            ServiceFeeCoords: preview.EffectiveServiceFeeCoords);
    }

    private static IReadOnlyList<SurveyBatchArtifact> ResolveArtifacts(
        ContractStateMachine contract,
        BatchCustodyStore custodyStore,
        IReadOnlyList<string>? sourceBatchIds)
    {
        var contractProtectedIds = contract.ProtectedBatches
            .Select(b => b.BatchId)
            .ToHashSet(StringComparer.Ordinal);

        HashSet<string>? requestedIds = sourceBatchIds is null
            ? null
            : new HashSet<string>(sourceBatchIds, StringComparer.Ordinal);

        return custodyStore.GetProtectedArtifactsForContract(contract.ContractId)
            .Where(a => contractProtectedIds.Contains(a.BatchId) && (requestedIds is null || requestedIds.Contains(a.BatchId)))
            .OrderBy(a => a.StartTimeSeconds)
            .ThenBy(a => a.BatchId, StringComparer.Ordinal)
            .ToArray();
    }
}

