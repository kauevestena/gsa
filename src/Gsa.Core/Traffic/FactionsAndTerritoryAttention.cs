using Gsa.Core.Geometry;

namespace Gsa.Core.Traffic;

public enum FactionId
{
    SindicatoDoMarcoTorto = 0,
    CartelDoCimento = 1,
    MafiaDoMapaVelho = 2,
    SociedadeDaTerraPlana = 3,
    MapaFacilSa = 4,
}

public enum FactionSignatureActionKind
{
    BenchmarkDisplacement = 0,
    IrregularWorkBlockade = 1,
    CadastralRoadblock = 2,
    GnssSpoofingAndPosterOnLens = 3,
    TirePunctureAndRivalBid = 4,
}

public readonly record struct TerritoryBoundsEnu(
    double MinEast,
    double MinNorth,
    double MaxEast,
    double MaxNorth)
{
    public bool Contains(double east, double north) =>
        east >= MinEast && east <= MaxEast && north >= MinNorth && north <= MaxNorth;
}

public sealed record FactionEmblemPalette(
    string PrimaryHex,
    string SecondaryHex,
    string EmblemKey,
    bool IsStrictlyFictional,
    bool UsesRealWorldSymbols);

public sealed record FactionDefinition(
    FactionId Id,
    string Code,
    string NameKey,
    string MotivationKey,
    FactionSignatureActionKind SignatureAction,
    FactionEmblemPalette Emblem,
    IReadOnlyList<string> NarrativeClueKeys,
    IReadOnlyList<string> PrimaryRegionIds);

public sealed record TerritoryZoneDefinition(
    string ZoneId,
    string RegionId,
    FactionId ControllingFaction,
    TerritoryBoundsEnu Bounds,
    IReadOnlyList<string> SabotagedBenchmarkIds,
    Vector3d SpoofedGnssOffsetEnuMeters);

public sealed record FactionIntegrityReport(
    bool Valid,
    int FactionCount,
    int TerritoryCount,
    bool PlayerWeaponsAllowed,
    IReadOnlyList<string> Errors);

/// <summary>Catalog of the 5 fictional factions, territories, narrative clues, and emblem integrity guards (T-F16-01).</summary>
public static class FactionCatalog
{
    public const bool PlayerWeaponsAllowed = false;

    public static IReadOnlyList<FactionDefinition> AllFactions { get; } =
    [
        new(
            Id: FactionId.SindicatoDoMarcoTorto,
            Code: "sindicato_marco_torto",
            NameKey: "faction.marco_torto.name",
            MotivationKey: "faction.marco_torto.motivation",
            SignatureAction: FactionSignatureActionKind.BenchmarkDisplacement,
            Emblem: new FactionEmblemPalette("#B85D19", "#F4D35E", "emblem.marco_torto.bent_pin", IsStrictlyFictional: true, UsesRealWorldSymbols: false),
            NarrativeClueKeys: ["clue.marco_torto.fresh_mortar", "clue.marco_torto.shifted_brass_cap"],
            PrimaryRegionIds: ["old_center", "waterfront", "serra"]),
        new(
            Id: FactionId.CartelDoCimento,
            Code: "cartel_do_cimento",
            NameKey: "faction.cimento.name",
            MotivationKey: "faction.cimento.motivation",
            SignatureAction: FactionSignatureActionKind.IrregularWorkBlockade,
            Emblem: new FactionEmblemPalette("#4A5759", "#EDAFB8", "emblem.cimento.mixer_cone", IsStrictlyFictional: true, UsesRealWorldSymbols: false),
            NarrativeClueKeys: ["clue.cimento.wet_curb_stamp", "clue.cimento.unlisted_mixer_cone"],
            PrimaryRegionIds: ["port", "hills", "serra"]),
        new(
            Id: FactionId.MafiaDoMapaVelho,
            Code: "mafia_do_mapa_velho",
            NameKey: "faction.mapa_velho.name",
            MotivationKey: "faction.mapa_velho.motivation",
            SignatureAction: FactionSignatureActionKind.CadastralRoadblock,
            Emblem: new FactionEmblemPalette("#5E503F", "#EAE0D5", "emblem.mapa_velho.sepia_scroll", IsStrictlyFictional: true, UsesRealWorldSymbols: false),
            NarrativeClueKeys: ["clue.mapa_velho.1974_blueprint_scrap", "clue.mapa_velho.padlocked_sauna_barrier"],
            PrimaryRegionIds: ["old_center", "port", "serra"]),
        new(
            Id: FactionId.SociedadeDaTerraPlana,
            Code: "sociedade_terra_plana",
            NameKey: "faction.terra_plana.name",
            MotivationKey: "faction.terra_plana.motivation",
            SignatureAction: FactionSignatureActionKind.GnssSpoofingAndPosterOnLens,
            Emblem: new FactionEmblemPalette("#2B2D42", "#8D99AE", "emblem.terra_plana.flat_disc", IsStrictlyFictional: true, UsesRealWorldSymbols: false),
            NarrativeClueKeys: ["clue.terra_plana.leaflet_on_lens", "clue.terra_plana.portable_beacon_hum"],
            PrimaryRegionIds: ["waterfront", "hills", "serra"]),
        new(
            Id: FactionId.MapaFacilSa,
            Code: "mapafacil_sa",
            NameKey: "faction.mapafacil.name",
            MotivationKey: "faction.mapafacil.motivation",
            SignatureAction: FactionSignatureActionKind.TirePunctureAndRivalBid,
            Emblem: new FactionEmblemPalette("#1D3557", "#A8DADC", "emblem.mapafacil.fast_pin", IsStrictlyFictional: true, UsesRealWorldSymbols: false),
            NarrativeClueKeys: ["clue.mapafacil.corporate_tire_strip", "clue.mapafacil.underbid_memo"],
            PrimaryRegionIds: ["old_center", "port", "waterfront", "serra"]),
    ];

    public static IReadOnlyList<TerritoryZoneDefinition> DefaultTerritoryZones { get; } =
    [
        new("zone_old_center_ring", "old_center", FactionId.MafiaDoMapaVelho, new TerritoryBoundsEnu(500320, 7400320, 500820, 7400820), ["marco_old_ring_01"], new Vector3d(0.0, 0.0, 0.0)),
        new("zone_old_center_marco", "old_center", FactionId.SindicatoDoMarcoTorto, new TerritoryBoundsEnu(500040, 7400420, 500300, 7400820), ["marco_zero_estacao", "marco_pracaprata_02"], new Vector3d(0.0, 0.0, 0.0)),
        new("zone_port_cimento", "port", FactionId.CartelDoCimento, new TerritoryBoundsEnu(500040, 7401040, 500480, 7401520), ["marco_doca_04"], new Vector3d(0.0, 0.0, 0.0)),
        new("zone_port_mapafacil", "port", FactionId.MapaFacilSa, new TerritoryBoundsEnu(500500, 7401040, 500980, 7401960), [], new Vector3d(0.0, 0.0, 0.0)),
        new("zone_waterfront_terra_plana", "waterfront", FactionId.SociedadeDaTerraPlana, new TerritoryBoundsEnu(501040, 7400040, 501500, 7400500), [], new Vector3d(18.0, -14.0, 4.5)),
        new("zone_waterfront_marco", "waterfront", FactionId.SindicatoDoMarcoTorto, new TerritoryBoundsEnu(501520, 7400120, 501960, 7400920), ["marco_orla_maregrafo"], new Vector3d(0.0, 0.0, 0.0)),
        new("zone_hills_cimento", "hills", FactionId.CartelDoCimento, new TerritoryBoundsEnu(501050, 7401050, 501500, 7401500), ["marco_serra_oeste"], new Vector3d(0.0, 0.0, 0.0)),
        new("zone_hills_terra_plana", "hills", FactionId.SociedadeDaTerraPlana, new TerritoryBoundsEnu(501520, 7401050, 501960, 7401900), [], new Vector3d(-22.0, 16.0, 6.0)),
        new("zone_serra_cimento", "serra", FactionId.CartelDoCimento, new TerritoryBoundsEnu(501050, 7401050, 501480, 7401480), ["survey_control_serra_foothill"], new Vector3d(0.0, 0.0, 0.0)),
        new("zone_serra_terra_plana", "serra", FactionId.SociedadeDaTerraPlana, new TerritoryBoundsEnu(501520, 7401050, 501960, 7401520), [], new Vector3d(-22.0, 16.0, 6.0)),
        new("zone_serra_divisas_marco", "serra", FactionId.SindicatoDoMarcoTorto, new TerritoryBoundsEnu(501280, 7401540, 501940, 7401940), ["survey_control_serra_summit", "serra_divisa_marco_01", "serra_divisa_marco_02", "serra_divisa_marco_03", "serra_divisa_marco_04", "serra_divisa_marco_05", "serra_divisa_marco_06"], new Vector3d(0.0, 0.0, 0.0)),
        new("zone_serra_tunnel_mafia", "serra", FactionId.MafiaDoMapaVelho, new TerritoryBoundsEnu(501020, 7401680, 501260, 7401960), [], new Vector3d(0.0, 0.0, 0.0)),
        new("zone_serra_ridge_mapafacil", "serra", FactionId.MapaFacilSa, new TerritoryBoundsEnu(501020, 7401490, 501270, 7401670), [], new Vector3d(0.0, 0.0, 0.0)),
    ];

    public static FactionDefinition GetFaction(FactionId id) =>
        AllFactions.FirstOrDefault(f => f.Id == id)
        ?? throw new KeyNotFoundException($"Unknown faction '{id}'.");

    public static TerritoryZoneDefinition GetZone(string zoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);
        return DefaultTerritoryZones.FirstOrDefault(z => string.Equals(z.ZoneId, zoneId, StringComparison.Ordinal))
               ?? throw new KeyNotFoundException($"Unknown territory zone '{zoneId}'.");
    }

    public static IReadOnlyList<TerritoryZoneDefinition> FindZonesContaining(string regionId, double east, double north)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionId);
        return DefaultTerritoryZones
            .Where(z => string.Equals(z.RegionId, regionId, StringComparison.Ordinal) && z.Bounds.Contains(east, north))
            .ToArray();
    }

    public static IReadOnlyList<TerritoryZoneDefinition> GetZonesForPlanningMap(IReadOnlyCollection<string> visibleRegionIds)
    {
        ArgumentNullException.ThrowIfNull(visibleRegionIds);
        return DefaultTerritoryZones
            .Where(z => visibleRegionIds.Contains(z.RegionId, StringComparer.Ordinal))
            .ToArray();
    }

    public static FactionIntegrityReport ValidateFictionalIntegrity()
    {
        var errors = new List<string>();
        if (AllFactions.Count != 5)
            errors.Add($"Expected 5 fictional factions, found {AllFactions.Count}.");

        var seenCodes = new HashSet<string>(StringComparer.Ordinal);
        var seenEmblems = new HashSet<string>(StringComparer.Ordinal);
        foreach (var faction in AllFactions)
        {
            if (!seenCodes.Add(faction.Code))
                errors.Add($"Duplicate faction code '{faction.Code}'.");
            if (!faction.Emblem.IsStrictlyFictional || faction.Emblem.UsesRealWorldSymbols)
                errors.Add($"Faction '{faction.Code}' emblem must be strictly fictional without real-world symbols.");
            if (!seenEmblems.Add(faction.Emblem.EmblemKey))
                errors.Add($"Duplicate emblem '{faction.Emblem.EmblemKey}'.");
            if (faction.NarrativeClueKeys.Count == 0)
                errors.Add($"Faction '{faction.Code}' missing narrative clue keys.");
            if (faction.PrimaryRegionIds.Count == 0)
                errors.Add($"Faction '{faction.Code}' missing primary regions.");
        }

        return new FactionIntegrityReport(
            Valid: errors.Count == 0,
            FactionCount: AllFactions.Count,
            TerritoryCount: DefaultTerritoryZones.Count,
            PlayerWeaponsAllowed: PlayerWeaponsAllowed,
            Errors: Array.AsReadOnly(errors.ToArray()));
    }
}

public enum TerritoryThreatLevel
{
    None = 0,
    Level1SpotterWarning = 1,
    Level2PaintAttack = 2,
    Level3TirePunctureOrPursuit = 3,
    Level4RoadblockOrPursuit = 4,
    Level5ParkedBombAttempt = 5,
}

public sealed record TerritoryZoneSnapshot(
    string ZoneId,
    double AttentionScore,
    double CooldownRemainingSeconds,
    double IdleCaptureSeconds);

public sealed record TerritoryPendingThreatSnapshot(string? WarningZoneId, TerritoryThreatLevel WarningLevel,
    double WarningElapsedSeconds, string? BombZoneId, double StationarySeconds, bool BombActive,
    double BombCountdownSeconds, double DisarmProgressSeconds);

public sealed record TerritoryAttentionSnapshot(
    int SchemaVersion,
    IReadOnlyList<TerritoryZoneSnapshot> Zones, TerritoryPendingThreatSnapshot? Pending = null);

public sealed record TerritoryEscalationOutcome(
    string? ZoneId,
    FactionId? ControllingFaction,
    double AttentionScore,
    TerritoryThreatLevel ThreatLevel,
    bool WarningIssued,
    double WarningLeadTimeSeconds,
    bool ActionTriggered,
    TerritoryThreatLevel? TriggeredActionLevel,
    bool BombWarningActive,
    double BombRemainingCountdownSeconds,
    bool BombDetonated,
    bool BombDisarmed,
    bool SuppressedByCalmCity,
    double CooldownRemainingSeconds,
    bool PlayerCanUseWeapons,
    string ReasonKey);

/// <summary>Tracks graduated [0..100] territory attention, 30s cooldowns, >=3s warnings, Levels 1..5 escalation, bomb disarm, and Calm City cancellation (T-F16-02).</summary>
public sealed class TerritoryAttentionTracker
{
    public const double GainRatePerSecondActiveCapture = 1.0;
    public const double DecayRatePerSecond = 2.0;
    public const double IdleCaptureGraceSeconds = 10.0;
    public const double ActionCooldownSeconds = 30.0;
    public const double MinimumWarningLeadTimeSeconds = 3.0;
    public const double BombRequiredStationarySeconds = 15.0;
    public const double BombWarningCountdownSeconds = 10.0;
    public const double BombDisarmInteractionSeconds = 5.0;
    public const double StationarySpeedThresholdMps = 0.15;

    private readonly Dictionary<string, double> _attentionByZone = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _cooldownByZone = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _idleSinceCaptureByZone = new(StringComparer.Ordinal);

    private string? _warningZoneId;
    private TerritoryThreatLevel _warningLevel;
    private double _warningElapsedSeconds;

    private string? _bombZoneId;
    private double _stationaryAtLevel5Seconds;
    private bool _bombWarningActive;
    private double _bombCountdownSeconds;
    private double _bombDisarmProgressSeconds;

    public TerritoryAttentionTracker(IEnumerable<TerritoryZoneDefinition>? zones = null)
    {
        foreach (var zone in zones ?? FactionCatalog.DefaultTerritoryZones)
        {
            _attentionByZone[zone.ZoneId] = 0.0;
            _cooldownByZone[zone.ZoneId] = 0.0;
            _idleSinceCaptureByZone[zone.ZoneId] = 0.0;
        }
    }

    public double GetAttention(string zoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);
        return _attentionByZone.GetValueOrDefault(zoneId, 0.0);
    }

    public double GetCooldownRemaining(string zoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneId);
        return _cooldownByZone.GetValueOrDefault(zoneId, 0.0);
    }

    public static TerritoryThreatLevel ClassifyThreatLevel(double attentionScore) => attentionScore switch
    {
        >= 100.0 => TerritoryThreatLevel.Level5ParkedBombAttempt,
        >= 80.0 => TerritoryThreatLevel.Level4RoadblockOrPursuit,
        >= 60.0 => TerritoryThreatLevel.Level3TirePunctureOrPursuit,
        >= 40.0 => TerritoryThreatLevel.Level2PaintAttack,
        >= 20.0 => TerritoryThreatLevel.Level1SpotterWarning,
        _ => TerritoryThreatLevel.None,
    };

    public TerritoryEscalationOutcome Step(
        string? currentZoneId,
        bool activeCapture,
        double playerSpeedMps,
        bool disarmInputHeld,
        double dtSeconds,
        CalmPolicyCoordinator? calmPolicy = null, double attentionGainMultiplier = 1.0, bool suppressThreats = false)
    {
        if (!double.IsFinite(attentionGainMultiplier) || attentionGainMultiplier < 0 || attentionGainMultiplier > 1)
            throw new ArgumentOutOfRangeException(nameof(attentionGainMultiplier));
        if (!double.IsFinite(playerSpeedMps) || playerSpeedMps < 0.0)
            throw new ArgumentOutOfRangeException(nameof(playerSpeedMps));
        if (!double.IsFinite(dtSeconds) || dtSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(dtSeconds));

        if (!string.IsNullOrWhiteSpace(currentZoneId) && !_attentionByZone.ContainsKey(currentZoneId))
        {
            _attentionByZone[currentZoneId] = 0.0;
            _cooldownByZone[currentZoneId] = 0.0;
            _idleSinceCaptureByZone[currentZoneId] = 0.0;
        }

        // Tick cooldowns and out-of-zone decay across all registered zones.
        foreach (var zoneId in _attentionByZone.Keys.ToArray())
        {
            if (_cooldownByZone[zoneId] > 0.0)
                _cooldownByZone[zoneId] = Math.Max(0.0, _cooldownByZone[zoneId] - dtSeconds);

            var isCurrent = string.Equals(zoneId, currentZoneId, StringComparison.Ordinal);
            if (!isCurrent)
            {
                _attentionByZone[zoneId] = Math.Max(0.0, _attentionByZone[zoneId] - DecayRatePerSecond * dtSeconds);
                _idleSinceCaptureByZone[zoneId] = 0.0;
            }
        }

        // Calm City suppresses and cancels all active warnings, pursuits, roadblocks, and bombs immediately.
        if (calmPolicy is { IsProtectionActive: true } || suppressThreats)
        {
            var wasBombActive = _bombWarningActive;
            ClearPendingThreatState();
            if (!string.IsNullOrWhiteSpace(currentZoneId))
                _attentionByZone[currentZoneId] = Math.Max(0.0, _attentionByZone[currentZoneId] - DecayRatePerSecond * dtSeconds);

            var currentAttentionCalm = !string.IsNullOrWhiteSpace(currentZoneId) ? _attentionByZone[currentZoneId] : 0.0;
            return new TerritoryEscalationOutcome(
                ZoneId: currentZoneId,
                ControllingFaction: TryResolveFaction(currentZoneId),
                AttentionScore: currentAttentionCalm,
                ThreatLevel: ClassifyThreatLevel(currentAttentionCalm),
                WarningIssued: false,
                WarningLeadTimeSeconds: 0.0,
                ActionTriggered: false,
                TriggeredActionLevel: null,
                BombWarningActive: false,
                BombRemainingCountdownSeconds: 0.0,
                BombDetonated: false,
                BombDisarmed: false,
                SuppressedByCalmCity: true,
                CooldownRemainingSeconds: !string.IsNullOrWhiteSpace(currentZoneId) ? _cooldownByZone[currentZoneId] : 0.0,
                PlayerCanUseWeapons: false,
                ReasonKey: wasBombActive ? "faction.bomb.cancelled_by_calm" : "faction.threat.suppressed_by_calm");
        }

        // Leaving the zone cancels pending warnings and any parked-bomb warning immediately.
        if (string.IsNullOrWhiteSpace(currentZoneId))
        {
            var bombCancelled = _bombWarningActive;
            ClearPendingThreatState();
            return new TerritoryEscalationOutcome(
                ZoneId: null,
                ControllingFaction: null,
                AttentionScore: 0.0,
                ThreatLevel: TerritoryThreatLevel.None,
                WarningIssued: false,
                WarningLeadTimeSeconds: 0.0,
                ActionTriggered: false,
                TriggeredActionLevel: null,
                BombWarningActive: false,
                BombRemainingCountdownSeconds: 0.0,
                BombDetonated: false,
                BombDisarmed: false,
                SuppressedByCalmCity: false,
                CooldownRemainingSeconds: 0.0,
                PlayerCanUseWeapons: false,
                ReasonKey: bombCancelled ? "faction.bomb.aborted_left_territory" : "faction.zone.outside");
        }

        if (_bombZoneId is not null && !string.Equals(_bombZoneId, currentZoneId, StringComparison.Ordinal))
        {
            _bombZoneId = null;
            _bombWarningActive = false;
            _bombCountdownSeconds = 0.0;
            _stationaryAtLevel5Seconds = 0.0;
            _bombDisarmProgressSeconds = 0.0;
        }

        if (activeCapture)
        {
            _idleSinceCaptureByZone[currentZoneId] = 0.0;
            _attentionByZone[currentZoneId] = Math.Min(100.0, _attentionByZone[currentZoneId] + GainRatePerSecondActiveCapture * dtSeconds * attentionGainMultiplier);
        }
        else
        {
            _idleSinceCaptureByZone[currentZoneId] += dtSeconds;
            if (_idleSinceCaptureByZone[currentZoneId] >= IdleCaptureGraceSeconds)
                _attentionByZone[currentZoneId] = Math.Max(0.0, _attentionByZone[currentZoneId] - DecayRatePerSecond * dtSeconds);
        }

        var attention = _attentionByZone[currentZoneId];
        var level = ClassifyThreatLevel(attention);
        var cooldown = _cooldownByZone[currentZoneId];
        var faction = TryResolveFaction(currentZoneId);
        var isStationary = playerSpeedMps <= StationarySpeedThresholdMps;

        // Level 5 parked-vehicle bomb logic takes priority once bomb warning is active or stationary threshold reached.
        if (level == TerritoryThreatLevel.Level5ParkedBombAttempt)
        {
            if (_bombWarningActive)
            {
                if (disarmInputHeld && isStationary)
                {
                    _bombDisarmProgressSeconds += dtSeconds;
                    if (_bombDisarmProgressSeconds >= BombDisarmInteractionSeconds)
                    {
                        _bombWarningActive = false;
                        _bombZoneId = null;
                        _bombCountdownSeconds = 0.0;
                        _stationaryAtLevel5Seconds = 0.0;
                        _bombDisarmProgressSeconds = 0.0;
                        _cooldownByZone[currentZoneId] = ActionCooldownSeconds;
                        return new TerritoryEscalationOutcome(
                            currentZoneId, faction, attention, level,
                            WarningIssued: false, WarningLeadTimeSeconds: 0.0,
                            ActionTriggered: false, TriggeredActionLevel: null,
                            BombWarningActive: false, BombRemainingCountdownSeconds: 0.0,
                            BombDetonated: false, BombDisarmed: true,
                            SuppressedByCalmCity: false, CooldownRemainingSeconds: ActionCooldownSeconds,
                            PlayerCanUseWeapons: false, ReasonKey: "faction.bomb.disarmed");
                    }
                }
                else
                {
                    _bombDisarmProgressSeconds = 0.0;
                }

                _bombCountdownSeconds -= dtSeconds;
                if (_bombCountdownSeconds <= 0.0)
                {
                    _bombWarningActive = false;
                    _bombZoneId = null;
                    _bombCountdownSeconds = 0.0;
                    _stationaryAtLevel5Seconds = 0.0;
                    _bombDisarmProgressSeconds = 0.0;
                    _cooldownByZone[currentZoneId] = ActionCooldownSeconds;
                    return new TerritoryEscalationOutcome(
                        currentZoneId, faction, attention, level,
                        WarningIssued: true, WarningLeadTimeSeconds: BombWarningCountdownSeconds,
                        ActionTriggered: true, TriggeredActionLevel: TerritoryThreatLevel.Level5ParkedBombAttempt,
                        BombWarningActive: false, BombRemainingCountdownSeconds: 0.0,
                        BombDetonated: true, BombDisarmed: false,
                        SuppressedByCalmCity: false, CooldownRemainingSeconds: ActionCooldownSeconds,
                        PlayerCanUseWeapons: false, ReasonKey: "faction.bomb.detonated_session_failed");
                }

                return new TerritoryEscalationOutcome(
                    currentZoneId, faction, attention, level,
                    WarningIssued: true, WarningLeadTimeSeconds: BombWarningCountdownSeconds - _bombCountdownSeconds,
                    ActionTriggered: false, TriggeredActionLevel: null,
                    BombWarningActive: true, BombRemainingCountdownSeconds: _bombCountdownSeconds,
                    BombDetonated: false, BombDisarmed: false,
                    SuppressedByCalmCity: false, CooldownRemainingSeconds: cooldown,
                    PlayerCanUseWeapons: false, ReasonKey: "faction.bomb.warning");
            }

            if (isStationary)
                _stationaryAtLevel5Seconds += dtSeconds;
            else
                _stationaryAtLevel5Seconds = 0.0;

            if (_stationaryAtLevel5Seconds >= BombRequiredStationarySeconds && cooldown <= 0.0)
            {
                _bombZoneId = currentZoneId;
                _bombWarningActive = true;
                _bombCountdownSeconds = BombWarningCountdownSeconds;
                _bombDisarmProgressSeconds = 0.0;
                return new TerritoryEscalationOutcome(
                    currentZoneId, faction, attention, level,
                    WarningIssued: true, WarningLeadTimeSeconds: 0.0,
                    ActionTriggered: false, TriggeredActionLevel: null,
                    BombWarningActive: true, BombRemainingCountdownSeconds: _bombCountdownSeconds,
                    BombDetonated: false, BombDisarmed: false,
                    SuppressedByCalmCity: false, CooldownRemainingSeconds: cooldown,
                    PlayerCanUseWeapons: false, ReasonKey: "faction.bomb.warning");
            }

            if (isStationary)
            {
                ResetPreActionWarning();
                return new TerritoryEscalationOutcome(
                    currentZoneId, faction, attention, level,
                    WarningIssued: true, WarningLeadTimeSeconds: 0.0,
                    ActionTriggered: false, TriggeredActionLevel: null,
                    BombWarningActive: false, BombRemainingCountdownSeconds: 0.0,
                    BombDetonated: false, BombDisarmed: false,
                    SuppressedByCalmCity: false, CooldownRemainingSeconds: cooldown,
                    PlayerCanUseWeapons: false, ReasonKey: "faction.attention.warning");
            }
        }
        else
        {
            _stationaryAtLevel5Seconds = 0.0;
            if (_bombWarningActive)
            {
                _bombWarningActive = false;
                _bombZoneId = null;
                _bombCountdownSeconds = 0.0;
                _bombDisarmProgressSeconds = 0.0;
            }
        }

        if (level == TerritoryThreatLevel.None)
        {
            ResetPreActionWarning();
            return new TerritoryEscalationOutcome(
                currentZoneId, faction, attention, level,
                WarningIssued: false, WarningLeadTimeSeconds: 0.0,
                ActionTriggered: false, TriggeredActionLevel: null,
                BombWarningActive: false, BombRemainingCountdownSeconds: 0.0,
                BombDetonated: false, BombDisarmed: false,
                SuppressedByCalmCity: false, CooldownRemainingSeconds: cooldown,
                PlayerCanUseWeapons: false, ReasonKey: "faction.attention.calm");
        }

        if (level == TerritoryThreatLevel.Level1SpotterWarning)
        {
            ResetPreActionWarning();
            return new TerritoryEscalationOutcome(
                currentZoneId, faction, attention, level,
                WarningIssued: true, WarningLeadTimeSeconds: 0.0,
                ActionTriggered: false, TriggeredActionLevel: null,
                BombWarningActive: false, BombRemainingCountdownSeconds: 0.0,
                BombDetonated: false, BombDisarmed: false,
                SuppressedByCalmCity: false, CooldownRemainingSeconds: cooldown,
                PlayerCanUseWeapons: false, ReasonKey: "faction.attention.warning");
        }

        if (cooldown > 0.0)
        {
            ResetPreActionWarning();
            return new TerritoryEscalationOutcome(
                currentZoneId, faction, attention, level,
                WarningIssued: false, WarningLeadTimeSeconds: 0.0,
                ActionTriggered: false, TriggeredActionLevel: null,
                BombWarningActive: false, BombRemainingCountdownSeconds: 0.0,
                BombDetonated: false, BombDisarmed: false,
                SuppressedByCalmCity: false, CooldownRemainingSeconds: cooldown,
                PlayerCanUseWeapons: false, ReasonKey: "faction.attention.cooldown_active");
        }

        if (!string.Equals(_warningZoneId, currentZoneId, StringComparison.Ordinal))
        {
            _warningZoneId = currentZoneId;
            _warningLevel = level;
            _warningElapsedSeconds = 0.0;
            return new TerritoryEscalationOutcome(
                currentZoneId, faction, attention, level,
                WarningIssued: true, WarningLeadTimeSeconds: 0.0,
                ActionTriggered: false, TriggeredActionLevel: null,
                BombWarningActive: false, BombRemainingCountdownSeconds: 0.0,
                BombDetonated: false, BombDisarmed: false,
                SuppressedByCalmCity: false, CooldownRemainingSeconds: cooldown,
                PlayerCanUseWeapons: false, ReasonKey: "faction.attention.warning");
        }

        if (level > _warningLevel)
            _warningLevel = level;

        _warningElapsedSeconds += dtSeconds;
        if (_warningElapsedSeconds < MinimumWarningLeadTimeSeconds)
        {
            return new TerritoryEscalationOutcome(
                currentZoneId, faction, attention, level,
                WarningIssued: true, WarningLeadTimeSeconds: _warningElapsedSeconds,
                ActionTriggered: false, TriggeredActionLevel: null,
                BombWarningActive: false, BombRemainingCountdownSeconds: 0.0,
                BombDetonated: false, BombDisarmed: false,
                SuppressedByCalmCity: false, CooldownRemainingSeconds: cooldown,
                PlayerCanUseWeapons: false, ReasonKey: "faction.attention.warning");
        }

        var triggeredLevel = level == TerritoryThreatLevel.Level5ParkedBombAttempt
            ? TerritoryThreatLevel.Level4RoadblockOrPursuit
            : level;
        var leadTime = _warningElapsedSeconds;
        ResetPreActionWarning();
        _cooldownByZone[currentZoneId] = ActionCooldownSeconds;

        return new TerritoryEscalationOutcome(
            currentZoneId, faction, attention, level,
            WarningIssued: true, WarningLeadTimeSeconds: leadTime,
            ActionTriggered: true, TriggeredActionLevel: triggeredLevel,
            BombWarningActive: false, BombRemainingCountdownSeconds: 0.0,
            BombDetonated: false, BombDisarmed: false,
            SuppressedByCalmCity: false, CooldownRemainingSeconds: ActionCooldownSeconds,
            PlayerCanUseWeapons: false, ReasonKey: triggeredLevel switch
            {
                TerritoryThreatLevel.Level2PaintAttack => "faction.action.paint_attack",
                TerritoryThreatLevel.Level3TirePunctureOrPursuit => "faction.action.tire_puncture_or_pursuit",
                _ => "faction.action.roadblock_or_pursuit",
            });
    }

    public static UrbanHazardOutcome? ApplyTriggeredActionToVehicle(
        TerritoryEscalationOutcome outcome,
        UrbanHazardEncounterCoordinator coordinator,
        ulong impactSeed = 303UL)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(coordinator);

        if (!outcome.ActionTriggered || outcome.TriggeredActionLevel is null)
            return null;

        var idPrefix = outcome.ZoneId ?? "unknown_zone";
        return outcome.TriggeredActionLevel.Value switch
        {
            TerritoryThreatLevel.Level2PaintAttack =>
                coordinator.TriggerHazard($"{idPrefix}_paint", UrbanHazardKind.SabotageVandalism, impactSeed),
            TerritoryThreatLevel.Level3TirePunctureOrPursuit =>
                coordinator.TriggerHazard($"{idPrefix}_puncture", UrbanHazardKind.ErraticVehicleCollision, impactSeed, deltaVMetresPerSecond: 3.1),
            TerritoryThreatLevel.Level4RoadblockOrPursuit =>
                coordinator.TriggerHazard($"{idPrefix}_roadblock", UrbanHazardKind.ErraticVehicleCollision, impactSeed, deltaVMetresPerSecond: 3.8),
            TerritoryThreatLevel.Level5ParkedBombAttempt =>
                coordinator.TriggerHazard($"{idPrefix}_bomb", UrbanHazardKind.FireworksOrBomb, impactSeed, deltaVMetresPerSecond: 4.5),
            _ => null,
        };
    }

    public TerritoryAttentionSnapshot ExportSnapshot()
    {
        var zones = _attentionByZone.Keys
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(z => new TerritoryZoneSnapshot(z, _attentionByZone[z], _cooldownByZone[z], _idleSinceCaptureByZone[z]))
            .ToArray();
        return new TerritoryAttentionSnapshot(1, zones, new(_warningZoneId, _warningLevel, _warningElapsedSeconds,
            _bombZoneId, _stationaryAtLevel5Seconds, _bombWarningActive, _bombCountdownSeconds, _bombDisarmProgressSeconds));
    }

    public void ImportSnapshot(TerritoryAttentionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion != 1 || snapshot.Zones is null ||
            snapshot.Zones.Select(z => z.ZoneId).Distinct(StringComparer.Ordinal).Count() != snapshot.Zones.Count)
            throw new ArgumentException("Invalid attention snapshot.", nameof(snapshot));
        static bool Timer(double value) => double.IsFinite(value) && value >= 0;
        foreach (var z in snapshot.Zones)
            if (string.IsNullOrWhiteSpace(z.ZoneId) || !Timer(z.AttentionScore) || z.AttentionScore > 100 ||
                !Timer(z.CooldownRemainingSeconds) || !Timer(z.IdleCaptureSeconds)) throw new ArgumentException("Invalid territory state.");
        if (snapshot.Pending is { } pending && (!Enum.IsDefined(pending.WarningLevel) ||
            !Timer(pending.WarningElapsedSeconds) || !Timer(pending.StationarySeconds) || !Timer(pending.BombCountdownSeconds) ||
            pending.BombCountdownSeconds > BombWarningCountdownSeconds || !Timer(pending.DisarmProgressSeconds) ||
            pending.DisarmProgressSeconds > BombDisarmInteractionSeconds ||
            (pending.WarningZoneId is { } w && !snapshot.Zones.Any(z => z.ZoneId == w)) ||
            (pending.BombZoneId is { } b && !snapshot.Zones.Any(z => z.ZoneId == b)) ||
            (pending.BombActive && (pending.BombZoneId is null || pending.BombCountdownSeconds <= 0))))
            throw new ArgumentException("Invalid pending threat.", nameof(snapshot));
        foreach (var entry in snapshot.Zones)
        {
            _attentionByZone[entry.ZoneId] = entry.AttentionScore;
            _cooldownByZone[entry.ZoneId] = entry.CooldownRemainingSeconds;
            _idleSinceCaptureByZone[entry.ZoneId] = entry.IdleCaptureSeconds;
        }
        ClearPendingThreatState();
        if (snapshot.Pending is { } p)
        {
            _warningZoneId = p.WarningZoneId; _warningLevel = p.WarningLevel; _warningElapsedSeconds = p.WarningElapsedSeconds;
            _bombZoneId = p.BombZoneId; _stationaryAtLevel5Seconds = p.StationarySeconds; _bombWarningActive = p.BombActive;
            _bombCountdownSeconds = p.BombCountdownSeconds; _bombDisarmProgressSeconds = p.DisarmProgressSeconds;
        }
    }

    public void CancelPendingThreats() => ClearPendingThreatState();

    private void ResetPreActionWarning()
    {
        _warningZoneId = null;
        _warningLevel = TerritoryThreatLevel.None;
        _warningElapsedSeconds = 0.0;
    }

    private void ClearPendingThreatState()
    {
        ResetPreActionWarning();
        _bombZoneId = null;
        _stationaryAtLevel5Seconds = 0.0;
        _bombWarningActive = false;
        _bombCountdownSeconds = 0.0;
        _bombDisarmProgressSeconds = 0.0;
    }

    private static FactionId? TryResolveFaction(string? zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId))
            return null;
        return FactionCatalog.DefaultTerritoryZones
            .FirstOrDefault(z => string.Equals(z.ZoneId, zoneId, StringComparison.Ordinal))
            ?.ControllingFaction;
    }
}
