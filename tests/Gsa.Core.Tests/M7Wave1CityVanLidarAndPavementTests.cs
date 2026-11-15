using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Fusion;
using Gsa.Core.Sensors.Lidar;
using Gsa.Core.World;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class M7Wave1CityVanLidarAndPavementTests
{
    private static WorldSceneManifest LoadTutorialManifest()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "game", "world", "tutorial-block.json")))
            dir = dir.Parent;
        var path = Path.Combine(dir!.FullName, "game", "world", "tutorial-block.json");
        return WorldSceneManifest.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void T_F02_06_FullCityFourKm2GeodeticNetworkDisplacementWithoutHistoricalMutationAndTraversal()
    {
        var tutorial = LoadTutorialManifest();
        var (fullManifest, network, surveyManifest) = FullCityGeodeticNetwork.AssembleFullCity(tutorial);

        // ~4 km^2 full city (4 regions x 100 cells = 400 sectors) and prior IDs preserved.
        Assert.Equal(4_000_000.0, surveyManifest.TotalCityAreaM2, 3);
        Assert.Equal(4, surveyManifest.TotalRegionCount);
        Assert.Equal(400, surveyManifest.TotalCellCount);
        Assert.True(surveyManifest.DistinctFromCheckpointsVerified);
        Assert.True(surveyManifest.PriorIdsPreserved);
        Assert.Equal(8, network.Benchmarks.Count);

        var distinctBenchmarkRegions = network.Benchmarks.Values
            .Select(b => b.RegionId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["old_center", "port", "serra", "waterfront"], distinctBenchmarkRegions);

        // Clandestine physical displacement updates field monument without rewriting cadastral reference or historical batches.
        var beforeNode = network.Benchmarks["survey_control_serra_summit"];
        var outcome = network.ApplyClandestineDisplacement(
            "survey_control_serra_summit",
            new Vector3d(1.6, -1.2, 0.0),
            alreadyCapturedBatchIds: ["batch_hist_01", "batch_hist_02"]);

        Assert.Equal(beforeNode.PublishedCadastralPositionEnu, outcome.PreservedPublishedEnu);
        Assert.NotEqual(outcome.PreservedPublishedEnu, outcome.UpdatedPhysicalEnu);
        Assert.Equal(2.0, outcome.HorizontalDisplacementMetres, 4);
        Assert.Equal(0, outcome.MutatedHistoricalBatchCount);

        var afterNode = network.Benchmarks["survey_control_serra_summit"];
        Assert.Equal(GeodeticBenchmarkStatus.ClandestinelyDisplaced, afterNode.Status);
        Assert.Equal(beforeNode.PublishedCadastralPositionEnu, afterNode.PublishedCadastralPositionEnu);

        // Full multi-region traversal with streaming active radius 600 m keeps Godot round-trip < 1e-9 m and H = h - Ng exact.
        var traversal = FullCityGeodeticNetwork.SimulateFullCityTraversal(
            fullManifest,
            network,
            streamingActiveRadiusMetres: 600.0,
            simulatedActiveCaptureBatches: 4);

        Assert.True(traversal.Succeeded);
        Assert.Equal(4, traversal.VisitedRegionIds.Count);
        Assert.Equal(8, traversal.OccupiedBenchmarkIds.Count);
        Assert.Equal(4, traversal.ActiveCaptureBatchesIntact);
        Assert.True(traversal.MaxGodotRoundTripErrorMetres < 1e-9);
        Assert.True(traversal.MaxGeoidHeightResidualMetres < 1e-12);
        Assert.All(traversal.Steps, step =>
        {
            Assert.True(step.CaptureAllowed);
            Assert.False(step.SuspendedByUnloadedSafetyBand);
        });
    }

    [Fact]
    public void T_F03_08_EngineeringVanLevel6DualLidarPavementCamerasSuiteMascotRetirementFleetPreservationAndReplay()
    {
        Assert.Equal(6, PlatformProfile.Van.Level);
        Assert.Equal(18.0, PlatformProfile.Van.MaxSpeedMetresPerSecond, 6);
        Assert.Equal(16000, PlatformProfile.Van.VirtualStorageCapacityUnits);
        Assert.Equal(5400.0, PlatformProfile.Van.PhysicalAutonomySeconds, 6);

        // Progression level < 6 cannot equip Level-6 Engineering Van rig.
        var denied = VanMmsRigCoordinator.ConfigureEngineeringVanRig(playerProgressionLevel: 5);
        Assert.False(denied.Succeeded);
        Assert.Equal("van.error.platform_requires_level_6", denied.ReasonKey);

        // Level-6 Engineering Van configures dual L6 LiDARs, 360 camera, dual downward pavement cameras, GNSS+IMU+DMI, and happy retired Gimbalina.
        var rig = VanMmsRigCoordinator.ConfigureEngineeringVanRig(playerProgressionLevel: 6, ImuGrade.Navigation);
        Assert.True(rig.Succeeded);
        Assert.Equal("van.rig.configured", rig.ReasonKey);
        Assert.NotNull(rig.Rack);
        Assert.Equal(LidarProfile.L6.Id, rig.EquippedLidarProfile.Id);
        Assert.Equal(["slot_lidar_a", "slot_lidar_b"], rig.LidarSlotIds);
        Assert.Equal(["slot_pavement_cam_a", "slot_pavement_cam_b"], rig.PavementCameraSlotIds);
        Assert.True(rig.HasPanoramic360);
        Assert.True(rig.HasDualAntennaHeading);
        Assert.True(rig.HasWheelDmi);
        Assert.Equal(ImuGrade.Navigation, rig.EquippedImu.Grade);
        Assert.True(rig.TotalPowerWatts <= VanMmsRigCoordinator.VanRackPowerBudgetWatts);
        Assert.True(rig.TotalWeightGrams <= VanMmsRigCoordinator.VanRackWeightBudgetGrams);

        Assert.True(rig.MascotState.IsRetiredDashboardMascot);
        Assert.False(rig.MascotState.Injured);
        Assert.Equal(0.0, rig.MascotState.Stress, 6);
        Assert.False(rig.MascotState.FeedsStabilizationErrorToSensors);
        Assert.True(rig.MascotState.DashboardBobbleVisualOnly);

        // Fleet preservation verifies all prior platforms remain intact and selectable.
        var fullFleet = new HashSet<LocomotionMode>
        {
            LocomotionMode.Handcart,
            LocomotionMode.Bicycle,
            LocomotionMode.Scooter,
            LocomotionMode.Car,
            LocomotionMode.Kombi,
            LocomotionMode.Pickup,
            LocomotionMode.Van,
        };
        Assert.True(VanMmsRigCoordinator.VerifyFleetPreservesPriorPlatforms(fullFleet));
        fullFleet.Remove(LocomotionMode.Pickup);
        Assert.False(VanMmsRigCoordinator.VerifyFleetPreservesPriorPlatforms(fullFleet));

        // Instrumented replay: nominal completion, virtual storage saturation, and physical autonomy exhaustion.
        var normalReplay = VanMmsRigCoordinator.RunInstrumentedRouteReplay(
            routeDistanceMetres: 600.0,
            targetSpeedMetresPerSecond: 25.0,
            storageUnitsPerSecond: 100);
        Assert.True(normalReplay.RouteCompleted);
        Assert.Equal(CaptureStopReason.None, normalReplay.StopReason);
        Assert.True(normalReplay.RemappedCaptureControlWorked);
        // Target 25 m/s is clamped to the Van's 18 m/s platform cap.
        Assert.Equal(18.0, normalReplay.PeakSpeedMetresPerSecond, 6);
        Assert.Equal(600.0, normalReplay.DistanceMetres, 3);
        Assert.False(normalReplay.MascotState.Injured);

        var storageFullReplay = VanMmsRigCoordinator.RunInstrumentedRouteReplay(
            routeDistanceMetres: 500.0,
            targetSpeedMetresPerSecond: 20.0,
            storageUnitsPerSecond: 50,
            initialUsedStorageUnits: PlatformProfile.Van.VirtualStorageCapacityUnits - 120);
        Assert.False(storageFullReplay.RouteCompleted);
        Assert.Equal(CaptureStopReason.VirtualStorageFull, storageFullReplay.StopReason);

        var autonomyStopReplay = VanMmsRigCoordinator.RunInstrumentedRouteReplay(
            routeDistanceMetres: 500.0,
            targetSpeedMetresPerSecond: 20.0,
            storageUnitsPerSecond: 10,
            initialUsedAutonomySeconds: PlatformProfile.Van.PhysicalAutonomySeconds - 5.0);
        Assert.False(autonomyStopReplay.RouteCompleted);
        Assert.Equal(CaptureStopReason.AutonomyExhausted, autonomyStopReplay.StopReason);
    }

    [Fact]
    public void T_F15_05_EngineeringVanLidar16kRaysSpatialCullingDynamicSnapshotsFpsInvarianceAndOctreeCqInvariance()
    {
        Assert.Equal(
            EngineeringVanLidarAcquisitionOptimizer.ExpectedPrimaryRaysPerSecond,
            LidarProfile.L6.PrimaryRaysPerSecond);

        // Synthetic 3D corridor with 100+ boxes along East axis so spatial grid culls out-of-range boxes cleanly.
        var boxes = new List<LidarAnalyticBox>(120);
        boxes.Add(new LidarAnalyticBox("road_asphalt", "asphalt", new Vector3d(500200.0, 7400050.0, -0.1), new Vector3d(800.0, 20.0, 0.2)));
        for (var i = 0; i < 119; i++)
        {
            var east = 499900.0 + i * 5.5;
            var north = (i % 2 == 0) ? 7400055.0 : 7400045.0;
            boxes.Add(new LidarAnalyticBox($"corridor_post_{i:000}", "concrete", new Vector3d(east, north, 2.0), new Vector3d(0.6, 0.6, 4.0)));
        }

        var poses = Enumerable.Range(0, 25)
            .Select(k => new EstimatedBodyPose(k * 0.1, new Vector3d(500000.0 + k * 1.0, 7400050.0, 1.5), Quaterniond.Identity))
            .ToArray();
        var trajectory = new EstimatedBodyTrajectory(poses);
        var culledCaster = EngineeringVanLidarAcquisitionOptimizer.CreateSpatiallyCulledRayCaster(boxes, LidarProfile.L6.MaxRangeMetres);

        // Stale snapshot (timestamp = 99.0 s) is rejected instead of using future/unaligned pose, while matching snapshot resolves cleanly.
        var staleSnapshots = new[]
        {
            new DynamicObjectTemporalSnapshot(
                "dyn_van_ahead",
                99.0,
                new LidarAnalyticBox("dyn_van_ahead", "metal", new Vector3d(500020.0, 7400050.0, 1.0), new Vector3d(4.0, 2.0, 2.0))),
        };

        var rep20 = EngineeringVanLidarAcquisitionOptimizer.SimulateAcquisitionAtRenderFps(20, 2.0, trajectory, culledCaster, staleSnapshots);
        var rep60 = EngineeringVanLidarAcquisitionOptimizer.SimulateAcquisitionAtRenderFps(60, 2.0, trajectory, culledCaster, staleSnapshots);
        var rep120 = EngineeringVanLidarAcquisitionOptimizer.SimulateAcquisitionAtRenderFps(120, 2.0, trajectory, culledCaster, staleSnapshots);

        Assert.Equal(rep20.PhysicalScanCount, rep60.PhysicalScanCount);
        Assert.Equal(rep60.PhysicalScanCount, rep120.PhysicalScanCount);
        Assert.True(rep20.PhysicalScanCount >= 9);
        Assert.Equal(rep20.PhysicalScanTimestamps, rep60.PhysicalScanTimestamps);
        Assert.Equal(rep60.PhysicalScanTimestamps, rep120.PhysicalScanTimestamps);
        Assert.Equal(rep20.TotalPrimaryRaysCast, rep120.TotalPrimaryRaysCast);
        Assert.Equal(rep20.TotalSamplesGeoreferenced, rep120.TotalSamplesGeoreferenced);
        Assert.True(rep20.TotalSamplesGeoreferenced > 0);
        Assert.Equal(rep20.PhysicalScanCount, rep20.DroppedByExpiredTemporalSnapshotCount);

        // Benchmark baseline linear caster vs spatially culled caster: zero scientific drift, identical hit count.
        var bench = EngineeringVanLidarAcquisitionOptimizer.BenchmarkBaselineVsOptimizedAcquisition(
            boxes,
            new Vector3d(500010.0, 7400050.0, 1.5),
            scanRevolutions: 1);
        Assert.True(bench.ScientificallyIdentical);
        Assert.True(bench.MaxPointDriftMetres < 1e-12);
        Assert.Equal(bench.BaselineHitCount, bench.OptimizedHitCount);
        Assert.True(bench.BaselineHitCount > 0);

        // Verify Octree scientific CQ digest stays invariant under 250k / 50k / 10k visual LOD queries & LRU chunk eviction.
        // 140,000 samples -> 3 chunks (65,536 + 65,536 + 8,928); a 4,000,000-byte LRU budget forces eviction + SHA-256 reload.
        var denseSamples = Enumerable.Range(0, 140_000)
            .Select(i => new GeoreferencedLidarSample(
                SampleId: 10_000L + i,
                PhysicalTimestampSeconds: 1.0 + i * 0.0001,
                MapPositionEnu: new Vector3d(500000.0 + (i % 500), 7400000.0 + (i / 500), 0.02 * (i % 5)),
                Intensity: 0.8f,
                ReturnNumber: 1,
                IsSpuriousRain: false))
            .ToArray();

        var lodReport = EngineeringVanLidarAcquisitionOptimizer.VerifyOctreeCqInvarianceAcrossVisualLodAndEviction(
            "CONTRACT-M7-OCTREE",
            denseSamples,
            ramBudgetBytes: 4_000_000L);

        Assert.True(lodReport.MetricsInvariantUnderLodAndEviction);
        Assert.Equal(140_000, lodReport.TotalScientificSamples);
        Assert.True(lodReport.LruEvictionCount >= 1);
        Assert.True(lodReport.BackingReloadCount >= 1);
        Assert.Equal(lodReport.BaselineScientificSha256, lodReport.PostLodAndEvictionScientificSha256);
        Assert.True(lodReport.Lod250kCount >= lodReport.Lod50kCount);
        Assert.True(lodReport.Lod50kCount >= lodReport.Lod10kCount);
    }

    [Fact]
    public void T_F04_07_DownwardPavementCameraAndLidarStationingPreservesExplicitGaps()
    {
        var originEast = 500000.0;
        var centerNorth = 7400050.0;
        var lidarSamples = new List<GeoreferencedLidarSample>();
        var camObs = new List<PavementCameraFootprintObservation>();
        var sampleId = 1L;

        for (var station = 0; station < 10; station++)
        {
            if (station == 4)
            {
                // Station 4 omitted entirely -> insufficient support gap.
                continue;
            }

            for (var k = 0; k < 4; k++)
            {
                var east = originEast + station + 0.15 + k * 0.2;
                var northOffset = (k % 2 == 0) ? -0.06 : 0.06;
                var elev = 10.00 + 0.02 * station + (northOffset > 0 ? 0.0025 : 0.0);
                lidarSamples.Add(new GeoreferencedLidarSample(
                    sampleId++,
                    station * 0.25 + k * 0.05,
                    new Vector3d(east, centerNorth + northOffset, elev),
                    0.85f,
                    1,
                    IsSpuriousRain: false));
            }

            camObs.Add(new PavementCameraFootprintObservation(
                SampleId: $"cam_pav_{station}",
                CameraSlotId: "slot_pavement_cam_a",
                TimestampSeconds: station * 0.25,
                ChainageMetres: station + 0.5,
                TransverseStripIndex: 0,
                TransverseOffsetMetres: 0.0,
                EstimatedElevationMetres: 10.00 + 0.02 * station,
                ObservedRutDepthMillimetres: 4.5,
                LensObstructionFraction: station == 5 ? 0.55 : 0.02,
                IsOccluded: false));
        }

        var extraction = PavementProfileExtractor.ExtractLongitudinalProfile(
            corridorId: "av_central",
            startChainageMetres: 0.0,
            endChainageMetres: 10.0,
            stripIndex: 0,
            lidarSamples: lidarSamples,
            cameraObservations: camObs,
            corridorOriginEast: originEast,
            corridorCenterNorth: centerNorth);

        Assert.Equal(10, extraction.StationCount);
        Assert.Equal(8, extraction.CoveredStationCount);
        Assert.Equal(2, extraction.ExplicitGapCount);
        Assert.Equal(0.8, extraction.SectionCoverageFraction, 6);

        var gap4 = extraction.Stations[4];
        Assert.True(gap4.IsExplicitGap);
        Assert.Equal("pavement.gap.insufficient_samples", gap4.GapReasonKey);
        Assert.Null(gap4.EstimatedElevationMetres);
        Assert.Null(gap4.EstimatedCrossSlopeFraction);

        var gap5 = extraction.Stations[5];
        Assert.True(gap5.IsExplicitGap);
        Assert.Equal("pavement.gap.lens_obstructed", gap5.GapReasonKey);
        Assert.Null(gap5.EstimatedElevationMetres);
        Assert.Null(gap5.EstimatedRutDepthMillimetres);

        var ok0 = extraction.Stations[0];
        Assert.False(ok0.IsExplicitGap);
        Assert.NotNull(ok0.EstimatedElevationMetres);
        Assert.InRange(ok0.EstimatedElevationMetres!.Value, 9.99, 10.02);
    }
}

