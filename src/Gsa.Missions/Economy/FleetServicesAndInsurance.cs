using Gsa.Core.Mobility;
using Gsa.Missions.Persistence;

namespace Gsa.Missions.Economy;

public enum ServiceKind
{
    RefuelOrCorn = 0,
    VehicleMaintenance = 1,
    InsurancePolicy = 2,
    InsuranceClaim = 3,
    FieldBaseStationSetup = 4,
    TemporaryCustodyStorage = 5,
    ValetParkingGuard = 6,
    SecurityEscort = 7,
    VehicleDisguise = 8,
    ControlledZoneAuthorization = 9,
}

public enum TrafficFineSeverity
{
    Light = 20,
    Medium = 30,
    Heavy = 40,
}

public sealed record ServiceCostQuote(
    ServiceKind Kind,
    bool Eligible,
    int BaseFeeCoords,
    double NegotiationDiscountFraction,
    int FinalFeeCoords,
    string FormulaKey,
    string? RejectionReasonKey = null);

public sealed record ServiceExecutionResult(
    bool Succeeded,
    ServiceKind Kind,
    LedgerOperationResult? LedgerResult,
    ServiceCostQuote Quote,
    string? RejectionReasonKey = null);

public sealed record InsuranceClaimRecord(
    string ClaimId,
    string TransactionId,
    double TimestampSeconds,
    string PlatformId,
    IReadOnlyList<string> InsuredSensorIds,
    int InsuredValueCoords,
    int DeductiblePaidCoords,
    int LostPendingBatchCount,
    bool VehicleRestored,
    bool SensorsRestored,
    bool LostBatchesRestored);

public sealed record ServiceInsuranceSnapshot(
    int SchemaVersion,
    bool InsuranceActive,
    string? InsuredPlatformId,
    IReadOnlyList<string> InsuredSensorIds,
    int InsuredValueCoords,
    int VehicleDamagePoints,
    IReadOnlyList<InsuranceClaimRecord> Claims,
    IReadOnlyList<string> PurchasedServiceReceiptIds);

/// <summary>Implements all M4 fleet operational services, 2%/10% insurance sinistro flow, and fine integration.</summary>
public sealed class FleetServiceAndInsuranceCoordinator
{
    public const int ManualOrBicycleRefuelCoords = 10;
    public const int MotorizedRefuelCoords = 50;
    public const int BaseStationSetupCoords = 20;
    public const int ValetParkingGuardCoords = 10;
    public const int VehicleDisguiseCoords = 200;

    public const double MaintenanceRatePerTenDamage = 0.01;
    public const double InsurancePremiumRate = 0.02;
    public const double InsuranceDeductibleRate = 0.10;
    public const double TemporaryStorageRate = 0.01;
    public const double SecurityEscortRate = 0.10;
    public const double ControlledZoneAuthorizationRate = 0.05;

    private readonly HashSet<string> _receiptIds = new(StringComparer.Ordinal);
    private readonly List<string> _insuredSensorIds = [];
    private readonly List<InsuranceClaimRecord> _claims = [];

    public bool InsuranceActive { get; private set; }
    public string? InsuredPlatformId { get; private set; }
    public IReadOnlyList<string> InsuredSensorIds => _insuredSensorIds;
    public int InsuredValueCoords { get; private set; }
    public int VehicleDamagePoints { get; private set; }
    public IReadOnlyList<InsuranceClaimRecord> Claims => _claims;
    public IReadOnlyList<string> PurchasedServiceReceiptIds => _receiptIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();

    public void RecordVehicleDamage(int damagePoints)
    {
        if (damagePoints < 0)
            throw new ArgumentOutOfRangeException(nameof(damagePoints), "Damage cannot be negative.");
        VehicleDamagePoints = Math.Clamp(VehicleDamagePoints + damagePoints, 0, 100);
    }

    public ServiceCostQuote QuoteService(
        ServiceKind kind,
        PlatformProfile platform,
        int insuredSensorsPriceCoords = 0,
        int contractOrCargoValueCoords = 0,
        int damagePointsOverride = -1,
        FleetProgressionManager? fleet = null,
        bool applyNegotiationDiscount = false)
    {
        ArgumentNullException.ThrowIfNull(platform);
        if (insuredSensorsPriceCoords < 0)
            throw new ArgumentOutOfRangeException(nameof(insuredSensorsPriceCoords), "Insured sensors value cannot be negative.");
        if (contractOrCargoValueCoords < 0)
            throw new ArgumentOutOfRangeException(nameof(contractOrCargoValueCoords), "Contract value cannot be negative.");

        var effectiveDamage = damagePointsOverride >= 0 ? Math.Clamp(damagePointsOverride, 0, 100) : VehicleDamagePoints;
        var totalAssetValue = platform.PriceCoords + insuredSensorsPriceCoords;
        var discountFraction = applyNegotiationDiscount && fleet is not null && kind != ServiceKind.InsuranceClaim
            ? fleet.GetNegotiationFeeDiscountFraction()
            : 0.0;

        return kind switch
        {
            ServiceKind.RefuelOrCorn => BuildQuote(
                kind,
                eligible: true,
                baseFee: platform.Level <= 1 ? ManualOrBicycleRefuelCoords : MotorizedRefuelCoords,
                discountFraction,
                platform.Level <= 1 ? "service.formula.refuel_10" : "service.formula.refuel_50"),

            ServiceKind.VehicleMaintenance => BuildQuote(
                kind,
                eligible: true,
                baseFee: ComputeMaintenanceFee(platform.PriceCoords, effectiveDamage),
                discountFraction,
                "service.formula.maintenance_1pct_per_10_dmg"),

            ServiceKind.InsurancePolicy => BuildQuote(
                kind,
                eligible: totalAssetValue > 0,
                baseFee: RoundPercent(totalAssetValue, InsurancePremiumRate, minWhenPositive: totalAssetValue > 0 ? 1 : 0),
                discountFraction,
                "service.formula.insurance_2pct",
                totalAssetValue > 0 ? null : "service.error.nothing_insurable"),

            ServiceKind.InsuranceClaim => BuildQuote(
                kind,
                eligible: InsuranceActive && InsuredValueCoords > 0,
                baseFee: InsuranceActive ? RoundPercent(InsuredValueCoords, InsuranceDeductibleRate, minWhenPositive: 1) : 0,
                discountFraction: 0.0,
                "service.formula.insurance_deductible_10pct",
                InsuranceActive && InsuredValueCoords > 0 ? null : "service.error.insurance_not_active"),

            ServiceKind.FieldBaseStationSetup => BuildQuote(
                kind,
                eligible: true,
                baseFee: BaseStationSetupCoords,
                discountFraction,
                "service.formula.base_station_20"),

            ServiceKind.TemporaryCustodyStorage => BuildQuote(
                kind,
                eligible: contractOrCargoValueCoords >= 0,
                baseFee: RoundPercent(contractOrCargoValueCoords, TemporaryStorageRate, minWhenPositive: contractOrCargoValueCoords > 0 ? 1 : 0),
                discountFraction,
                "service.formula.storage_1pct"),

            ServiceKind.ValetParkingGuard => BuildQuote(
                kind,
                eligible: true,
                baseFee: ValetParkingGuardCoords,
                discountFraction,
                "service.formula.valet_10"),

            ServiceKind.SecurityEscort => BuildQuote(
                kind,
                eligible: contractOrCargoValueCoords > 0,
                baseFee: RoundPercent(contractOrCargoValueCoords, SecurityEscortRate, minWhenPositive: 1),
                discountFraction,
                "service.formula.escort_10pct",
                contractOrCargoValueCoords > 0 ? null : "service.error.contract_value_required"),

            ServiceKind.VehicleDisguise => BuildQuote(
                kind,
                eligible: true,
                baseFee: VehicleDisguiseCoords,
                discountFraction,
                "service.formula.disguise_200"),

            ServiceKind.ControlledZoneAuthorization => BuildQuote(
                kind,
                eligible: contractOrCargoValueCoords > 0,
                baseFee: RoundPercent(contractOrCargoValueCoords, ControlledZoneAuthorizationRate, minWhenPositive: 1),
                discountFraction,
                "service.formula.authorization_5pct",
                contractOrCargoValueCoords > 0 ? null : "service.error.contract_value_required"),

            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported service kind."),
        };
    }

    public IReadOnlyList<ServiceCostQuote> GetServiceFeeMatrix(
        PlatformProfile platform,
        int insuredSensorsPriceCoords,
        int referenceContractValueCoords)
    {
        return Enum.GetValues<ServiceKind>()
            .Select(kind => QuoteService(kind, platform, insuredSensorsPriceCoords, referenceContractValueCoords))
            .ToArray();
    }

    public ServiceExecutionResult ExecuteService(
        string transactionId,
        double timestampSeconds,
        ServiceKind kind,
        PlatformProfile platform,
        CareerLedger ledger,
        int insuredSensorsPriceCoords = 0,
        IReadOnlyList<string>? insuredSensorIds = null,
        int contractOrCargoValueCoords = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(ledger);

        if (kind == ServiceKind.InsuranceClaim)
            throw new InvalidOperationException("Use SubmitTotalLossClaim for InsuranceClaim execution.");

        var quote = QuoteService(kind, platform, insuredSensorsPriceCoords, contractOrCargoValueCoords);
        if (!quote.Eligible)
            return new ServiceExecutionResult(false, kind, null, quote, quote.RejectionReasonKey);

        var ledgerResult = ledger.TryDebitPurchaseOrService(
            transactionId,
            $"service:{kind}",
            timestampSeconds,
            quote.FinalFeeCoords,
            "service",
            quote.FormulaKey);

        if (!ledgerResult.Succeeded)
            return new ServiceExecutionResult(false, kind, ledgerResult, quote, ledgerResult.ReasonKey);

        if (ledgerResult.Status == LedgerCommandStatus.Executed)
        {
            _receiptIds.Add(transactionId);
            if (kind == ServiceKind.VehicleMaintenance)
                VehicleDamagePoints = 0;
            else if (kind == ServiceKind.InsurancePolicy)
            {
                InsuranceActive = true;
                InsuredPlatformId = platform.Id;
                InsuredValueCoords = platform.PriceCoords + insuredSensorsPriceCoords;
                _insuredSensorIds.Clear();
                if (insuredSensorIds is not null)
                    _insuredSensorIds.AddRange(insuredSensorIds.Where(s => !string.IsNullOrWhiteSpace(s)));
            }
        }

        return new ServiceExecutionResult(true, kind, ledgerResult, quote);
    }

    public ServiceExecutionResult SubmitTotalLossClaim(
        string claimId,
        string transactionId,
        double timestampSeconds,
        PlatformProfile activePlatform,
        CareerLedger ledger,
        BatchCustodyStore? custodyStore,
        out InsuranceClaimRecord? claimRecord)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claimId);
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentNullException.ThrowIfNull(activePlatform);
        ArgumentNullException.ThrowIfNull(ledger);

        claimRecord = null;
        var quote = QuoteService(ServiceKind.InsuranceClaim, activePlatform);
        if (!quote.Eligible)
            return new ServiceExecutionResult(false, ServiceKind.InsuranceClaim, null, quote, quote.RejectionReasonKey);

        var existing = _claims.FirstOrDefault(c => string.Equals(c.ClaimId, claimId, StringComparison.Ordinal) ||
                                                   string.Equals(c.TransactionId, transactionId, StringComparison.Ordinal));
        if (existing is not null)
        {
            var idempotentLedger = ledger.TryDebitPurchaseOrService(
                existing.TransactionId,
                $"insurance_claim:{existing.ClaimId}",
                existing.TimestampSeconds,
                existing.DeductiblePaidCoords,
                "insurance_claim",
                quote.FormulaKey);
            claimRecord = existing;
            return new ServiceExecutionResult(true, ServiceKind.InsuranceClaim, idempotentLedger, quote);
        }

        var ledgerResult = ledger.TryDebitPurchaseOrService(
            transactionId,
            $"insurance_claim:{claimId}",
            timestampSeconds,
            quote.FinalFeeCoords,
            "insurance_claim",
            quote.FormulaKey);

        if (!ledgerResult.Succeeded)
            return new ServiceExecutionResult(false, ServiceKind.InsuranceClaim, ledgerResult, quote, ledgerResult.ReasonKey);

        var lostBatchCount = custodyStore?.HandleVehicleLoss(timestampSeconds) ?? 0;
        VehicleDamagePoints = 0;

        claimRecord = new InsuranceClaimRecord(
            ClaimId: claimId,
            TransactionId: transactionId,
            TimestampSeconds: timestampSeconds,
            PlatformId: InsuredPlatformId ?? activePlatform.Id,
            InsuredSensorIds: _insuredSensorIds.ToArray(),
            InsuredValueCoords: InsuredValueCoords,
            DeductiblePaidCoords: quote.FinalFeeCoords,
            LostPendingBatchCount: lostBatchCount,
            VehicleRestored: true,
            SensorsRestored: true,
            LostBatchesRestored: false);

        _claims.Add(claimRecord);
        _receiptIds.Add(transactionId);
        return new ServiceExecutionResult(true, ServiceKind.InsuranceClaim, ledgerResult, quote);
    }

    public static LedgerOperationResult ApplyTrafficFine(
        string transactionId,
        string causingEventId,
        double timestampSeconds,
        TrafficFineSeverity severity,
        CareerLedger ledger,
        string? reasonKey = null)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        var key = reasonKey ?? severity switch
        {
            TrafficFineSeverity.Light => "fine.traffic.light",
            TrafficFineSeverity.Medium => "fine.traffic.medium",
            TrafficFineSeverity.Heavy => "fine.traffic.heavy",
            _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unknown traffic fine severity."),
        };

        return ledger.ApplyTrafficFine(
            transactionId,
            causingEventId,
            timestampSeconds,
            (int)severity,
            key);
    }

    public ServiceInsuranceSnapshot ExportSnapshot() =>
        new(
            SchemaVersion: 1,
            InsuranceActive: InsuranceActive,
            InsuredPlatformId: InsuredPlatformId,
            InsuredSensorIds: _insuredSensorIds.ToArray(),
            InsuredValueCoords: InsuredValueCoords,
            VehicleDamagePoints: VehicleDamagePoints,
            Claims: _claims.ToArray(),
            PurchasedServiceReceiptIds: PurchasedServiceReceiptIds);

    public static FleetServiceAndInsuranceCoordinator FromSnapshot(ServiceInsuranceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var coordinator = new FleetServiceAndInsuranceCoordinator
        {
            InsuranceActive = snapshot.InsuranceActive,
            InsuredPlatformId = snapshot.InsuredPlatformId,
            InsuredValueCoords = Math.Max(0, snapshot.InsuredValueCoords),
            VehicleDamagePoints = Math.Clamp(snapshot.VehicleDamagePoints, 0, 100),
        };
        coordinator._insuredSensorIds.AddRange(snapshot.InsuredSensorIds);
        coordinator._claims.AddRange(snapshot.Claims);
        foreach (var receiptId in snapshot.PurchasedServiceReceiptIds)
            coordinator._receiptIds.Add(receiptId);
        return coordinator;
    }

    private static int ComputeMaintenanceFee(int platformPriceCoords, int damagePoints)
    {
        if (platformPriceCoords <= 0 || damagePoints <= 0)
            return 0;

        var stepsOfTen = (int)Math.Ceiling(damagePoints / 10.0);
        return RoundPercent(platformPriceCoords, MaintenanceRatePerTenDamage * stepsOfTen, minWhenPositive: 1);
    }

    private static int RoundPercent(int valueCoords, double rate, int minWhenPositive)
    {
        if (valueCoords <= 0 || rate <= 0)
            return 0;

        var computed = (int)Math.Round(valueCoords * rate, MidpointRounding.AwayFromZero);
        return Math.Max(minWhenPositive, computed);
    }

    private static ServiceCostQuote BuildQuote(
        ServiceKind kind,
        bool eligible,
        int baseFee,
        double discountFraction,
        string formulaKey,
        string? rejectionReasonKey = null)
    {
        var discount = (int)Math.Round(baseFee * discountFraction, MidpointRounding.AwayFromZero);
        var finalFee = Math.Max(0, baseFee - discount);
        return new ServiceCostQuote(
            Kind: kind,
            Eligible: eligible,
            BaseFeeCoords: baseFee,
            NegotiationDiscountFraction: discountFraction,
            FinalFeeCoords: finalFee,
            FormulaKey: formulaKey,
            RejectionReasonKey: rejectionReasonKey);
    }
}

