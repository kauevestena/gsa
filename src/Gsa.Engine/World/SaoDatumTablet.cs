using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Fusion;
using Gsa.Core.Sensors.Gnss;
using Gsa.Core.Traffic;
using Gsa.Core.World;
using Gsa.Engine.UI;
using Gsa.Missions.Activities;
using Gsa.Missions.Campaign;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Gameplay;
using Gsa.Missions.Office;
using Gsa.Missions.Persistence;
using Gsa.Missions.Presentation;
using Gsa.Missions.Quality;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private PanelContainer _tabletPanel = null!;
    private TabContainer _tabletTabs = null!;
    private VBoxContainer _contractsTab = null!;
    private Label _officeStatusLabel = null!;
    private VBoxContainer _fleetTab = null!;
    private CityFullMapControl _cityMap = null!;
    private Button _calmToggleBtn = null!;
    private Button _ditherToggleBtn = null!;
    private Button _daguerreotypeBtn = null!;
    private readonly List<RadarBlip> _radarBlips = new(32);
    private ulong _contractSeed = 101;
    private readonly List<GeneratedContractOffer> _offeredContracts = [];

    private void BuildHudAndTablet()
    {
        _gtaHud = new GsaGtaHud(this);
        _gtaHud.ConfigureRadar(_fullManifest, 125f);

        _gtaHud.AddQuickButton(Text("city.btn.tablet"), ToggleTablet);
        _gtaHud.AddQuickButton(Text("city.btn.capture"), () => _captureRequested = true);
        _gtaHud.AddQuickButton(Text("city.btn.vehicle"), CycleOwnedPlatform);
        _gtaHud.AddQuickButton(Text("city.btn.lidar"), () => _lidarEnabled = !_lidarEnabled);
        _gtaHud.AddQuickButton(Text("city.btn.view"), () => _chaseCamera.CycleMode());
        _gtaHud.AddQuickButton(Text("city.btn.radio"), () => _audio.CycleRadioStation());
        _gtaHud.AddQuickButton(Text("city.btn.save"), SaveCareer);

        GenerateContractOffers();
        BuildTabletOverlay(_gtaHud.Layer);
    }

    private void BuildTabletOverlay(CanvasLayer canvas)
    {
        _tabletPanel = new PanelContainer
        {
            Name = "CityTablet",
            AnchorLeft = 0.10f,
            AnchorTop = 0.06f,
            AnchorRight = 0.90f,
            AnchorBottom = 0.88f,
            Visible = false,
        };
        _tabletPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.06f, 0.08f, 0.96f),
            BorderColor = new Color(0.32f, 0.45f, 0.52f, 0.95f),
            BorderWidthBottom = 2,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        });
        canvas.AddChild(_tabletPanel);

        var rootBox = new VBoxContainer();
        _tabletPanel.AddChild(rootBox);

        var header = new HBoxContainer();
        rootBox.AddChild(header);
        var title = new Label
        {
            Text = Text("city.tablet.title"),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        title.AddThemeFontSizeOverride("font_size", 18);
        header.AddChild(title);
        var closeBtn = new Button { Text = Text("notebook.close") };
        closeBtn.Pressed += () => _tabletPanel.Hide();
        header.AddChild(closeBtn);

        _tabletTabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        rootBox.AddChild(_tabletTabs);

        VBoxContainer AddTab(string key)
        {
            var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            _tabletTabs.AddChild(scroll);
            _tabletTabs.SetTabTitle(_tabletTabs.GetTabCount() - 1, Text(key));
            var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            box.AddThemeConstantOverride("separation", 8);
            scroll.AddChild(box);
            return box;
        }

        // 1. Contracts Tab
        _contractsTab = AddTab("city.tab.contracts");
        // 2. Office & Calibration Tab
        var officeTab = AddTab("city.tab.office");
        _officeStatusLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        officeTab.AddChild(_officeStatusLabel);

        var officeBtns = new HBoxContainer();
        officeBtns.AddThemeConstantOverride("separation", 8);
        officeTab.AddChild(officeBtns);

        Button AddOfficeBtn(string key, Action action)
        {
            var b = new Button { Text = Text(key), FocusMode = Control.FocusModeEnum.None };
            b.Pressed += () => { action(); RefreshTabletContents(); };
            officeBtns.AddChild(b);
            return b;
        }
        AddOfficeBtn("city.office.mode_toggle", () =>
        {
            _officeMode = _officeMode == ProcessingMode.Arcade ? ProcessingMode.Technical : ProcessingMode.Arcade;
            _feedbackText = Text(_officeMode == ProcessingMode.Arcade ? "office.mode.arcade" : "office.mode.technical");
        });
        AddOfficeBtn("city.office.mask_privacy", () =>
        {
            _privacyMasksApplied = true;
            _audio.PlayOffloadChime();
            _feedbackText = Text("city.office.masks_applied");
        });
        AddOfficeBtn("city.office.calibrate_rack", () =>
        {
            _rackMisaligned = false; _cityHazards.RackYawErrorRadians = 0;
            _audio.PlayOffloadChime();
            _feedbackText = Text("city.office.rack_calibrated");
        });
        BuildCityHazardControls(officeTab);
        BuildViaductOfficeForm(officeTab);
        _rainFilterButton = AddOfficeBtn("city.rain_survey.enable_filter", ToggleRainSurveyFilter);

        AddOfficeBtn("city.office.process_deliver", ProcessAndDeliverCityContract);

        // 3. Fleet & Skills Tab
        _fleetTab = AddTab("city.tab.fleet");

        // 4. Full City Map Tab
        var mapTab = AddTab("notebook.tab.map");
        _cityMap = new CityFullMapControl { CustomMinimumSize = new Vector2(0, 340), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        mapTab.AddChild(_cityMap);
        _cityMapLegend = TechnicalLabel(Text("survey.map.legend"));
        mapTab.AddChild(_cityMapLegend);

        // 5. Geopédia & Collectibles Tab
        var geoTab = AddTab("city.tab.geopedia");
        BuildAdvancedGeopedia(geoTab);

        // 6. Settings & Sandbox Controls Tab
        var setTab = AddTab("city.tab.settings");
        _citySaveLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        setTab.AddChild(_citySaveLabel);
        _recoverCitySaveButton = new Button { Text = Text("save.ui.recover_previous"), FocusMode = Control.FocusModeEnum.None };
        _recoverCitySaveButton.Pressed += () => { LoadCityCareerState(recover: true); RefreshTabletContents(); };
        setTab.AddChild(_recoverCitySaveButton);

        _calmToggleBtn = new Button { FocusMode = Control.FocusModeEnum.None };
        _calmToggleBtn.Pressed += () =>
        {
            _calmPolicy.SetCalmCityEnabled(!_calmPolicy.CalmCityEnabled);
            SyncCityHazardProtection();
            RefreshTabletContents();
        };
        setTab.AddChild(_calmToggleBtn);

        _ditherToggleBtn = new Button { FocusMode = Control.FocusModeEnum.None };
        _ditherToggleBtn.Pressed += () =>
        {
            _atmosphere.SetRetroDitherEnabled(!_atmosphere.RetroDitherEnabled);
            RefreshTabletContents();
        };
        setTab.AddChild(_ditherToggleBtn);

        _daguerreotypeBtn = new Button { FocusMode = Control.FocusModeEnum.None };
        _daguerreotypeBtn.Pressed += () =>
        {
            _atmosphere.SetDaguerreotypeEnabled(!_atmosphere.DaguerreotypeEnabled);
            RefreshTabletContents();
        };
        setTab.AddChild(_daguerreotypeBtn);

        var rainBtn = new Button { Text = Text("city.settings.toggle_rain"), FocusMode = Control.FocusModeEnum.None };
        rainBtn.Pressed += ToggleCityRain;
        setTab.AddChild(rainBtn);

        var timeBtn = new Button { Text = Text("city.settings.advance_time"), FocusMode = Control.FocusModeEnum.None };
        timeBtn.Pressed += () => _calendar.AdvanceCalendarSeconds(7200.0);
        setTab.AddChild(timeBtn);

        // 7. Technical diagnostics. Reuses live snapshots; never approves scientific quality from a preview.
        BuildTechnicalTablet(AddTab("city.tab.technical"));

        RefreshTabletContents();
    }

    private void ToggleTablet()
    {
        _tabletPanel.Visible = !_tabletPanel.Visible;
        if (_tabletPanel.Visible)
            RefreshTabletContents();
    }

    private GeneratedContractOffer BuildCityContractOffer(string templateId, ulong seed)
    {
        var offer = MvpProceduralContractGenerator.GenerateFromTemplate(templateId, seed);
        var definition = offer.Contract;
        // The city clock runs 60×. Allow for returning to an upload point between passes.
        var due = Math.Max(definition.Deadline.DueAfterSeconds, 28800);
        return offer with { Contract = definition with
        {
            Requirements = CitySurveyRequirements(definition.AreaId, story: false),
            Deadline = definition.Deadline with { DueAfterSeconds = due, ExpiresAfterSeconds = due + 14400 },
        }};
    }

    private void GenerateContractOffers()
    {
        _offeredContracts.Clear();
        foreach (var template in MvpProceduralContractGenerator.Templates)
        {
            var offer = BuildCityContractOffer(template.TemplateId, _contractSeed++);
            if (!CityHasSurveyPlatform(offer.WitnessCertificate.RequiredPlatformLevel))
                offer = BuildCityContractOffer(template.TemplateId, _contractSeed++);
            if (CityHasSurveyPlatform(offer.WitnessCertificate.RequiredPlatformLevel))
                _offeredContracts.Add(offer);
        }
    }

    private bool TryAcceptCityProceduralOffer(GeneratedContractOffer offer)
    {
        if (!CityHasSurveyPlatform(offer.WitnessCertificate.RequiredPlatformLevel))
        {
            _feedbackText = Text("city.story.requires_platform");
            return false;
        }
        return AcceptCityContract(offer.Contract);
    }

    private void StartStoryMission(CampaignMissionFullSpec spec)
    {
        if (!SupportsCityStory(spec))
        {
            _feedbackText = Text(CityOfficeUnavailableKey(spec.ProductType));
            return;
        }
        if (!_isSandbox && spec.Preconditions.Any(id => !CityMissionCompleted(id)))
        {
            _feedbackText = Text("city.story.locked");
            return;
        }
        if (!CityHasSurveyPlatform(spec.MinimumPlatformLevel))
        {
            _feedbackText = Text("city.story.requires_platform");
            return;
        }

        var (prefix, count, area, client) = spec.MissionId switch
        {
            "HIST-01" => ("facade", 20, FacadeArea, "city_hall"),
            "HIST-02" => ("manhole", 12, DrainageArea, "city_hall"),
            "HIST-03" => ("sidewalk_seg", 8, CitySurveyInventory.SidewalkArea, "university"),
            "HIST-04" => ("sign", 12, CitySurveyInventory.SignsArea, "city_hall"),
            BusSurveyCorridor.MissionId => ("", 0, BusSurveyCorridor.AreaId, "city_hall"),
            PortViaductSurvey.MissionId => ("", 0, PortViaductSurvey.AreaId, "university"),
            WaterfrontRainSurvey.MissionId => ("", 0, WaterfrontRainSurvey.AreaId, "city_hall"),
            SerraBoundarySurvey.MissionId => ("", 0, SerraBoundarySurvey.AreaId, "city_hall"),
            PortWorksSurvey.MissionId => ("", 0, PortWorksSurvey.AreaId, "city_hall"),
            FinalCityPavementSurvey.MissionId => ("", 0, FinalCityPavementSurvey.AreaId, "city_hall"),
            _ => ("", 0, "port_tunnel", "university"),
        };
        var isBusSurvey = area == BusSurveyCorridor.AreaId;
        var isTunnel = spec.MissionId == TunnelMissionId;
        var isViaduct = spec.MissionId == PortViaductSurvey.MissionId;
        var isRain = spec.MissionId == WaterfrontRainSurvey.MissionId;
        var isBoundary = spec.MissionId == SerraBoundarySurvey.MissionId;
        var isWorks = spec.MissionId == PortWorksSurvey.MissionId;
        var isFinale = spec.MissionId == FinalCityPavementSurvey.MissionId;
        var featureIds = spec.MissionId switch
        {
            BusSurveyCorridor.MissionId => BusSurveyCorridor.Stops.Select(stop => stop.Id).ToArray(),
            SerraBoundarySurvey.MissionId => SerraBoundarySurvey.Sites.Select(site => site.Id).ToArray(),
            TunnelMissionId or PortViaductSurvey.MissionId or WaterfrontRainSurvey.MissionId or PortWorksSurvey.MissionId or FinalCityPavementSurvey.MissionId => [],
            _ => Enumerable.Range(1, count).Select(index => $"{prefix}_{index:00}").ToArray(),
        };
        var calm = !(isBusSurvey || isViaduct || isRain || isBoundary || isWorks || isFinale) || _calmPolicy.CalmCityEnabled;
        var def = new ContractDefinition(
            SchemaVersion: 1,
            Id: spec.MissionId,
            TitleKey: spec.TitleKey,
            ClientId: client,
            RegionId: spec.RegionIds[0],
            AreaId: area,
            Product: spec.ProductType,
            RequiredFeatureIds: featureIds,
            Requirements: area switch
            {
                WaterfrontRainSurvey.AreaId => WaterfrontRainQuality.Requirements,
                PortViaductSurvey.AreaId => PortViaductQuality.Requirements,
                SerraBoundarySurvey.AreaId => SerraBoundaryQuality.Requirements,
                PortWorksSurvey.AreaId => PortWorksQuality.Requirements,
                FinalCityPavementSurvey.AreaId => FinalCityPavementQuality.Requirements,
                "port_tunnel" => PortTunnelQuality.Requirements,
                _ => CitySurveyRequirements(area, story: true),
            },
            RequiredCapabilities: isFinale ? ["lidar_l6", "camera"] : isBoundary ? ["boundary_total_station"] : isRain ? ["lidar_l5"] : isTunnel || isViaduct || isWorks ? ["lidar_l4"] : ["camera"],
            Risk: calm ? "easy" : "moderate",
            Calm: calm,
            Deadline: spec.MissionId switch
            {
                "HIST-04" => new(25200, 32400, .05, .5),
                BusSurveyCorridor.MissionId => new(30 * 3600, 36 * 3600, .05, .5),
                FinalCityPavementSurvey.MissionId => new(FinalCityPavementSurvey.DueSeconds, FinalCityPavementSurvey.ExpirySeconds, .05, .5),
                _ => new(21600, 27000, .05, .5),
            },
            Reward: new ContractRewardSpec(spec.RewardCoords, 120 + spec.Act * 60, spec.ReputationDelta, spec.UnlockedPlatforms.ToArray()),
            PartialPolicy: new ContractPartialPolicySpec("disabled", []),
            DeliveryFormat: "gsa_bundle_v1",
            StoryPrerequisites: spec.Preconditions.ToArray());

        if (!AcceptCityContract(def, spec)) return;
        var summaryKey = spec.MissionId switch
        {
            "HIST-02" => "city.story.drainage_brief",
            "HIST-03" => "city.story.sidewalk_brief",
            "HIST-04" => "city.story.signs_brief",
            BusSurveyCorridor.MissionId => "city.story.bus_corridor_brief",
            TunnelMissionId => "city.lidar.field_brief",
            PortViaductSurvey.MissionId => "city.viaduct.field_brief",
            WaterfrontRainSurvey.MissionId => "city.rain_survey.field_brief",
            SerraBoundarySurvey.MissionId => "city.boundary.field_brief",
            PortWorksSurvey.MissionId => "city.works.field_brief",
            FinalCityPavementSurvey.MissionId => "city.finale.field_brief",
            _ => "city.story.facades_brief",
        };
        _gtaHud.ShowCutsceneLetterbox(
            $"{spec.MissionId} · {Text(spec.TitleKey)}",
            Text(summaryKey),
            () => _gtaHud.HideCutsceneLetterbox());
    }

    private bool AcceptCityContract(ContractDefinition def, CampaignMissionFullSpec? story = null)
    {
        if (_activeContract is not null) { _feedbackText = Text("city.hazards.active_job"); return false; }
        if (!SupportsCityDelivery(def) && !IsCityLidarCollection(def) && !IsBoundarySurvey(def))
        { _feedbackText = Text(CityOfficeUnavailableKey(def.Product)); return false; }
        if (_progression.SettledContractIds.Contains(def.Id) || (story is not null && CityMissionCompleted(story.MissionId)))
        {
            _feedbackText = Text("city.story.completed");
            return false;
        }
        _lastCitySurveyReview = null;
        _lastTunnelReview = null;
        RestoreViaductDraft(null);
        RestoreRainSurveyOffice();
        RestoreBoundarySurvey();
        RestoreWorksSurvey();
        _lastFinalSurveyReview = null;
        _finaleUploadedSectors.Clear();
        _protectedBoundaryShots.Clear();
        _lastSidewalkMeasurements = [];
        _lastAdjustedPhotoCount = 0;
        _activeStorySpec = story;
        _activeContract = def;
        PrepareCityPhotoCoverage();
        _activeMachine = new ContractStateMachine(def);
        if (IsRainCollection(def)) BeginRainSurveyWeather();
        _activeMachine.Accept(_calendar.CalendarTimeSeconds);
        _fieldSamples.Clear();
        RestorePendingCityLidar(null);
        _protectedLidarReturns = 0;
        if (IsCityLidarCollection(def)) _lidarEnabled = false;
        _cityCustody = new BatchCustodyStore(); _protectedCityPhotos.Clear(); _cityHazards.BeginContract();
        if (IsBoundarySurvey(def)) BeginBoundarySurvey();
        _cityHazards.Services.ExpireInsurance();
        _observedTargetIds.Clear();
        _coverageSpots.Clear();
        _privacyMasksApplied = _officeMode == ProcessingMode.Arcade;
        _audio.PlayOffloadChime();
        _feedbackText = $"{Text(def.TitleKey)} — {def.RequiredFeatureIds.Count} targets";
        _tabletPanel.Hide();
        SyncCityHazardProtection();
        if (!_isSandbox) SaveCityCareerState(SaveTrigger.ContractAccepted, announce: false);
        return true;
    }

    private bool CityPlatformStoryUnlocked(LocomotionMode mode) => _isSandbox || mode switch
    {
        LocomotionMode.Car => CityMissionCompleted("HIST-04"),
        LocomotionMode.Kombi => CityMissionCompleted(BusSurveyCorridor.MissionId),
        LocomotionMode.Pickup => CityMissionCompleted(PortViaductSurvey.MissionId),
        LocomotionMode.Van => CityMissionCompleted(PortWorksSurvey.MissionId),
        _ => true,
    };

    private bool TryPurchaseCityPlatform(LocomotionMode mode)
    {
        if (!CityPlatformStoryUnlocked(mode))
        {
            _feedbackText = Text("city.story.locked");
            return false;
        }
        var completed = _campaignState.ObjectiveStates.Keys.Where(CityMissionCompleted).ToHashSet(StringComparer.Ordinal);
        if (!_fleet.TryPurchasePlatform($"buy_{mode}_{_tick}", _calendar.CalendarTimeSeconds, mode, _ledger,
                _progression, completed, out _, out var quote))
        {
            _feedbackText = Text(quote.Unlocked ? "city.fleet.insufficient_funds" : "city.story.locked");
            _audio.PlayAlert();
            return false;
        }
        SwitchPlatform(mode);
        SaveHazardChange();
        return true;
    }

    private void RefreshTabletContents()
    {
        RefreshCityHazardStatus();
        // Contracts tab: Story Campaign (HIST-01..HIST-11) + Procedural Contracts
        foreach (var c in _contractsTab.GetChildren())
            c.QueueFree();

        var storyHeader = new Label { Text = "★ " + Text("city.story.header") };
        storyHeader.AddThemeFontSizeOverride("font_size", 15);
        storyHeader.AddThemeColorOverride("font_color", new Color(0.98f, 0.80f, 0.24f));
        _contractsTab.AddChild(storyHeader);

        foreach (var spec in FullCampaignCatalog.AllMissions)
        {
            var done = CityMissionCompleted(spec.MissionId);
            var supported = SupportsCityStory(spec);
            var prereqsMet = _isSandbox || spec.Preconditions.All(CityMissionCompleted);
            var equipped = CityHasSurveyPlatform(spec.MinimumPlatformLevel);
            var row = new HBoxContainer();
            var info = new Label
            {
                Text = $"[{spec.MissionId}] Act {spec.Act} · {Text(spec.TitleKey)} ({string.Join("/", spec.RegionIds)}) · {spec.RewardCoords} C$",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            var btn = new Button
            {
                Text = Text(done ? "city.story.completed" : !supported ? "city.office.unavailable" : !prereqsMet ? "city.story.locked" :
                    !equipped ? "city.story.requires_platform" : "city.story.start"),
                Disabled = done || !prereqsMet || !supported || !equipped,
                TooltipText = supported ? "" : Text(CityOfficeUnavailableKey(spec.ProductType)),
                FocusMode = Control.FocusModeEnum.None,
            };
            var capturedSpec = spec;
            btn.Pressed += () => StartStoryMission(capturedSpec);
            row.AddChild(info);
            row.AddChild(btn);
            _contractsTab.AddChild(row);
        }

        var refreshBtn = new Button { Text = Text("city.contracts.refresh"), FocusMode = Control.FocusModeEnum.None };
        refreshBtn.Pressed += () => { GenerateContractOffers(); RefreshTabletContents(); };
        _contractsTab.AddChild(refreshBtn);

        foreach (var offer in _offeredContracts)
        {
            var def = offer.Contract;
            var card = BoardOfficeAndReportPresenter.BuildContractBoardCard(def, TranslationServer.GetLocale().ToString());
            var row = new HBoxContainer();
            var info = new Label
            {
                Text = $"{Text(card.TitleKey)} · {card.RegionId} · {def.RequiredFeatureIds.Count} pts · {card.BaseCoords} C$ · {(card.IsCalm ? Text("board.badge.calm") : card.Risk)}",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            var compatible = CityHasSurveyPlatform(offer.WitnessCertificate.RequiredPlatformLevel);
            var supported = compatible && SupportsCityDelivery(def);
            var accept = new Button
            {
                Text = Text(supported ? "city.contracts.accept" : compatible ? "city.office.unavailable" : "city.story.requires_platform"),
                Disabled = !supported,
                FocusMode = Control.FocusModeEnum.None,
                TooltipText = supported ? "" : Text(CityOfficeUnavailableKey(def.Product)),
            };
            accept.Pressed += () => TryAcceptCityProceduralOffer(offer);
            row.AddChild(info);
            row.AddChild(accept);
            _contractsTab.AddChild(row);
        }

        // Office status
        var contractName = _activeContract is null ? Text("city.office.no_contract") : Text(_activeContract.TitleKey);
        var targetCount = _activeContract?.RequiredFeatureIds.Count ?? 0;
        var foundCount = _activeContract?.RequiredFeatureIds.Count(_observedTargetIds.Contains) ?? 0;
        _officeStatusLabel.Text =
            $"{Text("city.office.active")}: {contractName}\n" +
            $"{Text("training.targets")}: {foundCount}/{targetCount} · Samples: {_fieldSamples.Count} · LiDAR pts: {_lidarPointVisibleCount}\n" +
            (_activeContract is { } active && !SupportsCityDelivery(active) && !IsCityLidarCollection(active) && !IsBoundarySurvey(active)
                ? Text(CityOfficeUnavailableKey(active.Product)) + "\n" : "") +
            $"Mode: {Text(_officeMode == ProcessingMode.Arcade ? "office.mode.arcade" : "office.mode.technical")}\n" +
            $"Rack Calibration: {(_rackMisaligned ? Text("city.rack.misaligned") : Text("city.rack.ok"))}";

        if (_activeContract is { } survey && SupportsCityDelivery(survey))
        {
            var instruction = survey.AreaId switch
            {
                FacadeArea or ParkArea => "city.office.collect_surface",
                CitySurveyInventory.SidewalkArea => "city.office.collect_control",
                BusSurveyCorridor.AreaId => "city.story.bus_corridor_brief",
                _ => "city.office.collect_inventory",
            };
            _officeStatusLabel.Text += "\n" + Text(instruction);
        }
        if (IsTunnelCollection(_activeContract))
            _officeStatusLabel.Text += "\n" + Text("city.lidar.field_brief") + "\n" + CityLidarCollectionStatus();
        _viaductForm.Visible = IsViaductCollection(_activeContract);
        if (_viaductForm.Visible)
            _officeStatusLabel.Text += "\n" + Text("city.viaduct.field_brief") + "\n" + CityLidarCollectionStatus();
        var viaductReview = ViaductReviewText();
        if (viaductReview.Length > 0) _officeStatusLabel.Text += "\n" + viaductReview;
        var tunnelReview = TunnelReviewText();
        if (tunnelReview.Length > 0) _officeStatusLabel.Text += "\n" + tunnelReview;
        _rainFilterButton.Visible = IsRainCollection(_activeContract);
        if (_rainFilterButton.Visible)
        {
            _rainFilterButton.Text = Text(_rainSurveyFilterEnabled ? "city.rain_survey.disable_filter" : "city.rain_survey.enable_filter");
            _officeStatusLabel.Text += "\n" + Text("city.rain_survey.office_help") + "\n" + CityLidarCollectionStatus();
        }
        var rainReview = RainSurveyReviewText();
        if (rainReview.Length > 0) _officeStatusLabel.Text += "\n" + rainReview;
        if (IsBoundarySurvey(_activeContract))
            _officeStatusLabel.Text += "\n" + Text("city.boundary.office_help") + "\n" + BoundaryCollectionStatus();
        var boundaryReview = BoundaryReviewText();
        if (boundaryReview.Length > 0) _officeStatusLabel.Text += "\n" + boundaryReview;
        if (IsWorksSurvey(_activeContract))
            _officeStatusLabel.Text += "\n" + Text("city.works.office_help");
        if (IsFinalPavementSurvey(_activeContract))
            _officeStatusLabel.Text += "\n" + Text("city.finale.office_help") + "\n" + FinalSurveyStatus();
        var finalSurveyReview = FinalSurveyReviewText();
        if (finalSurveyReview.Length > 0) _officeStatusLabel.Text += "\n" + finalSurveyReview;
        var worksReview = WorksReviewText();
        if (worksReview.Length > 0) _officeStatusLabel.Text += "\n" + worksReview;
        var review = CitySurveyReviewText();
        if (review.Length > 0) _officeStatusLabel.Text += "\n" + review;
        if (_activeStorySpec?.MissionId == "HIST-02" && _activeMachine?.Snapshot is { } drainage && !_calendar.IsRaining)
        {
            var minutes = Math.Max(0, (drainage.AcceptedAtSeconds + DrainageRainDelaySeconds - _calendar.CalendarTimeSeconds) / 60);
            _officeStatusLabel.Text += "\n" + string.Format(Text("city.office.rain_forecast"), Math.Ceiling(minutes));
        }

        // Fleet & Skills tab
        foreach (var c in _fleetTab.GetChildren())
            c.QueueFree();
        var completedMissions = _campaignState.ObjectiveStates.Keys.Where(CityMissionCompleted).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in FleetShopCatalog.Platforms)
        {
            var mode = entry.Profile.Mode;
            var owned = _fleet.OwnedPlatforms.Contains(mode);
            var isCurrent = _locomotion.MountedVehicleMode == mode;
            var quote = _fleet.QuotePlatformPurchase(mode, _progression, completedMissions);
            var unlocked = quote.Unlocked && CityPlatformStoryUnlocked(mode);
            var row = new HBoxContainer();
            var lbl = new Label
            {
                Text = $"L{entry.Profile.Level} · {Text($"platform.{mode.ToString().ToLowerInvariant()}.name")} ({entry.Profile.MaxSpeedMetresPerSecond:F0} m/s · {quote.FinalPriceCoords} C$)",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            var actionBtn = new Button
            {
                Text = Text(isCurrent ? "city.fleet.active" : owned ? "city.fleet.drive" : unlocked ? "city.fleet.buy" : "city.story.locked"),
                Disabled = isCurrent || !owned && !unlocked,
                FocusMode = Control.FocusModeEnum.None,
            };
            actionBtn.Pressed += () =>
            {
                if (owned)
                {
                    SwitchPlatform(mode);
                }
                else
                {
                    TryPurchaseCityPlatform(mode);
                }
                RefreshTabletContents();
            };
            row.AddChild(lbl);
            row.AddChild(actionBtn);
            _fleetTab.AddChild(row);
        }

        // Map tab
        RefreshCityMapSnapshot();

        RefreshAdvancedGeopedia();
        RefreshTechnicalTablet();

        // Settings buttons
        _citySaveLabel.Text = Text("save.ui.help") + "\n" + _citySaveNotice;
        _recoverCitySaveButton.Visible = !_isSandbox && _citySaveStatus?.PreviousGenerationAvailable == true;
        _calmToggleBtn.Text = Text(_calmPolicy.CalmCityEnabled ? "hud.calm_protection.active" : "hud.calm_protection.inactive");
        _ditherToggleBtn.Text = Text("city.settings.dither") + ": " + (_atmosphere.RetroDitherEnabled ? "ON" : "OFF");
        _daguerreotypeBtn.Text = Text("city.settings.daguerreotype") + ": " + (_atmosphere.DaguerreotypeEnabled ? "ON" : "OFF");
    }

    private void RefreshCityHud(
        double dt,
        Vector3d pos,
        double heading,
        LocomotionMode mode,
        WeatherAndTimeSnapshot weather,
        double stress,
        TerritoryEscalationOutcome attn)
    {
        var hours = (int)weather.HourOfDay;
        var mins = (int)((weather.HourOfDay - hours) * 60.0);
        var clockText = $"{hours:00}:{mins:00}";
        var weatherText = weather.IsRaining ? Text("hud.weather.rain") : Text("hud.weather.dry");

        var gnss = _navigationRig?.LatestGnss;
        var gnssText = Text(NavigationTelemetryPresenter.GnssStatusKey(gnss));
        var nav = _navigationRig?.Estimator?.Current;
        if (nav is { BlackoutAlertActive: true } || nav is { HighUncertaintyAlertActive: true })
            gnssText += " · " + Text("navigation.badge.warning");
        var gnssColor = gnss is not { IsAvailable: true } ? new Color(.98f, .48f, .32f)
            : gnss.Dop > 4 || nav is { HighUncertaintyAlertActive: true } ? new Color(.96f, .78f, .24f)
            : new Color(.32f, .92f, .56f);

        var blips = _radarBlips;
        var foundTargets = 0;
        blips.Clear();
        foreach (var bm in _geodeticNetwork.Benchmarks.Values)
            blips.Add(new RadarBlip(bm.BenchmarkId, bm.PhysicalPositionEnu, new Color(0.35f, 0.92f, 0.48f), "▲", ClampToRim: false));

        if (_manifestObjectsById.TryGetValue("office_entry", out var off))
            blips.Add(new RadarBlip("office_entry", off.PositionEnu, new Color(0.20f, 0.90f, 0.95f), "O"));
        if (_manifestObjectsById.TryGetValue("unload_point", out var unl))
            blips.Add(new RadarBlip("unload_point", unl.PositionEnu, new Color(0.98f, 0.76f, 0.18f), "D"));

        var showFacadeLabels = _activeContract?.AreaId == FacadeArea;
        if (_cityFacadeLabels.Count > 0)
        {
            foreach (var (fid, lbl) in _cityFacadeLabels)
            {
                if (lbl.Visible != showFacadeLabels) lbl.Visible = showFacadeLabels;
                if (!showFacadeLabels) continue;
                var cells = _coveredCellsByTarget.TryGetValue(fid, out var cset) ? cset.Count : 0;
                var nextText = cells >= 4 ? $"#{fid[7..]} ✓" : cells > 0 ? $"#{fid[7..]} {cells}/4" : $"#{fid[7..]}";
                var nextColor = cells >= 4 ? new Color(0.28f, 0.95f, 0.50f) : cells > 0 ? new Color(0.30f, 0.90f, 0.98f) : new Color(1f, 0.85f, 0.25f);
                if (lbl.Text != nextText) lbl.Text = nextText;
                if (!lbl.Modulate.IsEqualApprox(nextColor)) lbl.Modulate = nextColor;
            }
        }

        if (_activeContract is not null)
        {
            foreach (var fid in _activeContract.RequiredFeatureIds)
            {
                Vector3d? tPos = _manifestObjectsById.TryGetValue(fid, out var fo) ? fo.PositionEnu : null;
                if (tPos is null && _geodeticNetwork.Benchmarks.TryGetValue(fid, out var bmNode))
                    tPos = bmNode.PhysicalPositionEnu;
                if (tPos is null) continue;
                var coveredCells = _coveredCellsByTarget.TryGetValue(fid, out var cset) ? cset.Count : 0;
                var needCells = _cityPhotoCoverage is not null ? _cityPhotoCoverage.GetTargetCellCount(fid) : 0;
                var done = needCells > 0 ? coveredCells >= needCells : _observedTargetIds.Contains(fid);
                var partial = !done && (coveredCells > 0 || _observedTargetIds.Contains(fid));
                if (done) foundTargets++;
                var blipColor = done ? new Color(0.3f, 0.9f, 0.5f) : partial ? new Color(0.30f, 0.90f, 0.98f) : new Color(1.0f, 0.82f, 0.22f);
                blips.Add(new RadarBlip(fid, tPos.Value, blipColor, "★", ClampToRim: !done));
            }
        }

        var promptText = string.IsNullOrEmpty(_hazardNotice) ? Text("city.prompt.controls") : _hazardNotice;
        if (!attn.BombWarningActive && (_everyday.DogActive || _everyday.Parking is not null)) promptText = CityEverydayPrompt();
        if (!attn.BombWarningActive && !_everyday.DogActive && _everyday.Parking is null && _cityTrafficWarningId is not null)
            promptText = Text("city.traffic.erratic_warning");
        if (string.IsNullOrEmpty(_hazardNotice) && _cityTrafficWarningId is null && !_everyday.DogActive && _everyday.Parking is null && mode == LocomotionMode.OnFoot)
        {
            foreach (var v in _worldParkedVehicles)
            {
                if (TrainingContractSession.HorizontalDistance(pos, v.PosEnu) < 4.2)
                {
                    promptText = Text("city.board_vehicle");
                    break;
                }
            }
        }

        var objTitle = _activeContract is not null ? Text(_activeContract.TitleKey)
            : (_isSandbox ? Text("city.sandbox.title") : Text("city.freeroam.title"));
        string objDetails;
        if (_activeContract is not null && _cityPhotoCoverage is { TotalCellCount: > 0 } cov)
        {
            var covPct = (int)Math.Floor(100.0 * _coveredCellTotal / cov.TotalCellCount);
            objDetails = string.Format(Text("city.story.coverage_status"), foundTargets, _activeContract.RequiredFeatureIds.Count, covPct, _fieldSamples.Count + _protectedCityPhotos.Count);
        }
        else if (_activeContract is not null)
        {
            objDetails = string.Format(Text("city.story.capture_status"), foundTargets, _activeContract.RequiredFeatureIds.Count, _fieldSamples.Count);
        }
        else
        {
            objDetails = $"Region: {Text(GetDistrictBannerKey(pos))} · Eggs: {_collectibles.CollectedEggIds.Count}/5 · Marks: {_collectibles.OccupiedLandmarkIds.Count}/8";
        }
        if (IsCityLidarCollection(_activeContract)) objDetails = CityLidarCollectionStatus();
        if (IsFinalPavementSurvey(_activeContract))
        {
            objDetails = FinalSurveyStatus();
            foreach (var sector in FinalCityPavementSurvey.Sectors)
            {
                var id = sector.Manifest.SectorId;
                var color = _finaleUploadedSectors.Contains(id) ? Colors.LimeGreen : Colors.DeepSkyBlue;
                blips.Add(new RadarBlip(id, sector.Start, Colors.Orange, "S", ClampToRim: true));
                blips.Add(new RadarBlip(id + "_upload", sector.Upload, color, "U", ClampToRim: false));
            }
        }
        if (IsWorksSurvey(_activeContract))
        {
            objDetails = WorksCollectionStatus();
            foreach (var (position, index) in PortWorksSurvey.UploadPads.Select((p, i) => (p, i)))
                blips.Add(new RadarBlip($"works_upload_{index}", position, Colors.OrangeRed, "U", ClampToRim: true));
            foreach (var side in PortWorksSurvey.Sides)
                blips.Add(new RadarBlip($"works_{side.Id}", side.Start, Colors.Orange, "", ClampToRim: false));
        }
        if (IsBoundarySurvey(_activeContract))
        {
            objDetails = BoundaryCollectionStatus();
            foreach (var (site, station, position) in BoundaryStations())
                blips.Add(new RadarBlip($"{site.Id}_{station}", position, Colors.Orange, station == 0 ? "A" : "B", ClampToRim: false));
            foreach (var (pad, index) in SerraBoundarySurvey.UploadPads.Select((position, i) => (position, i)))
                blips.Add(new RadarBlip($"boundary_upload_{index}", pad, Colors.DeepSkyBlue, "U", ClampToRim: true));
        }
        if (IsTunnelCollection(_activeContract))
        {
            blips.Add(new RadarBlip("tunnel_west", PortTunnelSurvey.WestStop, Colors.Orange, "W", ClampToRim: true));
            blips.Add(new RadarBlip("tunnel_east", PortTunnelSurvey.EastStop, Colors.Orange, "E", ClampToRim: true));
        }
        if (IsViaductCollection(_activeContract))
        {
            blips.Add(new RadarBlip("viaduct_west", PortViaductSurvey.WestPad, Colors.Orange, "W", ClampToRim: true));
            blips.Add(new RadarBlip("viaduct_east", PortViaductSurvey.EastPad, Colors.Orange, "E", ClampToRim: true));
        }
        if (IsRainCollection(_activeContract))
        {
            var pads = WaterfrontRainSurvey.UploadPads;
            blips.Add(new RadarBlip("rain_west", pads[0], Colors.Orange, "W", ClampToRim: true));
            blips.Add(new RadarBlip("rain_east", pads[^1], Colors.Orange, "E", ClampToRim: true));
        }

        var radioTitle = _audio.RadioStation > 0 ? "♫ " + Text(_audio.ActiveRadioTitleKey) : "";
        var radioSub = !string.IsNullOrEmpty(_audio.CurrentRadioSubtitleKey) ? Text(_audio.CurrentRadioSubtitleKey) : "";
        var platformKey = $"platform.{mode.ToString().ToLowerInvariant()}.name";

        var autonomyFrac = _carSim is not null
            ? _carSim.RemainingAutonomySeconds / _carSim.Profile.PhysicalAutonomySeconds
            : (_locomotion.CurrentProfile.HasFiniteAutonomy
                ? _locomotion.RemainingAutonomySeconds / _locomotion.CurrentProfile.PhysicalAutonomySeconds
                : 1.0);

        var kmh = (int)Math.Round(Math.Abs(_lastSpeedMps) * 3.6);
        var gearTag = mode == LocomotionMode.OnFoot ? ""
            : (_lastSpeedMps < -0.15 ? "[R]" : (_handbrakeActive ? "[DRIFT]" : "[D]"));
        var speedText = mode == LocomotionMode.OnFoot ? $"{kmh} km/h" : $"{kmh} km/h {gearTag}";

        _gtaHud.Radar.UpdateDynamicWorld(_cityBlockObstacles, _traffic.VisualAgents);
        _gtaHud.Update(
            dt,
            new GtaHudState(
                ClockText: clockText,
                WeatherText: weatherText,
                RadioTitleText: radioTitle,
                RadioSubtitleText: radioSub,
                BalanceCoords: _ledger.BalanceCoords,
                Xp: _progression.TotalXp,
                PlatformTitleText: Text(platformKey),
                GnssText: gnssText,
                GnssColor: gnssColor,
                ShowLidar: mode >= LocomotionMode.Kombi,
                LidarActive: mode >= LocomotionMode.Kombi && _lidarEnabled,
                AutonomyFraction: autonomyFrac,
                StorageUsed: _fieldSamples.Count + (_lidarPointVisibleCount / 400),
                StorageCapacity: _locomotion.CurrentProfile.VirtualStorageCapacityUnits,
                ShowGimbalinaStress: mode <= LocomotionMode.Bicycle,
                GimbalinaStressFraction: stress / 100.0,
                ShowVehicleCondition: mode >= LocomotionMode.Car,
                VehicleDamageFraction: _carSim is not null ? _carSim.ConditionDamage / 100.0 : 0.0,
                ShowAttentionStars: !_calmPolicy.IsProtectionActive && !_citySafeZone,
                AttentionStars: (int)attn.ThreatLevel,
                ObjectiveTitle: objTitle,
                ObjectiveDetails: objDetails,
                FeedbackText: _feedbackText,
                ContextPromptText: promptText,
                SurveyViewfinderActive: _chaseCamera.Mode == GsaCameraMode.SurveyView,
                DistrictPopup: _pendingDistrictPopup,
                VehiclePopup: _pendingVehiclePopup,
                SpeedText: speedText),
            pos,
            heading,
            blips,
            _coverageSpots);

        _pendingDistrictPopup = null;
        _pendingVehiclePopup = null;
    }

    private void SaveCareer() => SaveCityCareerState();

    private void TryLoadCareer() => LoadCityCareerState();

    private void RefreshCityMapSnapshot()
    {
        var targets = new List<Vector3d>();
        foreach (var id in IsBoundarySurvey(_activeContract) ? [] : _activeContract?.RequiredFeatureIds ?? [])
        {
            if (_manifestObjectsById.TryGetValue(id, out var obj)) targets.Add(obj.PositionEnu);
            else if (_geodeticNetwork.Benchmarks.TryGetValue(id, out var mark)) targets.Add(mark.PhysicalPositionEnu);
        }
        if (IsTunnelCollection(_activeContract))
            targets.AddRange([PortTunnelSurvey.WestStop, PortTunnelSurvey.MiddleUpload, PortTunnelSurvey.EastStop]);
        if (IsViaductCollection(_activeContract))
            targets.AddRange([PortViaductSurvey.WestPad, PortViaductSurvey.EastPad]);
        if (IsRainCollection(_activeContract))
            targets.AddRange(WaterfrontRainSurvey.UploadPads);
        if (IsWorksSurvey(_activeContract))
        {
            targets.AddRange(PortWorksSurvey.Sides.Select(side => side.Start));
            targets.AddRange(PortWorksSurvey.UploadPads);
        }
        if (IsBoundarySurvey(_activeContract))
        {
            targets.AddRange(BoundaryStations().Select(station => station.Position));
            targets.AddRange(SerraBoundarySurvey.UploadPads);
        }
        if (IsFinalPavementSurvey(_activeContract))
            foreach (var sector in FinalCityPavementSurvey.Sectors)
            {
                targets.Add(sector.Start);
                targets.Add(sector.Upload);
            }
        _cityMap.UpdateMap(CurrentPositionEnu(), CurrentHeadingRadians(), _geodeticNetwork.Benchmarks.Values.ToArray(), targets);
    }

}

/// <summary>Full 4 km² North-Up city map control displayed inside the São Datum Tablet.</summary>
public sealed partial class CityFullMapControl : Control
{
    private Vector3d _player;
    private double _heading;
    private IReadOnlyList<GeodeticBenchmarkNode> _benchmarks = [];
    private IReadOnlyList<Vector3d> _waypoints = [];
    private IReadOnlyList<AttentionZoneView> _attentionZones = [];
    private TrajectoryEstimateSample? _navigation;
    private IReadOnlyList<NavigationTrailPoint> _navigationTrail = [];
    internal int AttentionOverlayCount => _attentionZones.Count;
    internal int NavigationOverlayCount => _navigationTrail.Count;

    public void UpdateNavigation(TrajectoryEstimateSample? estimate, IReadOnlyList<NavigationTrailPoint> trail)
    {
        _navigation = estimate;
        _navigationTrail = trail;
        QueueRedraw();
    }

    public void UpdateAttention(IReadOnlyList<AttentionZoneView> zones)
    {
        _attentionZones = zones;
        QueueRedraw();
    }

    public void UpdateMap(
        Vector3d player,
        double heading,
        IReadOnlyList<GeodeticBenchmarkNode> benchmarks,
        IReadOnlyList<Vector3d> waypoints)
    {
        _player = player;
        _heading = heading;
        _benchmarks = benchmarks;
        _waypoints = waypoints;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var pad = 16f;
        var span = MathF.Max(80f, MathF.Min(Size.X - pad * 2f, Size.Y - pad * 2f));
        var origin = new Vector2((Size.X - span) * 0.5f, pad);
        DrawRect(new Rect2(origin, Vector2.One * span), new Color(0.06f, 0.09f, 0.12f));

        var half = span * 0.5f;
        // 4 regions: Old Center (SW), Port (NW), Waterfront (SE), Serra (NE)
        DrawRect(new Rect2(origin + new Vector2(0, half), new Vector2(half, half)), new Color(0.14f, 0.18f, 0.22f));
        DrawRect(new Rect2(origin, new Vector2(half, half)), new Color(0.18f, 0.17f, 0.14f));
        DrawRect(new Rect2(origin + new Vector2(half, half), new Vector2(half, half)), new Color(0.10f, 0.22f, 0.30f));
        DrawRect(new Rect2(origin + new Vector2(half, 0), new Vector2(half, half)), new Color(0.12f, 0.24f, 0.14f));

        Vector2 MapPt(Vector3d enu)
        {
            var nx = Math.Clamp((float)((enu.X - 500000.0) / 2000.0), 0f, 1f);
            var ny = Math.Clamp((float)(1.0 - (enu.Y - 7400000.0) / 2000.0), 0f, 1f);
            return origin + new Vector2(nx * span, ny * span);
        }

        // Harbor Basin (Port North), Waterfront Bay (East), Central Park & Arterial Avenue Grid
        DrawRect(new Rect2(MapPt(new Vector3d(500000, 7402000, 100)), new Vector2(half, span * 0.09f)), new Color(0.06f, 0.22f, 0.42f));
        DrawRect(new Rect2(MapPt(new Vector3d(501800, 7401000, 100)), new Vector2(span * 0.10f, half)), new Color(0.06f, 0.24f, 0.44f));
        DrawRect(new Rect2(MapPt(new Vector3d(500285, 7400445, 100)), new Vector2(span * 0.065f, span * 0.065f)), new Color(0.16f, 0.42f, 0.20f));

        var roadCol = new Color(0.30f, 0.34f, 0.38f);
        for (var n = 7400000.0; n <= 7401800.0; n += 120.0)
            DrawLine(MapPt(new Vector3d(500000, n, 100)), MapPt(new Vector3d(501850, n, 100)), roadCol, 1.4f);
        for (var e = 500000.0; e <= 501800.0; e += 120.0)
            DrawLine(MapPt(new Vector3d(e, 7400000, 100)), MapPt(new Vector3d(e, 7401850, 100)), roadCol, 1.4f);

        var font = ThemeDB.FallbackFont;
        DrawString(font, origin + new Vector2(10, 22), TranslationServer.Translate("city.map.port"), fontSize: 12);
        DrawString(font, origin + new Vector2(half + 10, 22), TranslationServer.Translate("city.map.serra"), fontSize: 12);
        DrawString(font, origin + new Vector2(10, half + 22), TranslationServer.Translate("city.map.old_center"), fontSize: 12);
        DrawString(font, origin + new Vector2(half + 10, half + 22), TranslationServer.Translate("city.map.waterfront"), fontSize: 12);

        for (var i = 0; i < _attentionZones.Count; i++)
        {
            var zone = _attentionZones[i];
            var topLeft = MapPt(new Vector3d(zone.Bounds.MinEast, zone.Bounds.MaxNorth, 0));
            var bottomRight = MapPt(new Vector3d(zone.Bounds.MaxEast, zone.Bounds.MinNorth, 0));
            var rect = new Rect2(topLeft, bottomRight - topLeft);
            var colour = zone.ProtectionActive ? new Color(0.30f, 0.82f, 0.88f) : new Color(0.98f, 0.66f, 0.28f);
            DrawRect(rect, new Color(colour, 0.08f + 0.20f * (float)(zone.AttentionScore / 100.0)));
            DrawRect(rect, colour, filled: false, width: 1f);
            DrawString(font, topLeft + new Vector2(2, 14), $"[{i + 1}] {zone.ThreatLevel}", fontSize: 12, modulate: colour);
        }

        foreach (var bm in _benchmarks)
        {
            var point = MapPt(bm.PhysicalPositionEnu);
            DrawColoredPolygon([point + new Vector2(0, -5), point + new Vector2(-4, 4), point + new Vector2(4, 4)], new Color(0.32f, 0.92f, 0.48f));
        }
        foreach (var wp in _waypoints)
        {
            var pt = MapPt(wp);
            DrawCircle(pt, 6f, Colors.Black);
            DrawCircle(pt, 4.5f, new Color(1.0f, 0.78f, 0.16f));
            DrawString(font, pt + new Vector2(5, -4), "★", fontSize: 12);
        }

        // Estimated-only trace. Axis bars are marginal +/-1 sigma, not a confidence ellipse or measured drift.
        Vector2 NavigationPt(Vector3d enu) => origin + new Vector2((float)((enu.X - 500000) / 2000 * span),
            (float)((1 - (enu.Y - 7400000) / 2000) * span));
        var mapRect = new Rect2(origin, Vector2.One * span);
        var navColour = new Color(.25f, .88f, 1);
        for (var i = 1; i < _navigationTrail.Count; i++)
        {
            var from = NavigationPt(_navigationTrail[i - 1].EstimatedPositionEnu);
            var to = NavigationPt(_navigationTrail[i].EstimatedPositionEnu);
            if (mapRect.HasPoint(from) && mapRect.HasPoint(to)) DrawLine(from, to, navColour, 1.5f);
        }
        if (_navigation is { } estimated)
        {
            var point = NavigationPt(estimated.PositionEnu);
            if (mapRect.HasPoint(point))
            {
                var sx = (float)(estimated.PositionSigmaEnu.X / 2000 * span);
                var sy = (float)(estimated.PositionSigmaEnu.Y / 2000 * span);
                DrawLine(new Vector2(Math.Max(origin.X, point.X - sx), point.Y),
                    new Vector2(Math.Min(origin.X + span, point.X + sx), point.Y), navColour, 1);
                DrawLine(new Vector2(point.X, Math.Max(origin.Y, point.Y - sy)),
                    new Vector2(point.X, Math.Min(origin.Y + span, point.Y + sy)), navColour, 1);
                DrawPolyline([point + new Vector2(0, -5), point + new Vector2(5, 0), point + new Vector2(0, 5),
                    point + new Vector2(-5, 0), point + new Vector2(0, -5)], navColour, 1.5f);
            }
        }

        var here = MapPt(_player);
        var fwd = new Vector2((float)Math.Cos(_heading), -(float)Math.Sin(_heading));
        var side = new Vector2(-fwd.Y, fwd.X);
        DrawColoredPolygon([here + fwd * 8f, here - fwd * 5f + side * 4f, here - fwd * 5f - side * 4f], Colors.Yellow);
    }
}
