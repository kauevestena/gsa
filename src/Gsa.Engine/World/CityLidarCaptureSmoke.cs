using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.World;
using Gsa.Missions.Campaign;
using Gsa.Missions.Contracts;
using Gsa.Missions.Economy;
using Gsa.Missions.Persistence;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private void RunCityLidarCaptureSmoke()
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("City LiDAR smoke: " + message);
        }

        // HIST-05 was settled through the real office above. Supply the Kombi for this capture fixture.
        const LocomotionMode mode = LocomotionMode.Kombi;
        var fleet = _fleet.ExportSnapshot();
        _fleet = FleetProgressionManager.FromSnapshot(fleet with { OwnedPlatforms = fleet.OwnedPlatforms.Append(mode).ToArray() });
        SwitchPlatform(mode);
        StartStoryMission(FullCampaignCatalog.GetMission(TunnelMissionId));
        Require(IsTunnelCollection(_activeContract) && !_lidarEnabled, "field job did not start with the scanner paused");
        var wallet = _ledger.BalanceCoords;
        var port = new Vector3d(500710, 7401720, 100);
        _lidarEnabled = true;
        _tick++;
        StepLiveLidar(1.0 / 60, port, 0, mode, false);
        Require(_pendingLidarReturns == 0 && _lidarTicksWithoutPose == 1, "a slice used truth before navigation initialized");

        void TraversePort(Vector3d start, int ticks = 150)
        {
            const double dt = 1.0 / 60;
            _carSim = null;
            ResetCityNavigation();
            _navigationEnvironmentAt = ResolveCityNavigationEnvironment;
            UpdateSectorStreaming(start);
            for (var i = 0; i < ticks; i++)
            {
                var position = start + Vector3d.UnitX * (i * .12);
                _locomotion.RepositionSafeAfterRecovery(position, mode, 0, false, false);
                _tick++;
                var before = _pendingLidar.Count;
                StepCityNavigation(dt, position, 0, mode, false);
                StepLiveLidar(dt, position, 0, mode, false);
                if (_pendingLidar.Count == before) continue;
                var pose = _pendingLidar[^1].Pose;
                var estimate = _navigationRig!.Estimator!.Current!;
                Require(new Vector3d(pose.East, pose.North, pose.Up) == estimate.PositionEnu && pose.TimeSeconds == _tick / 60.0,
                    "stored return was not paired with the current observed pose and physical time");
            }
        }

        _lidarCloudMesh.Visible = false;
        var displayed = _lidarPointVisibleCount;
        TraversePort(port);
        Require(_pendingLidarReturns > 0 && _lidarPointVisibleCount == displayed &&
            _locomotion.UsedVirtualStorageUnits == CityLidarSlice.StorageUnits(_pendingLidarReturns), "display or storage affected acquisition");
        Require(BuildProtectedCityCloud().GetMetrics().TotalSamples == 0, "pending returns entered the office cloud");
        var pendingBefore = JsonSerializer.Serialize(_pendingLidar);
        SaveCareer();
        TryLoadCareer();
        Require(!_lidarEnabled && _lidarTicksWithoutPose > 0 && JsonSerializer.Serialize(_pendingLidar) == pendingBefore,
            "reload lost pending LiDAR evidence or resumed scanning without a new estimate");

        _lidarEnabled = true;
        var vacantUnits = PlatformProfile.Kombi.VirtualStorageCapacityUnits - _locomotion.UsedVirtualStorageUnits;
        Require(_locomotion.TryConsumeCaptureUnits(vacantUnits, out _), "could not fill storage for the capacity test");
        // Drain any returns already covered by the last reserved storage unit before testing the boundary.
        TraversePort(port, 600);
        Require(_lidarTicksWithoutCapacity > 0 && _locomotion.UsedVirtualStorageUnits == PlatformProfile.Kombi.VirtualStorageCapacityUnits,
            $"full storage did not report skipped slices (skipped={_lidarTicksWithoutCapacity}, pending={_pendingLidarReturns}, used={_locomotion.UsedVirtualStorageUnits})");
        _locomotion.ReleaseCaptureUnits(vacantUnits);
        TraversePort(port + Vector3d.UnitY * 3);
        Require(_pendingLidar.Select(slice => slice.PhysicsTick).Distinct().Count() == _pendingLidar.Count,
            "reload reused a capture ID");
        var expected = _pendingLidarReturns;
        var office = _manifestObjectsById["office_entry"].PositionEnu + new Vector3d(0, -4, -1.5);
        _locomotion.RepositionSafeAfterRecovery(office, mode, 0, false, false);
        OffloadCityData();
        Require(_pendingLidarReturns == 0 && _protectedLidarReturns == expected && _locomotion.UsedVirtualStorageUnits == 0,
            "upload did not protect the raw returns and release storage");
        var first = BuildProtectedCityCloud();
        var bounds = _fullManifest.Bounds;
        var expandedMin = bounds.Min - new Vector3d(100, 100, 100);
        var expandedMax = bounds.Max + new Vector3d(100, 100, 100);
        var cloud = first.QueryScientificSamples(expandedMin, expandedMax);
        Require(cloud.Count == expected && first.QueryVisualLod(16).Count <= 16, "scientific cloud used display count or lost returns");
        Require(cloud.SequenceEqual(first.QueryScientificSamples(expandedMin, expandedMax)), "changing visual LOD changed the scientific cloud");

        ProcessAndDeliverCityContract();
        Require(IsTunnelCollection(_activeContract) && _ledger.BalanceCoords == wallet && !CityMissionCompleted(TunnelMissionId),
            "a field-only cloud settled or unlocked the campaign");

        TraversePort(port);
        Require(_pendingLidarReturns > 0, "no unprotected data to exercise vehicle loss");
        HandleCityVehicleLoss();
        TryLoadCareer();
        Require(_pendingLidarReturns == 0 && _protectedLidarReturns == expected &&
            cloud.SequenceEqual(BuildProtectedCityCloud().QueryScientificSamples(expandedMin, expandedMax)),
            "loss or reload changed protected scientific returns");
        RunPortTunnelDeliverySmoke(wallet);
        RunViaductDeliverySmoke();
        RunRainSurveySmoke();
        _lidarCloudMesh.Visible = true;
        GD.Print($"GSA_CITY_LIDAR_CAPTURE_SMOKE_OK protected_returns={expected} no_pose=explicit capacity=reported lod=independent loss=protected reload=stable payout=blocked");
    }

    private void RunPortTunnelDeliverySmoke(int wallet)
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Port tunnel smoke: " + message);
        }

        const LocomotionMode mode = LocomotionMode.Kombi;
        var west = PortTunnelSurvey.WestStop;
        var middle = PortTunnelSurvey.MiddleUpload;
        var east = PortTunnelSurvey.EastStop;
        _cityHazards.DisabledVehicle = null; // Isolate delivery from the loss fixture above.
        _rackMisaligned = false;
        _cityHazards.RackYawErrorRadians = 0;
        _carSim = null;
        _locomotion.RepositionSafeAfterRecovery(west, mode, 0, true, false);
        _lidarEnabled = true;
        ResetCityNavigation();
        UpdateSectorStreaming(west);

        void Frame(Vector3d pos)
        {
            const double dt = 1.0 / 60;
            _locomotion.RepositionSafeAfterRecovery(pos, mode, 0, false, false);
            _tick++;
            _calendar.StepPhysical(dt);
            StepCityNavigation(dt, pos, 0, mode, false);
            StepLiveLidar(dt, pos, 0, mode, false);
        }

        void Drive(Vector3d from, Vector3d to)
        {
            var steps = (int)Math.Ceiling((to - from).Length / .05);
            for (var i = 1; i <= steps; i++) Frame(from + (to - from) * (i / (double)steps));
        }

        void Stop(Vector3d at)
        {
            for (var i = 0; i < 270; i++) Frame(at);
        }

        Drive(west - Vector3d.UnitX * 7, west);
        Stop(west);
        Require(_pendingLidar.Any(slice => slice.TunnelRanges?.ZuptPortal == 0), "west zero-velocity observation missing");
        Drive(west, middle);
        Require(_navigationRig?.LatestGnss?.IsAvailable == false, "tunnel had GNSS fixes");
        OffloadCityData();
        ProcessAndDeliverCityContract();
        Require(_activeMachine?.State == ContractState.Retrabalho && _ledger.BalanceCoords == wallet &&
            _lastTunnelReview?.PortalCount == 1 && _lastTunnelReview.Cloud.CoverageFraction < .9,
            "incomplete first batch avoided rework: " + TunnelReviewText());

        TryLoadCareer();
        Require(_activeMachine?.State == ContractState.Retrabalho && !_lidarEnabled, "rework did not survive reload");
        _carSim = null;
        _lidarEnabled = true;
        Drive(middle, east);
        Stop(east);
        Require(_pendingLidar.Any(slice => slice.TunnelRanges?.ZuptPortal == 1), "east zero-velocity observation missing");
        ProcessAndDeliverCityContract();
        Require(_ledger.BalanceCoords == wallet && _lastTunnelReview?.PortalCount == 1, "pending work entered quality control");
        OffloadCityData();
        ProcessAndDeliverCityContract();
        Require(_lastTunnelReview?.Delivery.Passed == true && CityMissionCompleted(TunnelMissionId) &&
            _activeContract is null && _ledger.BalanceCoords > wallet,
            "completed tunnel did not settle: " + TunnelReviewText() + $" skipped={_lidarTicksWithoutCapacity}");
        var report = TunnelReviewText();
        var paid = _ledger.BalanceCoords;
        TryLoadCareer();
        StartStoryMission(FullCampaignCatalog.GetMission(TunnelMissionId));
        ProcessAndDeliverCityContract();
        Require(CityMissionCompleted(TunnelMissionId) && _ledger.BalanceCoords == paid && _activeContract is null,
            "settlement replayed or campaign progression was lost");
        GD.Print("GSA_PORT_TUNNEL_DELIVERY_SMOKE_OK " + report.Replace('\n', ' '));
    }
}
