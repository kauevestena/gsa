using Gsa.Core.Geodesy;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Calibration;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Lidar;
using Gsa.Core.Traffic;
using Gsa.Core.World;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class M6SerraPickupPairedLidarAndEventsTests
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
    public void T_F02_05_SerraRegionManifestSlopesDirtVegetationTunnelConnectionPickupRoutesCalmAlternativesAndStreamingSafety()
    {
        var tutorial = LoadTutorialManifest();
        var mvp = OldCenterMvpWorldAssembler.BuildOldCenterMvpManifest(tutorial);
        var act1City = RegionalSectorCatalog.BuildExpandedCityManifest(tutorial);
        var serraCity = SerraWorldAssembler.BuildSerraExpandedManifest(tutorial);

        // Full 4 km^2 city footprint: 400 x 100x100 m sectors, preserving 100% of MVP/Act1 IDs
        Assert.Equal(300, act1City.Cells.Count);
        Assert.Equal(400, serraCity.Cells.Count);
        Assert.Equal(100, serraCity.Cells.Count(c => c.Id.StartsWith("serra_sector_", StringComparison.Ordinal)));

        var preservation = RegionalSectorCatalog.VerifyMvpTargetIdsPreserved(mvp, serraCity);
        Assert.True(preservation.Preserved);
        Assert.Empty(preservation.MissingOrChangedObjectIds);

        // Serra semantic objects cover controls, tunnel connection, canopies, boundary monuments, poles, slopes, and rock obstacles
        Assert.Contains(serraCity.Objects, o => o.Id == "survey_control_serra_foothill");
        Assert.Contains(serraCity.Objects, o => o.Id == "survey_control_serra_summit");
        Assert.Contains(serraCity.Objects, o => o.Id == "serra_tunnel_east_portal_01" && o.RegionId == "serra");
        Assert.Contains(serraCity.Objects, o => o.Id == "serra_canopy_01" && o.Material == "vegetation");
        Assert.Contains(serraCity.Objects, o => o.Id == "serra_canopy_wall_01" && o.Material == "concrete");
        Assert.Equal(6, serraCity.Objects.Count(o => o.Id.StartsWith("serra_divisa_marco_", StringComparison.Ordinal)));
        Assert.Equal(8, serraCity.Objects.Count(o => o.Id.StartsWith("serra_utility_pole_", StringComparison.Ordinal)));

        // Geodetic height consistency H = h - Ng and terrain types across Serra cells
        var terrainMap = SerraWorldAssembler.BuildSerraTerrainMap();
        Assert.Equal(100, terrainMap.Count);
        Assert.Contains(terrainMap.Values, c => c.SurfaceType == SerraSurfaceType.DirtTrack && c.SlopeGradePercent >= 8.0);
        Assert.Contains(terrainMap.Values, c => c.SurfaceType == SerraSurfaceType.SteepDirtSlope && c.SlopeGradePercent >= 16.0);
        Assert.Contains(terrainMap.Values, c => c.SurfaceType == SerraSurfaceType.DenseCanopyCorridor && c.HasTransmissiveCanopy);
        Assert.Contains(terrainMap.Values, c => c.SurfaceType == SerraSurfaceType.TunnelGalleryConnection && c.IsTunnelInterior);

        foreach (var cell in terrainMap.Values)
        {
            var center = new Vector3d(
                (cell.BoundsEnu.Min.X + cell.BoundsEnu.Max.X) * 0.5,
                (cell.BoundsEnu.Min.Y + cell.BoundsEnu.Max.Y) * 0.5,
                cell.EllipsoidalHeightMetres);
            Assert.Equal(LocalGeoidModel.ToOrthometricHeight(center), cell.OrthometricHeightMetres, 6);
            Assert.True(cell.WetGripMultiplier < cell.DryGripMultiplier);
        }

        // Every non-calm Pickup route points to an existing calm-safe alternative
        var routesById = SerraWorldAssembler.PickupRoutes.ToDictionary(r => r.RouteId, StringComparer.Ordinal);
        foreach (var route in SerraWorldAssembler.PickupRoutes.Where(r => !r.IsCalmSafe))
        {
            Assert.NotNull(route.CalmAlternativeRouteId);
            Assert.True(routesById.TryGetValue(route.CalmAlternativeRouteId!, out var calmAlt));
            Assert.True(calmAlt!.IsCalmSafe);
        }

        // Streaming safety: unloaded required cell suspends capture with explicit warning rather than inventing empty hits
        var loadedCells = new HashSet<string>(["serra_sector_0_0", "serra_sector_1_1"], StringComparer.Ordinal);
        var unsafeStreaming = SerraWorldAssembler.EvaluateStreamingCaptureSafety(loadedCells, ["serra_sector_1_1", "serra_sector_2_2"]);
        Assert.False(unsafeStreaming.CaptureAllowed);
        Assert.True(unsafeStreaming.CaptureSuspendedWithWarning);
        Assert.Equal("world.streaming.cell_not_loaded_capture_suspended", unsafeStreaming.WarningKey);
        Assert.Equal(["serra_sector_2_2"], unsafeStreaming.MissingCellIds);

        var safeStreaming = SerraWorldAssembler.EvaluateStreamingCaptureSafety(loadedCells, ["serra_sector_0_0", "serra_sector_1_1"]);
        Assert.True(safeStreaming.CaptureAllowed);
        Assert.False(safeStreaming.CaptureSuspendedWithWarning);
    }

    [Fact]
    public void T_F03_07_PickupFourByFourTractionDryWetSlopeReplayObstacleCollisionAndDualLidarRackConfiguration()
    {
        Assert.Equal(5, PlatformProfile.Pickup.Level);
        Assert.Equal(20.0, PlatformProfile.Pickup.MaxSpeedMetresPerSecond, 6);
        Assert.Equal(8000, PlatformProfile.Pickup.VirtualStorageCapacityUnits);
        Assert.Equal(4200.0, PlatformProfile.Pickup.PhysicalAutonomySeconds, 6);
        Assert.Equal(30000, PlatformProfile.Pickup.PriceCoords);

        // Dual-LiDAR slot B requires Level 5 (Kombi Level 4 denied, Pickup Level 5 ready)
        Assert.False(PickupFourByFourRigCoordinator.ConfigureDualLidarRig(playerProgressionLevel: 4).Succeeded);
        var kombiRack = new ModularSensorRack(PlatformProfile.Kombi);
        var kombiSlotB = kombiRack.MountOrMoveSensor("lidar_b", RackSlotKind.LidarProfilerOrScanner, "slot_lidar_b", 0.0);
        Assert.False(kombiSlotB.Succeeded);
        Assert.Equal("rack.error.platform_level_insufficient", kombiSlotB.ReasonKey);

        var rig = PickupFourByFourRigCoordinator.ConfigureDualLidarRig(playerProgressionLevel: 5);
        Assert.True(rig.Succeeded);
        Assert.NotNull(rig.Rack);
        Assert.Equal(["slot_lidar_a", "slot_lidar_b"], rig.LidarSlotIds);
        Assert.True(rig.HasDualAntennaHeading);
        Assert.True(rig.HasWheelDmi);

        var tutorial = LoadTutorialManifest();
        var serraManifest = SerraWorldAssembler.BuildSerraExpandedManifest(tutorial);
        var terrainMap = SerraWorldAssembler.BuildSerraTerrainMap();
        var dirtRoute = SerraWorldAssembler.PickupRoutes.First(r => r.RouteId == "serra_foothill_dirt_route");
        var obstacles = serraManifest.Objects.Where(o => o.Collision).ToArray();

        // Dry vs Wet 4x4 slope replays: both succeed with 0 obstacle penetration; wet has higher slip & slower transit
        var dryReplay = PickupFourByFourSlopeSimulator.RunSerraSlopeReplay(dirtRoute, terrainMap, obstacles, isWet: false, fourWheelDriveEngaged: true);
        var wetReplay = PickupFourByFourSlopeSimulator.RunSerraSlopeReplay(dirtRoute, terrainMap, obstacles, isWet: true, fourWheelDriveEngaged: true);

        Assert.True(dryReplay.RouteCompleted);
        Assert.True(wetReplay.RouteCompleted);
        Assert.Equal(0, dryReplay.ObstaclePenetrationCount);
        Assert.Equal(0, wetReplay.ObstaclePenetrationCount);
        Assert.True(wetReplay.PeakWheelSlipRatio > dryReplay.PeakWheelSlipRatio);
        Assert.True(wetReplay.ElapsedSeconds > dryReplay.ElapsedSeconds);

        // Disabling 4x4 on steep wet mud stalls on steep climb instead of ignoring surface adherence
        var simNo4wd = new PickupFourByFourSlopeSimulator(new Vector3d(501400, 7401400, 150));
        var steepCell = terrainMap["serra_sector_5_5"];
        for (var i = 0; i < 30; i++)
        {
            var step = simNo4wd.StepAlongDirection(0.2, new Vector3d(1, 0, 0), 1.0, steepCell.SlopeAngleRadians, steepCell.WetGripMultiplier, fourWheelDriveEngaged: false, obstacles: []);
            if (i == 29)
                Assert.True(step.StalledOnSlope);
        }

        // Direct obstacle approach halts before penetrating solid rock box and emits impact event
        var rock = serraManifest.Objects.First(o => o.Id == "serra_rock_obstacle_01");
        var crashSim = new PickupFourByFourSlopeSimulator(new Vector3d(rock.PositionEnu.X - 18.0, rock.PositionEnu.Y, rock.PositionEnu.Z));
        PickupSlopeStepState? blockedState = null;
        for (var i = 0; i < 60; i++)
        {
            var s = crashSim.StepAlongDirection(0.1, new Vector3d(1, 0, 0), 1.0, 0.0, 1.0, fourWheelDriveEngaged: true, obstacles: [rock]);
            Assert.False(PickupFourByFourSlopeSimulator.IntersectsBox(s.PositionEnu, 0.1, rock));
            if (s.BlockedByObstacle)
            {
                blockedState = s;
                break;
            }
        }
        Assert.NotNull(blockedState);
        Assert.True(blockedState!.BlockedByObstacle);
        Assert.Equal(0.0, blockedState.SpeedMetresPerSecond, 6);
        Assert.NotEmpty(crashSim.ImpactLog);
    }

    [Fact]
    public void T_F15_04_PairedLidarL5MultiReturnTransmissiveVegetationRainIncidenceAndPerSensorMounting()
    {
        Assert.Equal(2, LidarProfile.L5.SensorUnits);
        Assert.Equal(8, LidarProfile.L5.ChannelsPerSensor);
        Assert.Equal(80.0, LidarProfile.L5.MaxRangeMetres, 6);
        Assert.Equal(8000, LidarProfile.L5.PrimaryRaysPerSecond);

        var rig = PickupFourByFourRigCoordinator.ConfigureDualLidarRig(playerProgressionLevel: 5);
        var rack = rig.Rack!;

        var tutorial = LoadTutorialManifest();
        var serraManifest = SerraWorldAssembler.BuildSerraExpandedManifest(tutorial);
        var ridgeRoute = SerraWorldAssembler.PickupRoutes.First(r => r.RouteId == "serra_ridge_canopy_route");

        // 1. Dry vegetation canopy replay at 6 m/s into octree store -> paired multi-return behind foliage + octree ingestion
        var simSlow = new PairedMultiReturnLidarSimulator(LidarProfile.L5, seed: 20261116UL);
        var store = new PointCloudOctreeAndChunkStore();
        var drySlow = simSlow.RunSerraVegetationReplay(ridgeRoute, serraManifest, rack, speedMetresPerSecond: 6.0, isRaining: false, octreeStore: store, zeroNoise: true);

        Assert.True(drySlow.ReturnsBySlot.GetValueOrDefault("slot_lidar_a") > 0);
        Assert.True(drySlow.ReturnsBySlot.GetValueOrDefault("slot_lidar_b") > 0);
        Assert.True(drySlow.MultiReturnPulseCount > 0);
        Assert.True(drySlow.FirstReturnCount > 0);
        Assert.True(drySlow.SecondPlusReturnCount > 0);
        Assert.Equal(0, drySlow.SpuriousRainReturnCount);
        Assert.Equal(drySlow.TotalReturnCount, store.GetMetrics().TotalSamples);

        // Coherent ReturnNumber / TotalReturnsInPulse grouping by (ScanId, SensorUnitId, PulseIndex)
        var pulses = drySlow.AllReturns.GroupBy(r => (r.ScanId, r.SensorUnitId, r.PulseIndex));
        foreach (var pulse in pulses)
        {
            var ordered = pulse.OrderBy(p => p.ReturnNumber).ToArray();
            var expectedTotal = ordered[0].TotalReturnsInPulse;
            Assert.True(expectedTotal >= 1 && expectedTotal <= LidarMaterialProperties.MaxReturnsPerPulse);
            for (var idx = 0; idx < ordered.Length; idx++)
            {
                Assert.Equal(idx + 1, ordered[idx].ReturnNumber);
                Assert.Equal(expectedTotal, ordered[idx].TotalReturnsInPulse);
                if (idx > 0)
                    Assert.True(ordered[idx].MeasuredRangeMetres > ordered[idx - 1].MeasuredRangeMetres);
            }
        }

        // 2. Speed proportionality (AC-F15-02): faster drive (15 m/s) collects fewer primary rays & returns than 6 m/s
        var simFast = new PairedMultiReturnLidarSimulator(LidarProfile.L5, seed: 20261116UL);
        var dryFast = simFast.RunSerraVegetationReplay(ridgeRoute, serraManifest, rack, speedMetresPerSecond: 15.0, isRaining: false, zeroNoise: true);
        Assert.True(dryFast.PrimaryRayCount < drySlow.PrimaryRayCount);
        Assert.True(dryFast.TotalReturnCount < drySlow.TotalReturnCount);

        // 3. Rain attenuation & spurious rain droplets (AC-F15-04) + empty sky gap preservation
        var simRain = new PairedMultiReturnLidarSimulator(LidarProfile.L5, seed: 20261116UL);
        var wetSlow = simRain.RunSerraVegetationReplay(ridgeRoute, serraManifest, rack, speedMetresPerSecond: 6.0, isRaining: true, zeroNoise: false);
        Assert.True(wetSlow.SpuriousRainReturnCount > 0);
        Assert.All(wetSlow.AllReturns.Where(r => r.IsSpuriousRain), r =>
            Assert.True(r.MeasuredRangeMetres <= LidarMaterialProperties.GetEffectiveRangeMetres(LidarProfile.L5.MaxRangeMetres, true) + 1e-6));

        // 4. Incidence attenuation + zero artificial points in water/sky gaps
        var normalProb = LidarMaterialProperties.ComputeReturnProbability("concrete", 1.0);
        var grazingProb = LidarMaterialProperties.ComputeReturnProbability("concrete", 0.25);
        Assert.True(normalProb > grazingProb);

        var gapRayCaster = LidarBenchSimulator.CreateAnalyticBoxRayCaster(Array.Empty<WorldBoxObject>());
        var pose = new EstimatedBodyPose(1.0, new Vector3d(501200, 7401320, 138), Quaterniond.Identity);
        var trajectory = new EstimatedBodyTrajectory([new EstimatedBodyPose(0.0, pose.PositionEnu, pose.BodyToMap), new EstimatedBodyPose(5.0, pose.PositionEnu, pose.BodyToMap)]);
        var gapScan = new PairedMultiReturnLidarSimulator(LidarProfile.L5).SimulatePairedScan(1.0, new RigidTransform(pose.BodyToMap, pose.PositionEnu), trajectory, rack, isRaining: false, gapRayCaster, zeroNoise: true);
        Assert.Empty(gapScan.Returns);

        // 5. Independent per-sensor mounting: altering ONLY slot_lidar_b lever arm changes slot_lidar_b georeferenced positions without changing slot_lidar_a
        var wallRayCaster = LidarBenchSimulator.CreateAnalyticBoxRayCaster([serraManifest.Objects.First(o => o.Id == "serra_canopy_wall_01")]);
        var poseNearWall = new Vector3d(501245.0, 7401308.0, 139.0);
        var trajWall = new EstimatedBodyTrajectory([new EstimatedBodyPose(0.0, poseNearWall, Quaterniond.Identity), new EstimatedBodyPose(10.0, poseNearWall, Quaterniond.Identity)]);

        var baseline = new PairedMultiReturnLidarSimulator(LidarProfile.L5).SimulatePairedScan(2.0, new RigidTransform(Quaterniond.Identity, poseNearWall), trajWall, rack, isRaining: false, wallRayCaster, zeroNoise: true);
        var baselineA = baseline.Returns.First(r => r.SlotId == "slot_lidar_a");
        var baselineB = baseline.Returns.First(r => r.SlotId == "slot_lidar_b");

        var slotBMount = rack.MountedSlots["slot_lidar_b"].EstimatedCalibrationMount;
        rack.ApplyEstimatedCalibration("slot_lidar_b", slotBMount.LeverArmBody + new Vector3d(0.0, 0.0, 0.75), slotBMount.SensorToBody, slotBMount.TimeOffsetSeconds, effectiveTimeSeconds: 3.0);

        var shifted = new PairedMultiReturnLidarSimulator(LidarProfile.L5).SimulatePairedScan(4.0, new RigidTransform(Quaterniond.Identity, poseNearWall), trajWall, rack, isRaining: false, wallRayCaster, zeroNoise: true);
        var shiftedA = shifted.Returns.First(r => r.SlotId == "slot_lidar_a" && r.PulseIndex == baselineA.PulseIndex && r.ReturnNumber == baselineA.ReturnNumber);
        var shiftedB = shifted.Returns.First(r => r.SlotId == "slot_lidar_b" && Math.Abs(r.AzimuthRadians - baselineB.AzimuthRadians) < 1e-9 && Math.Abs(r.ElevationRadians - baselineB.ElevationRadians) < 1e-9 && r.ReturnNumber == baselineB.ReturnNumber);

        Assert.Equal(baselineA.GeoreferencedPointEnu, shiftedA.GeoreferencedPointEnu);
        Assert.Equal(0.75, shiftedB.GeoreferencedPointEnu.Z - baselineB.GeoreferencedPointEnu.Z, 6);
    }

    [Fact]
    public void T_F18_03_AbsurdUrbanEventsTrioMarathonPigeonsWarningGraphDetourTerminationAndCalmPolicy()
    {
        var graph = AbsurdUrbanEventsCoordinator.CreateDetourAndBottleneckTestGraph();
        var coordinator = new AbsurdUrbanEventsCoordinator();

        // 1. Trio eletrico, street marathon, and pigeon flock start cleanly when detour exists
        var trio = coordinator.TryStartEvent("ev_trio", AbsurdUrbanEventKind.TrioEletrico, graph, ["seg_main"], "seg_entry", "seg_loop_back", requestedDurationSeconds: 180.0);
        Assert.True(trio.Started);
        Assert.True(trio.EventState!.AdvanceWarningIssued);
        Assert.Equal(15.0, trio.EventState.WarningLeadTimeSeconds, 6);
        Assert.Equal(120.0, trio.EventState.DurationSeconds, 6); // capped at 2 physical minutes
        Assert.Contains("seg_detour_in", trio.EventState.SignpostedDetourSegmentIds);
        Assert.Contains("seg_detour_out", trio.EventState.SignpostedDetourSegmentIds);

        var marathon = coordinator.TryStartEvent("ev_marathon", AbsurdUrbanEventKind.StreetMarathon, graph, ["seg_main"], "seg_entry", "seg_loop_back", requestedDurationSeconds: 90.0);
        Assert.True(marathon.Started);

        var pigeons = coordinator.TryStartEvent("ev_pigeons", AbsurdUrbanEventKind.PigeonFlock, graph, ["seg_main"], "seg_entry", "seg_loop_back", requestedDurationSeconds: 60.0);
        Assert.True(pigeons.Started);
        Assert.True(pigeons.EventState!.PigeonObservationClutterRate > 0.0);

        // 2. Blocking the unique bridge bottleneck seals the only route and MUST be rejected
        var rejected = coordinator.TryStartEvent("ev_blocked_bridge", AbsurdUrbanEventKind.StreetMarathon, graph, ["seg_unique_bridge"], "seg_entry", "seg_loop_back");
        Assert.False(rejected.Started);
        Assert.Equal("event.rejected.seals_unique_contract_route", rejected.ReasonKey);

        // 3. Calm contract & Calm City keep visual spectacle while zeroing threats/stress
        var calmContractEvent = coordinator.TryStartEvent("ev_calm_trio", AbsurdUrbanEventKind.TrioEletrico, graph, ["seg_main"], "seg_entry", "seg_loop_back", isCalmContract: true);
        Assert.True(calmContractEvent.Started);
        Assert.True(calmContractEvent.EventState!.EnvironmentalSpectacleOnly);
        Assert.False(calmContractEvent.EventState.CausesDamageOrThreat);
        Assert.False(calmContractEvent.EventState.CausesHostileStress);

        coordinator.ApplyCalmPolicy(true);
        Assert.All(coordinator.Events.Values.Where(e => e.Active), ev =>
        {
            Assert.True(ev.EnvironmentalSpectacleOnly);
            Assert.False(ev.CausesDamageOrThreat);
            Assert.False(ev.CausesHostileStress);
        });

        // 4. Stepping past duration ends every event on time and unblocks road segments
        coordinator.StepEvents(120.0);
        Assert.Empty(coordinator.GetCurrentlyBlockedSegmentIds());
        Assert.All(coordinator.Events.Values, ev =>
        {
            Assert.False(ev.Active);
            Assert.Equal("event.ended.duration_completed", ev.EndedReasonKey);
        });
    }
}
