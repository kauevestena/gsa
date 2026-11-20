using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Traffic;
using Gsa.Missions.Economy;

namespace Gsa.Missions.Gameplay;

public enum CityParkingPhase { Offered, Refused }
public sealed record CityParkingOffer(string EncounterId, string SiteId, Vector3d PositionEnu,
    LocomotionMode VehicleMode, CityParkingPhase Phase, double RetaliationRemainingSeconds);
public sealed record CityDogEncounter(string SiteId, DogPursuitState Pursuit, Vector3d PositionEnu, bool ContactApplied);
public sealed record CityEverydaySnapshot(int SchemaVersion, double PhysicalTimeSeconds, long NextSequence,
    CityDogEncounter? Dog, double BicycleStumbleRemainingSeconds, CityParkingOffer? Parking,
    IReadOnlyDictionary<string, double> SiteCooldownUntilSeconds,
    IReadOnlyDictionary<LocomotionMode, int> CosmeticDamage);
public readonly record struct CityEverydayStep(bool DogContact, bool DogEnded, bool CosmeticScratch,
    LocomotionMode? ScratchedVehicle);

/// <summary>Bounded, non-injurious encounters; all timers use physical seconds and cosmetic damage never enters CQ.</summary>
public sealed class CityEverydayEncounters
{
    public const double DogWarningSeconds = 2;
    public const double DogSpeedMetresPerSecond = 4.5; // Bicycle reaches 6 m/s: escape by pedalling remains available.
    public const double DogContactRadiusMetres = 1.2;
    public const double BicycleStumbleSeconds = 2;
    public const double SiteCooldownSeconds = 30;
    public const double ParkingRadiusMetres = 8;
    public const double ParkingRetaliationWarningSeconds = 3;
    private readonly Dictionary<string, double> _cooldowns = new(StringComparer.Ordinal);
    private readonly Dictionary<LocomotionMode, int> _cosmeticDamage = [];
    private DogPursuitTracker? _dog;
    private string? _dogSite;
    private bool _dogContactApplied;
    public double PhysicalTimeSeconds { get; private set; }
    public long NextSequence { get; private set; }
    public Vector3d DogPositionEnu { get; private set; }
    public bool DogActive => _dog?.Active == true;
    public double DogElapsedSeconds => _dog?.ElapsedSeconds ?? 0;
    public string? DogSiteId => _dogSite;
    public double BicycleStumbleRemainingSeconds { get; private set; }
    public CityParkingOffer? Parking { get; private set; }
    public int CosmeticDamageFor(LocomotionMode vehicle) => _cosmeticDamage.GetValueOrDefault(vehicle);
    public void ClearCosmeticDamage(LocomotionMode vehicle) => _cosmeticDamage.Remove(vehicle);
    public static double Distance(Vector3d a, Vector3d b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    private bool SiteReady(string id) => _cooldowns.GetValueOrDefault(id) <= PhysicalTimeSeconds;
    private void Cooldown(string id) => _cooldowns[id] = PhysicalTimeSeconds + SiteCooldownSeconds;

    public bool TryStartDog(string siteId, Vector3d home, Vector3d player, LocomotionMode mode, bool protectedNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);
        var distance = Distance(home, player);
        if (protectedNow || mode != LocomotionMode.Bicycle || DogActive || !SiteReady(siteId) || distance > DogPursuitTracker.TriggerRadiusMetres)
            return false;
        _dog = new DogPursuitTracker("city_dog_" + NextSequence++, distance, false);
        _dogSite = siteId; DogPositionEnu = home; _dogContactApplied = false;
        Cooldown(siteId); return true;
    }

    public bool TryOfferParking(string siteId, Vector3d site, Vector3d vehiclePosition, LocomotionMode vehicleMode,
        double speed, bool protectedNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);
        if (!double.IsFinite(speed)) throw new ArgumentOutOfRangeException(nameof(speed));
        if (protectedNow || Parking is not null || vehicleMode < LocomotionMode.Car || !Enum.IsDefined(vehicleMode) ||
            Math.Abs(speed) >= .1 || !SiteReady(siteId) || Distance(site, vehiclePosition) > ParkingRadiusMetres) return false;
        Parking = new("city_parking_" + NextSequence++, siteId, site, vehicleMode, CityParkingPhase.Offered, 0);
        return true;
    }

    public bool GiveTreat(bool protectedNow)
    {
        if (protectedNow) { SuppressHostileEncounters(); return false; }
        if (!DogActive) return false;
        _dog!.ApplyTreat(); if (_dogSite is { } site) Cooldown(site); return true;
    }

    public bool PayParking(CareerLedger ledger, double calendarSeconds, bool protectedNow)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        if (protectedNow) { SuppressHostileEncounters(); return false; }
        if (Parking is not { Phase: CityParkingPhase.Offered } offer) return false;
        var result = ledger.TryDebitPurchaseOrService(offer.EncounterId, offer.EncounterId, calendarSeconds,
            UrbanHazardEncounterCoordinator.FlanelinhaTipCoords, "parking", "city.encounters.parking_paid");
        if (!result.Succeeded) return false;
        Cooldown(offer.SiteId); Parking = null; return true;
    }

    public bool RefuseParking(bool protectedNow)
    {
        if (protectedNow) { SuppressHostileEncounters(); return false; }
        if (Parking is not { Phase: CityParkingPhase.Offered } offer) return false;
        Parking = offer with { Phase = CityParkingPhase.Refused, RetaliationRemainingSeconds = ParkingRetaliationWarningSeconds };
        return true;
    }

    public void SuppressHostileEncounters()
    {
        if (DogActive) { _dog!.SuppressByCalmPolicy(); if (_dogSite is { } site) Cooldown(site); }
        if (Parking is { } offer) { Cooldown(offer.SiteId); Parking = null; }
        BicycleStumbleRemainingSeconds = 0;
    }

    public CityEverydayStep Step(double dt, Vector3d player, LocomotionMode mode, Vector3d vehiclePosition,
        LocomotionMode vehicleMode, bool protectedNow, Func<Vector3d, Vector3d, Vector3d>? resolveDogMotion = null)
    {
        if (!double.IsFinite(dt) || dt <= 0) throw new ArgumentOutOfRangeException(nameof(dt));
        PhysicalTimeSeconds += dt;
        BicycleStumbleRemainingSeconds = Math.Max(0, BicycleStumbleRemainingSeconds - dt);
        var wasDogActive = DogActive; var contact = false; var scratch = false; LocomotionMode? scratched = null;
        if (protectedNow) { SuppressHostileEncounters(); return new(false, wasDogActive, false, null); }
        if (DogActive)
        {
            var distance = Distance(DogPositionEnu, player);
            if (mode != LocomotionMode.Bicycle) _dog!.StopFollowingBicycle();
            else
            {
                var elapsedBefore = _dog!.ElapsedSeconds;
                _dog.Step(dt, distance);
                if (DogActive && _dog.ElapsedSeconds >= DogWarningSeconds)
                {
                    var movingDt = Math.Max(0, _dog.ElapsedSeconds - DogWarningSeconds) - Math.Max(0, elapsedBefore - DogWarningSeconds);
                    var delta = new Vector3d(player.X - DogPositionEnu.X, player.Y - DogPositionEnu.Y, 0);
                    var next = distance > 1e-9 ? DogPositionEnu + delta * (Math.Min(distance, DogSpeedMetresPerSecond * movingDt) / distance) : DogPositionEnu;
                    DogPositionEnu = resolveDogMotion?.Invoke(DogPositionEnu, next) ?? next;
                    if (!_dogContactApplied && Distance(DogPositionEnu, player) <= DogContactRadiusMetres)
                    { _dogContactApplied = true; BicycleStumbleRemainingSeconds = BicycleStumbleSeconds; contact = true; }
                }
            }
            if (!DogActive && _dogSite is { } site) Cooldown(site);
        }
        if (Parking is { } offer)
        {
            if (vehicleMode != offer.VehicleMode || Distance(offer.PositionEnu, vehiclePosition) > ParkingRadiusMetres)
            { Cooldown(offer.SiteId); Parking = null; }
            else if (offer.Phase == CityParkingPhase.Refused)
            {
                var remaining = offer.RetaliationRemainingSeconds - dt;
                if (remaining > 1e-9) Parking = offer with { RetaliationRemainingSeconds = remaining };
                else
                {
                    _cosmeticDamage[offer.VehicleMode] = Math.Min(100, CosmeticDamageFor(offer.VehicleMode) + UrbanHazardEncounterCoordinator.FlanelinhaRefusalCosmeticDamage);
                    scratch = true; scratched = offer.VehicleMode; Cooldown(offer.SiteId); Parking = null;
                }
            }
        }
        return new(contact, wasDogActive && !DogActive, scratch, scratched);
    }

    public CityEverydaySnapshot Export() => new(1, PhysicalTimeSeconds, NextSequence,
        _dog is null ? null : new(_dogSite!, _dog.ExportState(), DogPositionEnu, _dogContactApplied),
        BicycleStumbleRemainingSeconds, Parking,
        _cooldowns.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
        _cosmeticDamage.OrderBy(p => p.Key).ToDictionary(p => p.Key, p => p.Value));

    public static CityEverydayEncounters Restore(CityEverydaySnapshot? snapshot)
    {
        if (snapshot is null) return new();
        static bool Timer(double t) => double.IsFinite(t) && t >= 0;
        if (snapshot.SchemaVersion != 1 || !Timer(snapshot.PhysicalTimeSeconds) || snapshot.NextSequence < 0 ||
            !Timer(snapshot.BicycleStumbleRemainingSeconds) || snapshot.BicycleStumbleRemainingSeconds > BicycleStumbleSeconds ||
            snapshot.SiteCooldownUntilSeconds is null || snapshot.CosmeticDamage is null ||
            snapshot.SiteCooldownUntilSeconds.Any(p => string.IsNullOrWhiteSpace(p.Key) || !Timer(p.Value)) ||
            snapshot.CosmeticDamage.Any(p => !Enum.IsDefined(p.Key) || p.Key < LocomotionMode.Car || p.Value is < 0 or > 100))
            throw new ArgumentException("Invalid everyday encounter state.");
        if (snapshot.Parking is { } p && (string.IsNullOrWhiteSpace(p.EncounterId) || string.IsNullOrWhiteSpace(p.SiteId) ||
            !Enum.IsDefined(p.Phase) || !Enum.IsDefined(p.VehicleMode) || p.VehicleMode < LocomotionMode.Car ||
            !Timer(p.RetaliationRemainingSeconds) || p.RetaliationRemainingSeconds > ParkingRetaliationWarningSeconds ||
            (p.Phase == CityParkingPhase.Offered && p.RetaliationRemainingSeconds != 0)))
            throw new ArgumentException("Invalid parking offer.");
        var state = new CityEverydayEncounters { PhysicalTimeSeconds = snapshot.PhysicalTimeSeconds,
            NextSequence = snapshot.NextSequence, BicycleStumbleRemainingSeconds = snapshot.BicycleStumbleRemainingSeconds, Parking = snapshot.Parking };
        if (snapshot.Dog is { } dog)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(dog.SiteId);
            state._dog = DogPursuitTracker.FromState(dog.Pursuit); state._dogSite = dog.SiteId;
            state.DogPositionEnu = dog.PositionEnu; state._dogContactApplied = dog.ContactApplied;
        }
        foreach (var pair in snapshot.SiteCooldownUntilSeconds) state._cooldowns.Add(pair.Key, pair.Value);
        foreach (var pair in snapshot.CosmeticDamage) state._cosmeticDamage.Add(pair.Key, pair.Value);
        return state;
    }
}
