using System.Text.Json;
using Gsa.Missions.Economy;
using Gsa.Missions.Quality;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class LedgerAndWalletTests
{
    private static PaymentQuote EligibleFullQuote(string contractId, int coords) =>
        new(contractId, true, false, false, 5, 1.0, 1.0, 0.0, 0.0, coords, coords, coords, 100, 3, [], ["HIST-02"]);

    [Fact]
    public void IdempotentTransactionsReplayCleanlyAcrossJsonSaveAndReload()
    {
        var ledger = new CareerLedger(250);
        var quote = EligibleFullQuote("contract_01", 350);

        var first = ledger.RecordContractSettlement("tx_settle_1", "contract_01", 100.0, quote);
        Assert.Equal(LedgerCommandStatus.Executed, first.Status);
        Assert.Equal(600, ledger.BalanceCoords);

        var duplicate = ledger.RecordContractSettlement("tx_settle_1", "contract_01", 105.0, quote);
        Assert.Equal(LedgerCommandStatus.IdempotentNoOp, duplicate.Status);
        Assert.Equal(600, ledger.BalanceCoords);

        var purchase = ledger.TryDebitPurchaseOrService("tx_buy_bike", "shop_01", 110.0, 400, "platform", "shop.bike");
        Assert.Equal(LedgerCommandStatus.Executed, purchase.Status);
        Assert.Equal(200, ledger.BalanceCoords);

        // Save + restore via JSON preserves idempotency table.
        var json = JsonSerializer.Serialize(ledger.ExportState());
        var restored = new CareerLedger(JsonSerializer.Deserialize<CareerLedgerState>(json)!);
        var replayedPurchase = restored.TryDebitPurchaseOrService("tx_buy_bike", "shop_01", 120.0, 400, "platform", "shop.bike");
        Assert.Equal(LedgerCommandStatus.IdempotentNoOp, replayedPurchase.Status);
        Assert.Equal(200, restored.BalanceCoords);
    }

    [Fact]
    public void InsufficientFundsRejectsPurchaseAndNeverProducesNegativeBalance()
    {
        var ledger = new CareerLedger(100);
        var refused = ledger.TryDebitPurchaseOrService("tx_expensive", "shop_01", 10.0, 400, "platform", "shop.bike");
        Assert.False(refused.Succeeded);
        Assert.Equal(LedgerCommandStatus.InsufficientFunds, refused.Status);
        Assert.Equal(100, ledger.BalanceCoords);

        // After earning more Coords, the previously refused transaction ID can succeed.
        var earn = ledger.RecordContractSettlement("tx_earn", "c1", 20.0, EligibleFullQuote("c1", 350));
        Assert.Equal(450, earn.BalanceCoords);
        var retry = ledger.TryDebitPurchaseOrService("tx_expensive", "shop_01", 30.0, 400, "platform", "shop.bike");
        Assert.Equal(LedgerCommandStatus.Executed, retry.Status);
        Assert.Equal(50, ledger.BalanceCoords);
    }

    [Fact]
    public void TrafficFineOverflowCreatesDebtAbatedByTwentyPercentAndStarterKitIsFree()
    {
        var ledger = new CareerLedger(20);
        var fine = ledger.ApplyTrafficFine("tx_fine_1", "red_light", 50.0, 70, "fine.signal");
        Assert.Equal(LedgerCommandStatus.Executed, fine.Status);
        Assert.Equal(0, ledger.BalanceCoords);
        Assert.Equal(50, ledger.FineDebtCoords);

        // At balance 0 + debt 50, starter kit recovery is always free.
        var kit = ledger.RecoverStarterKitFree("tx_kit_1", 60.0);
        Assert.True(kit.Succeeded);
        Assert.Equal(0, ledger.BalanceCoords);
        Assert.Equal(50, ledger.FineDebtCoords);

        // Calm contract with low balance (< 50) waives basic service fee (e.g. 50 fee -> 0 charged).
        // Payout of 200 -> 20% = 40 deducted from debt (debt 50 -> 10), net +160 to balance!
        var payout = ledger.RecordContractSettlement(
            "tx_calm_settle",
            "c_calm",
            120.0,
            EligibleFullQuote("c_calm", 200),
            isCalmContract: true,
            serviceFeeCoords: 50);

        Assert.Equal(0, payout.ServiceFeeChargedCoords);
        Assert.Equal(40, payout.DebtAbatedCoords);
        Assert.Equal(160, ledger.BalanceCoords);
        Assert.Equal(10, ledger.FineDebtCoords);
    }
}
