using Gsa.Missions.Quality;

namespace Gsa.Missions.Economy;

public enum LedgerEntryKind
{
    ContractPayout = 0,
    PurchaseOrService = 1,
    TrafficFine = 2,
    StarterKitRecovery = 3,
}

public enum LedgerCommandStatus
{
    Executed = 0,
    IdempotentNoOp = 1,
    InsufficientFunds = 2,
}

public sealed record LedgerEntry(
    long Sequence,
    string TransactionId,
    string CausingEventId,
    double TimestampSeconds,
    LedgerEntryKind Kind,
    int DeltaBalanceCoords,
    int DeltaDebtCoords,
    int DebtAbatedCoords,
    int ServiceFeeChargedCoords,
    int BalanceAfterCoords,
    int DebtAfterCoords,
    string Category,
    string DescriptionKey);

public sealed record CareerLedgerState(
    int BalanceCoords,
    int FineDebtCoords,
    long NextSequence,
    IReadOnlyList<LedgerEntry> Entries);

public sealed record LedgerOperationResult(
    LedgerCommandStatus Status,
    int BalanceCoords,
    int FineDebtCoords,
    int AppliedDeltaCoords,
    int DebtAbatedCoords,
    int ServiceFeeChargedCoords,
    string? ReasonKey = null,
    LedgerEntry? Entry = null)
{
    public bool Succeeded => Status != LedgerCommandStatus.InsufficientFunds;
}

/// <summary>Integer-Coords idempotent career ledger preventing negative balances and handling fine debt.</summary>
public sealed class CareerLedger
{
    public const int DefaultInitialBalanceCoords = 250;
    public const int LowBalanceSubsidyThresholdCoords = 50;
    public const double MaxDebtAbatementFraction = 0.20;

    private readonly List<LedgerEntry> _entries = [];
    private readonly Dictionary<string, LedgerEntry> _entriesByTxId = new(StringComparer.Ordinal);
    private long _nextSequence;

    public int BalanceCoords { get; private set; }
    public int FineDebtCoords { get; private set; }
    public IReadOnlyList<LedgerEntry> Entries => _entries;

    public CareerLedger(int initialBalanceCoords = DefaultInitialBalanceCoords, int initialDebtCoords = 0)
    {
        if (initialBalanceCoords < 0)
            throw new ArgumentOutOfRangeException(nameof(initialBalanceCoords), "Balance cannot be negative.");
        if (initialDebtCoords < 0)
            throw new ArgumentOutOfRangeException(nameof(initialDebtCoords), "Fine debt cannot be negative.");

        BalanceCoords = initialBalanceCoords;
        FineDebtCoords = initialDebtCoords;
    }

    public CareerLedger(CareerLedgerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.BalanceCoords < 0 || state.FineDebtCoords < 0 || state.NextSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(state), "Ledger state values cannot be negative.");
        ArgumentNullException.ThrowIfNull(state.Entries);

        BalanceCoords = state.BalanceCoords;
        FineDebtCoords = state.FineDebtCoords;
        _nextSequence = state.NextSequence;
        foreach (var entry in state.Entries)
        {
            _entries.Add(entry);
            _entriesByTxId[entry.TransactionId] = entry;
        }
    }

    public CareerLedgerState ExportState() =>
        new(BalanceCoords, FineDebtCoords, _nextSequence, _entries.ToArray());

    public LedgerOperationResult RecordContractSettlement(
        string transactionId,
        string contractId,
        double timestampSeconds,
        PaymentQuote quote,
        bool isCalmContract = false,
        int serviceFeeCoords = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);
        ValidateTime(timestampSeconds);
        ArgumentNullException.ThrowIfNull(quote);
        if (serviceFeeCoords < 0)
            throw new ArgumentOutOfRangeException(nameof(serviceFeeCoords), "Service fee cannot be negative.");

        if (_entriesByTxId.TryGetValue(transactionId, out var existing))
            return IdempotentResult(existing);

        if (!quote.Eligible || quote.NetCoords <= 0)
        {
            var zeroEntry = AppendEntry(
                transactionId,
                contractId,
                timestampSeconds,
                LedgerEntryKind.ContractPayout,
                deltaBalance: 0,
                deltaDebt: 0,
                debtAbated: 0,
                feeCharged: 0,
                category: "contract",
                descriptionKey: quote.ReasonKey ?? "ledger.settlement.ineligible");
            return ExecutedResult(zeroEntry, quote.ReasonKey);
        }

        var subsidized = isCalmContract && BalanceCoords < LowBalanceSubsidyThresholdCoords;
        var chargedFee = subsidized ? 0 : serviceFeeCoords;
        var netAfterFee = quote.NetCoords - chargedFee;
        if (BalanceCoords + netAfterFee < 0)
        {
            return new LedgerOperationResult(
                LedgerCommandStatus.InsufficientFunds,
                BalanceCoords,
                FineDebtCoords,
                0,
                0,
                0,
                "ledger.error.insufficient_funds");
        }

        var debtAbated = netAfterFee > 0 && FineDebtCoords > 0
            ? Math.Min(FineDebtCoords, (int)Math.Floor(netAfterFee * MaxDebtAbatementFraction))
            : 0;

        var balanceDelta = netAfterFee - debtAbated;
        BalanceCoords += balanceDelta;
        FineDebtCoords -= debtAbated;

        var entry = AppendEntry(
            transactionId,
            contractId,
            timestampSeconds,
            LedgerEntryKind.ContractPayout,
            deltaBalance: balanceDelta,
            deltaDebt: -debtAbated,
            debtAbated: debtAbated,
            feeCharged: chargedFee,
            category: "contract",
            descriptionKey: quote.IsPartial ? "ledger.contract.partial" : "ledger.contract.full");
        return ExecutedResult(entry);
    }

    public LedgerOperationResult TryDebitPurchaseOrService(
        string transactionId,
        string causingEventId,
        double timestampSeconds,
        int amountCoords,
        string category,
        string descriptionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(causingEventId);
        ValidateTime(timestampSeconds);
        if (amountCoords < 0)
            throw new ArgumentOutOfRangeException(nameof(amountCoords), "Debit amount cannot be negative.");
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptionKey);

        if (_entriesByTxId.TryGetValue(transactionId, out var existing))
            return IdempotentResult(existing);

        if (amountCoords > BalanceCoords)
        {
            return new LedgerOperationResult(
                LedgerCommandStatus.InsufficientFunds,
                BalanceCoords,
                FineDebtCoords,
                0,
                0,
                0,
                "ledger.error.insufficient_funds");
        }

        BalanceCoords -= amountCoords;
        var entry = AppendEntry(
            transactionId,
            causingEventId,
            timestampSeconds,
            LedgerEntryKind.PurchaseOrService,
            deltaBalance: -amountCoords,
            deltaDebt: 0,
            debtAbated: 0,
            feeCharged: 0,
            category: category,
            descriptionKey: descriptionKey);
        return ExecutedResult(entry);
    }

    public LedgerOperationResult ApplyTrafficFine(
        string transactionId,
        string causingEventId,
        double timestampSeconds,
        int fineCoords,
        string reasonKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(causingEventId);
        ValidateTime(timestampSeconds);
        if (fineCoords <= 0)
            throw new ArgumentOutOfRangeException(nameof(fineCoords), "Fine must be positive.");
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonKey);

        if (_entriesByTxId.TryGetValue(transactionId, out var existing))
            return IdempotentResult(existing);

        var paidNow = Math.Min(BalanceCoords, fineCoords);
        var unpaidDebt = fineCoords - paidNow;
        BalanceCoords -= paidNow;
        FineDebtCoords += unpaidDebt;

        var entry = AppendEntry(
            transactionId,
            causingEventId,
            timestampSeconds,
            LedgerEntryKind.TrafficFine,
            deltaBalance: -paidNow,
            deltaDebt: unpaidDebt,
            debtAbated: 0,
            feeCharged: 0,
            category: "fine",
            descriptionKey: reasonKey);
        return ExecutedResult(entry, reasonKey);
    }

    public LedgerOperationResult RecoverStarterKitFree(string transactionId, double timestampSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ValidateTime(timestampSeconds);

        if (_entriesByTxId.TryGetValue(transactionId, out var existing))
            return IdempotentResult(existing);

        var entry = AppendEntry(
            transactionId,
            "starter_kit_recovery",
            timestampSeconds,
            LedgerEntryKind.StarterKitRecovery,
            deltaBalance: 0,
            deltaDebt: 0,
            debtAbated: 0,
            feeCharged: 0,
            category: "recovery",
            descriptionKey: "ledger.recovery.starter_kit_free");
        return ExecutedResult(entry, "ledger.recovery.starter_kit_free");
    }

    public LedgerOperationResult RecordActivityReward(
        string transactionId,
        string causingEventId,
        double timestampSeconds,
        int rewardCoords,
        string category,
        string descriptionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(causingEventId);
        ValidateTime(timestampSeconds);
        if (rewardCoords < 0)
            throw new ArgumentOutOfRangeException(nameof(rewardCoords), "Activity reward cannot be negative.");
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptionKey);

        if (_entriesByTxId.TryGetValue(transactionId, out var existing))
            return IdempotentResult(existing);

        BalanceCoords += rewardCoords;
        var entry = AppendEntry(
            transactionId,
            causingEventId,
            timestampSeconds,
            LedgerEntryKind.ContractPayout,
            deltaBalance: rewardCoords,
            deltaDebt: 0,
            debtAbated: 0,
            feeCharged: 0,
            category: category,
            descriptionKey: descriptionKey);
        return ExecutedResult(entry, descriptionKey);
    }

    private LedgerEntry AppendEntry(
        string txId,
        string causingEventId,
        double timestampSeconds,
        LedgerEntryKind kind,
        int deltaBalance,
        int deltaDebt,
        int debtAbated,
        int feeCharged,
        string category,
        string descriptionKey)
    {
        var entry = new LedgerEntry(
            _nextSequence++,
            txId,
            causingEventId,
            timestampSeconds,
            kind,
            deltaBalance,
            deltaDebt,
            debtAbated,
            feeCharged,
            BalanceCoords,
            FineDebtCoords,
            category,
            descriptionKey);
        _entries.Add(entry);
        _entriesByTxId.Add(txId, entry);
        return entry;
    }

    private LedgerOperationResult IdempotentResult(LedgerEntry existing) =>
        new(
            LedgerCommandStatus.IdempotentNoOp,
            BalanceCoords,
            FineDebtCoords,
            existing.DeltaBalanceCoords,
            existing.DebtAbatedCoords,
            existing.ServiceFeeChargedCoords,
            existing.DescriptionKey,
            existing);

    private LedgerOperationResult ExecutedResult(LedgerEntry entry, string? reasonKey = null) =>
        new(
            LedgerCommandStatus.Executed,
            BalanceCoords,
            FineDebtCoords,
            entry.DeltaBalanceCoords,
            entry.DebtAbatedCoords,
            entry.ServiceFeeChargedCoords,
            reasonKey,
            entry);

    private static void ValidateTime(double t)
    {
        if (!double.IsFinite(t) || t < 0)
            throw new ArgumentOutOfRangeException(nameof(t), "Timestamp must be finite and non-negative.");
    }
}

