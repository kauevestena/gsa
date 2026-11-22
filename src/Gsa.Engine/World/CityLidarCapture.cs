using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Lidar;
using Gsa.Core.World;
using Gsa.Missions.Contracts;
using Gsa.Missions.Persistence;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private const string TunnelMissionId = "HIST-06";
    private const uint SurveyLidarSurfaceLayer = 2;
    private readonly List<CityLidarSlice> _pendingLidar = [];
    private int _pendingLidarReturns, _protectedLidarReturns;
    private int _lidarTicksWithoutPose, _lidarTicksWithoutCapacity;

    private static bool IsTunnelCollection(ContractDefinition? definition) =>
        definition is { Id: TunnelMissionId, Product: "point_cloud" };

    private static bool IsViaductCollection(ContractDefinition? definition) =>
        definition is { Id: PortViaductSurvey.MissionId, Product: "point_cloud" };

    private static bool IsCityLidarCollection(ContractDefinition? definition) =>
        IsTunnelCollection(definition) || IsViaductCollection(definition) || IsRainCollection(definition);

    private void BuildPortLidarDeck(Node3D parent)
    {
        // Match the flat asphalt deck's rendered upper surface in the Port.
        var deck = new StaticBody3D { Name = "PortLidarDeck", CollisionLayer = SurveyLidarSurfaceLayer, CollisionMask = 0,
            Position = _coordinates.ToGodot(new Vector3d(500500, 7401500, 99.45)) };
        deck.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(1000, .25f, 1000) } });
        parent.AddChild(deck);
    }

    private bool ShouldRecordCityLidar(Vector3d position, LocomotionMode mode, bool isRaining) =>
        (IsTunnelCollection(_activeContract) && GetRegionIdForEnu(position) == "port" ||
            IsViaductCollection(_activeContract) && PortViaductSurvey.IsSurveyLane(position) ||
            IsRainCollection(_activeContract) && mode >= LocomotionMode.Pickup && isRaining && _calendar.IsNight &&
                WaterfrontRainSurvey.IsSurveyLane(position)) &&
        _activeMachine is { IsActiveState: true, Snapshot: { } accepted } &&
        _calendar.CalendarTimeSeconds <= accepted.ExpiresAtSeconds &&
        _fleet.OwnedPlatforms.Contains(mode) && !_cityHazards.IsVehicleDisabled(mode);

    private void RecordCityLidarSlice(string profileId, EstimatedBodyPose pose, LidarMountEstimate mount, TunnelRangeObservation? ranges)
    {
        if (_lidarSliceScratch.Count == 0) return;
        var nextCount = _pendingLidarReturns + _lidarSliceScratch.Count;
        var units = CityLidarSlice.StorageUnits(nextCount) - CityLidarSlice.StorageUnits(_pendingLidarReturns);
        if (nextCount > CityLidarSlice.MaxBufferedReturns || nextCount + _protectedLidarReturns > CityLidarSlice.MaxContractReturns ||
            _locomotion.RemainingAutonomySeconds <= 0 || _locomotion.GimbalinaFled ||
            (units > 0 && !_locomotion.TryConsumeCaptureUnits(units, out _)))
        {
            _lidarTicksWithoutCapacity++;
            return;
        }

        if (ranges?.ZuptPortal is { } portal && !_rackMisaligned && !_pendingLidar.Any(slice =>
            !slice.RackNeedsCalibration && slice.TunnelRanges?.ZuptPortal == portal))
            _feedbackText = Text("city.tunnel.portal_observed");
        _pendingLidar.Add(CityLidarSlice.Capture(_tick, profileId, pose, mount, _rackMisaligned, _lidarSliceScratch) with { TunnelRanges = ranges });
        _pendingLidarReturns = nextCount;
    }

    private void ClearPendingCityLidar()
    {
        _pendingLidar.Clear();
        _pendingLidarReturns = 0;
    }

    private void RestorePendingCityLidar(CityLidarBufferSnapshot? snapshot)
    {
        ClearPendingCityLidar();
        _pendingLidar.AddRange(snapshot?.Pending ?? []);
        _pendingLidarReturns = _pendingLidar.Sum(slice => slice.Returns.Count);
        _lidarTicksWithoutPose = snapshot?.SkippedNoPose ?? 0;
        _lidarTicksWithoutCapacity = snapshot?.SkippedCapacity ?? 0;
    }

    private string CityLidarCollectionStatus() => string.Format(Text("city.lidar.capture_status"), _pendingLidarReturns,
        _protectedLidarReturns, _lidarTicksWithoutPose, _lidarTicksWithoutCapacity);

    private SurveyBatchArtifact[] ReadProtectedCityLidarBatches()
    {
        if (_activeMachine is not { } machine) return [];
        var protectedIds = machine.ProtectedBatches.Select(batch => batch.BatchId).ToHashSet(StringComparer.Ordinal);
        return _cityCustody.GetProtectedArtifactsForContract(machine.ContractId)
            .Where(batch => protectedIds.Contains(batch.BatchId) && batch.LidarSlices is { Count: > 0 }).ToArray();
    }

    private static PointCloudOctreeAndChunkStore BuildProtectedCityCloud(IEnumerable<CityLidarSlice> slices)
    {
        var store = new PointCloudOctreeAndChunkStore();
        store.IngestRevision(slices.OrderBy(slice => slice.PhysicsTick).SelectMany(slice => slice.Georeference()));
        return store;
    }

    private PointCloudOctreeAndChunkStore BuildProtectedCityCloud() =>
        BuildProtectedCityCloud(ReadProtectedCityLidarBatches().SelectMany(batch => batch.LidarSlices!));

}
