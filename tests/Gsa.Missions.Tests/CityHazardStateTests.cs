using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Traffic;
using Gsa.Missions.Economy;
using Gsa.Missions.Gameplay;
using Xunit;

namespace Gsa.Missions.Tests;

public sealed class CityHazardStateTests
{
    [Fact]
    public void JsonReloadKeepsDamageCountermeasuresMovedMarksAndEventIds()
    {
        var s = new CityHazardState(); s.Step(.1, "zone", false); s.StartSpoofing(); s.Step(2, "zone", false);
        s.Lens.ApplySplatter(.45); s.TirePunctured = true; s.RackYawErrorRadians = .01;
        s.SetVehicleDamage(LocomotionMode.Kombi, 25); s.DisabledVehicle = LocomotionMode.Car;
        s.Sabotage.MovedBenchmarks.RegisterDisplacement("mark", new(10, 20, 0), new(.8, -.3, 0), 1);
        s.Sabotage.ActivateKombiDisguise("zone", s.NextEventId(), 2, s.Services, PlatformProfile.Kombi, new CareerLedger(1000));
        var json = JsonSerializer.Serialize(s.Export()); var restored = CityHazardState.Restore(JsonSerializer.Deserialize<CityHazardSnapshot>(json));
        Assert.Equal(json, JsonSerializer.Serialize(restored.Export())); Assert.Equal(25, restored.GetVehicleDamage(LocomotionMode.Kombi));
        Assert.True(restored.IsVehicleDisabled(LocomotionMode.Car)); Assert.Equal("city_hazard_1", restored.NextEventId());
        restored.BeginContract(); Assert.Equal(1, restored.Sabotage.ComputeEffectiveAttentionMultiplier("zone"));
        Assert.Single(restored.Sabotage.MovedBenchmarks.Records); Assert.Equal(.45, restored.Lens.ObstructionFraction);
    }
    [Fact]
    public void SafeZonesAndCalmCancelActiveEffectsWithoutRepairingPhysicalDamage()
    {
        var s = new CityHazardState(); s.Step(1, "zone", false); s.StartSpoofing(); s.PursuitRemaining = 20;
        s.RoadblockRemaining = 30; s.RoadblockPosition = new(1, 2, 3); s.TirePunctured = true; s.Lens.ApplySplatter(.45);
        s.Step(.1, "zone", true); Assert.Equal(0, s.SpoofRemaining); Assert.Equal(0, s.PursuitRemaining); Assert.Null(s.RoadblockPosition);
        Assert.True(s.TirePunctured); Assert.Equal(.45, s.Lens.ObstructionFraction);
        s.StartSpoofing(); s.Step(.1, "other", false); Assert.Equal(0, s.SpoofRemaining);
    }
    [Fact]
    public void UnknownOrMalformedNestedHazardsAreRejectedAndLegacyDisguiseIsRetained()
    {
        var s = new CityHazardState().Export(); Assert.Throws<ArgumentException>(() => CityHazardState.Restore(s with { SchemaVersion = 2 }));
        Assert.Throws<ArgumentException>(() => CityHazardState.Restore(s with { Services = s.Services with { SchemaVersion = 2 } }));
        Assert.Throws<ArgumentException>(() => CityHazardState.Restore(s with { PursuitRemaining = double.NaN }));
        Assert.Throws<ArgumentException>(() => CityHazardState.Restore(s with { VehicleDamage = new Dictionary<LocomotionMode, double> { [LocomotionMode.Handcart] = 20 } }));
        Assert.True(CityHazardState.Restore(null, true).LegacyDisguise);
    }
    [Fact]
    public void PaidDisguiseUsesFullPhysicalTimeAndReplayDoesNotChargeTwice()
    {
        const string zone = "zone_port_cimento"; var c = new FactionSabotageAndCountermeasuresCoordinator();
        var services = new FleetServiceAndInsuranceCoordinator(); var ledger = new CareerLedger(1000);
        Assert.True(c.ActivateKombiDisguise(zone, "purchase", 0, services, PlatformProfile.Kombi, ledger).Succeeded);
        c.ActivateKombiDisguise(zone, "purchase", 0, services, PlatformProfile.Kombi, ledger); Assert.Equal(800, ledger.BalanceCoords);
        var t = new TerritoryAttentionTracker(); t.ImportSnapshot(new(1, [new(zone, 40, 0, 0)]));
        c.StepWithCountermeasures(t, zone, true, 5, false, 1);
        Assert.True(c.StepWithCountermeasures(t, zone, true, 5, false, 3).ActionTriggered); Assert.Equal(42, t.GetAttention(zone));
        Assert.Equal(20, c.StepWithCountermeasures(t, zone, true, 5, false, 10).CooldownRemainingSeconds);
    }
    [Fact]
    public void InsuranceCannotPayForAnotherVehicleAndExpiryRequiresNewPolicy()
    {
        var s = new FleetServiceAndInsuranceCoordinator(); var ledger = new CareerLedger(10000);
        Assert.True(s.ExecuteService("policy", 0, ServiceKind.InsurancePolicy, PlatformProfile.Kombi, ledger).Succeeded); Assert.Equal(9720, ledger.BalanceCoords);
        Assert.False(s.QuoteService(ServiceKind.InsuranceClaim, PlatformProfile.Van).Eligible);
        Assert.False(s.SubmitTotalLossClaim("wrong", "wrong", 1, PlatformProfile.Van, ledger, null, out _).Succeeded); Assert.Equal(9720, ledger.BalanceCoords);
        Assert.True(s.SubmitTotalLossClaim("claim", "claim", 2, PlatformProfile.Kombi, ledger, null, out var claim).Succeeded);
        Assert.Equal(1400, claim!.DeductiblePaidCoords); Assert.False(claim.LostBatchesRestored);
        s.ExpireInsurance(); Assert.False(s.QuoteService(ServiceKind.InsuranceClaim, PlatformProfile.Kombi).Eligible);
    }
}
