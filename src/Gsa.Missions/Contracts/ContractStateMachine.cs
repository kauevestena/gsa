using System.Text.Json;
using System.Text.Json.Serialization;
using Gsa.Missions.Content;

namespace Gsa.Missions.Contracts;

public enum ContractState
{
    Disponivel = 0,
    Aceito = 1,
    Coletando = 2,
    ProntoProcessar = 3,
    Processando = 4,
    EmCQ = 5,
    Retrabalho = 6,
    ProntoEntregar = 7,
    Entregue = 8,
    EntregueParcial = 9,
    Expirado = 10,
    Abandonado = 11,
}

public enum ContractCommandStatus
{
    Executed = 0,
    RejectedInvalidState = 1,
    RejectedPrecondition = 2,
    RejectedDuplicateId = 3,
    RejectedExpired = 4,
    IdempotentNoOp = 5,
}

public enum BatchCustodyState
{
    PendingField = 0,
    ProtectedOffloaded = 1,
    Discarded = 2,
}

public enum FieldIncidentKind
{
    VehicleDestroyed = 0,
    GimbalinaFled = 1,
}

public sealed record ContractDeadlineSpec(
    double DueAfterSeconds,
    double ExpiresAfterSeconds,
    double LatePenaltyPerHour,
    double LatePenaltyCap);

public sealed record ContractRewardSpec(
    int BaseCoords,
    int BaseXp,
    int Reputation,
    IReadOnlyList<string> StoryUnlocks);

[JsonPolymorphic]
[JsonDerivedType(typeof(ThresholdRequirementSpec), "threshold")]
[JsonDerivedType(typeof(PredicateRequirementSpec), "predicate")]
public abstract record ContractRequirementSpec(string Kind, string Metric);

public sealed record ThresholdRequirementSpec(
    string Metric,
    string Unit,
    string Direction,
    double Limit,
    double Excellent,
    string? SurfaceId = null,
    double? CellSizeMetres = null,
    double? TargetDensityPointsPerM2 = null) : ContractRequirementSpec("threshold", Metric)
{
    public bool IsBinaryLimit => Direction == "min" && Math.Abs(Limit - 1.0) <= 1e-12;
}

public sealed record PredicateRequirementSpec(
    string Metric,
    bool Expected) : ContractRequirementSpec("predicate", Metric);

public sealed record ContractDeliverableSpec(
    string Id,
    double Weight,
    string AreaId,
    IReadOnlyList<string> RequiredFeatureIds,
    IReadOnlyList<ContractRequirementSpec> Requirements);

public sealed record ContractPartialPolicySpec(
    string Mode,
    IReadOnlyList<ContractDeliverableSpec> Deliverables);

/// <summary>Validated immutable contract definition parsed from schema_version 1 JSON.</summary>
public sealed record ContractDefinition(
    int SchemaVersion,
    string Id,
    string TitleKey,
    string ClientId,
    string RegionId,
    string AreaId,
    string Product,
    IReadOnlyList<string> RequiredFeatureIds,
    IReadOnlyList<ContractRequirementSpec> Requirements,
    IReadOnlyList<string> RequiredCapabilities,
    string Risk,
    bool Calm,
    ContractDeadlineSpec Deadline,
    ContractRewardSpec Reward,
    ContractPartialPolicySpec PartialPolicy,
    string DeliveryFormat,
    IReadOnlyList<string> StoryPrerequisites)
{
    public static ContractDefinition Parse(string json, string sourceName = "contract.json")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        return FromJsonElement(document.RootElement, sourceName);
    }

    public static ContractDefinition FromJsonElement(JsonElement root, string sourceName = "contract.json")
    {
        var diagnostics = ContractValidator.Validate(root, sourceName);
        if (diagnostics.Count > 0)
        {
            var summary = string.Join("; ", diagnostics.Select(d => $"{d.Field}: {d.Reason}"));
            throw new FormatException($"Contract '{sourceName}' failed validation: {summary}");
        }

        var id = root.GetProperty("id").GetString()!;
        var titleKey = root.GetProperty("title_key").GetString()!;
        var clientId = root.GetProperty("client_id").GetString()!;
        var regionId = root.GetProperty("region_id").GetString()!;
        var areaId = root.GetProperty("area_id").GetString()!;
        var product = root.GetProperty("product").GetString()!;
        var requiredFeatures = ReadStringArray(root.GetProperty("required_feature_ids"));
        var requirements = ReadRequirements(root.GetProperty("requirements"));
        var capabilities = ReadStringArray(root.GetProperty("required_capabilities"));
        var risk = root.GetProperty("risk").GetString()!;
        var calm = root.GetProperty("calm").GetBoolean();
        var deliveryFormat = root.GetProperty("delivery_format").GetString()!;
        var storyPrereqs = ReadStringArray(root.GetProperty("story_prerequisites"));

        var deadlineEl = root.GetProperty("deadline");
        var deadline = new ContractDeadlineSpec(
            deadlineEl.GetProperty("due_after_s").GetDouble(),
            deadlineEl.GetProperty("expires_after_s").GetDouble(),
            deadlineEl.GetProperty("late_penalty_per_hour").GetDouble(),
            deadlineEl.GetProperty("late_penalty_cap").GetDouble());

        var rewardEl = root.GetProperty("reward");
        var reward = new ContractRewardSpec(
            rewardEl.GetProperty("base_coords").GetInt32(),
            rewardEl.GetProperty("base_xp").GetInt32(),
            rewardEl.GetProperty("reputation").GetInt32(),
            ReadStringArray(rewardEl.GetProperty("story_unlocks")));

        var partialEl = root.GetProperty("partial_policy");
        var partialMode = partialEl.GetProperty("mode").GetString()!;
        var deliverables = partialMode == "deliverables"
            ? ReadDeliverables(partialEl.GetProperty("deliverables"))
            : [];

        return new ContractDefinition(
            1,
            id,
            titleKey,
            clientId,
            regionId,
            areaId,
            product,
            requiredFeatures,
            requirements,
            capabilities,
            risk,
            calm,
            deadline,
            reward,
            new ContractPartialPolicySpec(partialMode, deliverables),
            deliveryFormat,
            storyPrereqs);
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement array) =>
        Array.AsReadOnly(array.EnumerateArray().Select(e => e.GetString()!).ToArray());

    private static IReadOnlyList<ContractRequirementSpec> ReadRequirements(JsonElement array)
    {
        var list = new List<ContractRequirementSpec>();
        foreach (var item in array.EnumerateArray())
        {
            var kind = item.GetProperty("kind").GetString()!;
            var metric = item.GetProperty("metric").GetString()!;
            if (kind == "predicate")
            {
                list.Add(new PredicateRequirementSpec(metric, item.GetProperty("expected").GetBoolean()));
            }
            else
            {
                var unit = item.GetProperty("unit").GetString()!;
                var direction = item.GetProperty("direction").GetString()!;
                var limit = item.GetProperty("limit").GetDouble();
                var excellent = item.GetProperty("excellent").GetDouble();
                string? surfaceId = item.TryGetProperty("surface_id", out var surfEl) ? surfEl.GetString() : null;
                double? cellSize = item.TryGetProperty("cell_size_m", out var cellEl) ? cellEl.GetDouble() : null;
                double? targetDensity = item.TryGetProperty("target_density_points_per_m2", out var densEl) ? densEl.GetDouble() : null;
                list.Add(new ThresholdRequirementSpec(metric, unit, direction, limit, excellent, surfaceId, cellSize, targetDensity));
            }
        }
        return Array.AsReadOnly(list.ToArray());
    }

    private static IReadOnlyList<ContractDeliverableSpec> ReadDeliverables(JsonElement array)
    {
        var list = new List<ContractDeliverableSpec>();
        foreach (var item in array.EnumerateArray())
        {
            list.Add(new ContractDeliverableSpec(
                item.GetProperty("id").GetString()!,
                item.GetProperty("weight").GetDouble(),
                item.GetProperty("area_id").GetString()!,
                ReadStringArray(item.GetProperty("required_feature_ids")),
                ReadRequirements(item.GetProperty("requirements"))));
        }
        return Array.AsReadOnly(list.ToArray());
    }
}

/// <summary>Immutable snapshot captured when a contract enters Aceito; insulated from subsequent catalog reloads.</summary>
public sealed record ContractRequirementsSnapshot(
    string ContractId,
    double AcceptedAtSeconds,
    double DueAtSeconds,
    double ExpiresAtSeconds,
    ContractDefinition Definition);

public sealed record CustodiedBatchRecord(
    string BatchId,
    double RecordedAtSeconds,
    double? ProtectedAtSeconds,
    BatchCustodyState Custody,
    string? Note = null);

public sealed record ContractDomainEvent(
    long Sequence,
    double TimestampSeconds,
    string ContractId,
    string EventType,
    ContractState PreviousState,
    ContractState NextState,
    string? BatchId = null,
    string? ReasonKey = null);

public sealed record ContractCommandResult(
    ContractCommandStatus Status,
    ContractState State,
    IReadOnlyList<ContractDomainEvent> EmittedEvents,
    string? ReasonKey = null)
{
    public bool Succeeded => Status == ContractCommandStatus.Executed || Status == ContractCommandStatus.IdempotentNoOp;
}

public sealed record ContractInstanceSnapshot(
    int SchemaVersion,
    ContractDefinition CatalogDefinition,
    ContractState State,
    ContractRequirementsSnapshot? Snapshot,
    bool CanDeliverFull,
    bool CanDeliverPartial,
    IReadOnlyList<string> ApprovedPartialDeliverableIds,
    IReadOnlyList<CustodiedBatchRecord> Batches,
    IReadOnlyList<ContractDomainEvent> Events,
    IReadOnlyList<string> DeliveredIdempotencyKeys,
    long NextEventSequence);

/// <summary>Deterministic state machine governing the lifecycle of a single contract instance.</summary>
public sealed class ContractStateMachine
{
    private readonly ContractDefinition _catalogDefinition;
    private readonly List<CustodiedBatchRecord> _batches = [];
    private readonly List<ContractDomainEvent> _eventLog = [];
    private readonly HashSet<string> _deliveredIdempotencyKeys = new(StringComparer.Ordinal);
    private readonly List<string> _approvedPartialDeliverableIds = [];
    private long _nextEventSeq;

    public string ContractId => _catalogDefinition.Id;
    public ContractState State { get; private set; } = ContractState.Disponivel;
    public ContractRequirementsSnapshot? Snapshot { get; private set; }
    public bool CanDeliverFull { get; private set; }
    public bool CanDeliverPartial { get; private set; }
    public IReadOnlyList<string> ApprovedPartialDeliverableIds => _approvedPartialDeliverableIds;
    public IReadOnlyList<CustodiedBatchRecord> PendingFieldBatches =>
        _batches.Where(b => b.Custody == BatchCustodyState.PendingField).ToArray();
    public IReadOnlyList<CustodiedBatchRecord> ProtectedBatches =>
        _batches.Where(b => b.Custody == BatchCustodyState.ProtectedOffloaded).ToArray();
    public IReadOnlyList<CustodiedBatchRecord> DiscardedBatches =>
        _batches.Where(b => b.Custody == BatchCustodyState.Discarded).ToArray();
    public IReadOnlyList<ContractDomainEvent> Events => _eventLog;
    public bool IsActiveState => IsActive(State);
    public bool IsTerminalState => !IsActive(State) && State != ContractState.Disponivel;

    public ContractStateMachine(ContractDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _catalogDefinition = definition;
    }

    public ContractInstanceSnapshot ExportSnapshot() =>
        new(
            1,
            _catalogDefinition,
            State,
            Snapshot,
            CanDeliverFull,
            CanDeliverPartial,
            _approvedPartialDeliverableIds.ToArray(),
            _batches.ToArray(),
            _eventLog.ToArray(),
            _deliveredIdempotencyKeys.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
            _nextEventSeq);

    public static ContractStateMachine FromSnapshot(ContractInstanceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var machine = new ContractStateMachine(snapshot.CatalogDefinition)
        {
            State = snapshot.State,
            Snapshot = snapshot.Snapshot,
            CanDeliverFull = snapshot.CanDeliverFull,
            CanDeliverPartial = snapshot.CanDeliverPartial,
            _nextEventSeq = snapshot.NextEventSequence,
        };
        machine._approvedPartialDeliverableIds.AddRange(snapshot.ApprovedPartialDeliverableIds);
        machine._batches.AddRange(snapshot.Batches);
        machine._eventLog.AddRange(snapshot.Events);
        foreach (var key in snapshot.DeliveredIdempotencyKeys)
            machine._deliveredIdempotencyKeys.Add(key);
        return machine;
    }

    public static bool IsActive(ContractState state) => state is
        ContractState.Aceito or
        ContractState.Coletando or
        ContractState.ProntoProcessar or
        ContractState.Processando or
        ContractState.EmCQ or
        ContractState.Retrabalho or
        ContractState.ProntoEntregar;

    public ContractCommandResult AdvanceClock(double nowSeconds)
    {
        ValidateTime(nowSeconds);
        if (Snapshot is not null && IsActive(State) && nowSeconds > Snapshot.ExpiresAtSeconds)
        {
            var prev = State;
            DiscardPendingBatches();
            State = ContractState.Expirado;
            var ev = Emit(nowSeconds, "ContractExpired", prev, State, reasonKey: "contract.expired");
            return new ContractCommandResult(ContractCommandStatus.Executed, State, [ev], "contract.expired");
        }
        return new ContractCommandResult(ContractCommandStatus.IdempotentNoOp, State, []);
    }

    public ContractCommandResult Accept(
        double nowSeconds,
        IReadOnlySet<string>? availableCapabilities = null,
        IReadOnlySet<string>? completedStoryIds = null)
    {
        ValidateTime(nowSeconds);
        if (State != ContractState.Disponivel)
            return Reject(ContractCommandStatus.RejectedInvalidState, "contract.error.invalid_state");

        if (availableCapabilities is not null)
        {
            foreach (var cap in _catalogDefinition.RequiredCapabilities)
            {
                if (!availableCapabilities.Contains(cap))
                    return Reject(ContractCommandStatus.RejectedPrecondition, $"contract.error.missing_capability.{cap}");
            }
        }

        if (completedStoryIds is not null)
        {
            foreach (var prereq in _catalogDefinition.StoryPrerequisites)
            {
                if (!completedStoryIds.Contains(prereq))
                    return Reject(ContractCommandStatus.RejectedPrecondition, $"contract.error.missing_story.{prereq}");
            }
        }

        Snapshot = new ContractRequirementsSnapshot(
            _catalogDefinition.Id,
            nowSeconds,
            nowSeconds + _catalogDefinition.Deadline.DueAfterSeconds,
            nowSeconds + _catalogDefinition.Deadline.ExpiresAfterSeconds,
            _catalogDefinition);

        var prev = State;
        State = ContractState.Aceito;
        var ev = Emit(nowSeconds, "ContractAccepted", prev, State);
        return new ContractCommandResult(ContractCommandStatus.Executed, State, [ev]);
    }

    public ContractCommandResult RecordPendingBatch(double nowSeconds, string batchId, string? note = null)
    {
        ValidateTime(nowSeconds);
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        if (CheckExpired(nowSeconds) is { } expired)
            return expired;

        if (State is not (ContractState.Aceito or ContractState.Coletando or ContractState.Retrabalho))
            return Reject(ContractCommandStatus.RejectedInvalidState, "contract.error.cannot_collect_in_state");

        if (_batches.Any(b => string.Equals(b.BatchId, batchId, StringComparison.Ordinal)))
            return Reject(ContractCommandStatus.RejectedDuplicateId, "contract.error.duplicate_batch");

        var prev = State;
        _batches.Add(new CustodiedBatchRecord(batchId, nowSeconds, null, BatchCustodyState.PendingField, note));
        if (State == ContractState.Aceito)
            State = ContractState.Coletando;

        var ev = Emit(nowSeconds, "BatchRecordedPending", prev, State, batchId: batchId);
        return new ContractCommandResult(ContractCommandStatus.Executed, State, [ev]);
    }

    public ContractCommandResult ProtectBatchAtUnloadPoint(double nowSeconds, string batchId)
    {
        ValidateTime(nowSeconds);
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        if (CheckExpired(nowSeconds) is { } expired)
            return expired;

        if (State is not (ContractState.Coletando or ContractState.Retrabalho))
            return Reject(ContractCommandStatus.RejectedInvalidState, "contract.error.cannot_unload_in_state");

        var index = _batches.FindIndex(b => string.Equals(b.BatchId, batchId, StringComparison.Ordinal));
        if (index < 0)
            return Reject(ContractCommandStatus.RejectedPrecondition, "contract.error.batch_not_found");

        var record = _batches[index];
        if (record.Custody == BatchCustodyState.ProtectedOffloaded)
            return new ContractCommandResult(ContractCommandStatus.IdempotentNoOp, State, []);
        if (record.Custody != BatchCustodyState.PendingField)
            return Reject(ContractCommandStatus.RejectedInvalidState, "contract.error.batch_discarded");

        _batches[index] = record with { Custody = BatchCustodyState.ProtectedOffloaded, ProtectedAtSeconds = nowSeconds };
        var ev = Emit(nowSeconds, "BatchProtected", State, State, batchId: batchId);
        return new ContractCommandResult(ContractCommandStatus.Executed, State, [ev]);
    }

    public ContractCommandResult HandleFieldIncident(double nowSeconds, FieldIncidentKind incident)
    {
        ValidateTime(nowSeconds);
        if (CheckExpired(nowSeconds) is { } expired)
            return expired;

        if (!IsActive(State))
            return Reject(ContractCommandStatus.RejectedInvalidState, "contract.error.contract_not_active");

        var events = new List<ContractDomainEvent>();
        for (var i = 0; i < _batches.Count; i++)
        {
            if (_batches[i].Custody == BatchCustodyState.PendingField)
            {
                var id = _batches[i].BatchId;
                _batches[i] = _batches[i] with { Custody = BatchCustodyState.Discarded };
                events.Add(Emit(nowSeconds, "PendingBatchDiscarded", State, State, batchId: id, reasonKey: $"incident.{incident}"));
            }
        }

        return new ContractCommandResult(ContractCommandStatus.Executed, State, events);
    }

    public ContractCommandResult MarkReadyToProcess(double nowSeconds)
    {
        ValidateTime(nowSeconds);
        if (CheckExpired(nowSeconds) is { } expired)
            return expired;

        if (State is not (ContractState.Coletando or ContractState.Retrabalho))
            return Reject(ContractCommandStatus.RejectedInvalidState, "contract.error.cannot_queue_processing");

        if (_batches.All(b => b.Custody != BatchCustodyState.ProtectedOffloaded))
            return Reject(ContractCommandStatus.RejectedPrecondition, "contract.error.no_protected_batches");

        var prev = State;
        State = ContractState.ProntoProcessar;
        var ev = Emit(nowSeconds, "ReadyForProcessing", prev, State);
        return new ContractCommandResult(ContractCommandStatus.Executed, State, [ev]);
    }

    public ContractCommandResult StartProcessing(double nowSeconds)
    {
        ValidateTime(nowSeconds);
        if (CheckExpired(nowSeconds) is { } expired)
            return expired;

        if (State != ContractState.ProntoProcessar)
            return Reject(ContractCommandStatus.RejectedInvalidState, "contract.error.not_ready_to_process");

        var prev = State;
        State = ContractState.Processando;
        var ev = Emit(nowSeconds, "ProcessingStarted", prev, State);
        return new ContractCommandResult(ContractCommandStatus.Executed, State, [ev]);
    }

    public ContractCommandResult CompleteProcessing(double nowSeconds)
    {
        ValidateTime(nowSeconds);
        if (CheckExpired(nowSeconds) is { } expired)
            return expired;

        if (State != ContractState.Processando)
            return Reject(ContractCommandStatus.RejectedInvalidState, "contract.error.not_processing");

        var prev = State;
        State = ContractState.EmCQ;
        var ev = Emit(nowSeconds, "ProcessingCompletedEnteredQc", prev, State);
        return new ContractCommandResult(ContractCommandStatus.Executed, State, [ev]);
    }

    public ContractCommandResult CancelProcessing(double nowSeconds, string? reasonKey = null)
    {
        ValidateTime(nowSeconds);
        if (CheckExpired(nowSeconds) is { } expired)
            return expired;

        if (State != ContractState.Processando)
            return Reject(ContractCommandStatus.RejectedInvalidState, "contract.error.not_processing");

        var prev = State;
        State = _eventLog.Any(e => string.Equals(e.EventType, "QualityRejectedRework", StringComparison.Ordinal))
            ? ContractState.Retrabalho
            : ContractState.Coletando;
        var ev = Emit(nowSeconds, "ProcessingCancelled", prev, State, reasonKey: reasonKey ?? "contract.processing.cancelled");
        return new ContractCommandResult(ContractCommandStatus.Executed, State, [ev]);
    }

    public ContractCommandResult RecordQualityEvaluation(
        double nowSeconds,
        bool fullPassed,
        IEnumerable<string>? passedDeliverableIds = null,
        string? primaryFailureKey = null)
    {
        ValidateTime(nowSeconds);
        if (CheckExpired(nowSeconds) is { } expired)
            return expired;

        if (State != ContractState.EmCQ)
            return Reject(ContractCommandStatus.RejectedInvalidState, "contract.error.not_in_qc");

        _approvedPartialDeliverableIds.Clear();
        if (passedDeliverableIds is not null)
        {
            var validIds = Snapshot!.Definition.PartialPolicy.Deliverables.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var deliverableId in passedDeliverableIds.Distinct(StringComparer.Ordinal))
            {
                if (!validIds.Contains(deliverableId))
                    return Reject(ContractCommandStatus.RejectedPrecondition, $"contract.error.unknown_deliverable.{deliverableId}");
                _approvedPartialDeliverableIds.Add(deliverableId);
            }
        }

        var prev = State;
        if (fullPassed)
        {
            CanDeliverFull = true;
            CanDeliverPartial = false;
            State = ContractState.ProntoEntregar;
            var ev = Emit(nowSeconds, "QualityApprovedFull", prev, State);
            return new ContractCommandResult(ContractCommandStatus.Executed, State, [ev]);
        }

        CanDeliverFull = false;
        CanDeliverPartial = Snapshot!.Definition.PartialPolicy.Mode == "deliverables" && _approvedPartialDeliverableIds.Count > 0;
        State = ContractState.Retrabalho;
        var reworkEv = Emit(nowSeconds, "QualityRejectedRework", prev, State, reasonKey: primaryFailureKey);
        return new ContractCommandResult(ContractCommandStatus.Executed, State, [reworkEv]);
    }

    public ContractCommandResult RequestPartialDelivery(double nowSeconds)
    {
        ValidateTime(nowSeconds);
        if (CheckExpired(nowSeconds) is { } expired)
            return expired;

        if (State != ContractState.Retrabalho)
            return Reject(ContractCommandStatus.RejectedInvalidState, "contract.error.partial_requires_rework_state");

        if (!CanDeliverPartial || Snapshot!.Definition.PartialPolicy.Mode != "deliverables" || _approvedPartialDeliverableIds.Count == 0)
            return Reject(ContractCommandStatus.RejectedPrecondition, "contract.error.partial_not_eligible");

        var prev = State;
        State = ContractState.ProntoEntregar;
        var ev = Emit(nowSeconds, "PartialDeliveryPrepared", prev, State);
        return new ContractCommandResult(ContractCommandStatus.Executed, State, [ev]);
    }

    public ContractCommandResult Deliver(double nowSeconds, string? idempotencyKey = null)
    {
        ValidateTime(nowSeconds);
        if (idempotencyKey is not null && _deliveredIdempotencyKeys.Contains(idempotencyKey))
            return new ContractCommandResult(ContractCommandStatus.IdempotentNoOp, State, []);

        if (CheckExpired(nowSeconds) is { } expired)
            return expired;

        if (State != ContractState.ProntoEntregar)
            return Reject(ContractCommandStatus.RejectedInvalidState, "contract.error.not_ready_to_deliver");

        if (!CanDeliverFull && !CanDeliverPartial)
            return Reject(ContractCommandStatus.RejectedPrecondition, "contract.error.no_approved_delivery");

        if (idempotencyKey is not null)
            _deliveredIdempotencyKeys.Add(idempotencyKey);

        var prev = State;
        State = CanDeliverFull ? ContractState.Entregue : ContractState.EntregueParcial;
        var ev = Emit(nowSeconds, CanDeliverFull ? "ContractDelivered" : "ContractPartiallyDelivered", prev, State);
        return new ContractCommandResult(ContractCommandStatus.Executed, State, [ev]);
    }

    public ContractCommandResult Abandon(double nowSeconds, string? reasonKey = null)
    {
        ValidateTime(nowSeconds);
        if (CheckExpired(nowSeconds) is { } expired)
            return expired;

        if (!IsActive(State))
            return Reject(ContractCommandStatus.RejectedInvalidState, "contract.error.cannot_abandon_state");

        var prev = State;
        DiscardPendingBatches();
        State = ContractState.Abandonado;
        var ev = Emit(nowSeconds, "ContractAbandoned", prev, State, reasonKey: reasonKey ?? "contract.abandoned");
        return new ContractCommandResult(ContractCommandStatus.Executed, State, [ev]);
    }

    private ContractCommandResult? CheckExpired(double nowSeconds)
    {
        if (Snapshot is not null && IsActive(State) && nowSeconds > Snapshot.ExpiresAtSeconds)
            return AdvanceClock(nowSeconds);
        return State == ContractState.Expirado
            ? Reject(ContractCommandStatus.RejectedExpired, "contract.error.already_expired")
            : null;
    }

    private void DiscardPendingBatches()
    {
        for (var i = 0; i < _batches.Count; i++)
        {
            if (_batches[i].Custody == BatchCustodyState.PendingField)
                _batches[i] = _batches[i] with { Custody = BatchCustodyState.Discarded };
        }
    }

    private ContractDomainEvent Emit(
        double nowSeconds,
        string eventType,
        ContractState prev,
        ContractState next,
        string? batchId = null,
        string? reasonKey = null)
    {
        var ev = new ContractDomainEvent(_nextEventSeq++, nowSeconds, ContractId, eventType, prev, next, batchId, reasonKey);
        _eventLog.Add(ev);
        return ev;
    }

    private ContractCommandResult Reject(ContractCommandStatus status, string reasonKey) =>
        new(status, State, [], reasonKey);

    private static void ValidateTime(double nowSeconds)
    {
        if (!double.IsFinite(nowSeconds) || nowSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(nowSeconds), "Simulation time must be finite and non-negative.");
    }
}
