using Gsa.Core.Geometry;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Lidar;
using Gsa.Core.World;
using System.Text.Json.Serialization;

namespace Gsa.Missions.Persistence;

public sealed record CityLidarReturn(double Azimuth, double Elevation, double Range, float Intensity,
    int Number, int Total, bool Rain);

/// <summary>One synchronous physics slice with raw returns and the parameters needed to reproject them.</summary>
public sealed record CityLidarSlice(long PhysicsTick, string ProfileId, SerializablePoseDto Pose,
    SerializableMountDto Mount, bool RackNeedsCalibration, IReadOnlyList<CityLidarReturn> Returns)
{
    public const int MaxBufferedReturns = 48_000;
    public const int MaxContractReturns = 160_000;
    private const int ReturnSlotsPerTick = 1024;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TunnelRangeObservation? TunnelRanges { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PavementCameraFootprintObservation? PavementFrame { get; init; }

    public static int StorageUnits(int returns) => (returns + 999) / 1000;

    public static CityLidarSlice Capture(long tick, string profileId, EstimatedBodyPose pose, LidarMountEstimate mount,
        bool rackNeedsCalibration, IReadOnlyList<LidarMeasuredReturn> returns) => new(tick, profileId,
        new(pose.TimeSeconds, pose.PositionEnu.X, pose.PositionEnu.Y, pose.PositionEnu.Z,
            pose.BodyToMap.X, pose.BodyToMap.Y, pose.BodyToMap.Z, pose.BodyToMap.W),
        new(mount.LeverArmBodyMetres.X, mount.LeverArmBodyMetres.Y, mount.LeverArmBodyMetres.Z,
            mount.LidarToBodyRotation.X, mount.LidarToBodyRotation.Y, mount.LidarToBodyRotation.Z,
            mount.LidarToBodyRotation.W, mount.TimeOffsetSeconds), rackNeedsCalibration,
        returns.Select(r => new CityLidarReturn(r.AzimuthRadians, r.ElevationRadians, r.MeasuredRangeMetres,
            (float)r.Intensity, r.ReturnNumber, r.TotalReturnsInPulse, r.IsSpuriousRain)).ToArray());

    public IReadOnlyList<GeoreferencedLidarSample> Georeference()
    {
        var pose = TunnelRanges is { } ranges ? PortTunnelSurvey.Solve(ranges, Pose.TimeSeconds) : ToPose();
        var mount = ToMount();
        var transform = LidarBenchSimulator.ComposeLidarToMap(new RigidTransform(pose.BodyToMap, pose.PositionEnu), mount);
        return Returns.Select((r, index) => new GeoreferencedLidarSample(checked(PhysicsTick * ReturnSlotsPerTick + index),
            pose.TimeSeconds, transform.Transform(LidarProfile.DirectionFromAngles(r.Azimuth, r.Elevation) * r.Range),
            r.Intensity, r.Number, r.Rain)).ToArray();
    }

    internal void Validate(long maxTick)
    {
        if (PhysicsTick <= 0 || PhysicsTick > maxTick || PhysicsTick >= long.MaxValue / ReturnSlotsPerTick ||
            !LidarProfile.AllProfiles.Any(profile => profile.Id == ProfileId) || Pose is null || Mount is null ||
            Mount.TimeOffsetSeconds != 0 || Pose.TimeSeconds != PhysicsTick / (double)CameraCaptureScheduler.PhysicsTicksPerSecond ||
            Returns is null || Returns.Count > ReturnSlotsPerTick || Returns.Count == 0 && PavementFrame is null || Returns.Any(r => r is null ||
                !double.IsFinite(r.Azimuth) || !double.IsFinite(r.Elevation) || !double.IsFinite(r.Range) || r.Range <= 0 ||
                !float.IsFinite(r.Intensity) || r.Intensity is < 0 or > 1 || r.Number < 1 || r.Number > r.Total ||
                r.Total > LidarMaterialProperties.MaxReturnsPerPulse))
            throw new ArgumentException("Invalid city LiDAR slice.");

        if (PavementFrame is { } frame &&
            (FinalCityPavementSurvey.FindSector(TunnelRanges?.Corridor) is null || ProfileId != "lidar-l6" ||
             PhysicsTick % FinalCityPavementSurvey.CameraTickInterval != 0 || frame.SampleId != $"final_camera_{PhysicsTick}" ||
             frame.TimestampSeconds != Pose.TimeSeconds || frame.CameraSlotId != "van_pavement_down" || frame.TransverseStripIndex != 0 ||
             !double.IsFinite(frame.ChainageMetres) || frame.ChainageMetres is < -3.1 or > FinalCityPavementSurvey.Length + 3.1 ||
             !double.IsFinite(frame.TransverseOffsetMetres) || Math.Abs(frame.TransverseOffsetMetres) > FinalCityPavementSurvey.DriveHalfWidth + .05 ||
             !double.IsFinite(frame.EstimatedElevationMetres) ||
             !double.IsFinite(frame.ObservedRutDepthMillimetres) || frame.ObservedRutDepthMillimetres < 0 ||
             !double.IsFinite(frame.LensObstructionFraction) || frame.LensObstructionFraction is < 0 or > 1))
            throw new ArgumentException("Invalid synchronized pavement camera frame.");

        _ = ToPose();
        _ = ToMount();
        if (TunnelRanges is { } ranges) _ = PortTunnelSurvey.Solve(ranges, Pose.TimeSeconds);
    }

    private EstimatedBodyPose ToPose() => new(Pose.TimeSeconds, new Vector3d(Pose.East, Pose.North, Pose.Up),
        new Quaterniond(Pose.Qx, Pose.Qy, Pose.Qz, Pose.Qw));

    private LidarMountEstimate ToMount() => new(new Vector3d(Mount.LeverX, Mount.LeverY, Mount.LeverZ),
        new Quaterniond(Mount.Qx, Mount.Qy, Mount.Qz, Mount.Qw), Mount.TimeOffsetSeconds);
}
