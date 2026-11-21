using Gsa.Core.Mobility;
using Gsa.Core.Traffic;
using Gsa.Missions.Economy;
using Gsa.Missions.Persistence;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private readonly List<(Button Button, string Key)> _hazardButtons = [];
    private void BuildCityHazardControls(VBoxContainer tab)
    {
        _hazardStatus = TechnicalLabel(); tab.AddChild(_hazardStatus);
        _protectionZone = new OptionButton { FitToLongestItem = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        tab.AddChild(_protectionZone);
        void Button(string key, Action action)
        {
            var button = new Button { Text = Text(key) }; _hazardButtons.Add((button, key));
            button.Pressed += () => { action(); RefreshTabletContents(); }; tab.AddChild(button);
        }
        Button("city.hazards.upload", OffloadCityData);
        Button("city.hazards.clean", () => _cleanLensRequested = true);
        Button("city.hazards.disarm", () => _disarmRequested = true);
        Button("city.hazards.disguise", () => BuyCityProtection(ServiceKind.VehicleDisguise));
        Button("city.hazards.escort", () => BuyCityProtection(ServiceKind.SecurityEscort));
        Button("city.hazards.authorize", () => BuyCityProtection(ServiceKind.ControlledZoneAuthorization));
        Button("city.hazards.insurance", BuyCityInsurance);
        Button("city.hazards.repair", RepairCityVehicle);
        Button("city.hazards.abandon", AbandonCityContract);
        BuildCityEverydayControls(tab);
        RefreshCityHazardLocale();
    }

    private void RefreshCityHazardLocale()
    {
        foreach (var (button, key) in _hazardButtons) button.Text = Text(key);
        var selected = Math.Max(0, _protectionZone.Selected); _protectionZone.Clear();
        foreach (var zone in _planningZones)
            _protectionZone.AddItem(Text(FactionCatalog.GetFaction(zone.ControllingFaction).NameKey) + " · " + Text("city.map." + zone.RegionId));
        _protectionZone.Select(Math.Min(selected, _planningZones.Count - 1)); RefreshCityHazardStatus();
        RefreshCityEverydayLocale();
    }

    private void RefreshCityHazardStatus()
    {
        RefreshCityEverydayStatus();
        if (_hazardStatus is null) return;
        _hazardStatus.Text = string.Format(Text("city.hazards.status"), _fieldSamples.Count, _protectedCityPhotos.Count,
            TechnicalNumber(_cityHazards.Lens.ObstructionFraction * 100, 0),
            Text(_cityHazards.TirePunctured ? "city.hazards.tyre_bad" : "city.hazards.tyre_ok")) + "\n" + _hazardNotice;
        if (_cityHazards.DisabledVehicle is { } disabled)
        {
            var p = PlatformProfile.ForMode(disabled); var quote = _cityHazards.Services.QuoteService(ServiceKind.InsuranceClaim, p);
            _hazardStatus.Text += "\n" + string.Format(Text("city.hazards.replacement_cost"), Text("platform." + p.Id + ".name"),
                quote.Eligible ? quote.FinalFeeCoords : p.PriceCoords);
        }
    }

    private void BuyCityProtection(ServiceKind kind)
    {
        SyncCityHazardProtection();
        if (_activeContract is null) { _feedbackText = Text("city.office.no_contract"); return; }
        if (_calmPolicy.IsProtectionActive) { _feedbackText = Text("city.hazards.no_protection_fee"); return; }
        var zone = _planningZones[Math.Max(0, _protectionZone.Selected)];
        var prior = _cityHazards.Sabotage.GetZoneState(zone.ZoneId);
        if ((kind == ServiceKind.VehicleDisguise && prior.DisguiseActive && !prior.DisguiseBrokenByAttack) ||
            (kind == ServiceKind.SecurityEscort && prior.EscortActive) ||
            (kind == ServiceKind.ControlledZoneAuthorization && prior.CommunityAuthorizationActive))
        { _feedbackText = Text("city.hazards.protection_active"); return; }
        var platform = PlatformProfile.ForMode(_locomotion.MountedVehicleMode);
        if (kind == ServiceKind.VehicleDisguise && platform.Mode != LocomotionMode.Kombi)
        { _feedbackText = Text("city.hazards.kombi_required"); return; }
        var id = _cityHazards.NextEventId(); ServiceExecutionResult? result;
        if (kind == ServiceKind.VehicleDisguise)
            result = _cityHazards.Sabotage.ActivateKombiDisguise(zone.ZoneId, id, _calendar.CalendarTimeSeconds,
                _cityHazards.Services, platform, _ledger, _calmPolicy);
        else if (kind == ServiceKind.SecurityEscort)
            result = _cityHazards.Sabotage.ActivateEscort(zone.ZoneId, id, _calendar.CalendarTimeSeconds,
                _cityHazards.Services, platform, _ledger, _activeContract.Reward.BaseCoords, _calmPolicy);
        else
        {
            var authorization = _cityHazards.Sabotage.NegotiateCommunityAuthorization(zone.ZoneId,
                FactionSabotageAndCountermeasuresCoordinator.LegitimateCommunityAssociationId, 1, _fleet,
                _cityHazards.Services, platform, _ledger, id, _calendar.CalendarTimeSeconds, _activeContract.Reward.BaseCoords, _calmPolicy);
            if (!authorization.Succeeded) { _feedbackText = Text("city.hazards.authorization_failed"); return; }
            result = authorization.ServiceResult;
            if (_cityHazards.ActiveZoneId == zone.ZoneId) { _cityHazards.CancelActiveThreats(); _attention.CancelPendingThreats(); ClearCityThreatVisuals(); }
        }
        _feedbackText = result is null || result.Succeeded ? Text("city.hazards.protection_active") : Text("city.hazards.insufficient");
        SaveHazardChange();
    }

    private void BuyCityInsurance()
    {
        if (_activeContract is null || _cityHazards.DisabledVehicle is not null || _locomotion.MountedVehicleMode < LocomotionMode.Car)
        { _feedbackText = Text("city.hazards.insurance_unavailable"); return; }
        var platform = PlatformProfile.ForMode(_locomotion.MountedVehicleMode);
        if (_cityHazards.Services.InsuranceActive && _cityHazards.Services.InsuredPlatformId == platform.Id)
        { _feedbackText = Text("city.hazards.insured"); return; }
        var result = _cityHazards.Services.ExecuteService(_cityHazards.NextEventId(), _calendar.CalendarTimeSeconds,
            ServiceKind.InsurancePolicy, platform, _ledger);
        _feedbackText = Text(result.Succeeded ? "city.hazards.insured" : "city.hazards.insufficient"); SaveHazardChange();
    }

    private void RepairCityVehicle()
    {
        if (_cityHazards.DisabledVehicle is { } mode)
        {
            if (!NearCityUnload()) { _feedbackText = Text("city.hazards.recover_location"); return; }
            var platform = PlatformProfile.ForMode(mode); var quote = _cityHazards.Services.QuoteService(ServiceKind.InsuranceClaim, platform);
            var id = _cityHazards.NextEventId();
            var paid = quote.Eligible
                ? _cityHazards.Services.SubmitTotalLossClaim(id, id, _calendar.CalendarTimeSeconds, platform, _ledger, null, out _).Succeeded
                : _ledger.TryDebitPurchaseOrService(id, id, _calendar.CalendarTimeSeconds, platform.PriceCoords,
                    "replacement", "city.hazards.replacement_cost").Succeeded;
            if (!paid) { _feedbackText = Text("city.hazards.insufficient"); return; }
            _cityHazards.DisabledVehicle = null; _cityHazards.Services.ExpireInsurance();
            _everyday.ClearCosmeticDamage(mode);
            _rackMisaligned = false; _cityHazards.RackYawErrorRadians = 0; SwitchPlatform(mode);
        }
        else if (_cityHazards.TirePunctured)
        {
            if (Math.Abs(_carSim?.SpeedMetresPerSecond ?? _locomotion.ForwardSpeedMetresPerSecond) >= .1)
            { _feedbackText = Text("city.hazards.stop_first"); return; }
            var id = _cityHazards.NextEventId();
            if (!_ledger.TryDebitPurchaseOrService(id, id, _calendar.CalendarTimeSeconds, 50, "repair", "city.hazards.tyre_bad").Succeeded)
            { _feedbackText = Text("city.hazards.insufficient"); return; }
            _cityHazards.TirePunctured = false;
        }
        _feedbackText = Text("city.hazards.repaired"); SaveHazardChange();
    }

    private void AbandonCityContract()
    {
        if (_activeContract is null) return;
        _activeMachine?.Abandon(_calendar.CalendarTimeSeconds);
        _activeContract = null; _cityPhotoCoverage = null; _activeMachine = null; _activeStorySpec = null;
        _locomotion.ReleaseCaptureUnits(CityLidarSlice.StorageUnits(_pendingLidarReturns));
        ClearPendingCityLidar();
        _lastCitySurveyReview = null;
        _lastSidewalkMeasurements = [];
        _cityHazards.Services.ExpireInsurance(); _cityHazards.BeginContract(); SyncCityHazardProtection();
        _feedbackText = Text("city.hazards.abandoned"); SaveHazardChange();
    }
}
