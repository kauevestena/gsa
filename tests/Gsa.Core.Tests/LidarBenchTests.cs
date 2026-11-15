using System.Reflection;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Lidar;
using Gsa.Core.World;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class LidarBenchTests
{
    [Fact]
    public void ProfilesMatchParametrosAndNeverUnlockBelowPlatformLevel4OrMvpShop()
    {
        Assert.Equal(1000, LidarProfile.L4.PrimaryRaysPerSecond);
        Assert.Equal(50.0, LidarProfile.L4.MaxRangeMetres);
        Assert.Equal(0.02, LidarProfile.L4.RangeSigmaMetres);

        Assert.Equal(8000, LidarProfile.L5.PrimaryRaysPerSecond);
        Assert.Equal(80.0, LidarProfile.L5.MaxRangeMetres);
        Assert.Equal(0.01, LidarProfile.L5.RangeSigmaMetres);

        Assert.Equal(16000, LidarProfile.L6.PrimaryRaysPerSecond);
        Assert.Equal(120.0, LidarProfile.L6.MaxRangeMetres);
        Assert.Equal(0.005, LidarProfile.L6.RangeSigmaMetres);

        foreach (var profile in LidarProfile.AllProfiles)
        {
            Assert.False(profile.OfferedInMvpShop);
            Assert.False(profile.CanEquipOn(PlatformProfile.OnFoot));
            Assert.False(profile.CanEquipOn(PlatformProfile.Handcart));
        }

        var lvl4Platform = new PlatformProfile(
            level: 4, id: "truck_l4", mode: LocomotionMode.Handcart,
            maxSpeedMetresPerSecond: 14.0, accelerationMetresPerSecondSquared: 4.0,
            decelerationMetresPerSecondSquared: 8.0, turnRateRadiansPerSecond: 2.0,
            collisionRadiusMetres: 1.0, collisionHeightMetres: 2.0,
            virtualStorageCapacityUnits: 2000, hasFiniteAutonomy: true, physicalAutonomySeconds: 3600, priceCoords: 10000);
        Assert.True(LidarProfile.L4.CanEquipOn(lvl4Platform));
        Assert.False(LidarProfile.L5.CanEquipOn(lvl4Platform));
    }

    [Fact]
    public void MaterialIncidenceAndRainFollowParametrosContract()
    {
        Assert.Equal(0.9, LidarMaterialProperties.ComputeReturnProbability("concrete", 1.0), 6);
        Assert.Equal(0.9 * 0.1, LidarMaterialProperties.ComputeReturnProbability("concrete", 0.0), 6);
        Assert.Equal(0.05, LidarMaterialProperties.ComputeReturnProbability("water", 1.0), 6);
        Assert.Equal(0.1, LidarMaterialProperties.ComputeReturnProbability("glass", 1.0), 6);
        Assert.Equal(40.0, LidarMaterialProperties.GetEffectiveRangeMetres(50.0, isRaining: true), 6);
    }

    [Fact]
    public void VegetationMultiReturnCapsAtThreeAndMinEnergyAndAdvancesByOneCentimetre()
    {
        // Four thin vegetation leaves followed by solid concrete wall along +East ray.
        var objects = new LidarAnalyticBox[]
        {
            new("leaf_1", "vegetation", new Vector3d(500010, 7400000, 100), new Vector3d(0.2, 12, 12)),
            new("leaf_2", "vegetation", new Vector3d(500012, 7400000, 100), new Vector3d(0.2, 12, 12)),
            new("leaf_3", "vegetation", new Vector3d(500014, 7400000, 100), new Vector3d(0.2, 12, 12)),
            new("wall_4", "concrete", new Vector3d(500018, 7400000, 100), new Vector3d(0.5, 16, 16)),
        };

        var queriedStarts = new List<double>();
        var origin = new Vector3d(500000, 7400000, 101);
        var rawCaster = LidarBenchSimulator.CreateAnalyticBoxRayCaster(objects);
        Func<Vector3d, Vector3d, double, LidarRayHit?> spyCaster = (start, dir, rem) =>
        {
            queriedStarts.Add(start.X - origin.X);
            return rawCaster(start, dir, rem);
        };

        // Test single-pulse multi-return via L4 scan directed east along leaf_1..leaf_3 (transmission 0.4 -> E: 1.0 -> 0.4 -> 0.16 -> 0.064 < 0.1!).
        var pose = new EstimatedBodyPose(0.0, origin, Quaterniond.Identity);
        var trajectory = new EstimatedBodyTrajectory([pose]);
        var mount = LidarMountEstimate.Zero;
        var sim = new LidarBenchSimulator(LidarProfile.L4, seed: 42UL);

        // Scan ray close to azimuth 0 (i=49/50 in [-pi/2, +pi/2] with -0.15 rad pitch -> slight tilt; center at z=101 inside 2 m Z height box).
        var ok = sim.TrySimulateScan(0.0, new RigidTransform(Quaterniond.Identity, origin), trajectory, mount,
            isRaining: false, spyCaster, out var scan, out _, zeroNoise: true);
        Assert.True(ok);
        Assert.NotNull(scan);

        // Energy hits: leaf_1 (E=1.0, next=0.4), leaf_2 (E=0.4, next=0.16), leaf_3 (E=0.16, next=0.064 < 0.1 stops before wall_4!).
        // Also verify 0.01 m advance step occurred on bounce start positions for central hit.
        Assert.Contains(queriedStarts, x => x > 10.0 && Math.Abs(x - 10.11) < 0.05);
        var multiPulseReturns = scan!.Returns.Where(r => r.TotalReturnsInPulse > 1).ToArray();
        Assert.NotEmpty(multiPulseReturns);
        Assert.All(scan.Returns, r => Assert.InRange(r.ReturnNumber, 1, LidarMaterialProperties.MaxReturnsPerPulse));
        Assert.True(scan.Returns.Max(r => r.TotalReturnsInPulse) <= 3);
    }

    [Fact]
    public void RainAddsAboutTwoPercentSpuriousReturnsAndReducesRangeTwentyPercent()
    {
        var distantWall = new LidarAnalyticBox(
            "far_wall", "concrete",
            new Vector3d(500045, 7400000, 100), new Vector3d(1, 200, 40)); // 45 m East (> 40 m L4 rain range, < 50 m L4 dry range)
        var rawCaster = LidarBenchSimulator.CreateAnalyticBoxRayCaster([distantWall]);
        var origin = new Vector3d(500000, 7400000, 100);
        var pose = new EstimatedBodyPose(0.0, origin, Quaterniond.Identity);
        var trajectory = new EstimatedBodyTrajectory([pose]);

        var drySimL4 = new LidarBenchSimulator(LidarProfile.L4, seed: 1001UL);
        drySimL4.TrySimulateScan(0.0, new RigidTransform(Quaterniond.Identity, origin), trajectory, LidarMountEstimate.Zero,
            isRaining: false, rawCaster, out var dryObs, out _, zeroNoise: true);
        Assert.Contains(dryObs!.Returns, r => r.MeasuredRangeMetres > 40.5);

        // In rain, L4 effective range drops -20% from 50 m to 40 m so no hit occurs past 40 m.
        var rainSimL4 = new LidarBenchSimulator(LidarProfile.L4, seed: 1001UL);
        Assert.True(rainSimL4.TrySimulateScan(0.0, new RigidTransform(Quaterniond.Identity, origin), trajectory, LidarMountEstimate.Zero,
            isRaining: true, rawCaster, out var rainObsL4, out _));
        Assert.All(rainObsL4!.Returns, r => Assert.True(r.MeasuredRangeMetres <= 40.0 + 0.05));

        // Across 10 L5 scans = 16,000 rays in rain, spurious rate matches ~2% and range respects L5 64 m rain cap.
        var rainSimL5 = new LidarBenchSimulator(LidarProfile.L5, seed: 1002UL);
        var totalSpurious = 0;
        for (var i = 0; i < 10; i++)
        {
            Assert.True(rainSimL5.TrySimulateScan(0.0, new RigidTransform(Quaterniond.Identity, origin), trajectory, LidarMountEstimate.Zero,
                isRaining: true, rawCaster, out var rainObsL5, out _));
            totalSpurious += rainObsL5!.SpuriousRainReturns;
            Assert.All(rainObsL5.Returns, r => Assert.True(r.MeasuredRangeMetres <= 64.0 + 0.05));
        }
        var rate = totalSpurious / 16000.0;
        Assert.InRange(rate, 0.015, 0.025);
    }

    [Fact]
    public void AnalyticalPlanarBoresightLeverArmAndTimeOffsetGeoreferenceExactlyWithoutTruth()
    {
        // Known plane at East = 500020.0 (normal -X). Vehicle travels North (+Y) at 10 m/s, Lidar lever-arm (0.5, -0.2, 0.3) m,
        // boresight yaw +90 deg (Lidar +X points Left / North, unless rotated back -90 deg), time offset +0.02 s!
        var boresightYaw = Quaterniond.FromAxisAngle(Vector3d.UnitZ, 0.15);
        var leverArm = new Vector3d(0.4, -0.3, 0.6);
        var timeOffset = 0.02;
        var mount = new LidarMountEstimate(leverArm, boresightYaw, timeOffset);

        var plane = new LidarAnalyticBox(
            "calib_plane", "concrete",
            new Vector3d(500020.5, 7400020.0, 102.0), new Vector3d(1.0, 80.0, 30.0)); // west face at X = 500020.0
        var caster = LidarBenchSimulator.CreateAnalyticBoxRayCaster([plane]);

        var bodyPos0 = new Vector3d(500000.0, 7400010.0, 100.0);
        var bodyVel = new Vector3d(0.0, 10.0, 0.0);
        var bodyPose0 = new EstimatedBodyPose(0.0, bodyPos0, Quaterniond.Identity);
        var bodyPose1 = new EstimatedBodyPose(0.1, bodyPos0 + bodyVel * 0.1, Quaterniond.Identity);
        var trajectory = new EstimatedBodyTrajectory([bodyPose0, bodyPose1]);

        var trueBodyAtSample = new RigidTransform(Quaterniond.Identity, bodyPos0 + bodyVel * timeOffset);
        var trueLidarToMap = LidarBenchSimulator.ComposeLidarToMap(trueBodyAtSample, mount);

        var sim = new LidarBenchSimulator(LidarProfile.L4, seed: 55UL);
        var ok = sim.TrySimulateScan(0.0, trueLidarToMap, trajectory, mount, isRaining: false, caster,
            out var obs, out _, zeroNoise: true);
        Assert.True(ok);
        Assert.NotNull(obs);
        Assert.NotEmpty(obs!.Returns);

        // Every point hitting the west plane must georeference strictly onto X = 500020.0 within 1e-6 m!
        Assert.All(obs.Returns, pt => Assert.InRange(Math.Abs(pt.GeoreferencedPointEnu.X - 500020.0), 0.0, 1e-6));

        // Backlog 0.5 s saturation check (L4 @ 10 Hz -> MaxPending = 5).
        for (var i = 0; i < sim.MaxPendingQueueScans; i++)
            Assert.True(sim.TrySimulateScan(0.0, trueLidarToMap, trajectory, mount, false, caster, out _, out _, autoDrain: false));
        Assert.False(sim.TrySimulateScan(0.0, trueLidarToMap, trajectory, mount, false, caster, out _, out var satReason, autoDrain: false));
        Assert.Equal("sensor_queue_saturated", satReason);

        // Reflection guard: no TruthSample / GroundTruth / TruePose in Lidar DTOs.
        Type[] dtoTypes = [typeof(LidarScanObservation), typeof(LidarMeasuredReturn), typeof(LidarMountEstimate)];
        foreach (var type in dtoTypes)
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.DoesNotContain("Truth", prop.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("True", prop.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
