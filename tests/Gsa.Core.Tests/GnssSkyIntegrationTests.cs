using System.Reflection;
using System.Text.Json;
using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.World;
using Xunit;

namespace Gsa.Core.Sensors.Gnss;

public sealed class GnssSkyIntegrationTests
{
    private static WorldSceneManifest CreateTestManifest(double width = 60.0, double northDepth = 20.0, params object[] extraObjects)
    {
        var bounds = new
        {
            min = new[] { 500000.0, 7400000.0, 90.0 },
            max = new[] { 500000.0 + width, 7400000.0 + northDepth, 130.0 },
        };
        var objects = new List<object>
        {
            Box("ground", width / 2.0, northDepth / 2.0, 99.9, width, northDepth, 0.2, material: "asphalt", tags: new[] { "ground" }),
        };
        objects.AddRange(extraObjects);

        var json = JsonSerializer.Serialize(new
        {
            schema_version = 1,
            scene_id = "gnss_sky_fixture",
            map_version = 1,
            datum_id = "SAO_DATUM_LOCAL_V1",
            origin_enu = new[] { 500000.0, 7400000.0, 100.0 },
            bounds_enu = bounds,
            cells = new[] { new { id = "cell_01", bounds_enu = bounds, environment_class = "urban_canyon" } },
            objects,
        });
        return WorldSceneManifest.Parse(json);
    }

    private static object Box(
        string id,
        double localEast,
        double localNorth,
        double up,
        double eastSize,
        double northSize,
        double upSize,
        string material = "concrete",
        string @class = "building",
        string[]? tags = null,
        bool collision = true) => new
    {
        id,
        @class,
        material,
        region_id = "old_center",
        surface_ids = new[] { "test_surface" },
        tags = tags ?? new[] { "building" },
        dynamic = false,
        position_enu = new[] { 500000.0 + localEast, 7400000.0 + localNorth, up },
        size_enu = new[] { eastSize, northSize, upSize },
        collision,
    };

    [Fact]
    public void PlatformProfileMapsNominalGnssAndHeadingProfiles()
    {
        Assert.Equal(GnssProfile.Cellphone, GnssProfileSelector.SelectGnssProfile(PlatformProfile.Handcart));
        Assert.Equal(GnssProfile.Cellphone, GnssProfileSelector.SelectGnssProfile(PlatformProfile.OnFoot));
        Assert.Equal(GnssProfile.Navigation, GnssProfileSelector.SelectGnssProfile(PlatformProfile.Handcart, hasNavigationUpgrade: true));

        var level1 = new PlatformProfile(1, "bike", LocomotionMode.Handcart, 6, 4, 8, 2.5, 0.5, 1.5, 400, true, 2400, 400);
        var level2 = new PlatformProfile(2, "scooter", LocomotionMode.Handcart, 12, 5, 10, 2.5, 0.6, 1.5, 800, true, 3000, 1200);
        Assert.Equal(GnssProfile.Navigation, GnssProfileSelector.SelectGnssProfile(level1));
        Assert.Equal(HeadingProfile.Level0, GnssProfileSelector.SelectHeadingProfile(PlatformProfile.Handcart));
        Assert.Equal(HeadingProfile.Level1, GnssProfileSelector.SelectHeadingProfile(level1));
        Assert.Equal(HeadingProfile.Level2, GnssProfileSelector.SelectHeadingProfile(level2));
    }

    [Fact]
    public void SkyCacheOpennessRoofBlockAndOutOfGridControlDopAndAvailability()
    {
        // Cell [0,0] [0..10, 0..10]: open sky (openness ~ 1).
        // Cell [2,0] [20..30, 0..10]: tall nearby facade at North=8..8.2 -> partial openness, higher DOP.
        // Cell [4,0] [40..50, 0..10]: enclosed tunnel/roof box -> openness 0 (< 0.1) -> unavailable.
        var manifest = CreateTestManifest(width: 60.0, northDepth: 10.0,
            Box("facade_canyon", 25.0, 8.0, 105.0, 8.0, 0.2, 10.0),
            Box("tunnel_roof", 45.0, 5.0, 105.0, 10.0, 10.0, 1.0),
            Box("tunnel_west", 40.1, 5.0, 102.5, 0.2, 10.0, 5.0),
            Box("tunnel_east", 49.9, 5.0, 102.5, 0.2, 10.0, 5.0),
            Box("tunnel_south", 45.0, 0.1, 102.5, 10.0, 0.2, 5.0),
            Box("tunnel_north", 45.0, 9.9, 102.5, 10.0, 0.2, 5.0));

        var cache = SkyPrecomputer.Compute(manifest, 10.0, 2.0, 64);
        var resolver = new GnssSkyEnvironmentResolver(cache, manifest, multipathSeed: 0xAA11UL);

        var open = resolver.Resolve(new Vector3d(500005.0, 7400005.0, 102.0));
        var canyon = resolver.Resolve(new Vector3d(500025.0, 7400005.0, 102.0));
        var tunnel = resolver.Resolve(new Vector3d(500045.0, 7400005.0, 102.0));
        var outside = resolver.Resolve(new Vector3d(499950.0, 7400005.0, 102.0));

        Assert.True(open.InSkyGrid);
        Assert.True(open.Environment.SignalAvailable);
        Assert.InRange(open.Environment.Openness, 0.7, 1.0);

        Assert.True(canyon.InSkyGrid);
        Assert.True(canyon.Environment.SignalAvailable);
        Assert.True(canyon.Environment.Openness < open.Environment.Openness);
        Assert.True(canyon.Environment.Dop > open.Environment.Dop);
        Assert.Equal(1 + 4 * (1 - canyon.Environment.Openness), canyon.Environment.Dop, 9);

        Assert.True(tunnel.InSkyGrid);
        Assert.Equal(0.0, tunnel.Environment.Openness);
        Assert.False(tunnel.Environment.SignalAvailable);
        Assert.Null(new GnssSimulator(1).Observe(0.0, new Vector3d(500045.0, 7400005.0, 102.0), GnssProfile.Cellphone, tunnel.Environment, Vector3d.Zero).MeasuredPositionEnu);

        // Outside the baked sky grid never fabricates open-sky availability.
        Assert.False(outside.InSkyGrid);
        Assert.False(outside.Environment.SignalAvailable);
        Assert.Null(new GnssSimulator(1).Observe(0.0, new Vector3d(499950.0, 7400005.0, 102.0), GnssProfile.Cellphone, outside.Environment, Vector3d.Zero).MeasuredPositionEnu);
    }

    [Fact]
    public void MetalOrGlassFacadeWithinFifteenMetresAmplifiesCellMultipathWithSameDirection()
    {
        // Glass wall at local X:[45, 55], Y:[0, 10].
        var glassWall = Box("glass_wall", 50.0, 5.0, 105.0, 10.0, 10.0, 10.0, material: "glass");
        var concreteWall = Box("glass_wall", 50.0, 5.0, 105.0, 10.0, 10.0, 10.0, material: "concrete");
        var manifestReflective = CreateTestManifest(width: 60.0, northDepth: 10.0, glassWall);
        var manifestNonReflective = CreateTestManifest(width: 60.0, northDepth: 10.0, concreteWall);

        var cacheReflective = SkyPrecomputer.Compute(manifestReflective, 10.0, 2.0, 32);
        var cacheNonReflective = SkyPrecomputer.Compute(manifestNonReflective, 10.0, 2.0, 32);

        var resolverReflective = new GnssSkyEnvironmentResolver(cacheReflective, manifestReflective, multipathSeed: 0x55AAUL, baseMultipathAmplitudeMetres: 1.0, metalGlassFacadeFactor: 3.0);
        var resolverConcrete = new GnssSkyEnvironmentResolver(cacheNonReflective, manifestNonReflective, multipathSeed: 0x55AAUL, baseMultipathAmplitudeMetres: 1.0, metalGlassFacadeFactor: 3.0);

        var nearPos = new Vector3d(500034.0, 7400005.0, 102.0); // 11 m from X=500045
        var farPos = new Vector3d(500020.0, 7400005.0, 102.0);  // 25 m from X=500045

        var near = resolverReflective.Resolve(nearPos);
        var far = resolverReflective.Resolve(farPos);
        Assert.True(near.NearMetalOrGlass);
        Assert.Equal(3.0, near.FacadeFactor);
        Assert.Equal(3.0, near.Environment.MultipathBiasEnu.Length, 9);
        Assert.False(far.NearMetalOrGlass);
        Assert.Equal(1.0, far.FacadeFactor);
        Assert.Equal(1.0, far.Environment.MultipathBiasEnu.Length, 9);

        var sameCellWithoutReflective = resolverConcrete.Resolve(nearPos);
        Assert.False(sameCellWithoutReflective.NearMetalOrGlass);
        Assert.Equal(1.0, Vector3d.Dot(near.Environment.MultipathBiasEnu.Normalized(), sameCellWithoutReflective.Environment.MultipathBiasEnu.Normalized()), 9);
    }

    [Fact]
    public void StoppedPlayerWithoutReliableHeadingProducesUnavailableHeadingAndCannotFabricateTrajectory()
    {
        var manifest = CreateTestManifest(width: 20.0, northDepth: 20.0);
        var cache = SkyPrecomputer.Compute(manifest, 10.0, 2.0, 32);
        var resolver = new GnssSkyEnvironmentResolver(cache, manifest, multipathSeed: 99UL);
        var rig = new GnssHeadingRig(resolver, 100UL, 200UL, 300UL);

        var stoppedPos = new Vector3d(500010.0, 7400010.0, 102.0);
        var obs0 = rig.Observe(0.0, stoppedPos, 1.2, PlatformProfile.Handcart, headingReliable: false);
        var obs1 = rig.Observe(0.2, stoppedPos, 1.2, PlatformProfile.Handcart, headingReliable: false);

        Assert.True(obs0.Gnss.IsAvailable);
        Assert.NotNull(obs0.Gnss.MeasuredPositionEnu);
        Assert.False(obs0.Heading.IsAvailable);
        Assert.Null(obs0.Heading.MeasuredRadians);
        Assert.False(obs0.HasCompletePose);

        var build = InitialTrajectoryBuilder.Build([obs0, obs1]);
        Assert.False(build.Succeeded);
        Assert.Null(build.Trajectory);
        Assert.Equal(TrajectoryBuildStatus.MissingHeading, build.Status);
        Assert.Equal(0, build.UsablePoseSamples);
        Assert.Equal(2, build.UnavailableHeadingCount);
    }

    [Fact]
    public void ReliableObservationsBuildEstimatedTrajectoryAndRigStateReplaysDeterministically()
    {
        var manifest = CreateTestManifest(width: 20.0, northDepth: 20.0);
        var cache = SkyPrecomputer.Compute(manifest, 10.0, 2.0, 32);
        var resolver = new GnssSkyEnvironmentResolver(cache, manifest, multipathSeed: 1234UL);
        var rig = new GnssHeadingRig(resolver, 101UL, 202UL, 303UL);

        var samples = new List<PoseSensorObservation>();
        for (var i = 0; i < 6; i++)
        {
            var t = i * 0.2;
            samples.Add(rig.Observe(t, new Vector3d(500005.0 + i * 0.4, 7400005.0, 102.0), 0.25, PlatformProfile.Handcart, headingReliable: true));
        }

        var result = InitialTrajectoryBuilder.Build(samples);
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Trajectory);
        Assert.Equal(6, result.UsablePoseSamples);
        Assert.True(result.Trajectory!.TrySample(0.5, out var pose));
        Assert.NotNull(pose);
        Assert.False(result.Trajectory.TrySample(1.5, out _));

        // Deterministic state serialization roundtrip.
        var json = JsonSerializer.Serialize(rig.ExportState());
        var restored = new GnssHeadingRig(resolver, JsonSerializer.Deserialize<GnssHeadingRigState>(json));
        var expectedNext = rig.Observe(1.2, new Vector3d(500007.4, 7400005.0, 102.0), 0.25, PlatformProfile.Handcart, headingReliable: true);
        var actualNext = restored.Observe(1.2, new Vector3d(500007.4, 7400005.0, 102.0), 0.25, PlatformProfile.Handcart, headingReliable: true);
        Assert.Equal(expectedNext, actualNext);
    }

    [Fact]
    public void ObservationDtosAndTrajectoryBuilderNeverExposeTruthErrorOrBias()
    {
        foreach (var type in new[] { typeof(PoseSensorObservation), typeof(TrajectoryBuildResult), typeof(InitialTrajectoryBuilder) })
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.DoesNotContain("Truth", prop.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Error", prop.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Bias", prop.Name, StringComparison.OrdinalIgnoreCase);
            }
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType.Name.Contains("Truth", StringComparison.OrdinalIgnoreCase));
        }
    }
}
