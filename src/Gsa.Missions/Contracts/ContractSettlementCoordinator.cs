using Gsa.Missions.Economy;
using Gsa.Missions.Persistence;
using Gsa.Missions.Quality;

namespace Gsa.Missions.Contracts;

public sealed record CareerProgressionSnapshot(
    int SchemaVersion,
    int TotalXp,
    IReadOnlyDictionary<string, int> ReputationByClient,
    IReadOnlyDictionary<string, int> ReputationByRegion,
    IReadOnlyList<string> UnlockedStoryNodes,
    IReadOnlyList<string> SettledContractIds);

/// <summary>Tracks career XP, non-negative client/region reputation (global non-duplicated), and story unlocks.</summary>
public sealed class CareerProgressionState
{
    private readonly Dictionary<string, int> _reputationByClient = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _reputationByRegion = new(StringComparer.Ordinal);
    private readonly List<string> _unlockedStoryNodes = [];
    private readonly HashSet<string> _unlockedStoryNodeSet = new(StringComparer.Ordinal);
    private readonly HashSet<string> _settledContractIds = new(StringComparer.Ordinal);

    public int TotalXp { get; private set; }

    /// <summary>Global reputation is the sum of net client standings (never double-counting client and region).</summary>
    public int GlobalReputation => _reputationByClient.Values.Sum();

    public IReadOnlyDictionary<string, int> ReputationByClient => _reputationByClient;
    public IReadOnlyDictionary<string, int> ReputationByRegion => _reputationByRegion;
    public IReadOnlyList<string> UnlockedStoryNodes => _unlockedStoryNodes;
    public IReadOnlySet<string> SettledContractIds => _settledContractIds;

    public bool HasStoryUnlock(string nodeId) =>
        !string.IsNullOrWhiteSpace(nodeId) && _unlockedStoryNodeSet.Contains(nodeId);

    public int GetClientReputation(string clientId) =>
        _reputationByClient.TryGetValue(clientId, out var rep) ? rep : 0;

    public int GetRegionReputation(string regionId) =>
        _reputationByRegion.TryGetValue(regionId, out var rep) ? rep : 0;

    public bool ApplySettlement(ContractDefinition definition, PaymentQuote quote)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(quote);
        if (!string.Equals(definition.Id, quote.ContractId, StringComparison.Ordinal))
            throw new ArgumentException("Quote contract ID does not match ContractDefinition.", nameof(quote));

        if (!quote.Eligible && !quote.IsExpired)
            return false;

        if (!_settledContractIds.Add(definition.Id))
            return false;

        if (quote.XpAwarded > 0)
            TotalXp += quote.XpAwarded;

        var currentClient = GetClientReputation(definition.ClientId);
        _reputationByClient[definition.ClientId] = Math.Max(0, currentClient + quote.ReputationDelta);

        var currentRegion = GetRegionReputation(definition.RegionId);
        _reputationByRegion[definition.RegionId] = Math.Max(0, currentRegion + quote.ReputationDelta);

        foreach (var unlock in quote.StoryUnlocks)
        {
            if (!string.IsNullOrWhiteSpace(unlock) && _unlockedStoryNodeSet.Add(unlock))
                _unlockedStoryNodes.Add(unlock);
        }

        return true;
    }

    public CareerProgressionSnapshot ExportSnapshot() =>
        new(
            1,
            TotalXp,
            new Dictionary<string, int>(_reputationByClient, StringComparer.Ordinal),
            new Dictionary<string, int>(_reputationByRegion, StringComparer.Ordinal),
            _unlockedStoryNodes.ToArray(),
            _settledContractIds.OrderBy(id => id, StringComparer.Ordinal).ToArray());

    public static CareerProgressionState FromSnapshot(CareerProgressionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.TotalXp < 0)
            throw new ArgumentOutOfRangeException(nameof(snapshot), "Total XP cannot be negative.");

        var state = new CareerProgressionState
        {
            TotalXp = snapshot.TotalXp,
        };

        foreach (var (clientId, value) in snapshot.ReputationByClient)
            state._reputationByClient[clientId] = Math.Max(0, value);

        foreach (var (regionId, value) in snapshot.ReputationByRegion)
            state._reputationByRegion[regionId] = Math.Max(0, value);

        foreach (var node in snapshot.UnlockedStoryNodes)
        {
            if (!string.IsNullOrWhiteSpace(node) && state._unlockedStoryNodeSet.Add(node))
                state._unlockedStoryNodes.Add(node);
        }

        foreach (var contractId in snapshot.SettledContractIds)
            state._settledContractIds.Add(contractId);

        return state;
    }
}

public sealed record SettlementEvaluationResult(
    QualityReport Report,
    PaymentQuote? Quote,
    ContractCommandResult StateTransition);

public sealed record ContractSettlementResult(
    bool Succeeded,
    bool WasIdempotentReplay,
    ContractCommandStatus ContractStatus,
    ContractState ContractState,
    PaymentQuote Quote,
    LedgerOperationResult? LedgerResult = null,
    string? ReasonKey = null);

/// <summary>Unifies protected batches for CQ, drives rework loops, and settles payments/progression idempotently.</summary>
public static class ContractSettlementCoordinator
{
    public static QualitySubmission BuildMergedSubmission(
        ContractStateMachine contract,
        BatchCustodyStore custodyStore,
        bool fullTexturePersisted = false,
        bool overlapOk = true,
        IEnumerable<string>? corruptSampleErrors = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(custodyStore);

        var definition = contract.Snapshot?.Definition
            ?? throw new InvalidOperationException("Contract must be accepted before building a submission.");

        var protectedInContract = contract.ProtectedBatches
            .Select(b => b.BatchId)
            .ToHashSet(StringComparer.Ordinal);

        var artifacts = custodyStore.GetProtectedArtifactsForContract(contract.ContractId)
            .Where(a => protectedInContract.Contains(a.BatchId))
            .OrderBy(a => a.StartTimeSeconds)
            .ThenBy(a => a.BatchId, StringComparer.Ordinal)
            .ToArray();

        var batchIds = new List<string>(artifacts.Length);
        var samples = new List<Gsa.Core.Sensors.Camera.CameraObservationSample>();
        var seenSamples = new HashSet<string>(StringComparer.Ordinal);
        var footprints = new List<SampleFootprintObservation>();
        var seenFootprints = new HashSet<string>(StringComparer.Ordinal);
        var featuresByTarget = new Dictionary<string, Gsa.Core.Sensors.Camera.TriangulatedFeatureEntry>(StringComparer.Ordinal);
        var checkpointsById = new Dictionary<string, SubmittedCheckpointEstimate>(StringComparer.Ordinal);

        foreach (var artifact in artifacts)
        {
            batchIds.Add(artifact.BatchId);

            foreach (var sampleDto in artifact.Samples)
            {
                if (seenSamples.Add(sampleDto.SampleId))
                    samples.Add(sampleDto.ToDomain());
            }

            foreach (var fp in artifact.Footprints)
            {
                var fpKey = $"{fp.SampleId}\0{fp.SurfaceId}";
                if (seenFootprints.Add(fpKey))
                    footprints.Add(fp);
            }

            foreach (var feat in artifact.Features)
            {
                if (!featuresByTarget.TryGetValue(feat.TargetId, out var existing) || IsBetterFeatureEntry(feat, existing))
                    featuresByTarget[feat.TargetId] = feat;
            }

            foreach (var cp in artifact.Checkpoints)
                checkpointsById[cp.CheckpointId] = cp;
        }

        return new QualitySubmission(
            contract.ContractId,
            batchIds,
            definition.DeliveryFormat,
            1,
            fullTexturePersisted,
            samples,
            footprints,
            featuresByTarget.Values.OrderBy(f => f.TargetId, StringComparer.Ordinal).ToArray(),
            checkpointsById.Values.OrderBy(c => c.CheckpointId, StringComparer.Ordinal).ToArray(),
            overlapOk,
            corruptSampleErrors?.ToArray());
    }

    public static SettlementEvaluationResult EvaluateProtectedBatchesAndUpdateQc(
        ContractStateMachine contract,
        BatchCustodyStore custodyStore,
        EvaluationReference reference,
        double nowSeconds,
        bool fullTexturePersisted = false,
        bool overlapOk = true,
        IEnumerable<string>? corruptSampleErrors = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(custodyStore);
        ArgumentNullException.ThrowIfNull(reference);

        if (contract.Snapshot is null)
            throw new InvalidOperationException("Contract must be accepted before CQ evaluation.");

        var definition = contract.Snapshot.Definition;
        var submission = BuildMergedSubmission(contract, custodyStore, fullTexturePersisted, overlapOk, corruptSampleErrors);
        var report = QualityEvaluator.Evaluate(definition, reference, submission);

        ContractCommandResult transition;
        if (contract.State is ContractState.Coletando or ContractState.Retrabalho)
        {
            var queueResult = contract.MarkReadyToProcess(nowSeconds);
            if (!queueResult.Succeeded)
                return new SettlementEvaluationResult(report, null, queueResult);

            var startResult = contract.StartProcessing(nowSeconds);
            if (!startResult.Succeeded)
                return new SettlementEvaluationResult(report, null, startResult);

            var completeResult = contract.CompleteProcessing(nowSeconds);
            if (!completeResult.Succeeded)
                return new SettlementEvaluationResult(report, null, completeResult);
        }

        if (contract.State != ContractState.EmCQ)
        {
            var rejected = contract.RecordQualityEvaluation(nowSeconds, report.Passed, report.PassedDeliverableIds, report.FailureReasonKeys.FirstOrDefault());
            return new SettlementEvaluationResult(report, null, rejected);
        }

        transition = contract.RecordQualityEvaluation(
            nowSeconds,
            report.Passed,
            report.PassedDeliverableIds,
            report.FailureReasonKeys.FirstOrDefault());

        PaymentQuote? quote = null;
        if (transition.Succeeded)
        {
            var candidate = DeliveryQuoteCalculator.Calculate(definition, report, contract.Snapshot.AcceptedAtSeconds, nowSeconds);
            if (candidate.Eligible)
                quote = candidate;
        }

        return new SettlementEvaluationResult(report, quote, transition);
    }

    public static ContractSettlementResult SettleDelivery(
        ContractStateMachine contract,
        CareerLedger ledger,
        CareerProgressionState progression,
        QualityReport report,
        double nowSeconds,
        DeliveryMode requestedMode = DeliveryMode.Auto,
        int serviceFeeCoords = 0)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(progression);
        ArgumentNullException.ThrowIfNull(report);

        var snapshot = contract.Snapshot
            ?? throw new InvalidOperationException("Contract must be accepted before settlement.");
        var definition = snapshot.Definition;
        var txId = $"settlement:{contract.ContractId}";

        var effectiveTimestamp = contract.State is ContractState.Entregue or ContractState.EntregueParcial
            ? contract.Events.LastOrDefault(e => e.EventType is "ContractDelivered" or "ContractPartiallyDelivered")?.TimestampSeconds ?? nowSeconds
            : nowSeconds;

        var effectiveMode = requestedMode == DeliveryMode.Auto && contract.State == ContractState.EntregueParcial
            ? DeliveryMode.Partial
            : requestedMode;

        var quote = DeliveryQuoteCalculator.Calculate(definition, report, snapshot.AcceptedAtSeconds, effectiveTimestamp, effectiveMode);

        if (contract.State is ContractState.Entregue or ContractState.EntregueParcial)
        {
            var replayDeliver = contract.Deliver(nowSeconds, txId);
            var replayLedger = ledger.RecordContractSettlement(txId, contract.ContractId, effectiveTimestamp, quote, definition.Calm, serviceFeeCoords);
            progression.ApplySettlement(definition, quote);

            var wasReplay = replayDeliver.Status == ContractCommandStatus.IdempotentNoOp
                && replayLedger.Status == LedgerCommandStatus.IdempotentNoOp;
            return new ContractSettlementResult(
                true,
                wasReplay,
                replayDeliver.Status,
                contract.State,
                quote,
                replayLedger,
                quote.ReasonKey);
        }

        if (quote.IsExpired)
        {
            var expiredTransition = contract.AdvanceClock(nowSeconds);
            var expiredLedger = ledger.RecordContractSettlement(txId, contract.ContractId, nowSeconds, quote, definition.Calm, 0);
            progression.ApplySettlement(definition, quote);
            return new ContractSettlementResult(
                false,
                expiredLedger.Status == LedgerCommandStatus.IdempotentNoOp && !expiredTransition.Succeeded,
                expiredTransition.Status,
                contract.State,
                quote,
                expiredLedger,
                quote.ReasonKey);
        }

        if (!quote.Eligible)
        {
            return new ContractSettlementResult(
                false,
                false,
                ContractCommandStatus.RejectedPrecondition,
                contract.State,
                quote,
                null,
                quote.ReasonKey);
        }

        if (contract.State == ContractState.Retrabalho && quote.IsPartial)
        {
            var preparePartial = contract.RequestPartialDelivery(nowSeconds);
            if (!preparePartial.Succeeded)
            {
                return new ContractSettlementResult(
                    false,
                    false,
                    preparePartial.Status,
                    contract.State,
                    quote,
                    null,
                    preparePartial.ReasonKey);
            }
        }

        var deliverResult = contract.Deliver(nowSeconds, txId);
        if (!deliverResult.Succeeded)
        {
            return new ContractSettlementResult(
                false,
                false,
                deliverResult.Status,
                contract.State,
                quote,
                null,
                deliverResult.ReasonKey);
        }

        var ledgerResult = ledger.RecordContractSettlement(txId, contract.ContractId, nowSeconds, quote, definition.Calm, serviceFeeCoords);
        if (!ledgerResult.Succeeded)
        {
            return new ContractSettlementResult(
                false,
                false,
                deliverResult.Status,
                contract.State,
                quote,
                ledgerResult,
                ledgerResult.ReasonKey);
        }

        progression.ApplySettlement(definition, quote);
        return new ContractSettlementResult(
            true,
            false,
            deliverResult.Status,
            contract.State,
            quote,
            ledgerResult,
            null);
    }

    private static bool IsBetterFeatureEntry(
        Gsa.Core.Sensors.Camera.TriangulatedFeatureEntry candidate,
        Gsa.Core.Sensors.Camera.TriangulatedFeatureEntry existing)
    {
        var candResolved = candidate.Confirmed && candidate.Status == Gsa.Core.Sensors.Camera.TriangulationStatus.Resolved && candidate.EstimatedPositionEnu.HasValue;
        var existResolved = existing.Confirmed && existing.Status == Gsa.Core.Sensors.Camera.TriangulationStatus.Resolved && existing.EstimatedPositionEnu.HasValue;
        if (candResolved != existResolved)
            return candResolved;
        return candidate.ContributingObservationCount > existing.ContributingObservationCount;
    }
}
