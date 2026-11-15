using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Gnss;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Office;
using Gsa.Missions.Quality;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class FleetShopAndProgressionTests
{
    private static ContractDefinition LoadContract(string fixture) =>
        ContractDefinition.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)), fixture);

    private static (CareerProgressionState Progression, HashSet<string> CompletedMissions) BuildStateWithXpAndReputation(
        int settlements,
        string clientId,
        bool includeHist02,
        bool includeHist03)
    {
        var state = new CareerProgressionState();
        var def = LoadContract("facades.json") with { ClientId = clientId };

        for (var i = 0; i < settlements; i++)
        {
            var settledDef = def with { Id = $"{def.Id}_{i}" };
            var quote = new PaymentQuote(
                contractId: settledDef.Id,
                eligible: true,
                isPartial: false,
                isExpired: false,
                stars: 3,
                qualityScore: 1.0,
                partialWeight: 1.0,
                lateHours: 0.0,
                latePenaltyFraction: 0.0,
                baseCoords: 350,
                grossCoords: 350,
                netCoords: 350,
                xpAwarded: 200,
                reputationDelta: 30,
                passedDeliverableIds: [],
                storyUnlocks: []);
            state.ApplySettlement(settledDef, quote);
        }

        var completed = new HashSet<string>(StringComparer.Ordinal);
        if (includeHist02)
            completed.Add("HIST-02");
        if (includeHist03)
            completed.Add("HIST-03");
        return (state, completed);
    }

    [Fact]
    public void PlatformUnlockDiscountLidarGateSkillTracksAndStarterRecoveryWorkDeterministically()
    {
        var manager = new FleetProgressionManager();
        var ledger = new CareerLedger(1000);

        // Before HIST-02, Bicycle is locked
        var (lowRep, noneCompleted) = BuildStateWithXpAndReputation(settlements: 2, "client_prefeitura", false, false);
        var lockedQuote = manager.QuotePlatformPurchase(LocomotionMode.Bicycle, lowRep, noneCompleted);
        Assert.False(lockedQuote.Unlocked);
        Assert.Equal("shop.error.story_prerequisite_missing", lockedQuote.LockReasonKey);

        // 5 settlements * 30 rep = 150 global reputation -> 150/1000 = 15% cap!
        // With 20 settlements * 30 rep = 600 rep -> still capped at 15%!
        var (highRep, completedHist02) = BuildStateWithXpAndReputation(settlements: 20, "client_prefeitura", includeHist02: true, includeHist03: false);
        Assert.Equal(600, highRep.GlobalReputation);
        Assert.Equal(0.15, FleetProgressionManager.ComputeReputationDiscountFraction(highRep.GlobalReputation), 6);

        var bikeQuote = manager.QuotePlatformPurchase(LocomotionMode.Bicycle, highRep, completedHist02);
        Assert.True(bikeQuote.Unlocked);
        Assert.Equal(400, bikeQuote.BasePriceCoords);
        Assert.Equal(60, bikeQuote.DiscountCoords);
        Assert.Equal(340, bikeQuote.FinalPriceCoords);

        Assert.True(manager.TryPurchasePlatform("buy_bike", 10.0, LocomotionMode.Bicycle, ledger, highRep, completedHist02, out var bikeLedger, out _));
        Assert.NotNull(bikeLedger);
        Assert.Equal(660, ledger.BalanceCoords);
        Assert.Equal(LocomotionMode.Bicycle, manager.ActivePlatform.Mode);
        Assert.Equal(6.0, manager.ActivePlatform.MaxSpeedMetresPerSecond, 6);
        Assert.Equal(400, manager.ActivePlatform.VirtualStorageCapacityUnits);

        // LiDAR blocked below platform Level 4
        var lidarQuote = manager.QuoteSensorUpgrade("lidar_basic", FleetShopCatalog.LidarBasicUpgradePriceCoords, highRep, requiresLidarSlot: true);
        Assert.False(lidarQuote.Eligible);
        Assert.Equal(4, lidarQuote.RequiredPlatformLevel);
        Assert.Equal(1, lidarQuote.CurrentPlatformLevel);
        Assert.Equal("shop.error.lidar_requires_level_4", lidarQuote.RejectionReasonKey);

        // Scooter stays locked until HIST-03
        var scooterLocked = manager.QuotePlatformPurchase(LocomotionMode.Scooter, highRep, completedHist02);
        Assert.False(scooterLocked.Unlocked);

        // Insufficient funds test when ledger balance < discounted scooter price (1020)
        var (rep0, completedHist03) = BuildStateWithXpAndReputation(settlements: 3, "client_prefeitura", true, true);
        var lowBalanceLedger = new CareerLedger(500);
        Assert.False(manager.TryPurchasePlatform("buy_scooter_fail", 20.0, LocomotionMode.Scooter, lowBalanceLedger, rep0, completedHist03, out var failDebit, out _));
        Assert.Equal(LedgerCommandStatus.InsufficientFunds, failDebit!.Status);
        Assert.Equal(500, lowBalanceLedger.BalanceCoords);

        // XP skill tracks: 20 settlements * 200 XP = 4000 XP => 8 points; max rank 3 per track
        Assert.Equal(4000, highRep.TotalXp);
        Assert.Equal(8, manager.GetTotalEarnedSkillPoints(highRep));
        for (var r = 1; r <= 3; r++)
        {
            Assert.True(manager.TryUnlockSkillRank(SkillTrackKind.Processing, highRep, out var rank, out _));
            Assert.Equal(r, rank);
        }
        Assert.False(manager.TryUnlockSkillRank(SkillTrackKind.Processing, highRep, out _, out var maxErr));
        Assert.Equal("progression.error.track_maxed", maxErr);
        Assert.Equal(0.30, manager.GetProcessingTimeReductionFraction(), 6);

        // Low balance (<50) starter recovery keeps free handcart + low-balance subsidy
        var brokeLedger = new CareerLedger(20);
        var recovery = manager.RecoverStarterKitAndEvaluateSubsidy("recover_1", 30.0, brokeLedger);
        Assert.Equal(LocomotionMode.Handcart, recovery.ActiveMode);
        Assert.Equal(20, brokeLedger.BalanceCoords);
        Assert.True(recovery.LowBalanceSubsidyActive);
    }

    [Fact]
    public void DgnssBaseRentalCharges20CoordsRefusesInsufficientFundsAndSupportsCalmSubsidy()
    {
        var mark = new SurveyMarkBaseStationSpec("mark_m1", "Praça Matriz", new Vector3d(500100, 7400100, 100), 0.0, 600.0, 0.8);
        var commercialContract = LoadContract("facades.json") with { Calm = false };
        var calmContract = LoadContract("inventory.json") with { Calm = true };

        var healthyLedger = new CareerLedger(100);
        var rental = DgnssRentalService.TryRentBaseForContract("rent_1", 5.0, commercialContract, mark, healthyLedger, out var rentedSpec);
        Assert.True(rental.Succeeded);
        Assert.NotNull(rentedSpec);
        Assert.Equal(20, rental.Offer.EffectiveFeeCoords);
        Assert.Equal(80, healthyLedger.BalanceCoords);

        // Insufficient funds on commercial contract with 10 Coords
        var thinLedger = new CareerLedger(10);
        var failed = DgnssRentalService.TryRentBaseForContract("rent_fail", 6.0, commercialContract, mark, thinLedger, out var noSpec);
        Assert.False(failed.Succeeded);
        Assert.Null(noSpec);
        Assert.Equal(10, thinLedger.BalanceCoords);

        // Low-balance Calm contract subsidizes base rental to 0 Coords
        var subsidized = DgnssRentalService.TryRentBaseForContract("rent_calm_sub", 7.0, calmContract, mark, thinLedger, out var subSpec);
        Assert.True(subsidized.Succeeded);
        Assert.NotNull(subSpec);
        Assert.Equal(0, subsidized.Offer.EffectiveFeeCoords);
        Assert.Equal(10, thinLedger.BalanceCoords);
    }
}
