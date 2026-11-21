using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Missions.Campaign;
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
        _lidarCloudMesh.Visible = true;
        GD.Print($"GSA_CITY_LIDAR_CAPTURE_SMOKE_OK protected_returns={expected} no_pose=explicit capacity=reported lod=independent loss=protected reload=stable payout=blocked");
    }
}
