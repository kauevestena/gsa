using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Fusion;
using Gsa.Core.Sensors.Gnss;
using Gsa.Core.World;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private LiveNavigationRig? _navigationRig;
    private Vector3d _previousNavigationPosition;
    private Func<Vector3d, GnssEnvironment>? _navigationEnvironmentAt;
    private PhysicsRayQueryParameters3D? _navigationSkyRay;
    private static readonly IReadOnlyList<Vector3d> NavigationSkyDirections = SkyPrecomputer.CreateHemisphereDirections(32);
    private readonly List<NavigationTrailPoint> _navigationTrailScratch = new(LiveNavigationRig.TrailCapacity);
    private int _navigationSkyQueries;
    private Vector3d _cachedSkyPos;
    private LocomotionMode _cachedSkyMode;
    private bool _hasCachedSky;
    private int _cachedSkyOpen;
    private bool _cachedSkyReflective;

    private void ResetCityNavigation()
    {
        // This is a diagnostic session, not acquired/persisted raw observations. A new fix is required after reset.
        _navigationRig = null;
        _hasCachedSky = false;
        _navigationTrailScratch.Clear();
        if (_cityMap is not null) _cityMap.UpdateNavigation(null, []);
    }

    private void StepCityNavigation(double dt, Vector3d position, double heading, LocomotionMode mode, bool wetRoad)
    {
        var maxStepDistance = Math.Max(5, PlatformProfile.ForMode(mode).MaxSpeedMetresPerSecond * dt * 4);
        if (_navigationRig is null || _navigationRig.Mode != mode ||
            (position - _previousNavigationPosition).Length > maxStepDistance)
        {
            ResetCityNavigation();
            _navigationRig = new LiveNavigationRig(mode, new(position, Vector3d.Zero, heading), 71117UL ^ (ulong)_tick);
            _previousNavigationPosition = position;
            return;
        }
        var motion = new NavigationTruthMotion(position, (position - _previousNavigationPosition) / dt, heading);
        _navigationEnvironmentAt ??= ResolveCityNavigationEnvironment;
        _navigationRig.Advance(dt, motion, _navigationEnvironmentAt, wetRoad);
        _previousNavigationPosition = position;
    }

    private GnssEnvironment ResolveCityNavigationEnvironment(Vector3d position)
    {
        _navigationSkyQueries++;
        if (PortTunnelSurvey.IsUnderRoof(position)) return new(0, Vector3d.Zero);
        var mode = _navigationRig?.Mode ?? LocomotionMode.Handcart;
        int open;
        bool reflective;
        if (_hasCachedSky && mode == _cachedSkyMode &&
            Vector3d.Dot(position - _cachedSkyPos, position - _cachedSkyPos) < 1.0)
        {
            open = _cachedSkyOpen;
            reflective = _cachedSkyReflective;
        }
        else
        {
            _navigationSkyRay ??= new PhysicsRayQueryParameters3D { CollisionMask = 1, HitFromInside = true };
            var height = mode switch
            {
                LocomotionMode.Van => 2.8, LocomotionMode.Kombi or LocomotionMode.Pickup => 2.4,
                LocomotionMode.Car => 1.8, _ => 1.5,
            };
            var origin = position + Vector3d.UnitZ * height;
            var space = GetWorld3D().DirectSpaceState;
            open = 0;
            reflective = false;
            _navigationSkyRay.From = _coordinates.ToGodot(origin);
            foreach (var direction in NavigationSkyDirections)
            {
                _navigationSkyRay.To = _coordinates.ToGodot(origin + direction * 250);
                var hit = space.IntersectRay(_navigationSkyRay);
                if (hit.Count == 0) { open++; continue; }
                var collider = hit["collider"].AsGodotObject();
                if (collider is not null && _registry.TryGetFastMetadata(collider.GetInstanceId(), out _, out var material) &&
                    material is "glass" or "metal" &&
                    (_coordinates.ToMap(hit["position"].AsVector3()) - origin).Length <= 15) reflective = true;
            }
            _cachedSkyPos = position;
            _cachedSkyMode = mode;
            _cachedSkyOpen = open;
            _cachedSkyReflective = reflective;
            _hasCachedSky = true;
        }
        var cell = $"city_nav:{(int)Math.Floor(position.X / 10)}:{(int)Math.Floor(position.Y / 10)}";
        var bias = CellMultipathBias.ForCell(71117, cell, .5, reflective ? 2.5 : 1);
        if (_cityHazards.SpoofRemaining > 0 && !_calmPolicy.IsProtectionActive && !_citySafeZone)
            bias += Gsa.Missions.Economy.ProgressiveGnssSpoofingDetector.ComputeSpoofingOffsetEnu(_cityHazards.SpoofElapsed);
        return new GnssEnvironment((double)open / NavigationSkyDirections.Count, bias);
    }
}
