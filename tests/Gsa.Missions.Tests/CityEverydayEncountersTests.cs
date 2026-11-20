using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Missions.Economy;
using Gsa.Missions.Gameplay;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class CityEverydayEncountersTests
{
    private static CityEverydayEncounters Reload(CityEverydayEncounters s) =>
        CityEverydayEncounters.Restore(JsonSerializer.Deserialize<CityEverydaySnapshot>(JsonSerializer.Serialize(s.Export())));
    private static CityEverydayStep Step(CityEverydayEncounters s, double dt, Vector3d player, bool calm = false,
        LocomotionMode mode = LocomotionMode.Bicycle) => s.Step(dt, player, mode, player, mode, calm);
    private static CityEverydayEncounters Parking()
    {
        var s = new CityEverydayEncounters(); Assert.True(s.TryOfferParking("site", Vector3d.Zero, Vector3d.Zero, LocomotionMode.Car, 0, false)); return s;
    }

    [Fact]
    public void OnlyBicyclesInRangeStartDogsAndWarningPrecedesAnyMovement()
    {
        var s = new CityEverydayEncounters(); var player = new Vector3d(10, 0, 0);
        Assert.False(s.TryStartDog("dog", Vector3d.Zero, player, LocomotionMode.Handcart, false));
        Assert.False(s.TryStartDog("dog", Vector3d.Zero, new(31, 0, 0), LocomotionMode.Bicycle, false));
        Assert.False(s.TryStartDog("dog", Vector3d.Zero, player, LocomotionMode.Bicycle, true));
        Assert.True(s.TryStartDog("dog", Vector3d.Zero, player, LocomotionMode.Bicycle, false));
        Assert.False(Step(s, 2, player).DogContact); Assert.Equal(Vector3d.Zero, s.DogPositionEnu);
        Step(s, .5, player); Assert.Equal(2.25, s.DogPositionEnu.X, 8);
    }
    [Fact]
    public void TimeoutPersistsAcrossReloadAndAnEncounterStumblesOnlyOnce()
    {
        var s = new CityEverydayEncounters(); var p = new Vector3d(1.3, 0, 0);
        s.TryStartDog("dog", Vector3d.Zero, p, LocomotionMode.Bicycle, false);
        Assert.True(Step(s, 2.1, p).DogContact); s = Reload(s);
        Assert.Equal(2, s.BicycleStumbleRemainingSeconds); Assert.False(Step(s, .5, p).DogContact);
        Assert.Equal(1.5, s.BicycleStumbleRemainingSeconds, 8);
        Assert.True(Step(s, 7.5, p).DogEnded); Assert.False(s.DogActive);
        Assert.False(s.TryStartDog("dog", Vector3d.Zero, p, LocomotionMode.Bicycle, false));
    }
    [Fact]
    public void TreatAndFortyMetreEscapeEndPursuitAndCooldownSurvivesReload()
    {
        var s = new CityEverydayEncounters(); s.TryStartDog("dog", Vector3d.Zero, new(20, 0, 0), LocomotionMode.Bicycle, false);
        Assert.True(s.GiveTreat(false)); Assert.False(s.GiveTreat(false)); s = Reload(s);
        Assert.True(s.Export().Dog!.Pursuit.PacifiedByTreat); Assert.False(s.DogActive);
        Assert.False(s.TryStartDog("dog", Vector3d.Zero, new(20, 0, 0), LocomotionMode.Bicycle, false));
        Step(s, 30, Vector3d.Zero); Assert.True(s.TryStartDog("dog", Vector3d.Zero, new(20, 0, 0), LocomotionMode.Bicycle, false));
        Assert.True(Step(s, .1, new(41, 0, 0)).DogEnded);
    }
    [Fact]
    public void CalmAndDismountRemovePursuitWithoutDelayedContact()
    {
        var s = new CityEverydayEncounters(); s.TryStartDog("dog", Vector3d.Zero, new(2, 0, 0), LocomotionMode.Bicycle, false);
        Step(s, 2, new(2, 0, 0), calm: true); Assert.False(Step(s, 3, Vector3d.Zero).DogContact);
        Assert.False(s.DogActive); Assert.Equal(0, s.BicycleStumbleRemainingSeconds);
        s.TryStartDog("other", Vector3d.Zero, new(2, 0, 0), LocomotionMode.Bicycle, false);
        Step(s, .1, new(2, 0, 0), mode: LocomotionMode.OnFoot); Assert.False(s.DogActive);
        Assert.False(s.Export().Dog!.Pursuit.SuppressedByCalmPolicy);
    }
    [Fact]
    public void ParkingPaymentCostsTenAndCannotBeDuplicatedAfterReload()
    {
        var s = Parking(); var ledger = new CareerLedger(100); Assert.True(s.PayParking(ledger, 10, false));
        Assert.Equal(90, ledger.BalanceCoords); Assert.False(s.PayParking(ledger, 10, false));
        s = Reload(s); Assert.False(s.PayParking(ledger, 11, false)); Assert.Equal(90, ledger.BalanceCoords);
        Assert.Null(s.Parking); Assert.Equal(0, s.CosmeticDamageFor(LocomotionMode.Car));
    }
    [Fact]
    public void RefusalHasThreeSecondWarningAndSavedScratchOccursExactlyOnceForThatVehicle()
    {
        var s = Parking(); Assert.True(s.RefuseParking(false)); Assert.False(s.RefuseParking(false));
        s.Step(2, Vector3d.Zero, LocomotionMode.Car, Vector3d.Zero, LocomotionMode.Car, false); s = Reload(s);
        Assert.Equal(1, s.Parking!.RetaliationRemainingSeconds);
        Assert.True(s.Step(1, Vector3d.Zero, LocomotionMode.Car, Vector3d.Zero, LocomotionMode.Car, false).CosmeticScratch);
        s = Reload(s); Assert.False(s.Step(5, Vector3d.Zero, LocomotionMode.Car, Vector3d.Zero, LocomotionMode.Car, false).CosmeticScratch);
        Assert.Equal(5, s.CosmeticDamageFor(LocomotionMode.Car)); Assert.Equal(0, s.CosmeticDamageFor(LocomotionMode.Kombi));
        s.ClearCosmeticDamage(LocomotionMode.Kombi); Assert.Equal(5, s.CosmeticDamageFor(LocomotionMode.Car));
    }
    [Fact]
    public void DrivingAwayOrEnablingCalmCancelsPendingRetaliation()
    {
        var s = Parking(); s.RefuseParking(false);
        s.Step(3, new(9, 0, 0), LocomotionMode.Car, new(9, 0, 0), LocomotionMode.Car, false);
        Assert.Null(s.Parking); Assert.Equal(0, s.CosmeticDamageFor(LocomotionMode.Car));
        s = Parking(); s.RefuseParking(false); s.Step(1, Vector3d.Zero, LocomotionMode.Car, Vector3d.Zero, LocomotionMode.Car, true);
        s = Reload(s); s.Step(10, Vector3d.Zero, LocomotionMode.Car, Vector3d.Zero, LocomotionMode.Car, false);
        Assert.Equal(0, s.CosmeticDamageFor(LocomotionMode.Car)); Assert.Null(s.Parking);
        s = Parking(); var ledger = new CareerLedger(100); Assert.False(s.PayParking(ledger, 1, true)); Assert.Equal(100, ledger.BalanceCoords);
    }
    [Fact]
    public void InsufficientFundsPreserveChoiceAndMovingVehiclesAreNotOfferedParking()
    {
        var s = Parking(); var ledger = new CareerLedger(5); Assert.False(s.PayParking(ledger, 1, false));
        Assert.Equal(CityParkingPhase.Offered, s.Parking!.Phase); Assert.Equal(5, ledger.BalanceCoords);
        s = new(); Assert.False(s.TryOfferParking("site", Vector3d.Zero, Vector3d.Zero, LocomotionMode.Car, .2, false));
        Assert.False(s.TryOfferParking("site", Vector3d.Zero, Vector3d.Zero, LocomotionMode.Bicycle, 0, false));
    }
    [Fact]
    public void FutureAndImpossibleSnapshotsAreRejected()
    {
        var snapshot = Parking().Export();
        Assert.Throws<ArgumentException>(() => CityEverydayEncounters.Restore(snapshot with { SchemaVersion = 99 }));
        Assert.Throws<ArgumentException>(() => CityEverydayEncounters.Restore(snapshot with { BicycleStumbleRemainingSeconds = 3 }));
        Assert.Throws<ArgumentException>(() => CityEverydayEncounters.Restore(snapshot with { Parking = snapshot.Parking! with { RetaliationRemainingSeconds = double.NaN } }));
        Assert.Throws<ArgumentException>(() => CityEverydayEncounters.Restore(snapshot with { CosmeticDamage = new Dictionary<LocomotionMode, int> { [LocomotionMode.Car] = -1 } }));
    }
}
