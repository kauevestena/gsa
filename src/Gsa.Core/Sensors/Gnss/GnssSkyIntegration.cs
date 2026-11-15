using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.World;

namespace Gsa.Core.Sensors.Gnss;

/// <summary>Selects the active nominal GNSS and heading profiles from platform tier and upgrades.</summary>
public static class GnssProfileSelector
{
    public static GnssProfile SelectGnssProfile(PlatformProfile platform, bool hasNavigationUpgrade = false)
    {
        ArgumentNullException.ThrowIfNull(platform);
        return platform.Level >= 1 || hasNavigationUpgrade ? GnssProfile.Navigation : GnssProfile.Cellphone;
    }

    public static HeadingProfile SelectHeadingProfile(PlatformProfile platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        return platform.Level switch
        {
            <= 0 => HeadingProfile.Level0,
            1 => HeadingProfile.Level1,
            _ => HeadingProfile.Level2,
        };
    }
}

/// <summary>Simulator environment resolution from static sky cache and nearby reflective facades.</summary>
public sealed record ResolvedGnssEnvironment(
    GnssEnvironment Environment,
    bool InSkyGrid,
    string CellId,
    double FacadeFactor,
    bool NearMetalOrGlass,
    int NearbyMetalOrGlassCount);

/// <summary>Resolves GNSS openness, canyon availability, DOP and metal/glass multipath amplification.</summary>
public sealed class GnssSkyEnvironmentResolver
{
    public const double MinimumUsableOpenness = 0.1;
    public const double DefaultMetalGlassDistanceMetres = 15.0;
    public const double DefaultBaseMultipathAmplitudeMetres = 0.5;
    public const double DefaultMetalGlassFacadeFactor = 2.5;

    private readonly SkyVisibilityCache _cache;
    private readonly WorldBoxObject[] _reflectiveObjects;
    private readonly ulong _multipathSeed;
    private readonly double _baseMultipathAmplitudeMetres;
    private readonly double _metalGlassFacadeFactor;
    private readonly double _metalGlassDistanceMetres;
    private readonly double _maxHeightDeltaMetres;

    public GnssSkyEnvironmentResolver(
        SkyVisibilityCache cache,
        WorldSceneManifest manifest,
        ulong multipathSeed,
        double baseMultipathAmplitudeMetres = DefaultBaseMultipathAmplitudeMetres,
        double metalGlassFacadeFactor = DefaultMetalGlassFacadeFactor,
        double metalGlassDistanceMetres = DefaultMetalGlassDistanceMetres,
        double maxHeightDeltaMetres = 0.5)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(manifest);
        cache.ValidateFor(manifest);
        if (!double.IsFinite(baseMultipathAmplitudeMetres) || baseMultipathAmplitudeMetres < 0)
            throw new ArgumentOutOfRangeException(nameof(baseMultipathAmplitudeMetres), "Multipath amplitude must be finite and non-negative.");
        if (!double.IsFinite(metalGlassFacadeFactor) || metalGlassFacadeFactor < 1.0)
            throw new ArgumentOutOfRangeException(nameof(metalGlassFacadeFactor), "Facade amplification factor must be at least 1.");
        if (!double.IsFinite(metalGlassDistanceMetres) || metalGlassDistanceMetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(metalGlassDistanceMetres), "Reflective influence radius must be positive.");
        if (!double.IsFinite(maxHeightDeltaMetres) || maxHeightDeltaMetres < 0)
            throw new ArgumentOutOfRangeException(nameof(maxHeightDeltaMetres), "Height delta must be finite and non-negative.");

        _cache = cache;
        _multipathSeed = multipathSeed;
        _baseMultipathAmplitudeMetres = baseMultipathAmplitudeMetres;
        _metalGlassFacadeFactor = metalGlassFacadeFactor;
        _metalGlassDistanceMetres = metalGlassDistanceMetres;
        _maxHeightDeltaMetres = maxHeightDeltaMetres;
        _reflectiveObjects = manifest.Objects
            .Where(item => !item.Tags.Contains("ground") &&
                           (string.Equals(item.Material, "metal", StringComparison.Ordinal) ||
                            string.Equals(item.Material, "glass", StringComparison.Ordinal)))
            .ToArray();
    }

    public ResolvedGnssEnvironment Resolve(Vector3d positionEnu, bool explicitSignalAvailable = true)
    {
        if (!double.IsFinite(positionEnu.X) || !double.IsFinite(positionEnu.Y) || !double.IsFinite(positionEnu.Z))
            throw new ArgumentOutOfRangeException(nameof(positionEnu), "Query position must be finite.");

        if (!_cache.TryGetCell(positionEnu, out var cell, _maxHeightDeltaMetres) || cell is null)
        {
            return new ResolvedGnssEnvironment(
                new GnssEnvironment(0.0, Vector3d.Zero, signalAvailable: false),
                InSkyGrid: false,
                CellId: $"{_cache.SceneId}:outside_grid",
                FacadeFactor: 1.0,
                NearMetalOrGlass: false,
                NearbyMetalOrGlassCount: 0);
        }

        var nearbyCount = 0;
        foreach (var item in _reflectiveObjects)
        {
            if (HorizontalDistanceToBox(positionEnu, item) <= _metalGlassDistanceMetres)
                nearbyCount++;
        }

        var nearMetalOrGlass = nearbyCount > 0;
        var facadeFactor = nearMetalOrGlass ? _metalGlassFacadeFactor : 1.0;
        var cellId = $"{_cache.SceneId}:{cell.EastIndex}:{cell.NorthIndex}";
        var multipath = CellMultipathBias.ForCell(_multipathSeed, cellId, _baseMultipathAmplitudeMetres, facadeFactor);
        var signalAvailable = explicitSignalAvailable && cell.Openness >= MinimumUsableOpenness;
        var environment = new GnssEnvironment(cell.Openness, multipath, signalAvailable);
        return new ResolvedGnssEnvironment(
            environment,
            InSkyGrid: true,
            CellId: cellId,
            FacadeFactor: facadeFactor,
            NearMetalOrGlass: nearMetalOrGlass,
            NearbyMetalOrGlassCount: nearbyCount);
    }

    private static double HorizontalDistanceToBox(Vector3d point, WorldBoxObject item)
    {
        var halfX = item.SizeEnu.X * 0.5;
        var halfY = item.SizeEnu.Y * 0.5;
        var dx = Math.Max(0.0, Math.Max((item.PositionEnu.X - halfX) - point.X, point.X - (item.PositionEnu.X + halfX)));
        var dy = Math.Max(0.0, Math.Max((item.PositionEnu.Y - halfY) - point.Y, point.Y - (item.PositionEnu.Y + halfY)));
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>Coupled measurement-only GNSS + heading epoch; exposes no world truth or realized error.</summary>
public sealed record PoseSensorObservation
{
    public double TimestampSeconds { get; }
    public string PlatformId { get; }
    public GnssObservation Gnss { get; }
    public HeadingObservation Heading { get; }
    public bool HasCompletePose => Gnss.IsAvailable && Heading.IsAvailable;

    public PoseSensorObservation(
        double timestampSeconds,
        string platformId,
        GnssObservation gnss,
        HeadingObservation heading)
    {
        GnssSimulator.ValidateTime(timestampSeconds);
        ArgumentException.ThrowIfNullOrWhiteSpace(platformId);
        ArgumentNullException.ThrowIfNull(gnss);
        ArgumentNullException.ThrowIfNull(heading);
        if (gnss.TimestampSeconds != timestampSeconds || heading.TimestampSeconds != timestampSeconds)
            throw new ArgumentException("Coupled GNSS and heading observations must share the epoch timestamp.");

        TimestampSeconds = timestampSeconds;
        PlatformId = platformId;
        Gnss = gnss;
        Heading = heading;
    }
}

public readonly record struct GnssHeadingRigState(
    GnssSimulatorState Gnss,
    HeadingSimulatorState Heading,
    CommonGnssErrorState CommonError);

/// <summary>Synthesizes sky-aware GNSS and heading measurements with deterministic state replay.</summary>
public sealed class GnssHeadingRig
{
    private readonly GnssSkyEnvironmentResolver _resolver;
    private readonly GnssSimulator _gnss;
    private readonly HeadingSimulator _heading;
    private readonly CommonGnssErrorProcess _commonError;

    public GnssHeadingRig(
        GnssSkyEnvironmentResolver resolver,
        ulong gnssNoiseSeed,
        ulong headingNoiseSeed,
        ulong commonErrorSeed,
        double commonErrorSigmaMetres = 0.5,
        double commonErrorCorrelationSeconds = 30.0)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
        _gnss = new GnssSimulator(gnssNoiseSeed);
        _heading = new HeadingSimulator(headingNoiseSeed);
        _commonError = new CommonGnssErrorProcess(commonErrorSeed, commonErrorSigmaMetres, commonErrorCorrelationSeconds);
    }

    public GnssHeadingRig(GnssSkyEnvironmentResolver resolver, GnssHeadingRigState state)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
        _gnss = new GnssSimulator(state.Gnss);
        _heading = new HeadingSimulator(state.Heading);
        _commonError = new CommonGnssErrorProcess(state.CommonError);
    }

    public GnssHeadingRigState ExportState() => new(
        _gnss.ExportState(),
        _heading.ExportState(),
        _commonError.ExportState());

    public PoseSensorObservation Observe(
        double timeSeconds,
        Vector3d truePositionEnu,
        double trueHeadingRadians,
        PlatformProfile platform,
        bool headingReliable,
        bool hasNavigationUpgrade = false,
        bool explicitGnssAvailable = true)
    {
        GnssSimulator.ValidateTime(timeSeconds);
        if (!double.IsFinite(truePositionEnu.X) || !double.IsFinite(truePositionEnu.Y) || !double.IsFinite(truePositionEnu.Z))
            throw new ArgumentOutOfRangeException(nameof(truePositionEnu), "Position must be finite.");
        ArgumentNullException.ThrowIfNull(platform);

        var resolved = _resolver.Resolve(truePositionEnu, explicitGnssAvailable);
        var common = _commonError.At(timeSeconds);
        var gnssProfile = GnssProfileSelector.SelectGnssProfile(platform, hasNavigationUpgrade);
        var headingProfile = GnssProfileSelector.SelectHeadingProfile(platform);
        var gnssObservation = _gnss.Observe(timeSeconds, truePositionEnu, gnssProfile, resolved.Environment, common);
        var headingObservation = _heading.Observe(timeSeconds, trueHeadingRadians, headingProfile, headingReliable);
        return new PoseSensorObservation(timeSeconds, platform.Id, gnssObservation, headingObservation);
    }
}

public enum TrajectoryBuildStatus
{
    Ready = 0,
    EmptyInput = 1,
    MissingGnss = 2,
    MissingHeading = 3,
    MissingPositionAndHeading = 4,
}

public sealed record TrajectoryBuildResult(
    bool Succeeded,
    EstimatedBodyTrajectory? Trajectory,
    TrajectoryBuildStatus Status,
    int TotalSamples,
    int UsablePoseSamples,
    int UnavailableGnssCount,
    int UnavailableHeadingCount);

/// <summary>Builds an estimated ENU trajectory exclusively from measured GNSS and heading observations.</summary>
public static class InitialTrajectoryBuilder
{
    public static TrajectoryBuildResult Build(IEnumerable<PoseSensorObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        var items = observations.ToArray();
        if (items.Length == 0)
            return new TrajectoryBuildResult(false, null, TrajectoryBuildStatus.EmptyInput, 0, 0, 0, 0);

        var poses = new List<EstimatedBodyPose>(items.Length);
        var missingGnss = 0;
        var missingHeading = 0;

        for (var i = 0; i < items.Length; i++)
        {
            var current = items[i] ?? throw new ArgumentException("Observations cannot contain null entries.", nameof(observations));
            if (i > 0 && current.TimestampSeconds <= items[i - 1].TimestampSeconds)
                throw new ArgumentException("Pose sensor observations must have strictly increasing timestamps.", nameof(observations));

            var hasGnss = current.Gnss.IsAvailable && current.Gnss.MeasuredPositionEnu.HasValue;
            var hasHeading = current.Heading.IsAvailable && current.Heading.MeasuredRadians.HasValue;
            if (!hasGnss)
                missingGnss++;
            if (!hasHeading)
                missingHeading++;
            if (!hasGnss || !hasHeading)
                continue;

            var orientation = Quaterniond.FromAxisAngle(Vector3d.UnitZ, current.Heading.MeasuredRadians!.Value);
            poses.Add(new EstimatedBodyPose(current.TimestampSeconds, current.Gnss.MeasuredPositionEnu!.Value, orientation));
        }

        if (poses.Count == 0)
        {
            var status = (missingGnss > 0, missingHeading > 0) switch
            {
                (true, true) => TrajectoryBuildStatus.MissingPositionAndHeading,
                (true, false) => TrajectoryBuildStatus.MissingGnss,
                _ => TrajectoryBuildStatus.MissingHeading,
            };
            return new TrajectoryBuildResult(false, null, status, items.Length, 0, missingGnss, missingHeading);
        }

        var trajectory = new EstimatedBodyTrajectory(poses);
        return new TrajectoryBuildResult(true, trajectory, TrajectoryBuildStatus.Ready, items.Length, poses.Count, missingGnss, missingHeading);
    }
}
