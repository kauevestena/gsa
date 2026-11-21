using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Lidar;
using Gsa.Missions.Contracts;
using Gsa.Missions.Persistence;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private const string TunnelMissionId = "HIST-06";
    private const uint PortLidarDeckLayer = 2;
    private readonly List<CityLidarSlice> _pendingLidar = [];
    private int _pendingLidarReturns, _protectedLidarReturns;
    private int _lidarTicksWithoutPose, _lidarTicksWithoutCapacity;

    private static bool IsTunnelCollection(ContractDefinition? definition) =>
        definition is { Id: TunnelMissionId, Product: "point_cloud" };

    private void BuildPortLidarDeck(Node3D parent)
    {
        // Match the flat asphalt deck's rendered upper surface in the Port.
        var deck = new StaticBody3D { Name = "PortLidarDeck", CollisionLayer = PortLidarDeckLayer, CollisionMask = 0,
            Position = _coordinates.ToGodot(new Vector3d(500500, 7401500, 99.45)) };
        deck.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(1000, .25f, 1000) } });
        parent.AddChild(deck);
    }

    private bool ShouldRecordCityLidar(Vector3d position, LocomotionMode mode) => IsTunnelCollection(_activeContract) &&
        _activeMachine is { IsActiveState: true, Snapshot: { } accepted } &&
        _calendar.CalendarTimeSeconds <= accepted.ExpiresAtSeconds &&
        _fleet.OwnedPlatforms.Contains(mode) && !_cityHazards.IsVehicleDisabled(mode) && GetRegionIdForEnu(position) == "port";

    private void RecordCityLidarSlice(string profileId, EstimatedBodyPose pose, LidarMountEstimate mount)
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

        _pendingLidar.Add(CityLidarSlice.Capture(_tick, profileId, pose, mount, _rackMisaligned, _lidarSliceScratch));
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

    private PointCloudOctreeAndChunkStore BuildProtectedCityCloud()
    {
        var store = new PointCloudOctreeAndChunkStore();
        if (_activeMachine is not { } machine) return store;
        var protectedIds = machine.ProtectedBatches.Select(batch => batch.BatchId).ToHashSet(StringComparer.Ordinal);
        var slices = _cityCustody.GetProtectedArtifactsForContract(machine.ContractId)
            .Where(batch => protectedIds.Contains(batch.BatchId)).SelectMany(batch => batch.LidarSlices ?? []);
        store.IngestRevision(slices.OrderBy(slice => slice.PhysicsTick).SelectMany(slice => slice.Georeference()));
        return store;
    }
}
