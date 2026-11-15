using Gsa.Core.Sensors.Gnss;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;

namespace Gsa.Missions.Office;

public sealed record DgnssRentalOffer(
    string MarkId,
    string ContractId,
    int BasePriceCoords,
    int EffectiveFeeCoords,
    double OperatingWindowStartSeconds,
    double OperatingWindowEndSeconds,
    double MinimumBaseOpenness);

public sealed record DgnssRentalResult(
    bool Succeeded,
    DgnssRentalOffer Offer,
    SurveyMarkBaseStationSpec? BaseStationSpec,
    LedgerOperationResult LedgerResult,
    string? ReasonKey = null);

/// <summary>Contracts a DGNSS base station log over a validated mark for 20 Coords before post-processing.</summary>
public static class DgnssRentalService
{
    public const int BaseRentalPriceCoords = 20;

    public static DgnssRentalOffer QuoteRental(
        ContractDefinition contract,
        SurveyMarkBaseStationSpec markSpec,
        CareerLedger ledger,
        bool applyCalmLowBalanceSubsidy = true)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(markSpec);
        ArgumentNullException.ThrowIfNull(ledger);

        var subsidized = applyCalmLowBalanceSubsidy && contract.Calm && ledger.BalanceCoords < CareerLedger.LowBalanceSubsidyThresholdCoords;
        var effectiveFee = subsidized ? 0 : BaseRentalPriceCoords;

        return new DgnssRentalOffer(
            markSpec.MarkId,
            contract.Id,
            BaseRentalPriceCoords,
            effectiveFee,
            markSpec.OperatingWindowStartSeconds,
            markSpec.OperatingWindowEndSeconds,
            markSpec.MinimumOpenness);
    }

    public static DgnssRentalResult TryRentBaseForContract(
        string transactionId,
        double timestampSeconds,
        ContractDefinition contract,
        SurveyMarkBaseStationSpec markSpec,
        CareerLedger ledger,
        out SurveyMarkBaseStationSpec? rentedSpec,
        bool applyCalmLowBalanceSubsidy = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(markSpec);
        ArgumentNullException.ThrowIfNull(ledger);

        var offer = QuoteRental(contract, markSpec, ledger, applyCalmLowBalanceSubsidy);
        if (markSpec.MinimumOpenness < DgnssBaseSimulator.RequiredBaseMinimumOpenness)
        {
            rentedSpec = null;
            var rejected = new LedgerOperationResult(
                LedgerCommandStatus.IdempotentNoOp,
                ledger.BalanceCoords,
                ledger.FineDebtCoords,
                0,
                0,
                0,
                "gnss.dgnss.base_unavailable");
            return new DgnssRentalResult(false, offer, null, rejected, "gnss.dgnss.base_unavailable");
        }

        var debitResult = ledger.TryDebitPurchaseOrService(
            transactionId,
            $"dgnss_rental:{contract.Id}:{markSpec.MarkId}",
            timestampSeconds,
            offer.EffectiveFeeCoords,
            "dgnss_rental",
            "gnss.dgnss.rental_fee");

        if (!debitResult.Succeeded)
        {
            rentedSpec = null;
            return new DgnssRentalResult(false, offer, null, debitResult, debitResult.ReasonKey ?? "economy.error.insufficient_funds");
        }

        rentedSpec = markSpec;
        return new DgnssRentalResult(true, offer, markSpec, debitResult, null);
    }
}
