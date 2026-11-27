using Gsa.Core.Geometry;
using Gsa.Core.Mobility;
using Gsa.Core.Sensors.Camera;
using Gsa.Core.Sensors.Fusion;
using Gsa.Core.Traffic;
using Gsa.Core.World;
using Gsa.Engine.Visuals;
using Godot;

namespace Gsa.Engine.World;

public partial class SaoDatumCityScene
{
    private (CameraCaptureScheduler Scheduler, CameraMountEstimate Mount)[] _panoramicCameras = [];
    private LocomotionMode _panoramicMode;

    private long NextCityCameraSequence => _panoramicCameras.Length == 0
        ? _camScheduler.NextCaptureSequence
        : Math.Max(_camScheduler.NextCaptureSequence, _panoramicCameras.Max(camera => camera.Scheduler.NextCaptureSequence));

    private void BuildBusCorridor(Node3D parent)
    {
        var batch = new GsaStaticMeshBatcher();
        var lane = GsaMeshBuilder.SolidMat(new Color(.22f, .40f, .53f));
        var stripe = GsaMeshBuilder.SolidMat(new Color(.94f, .85f, .47f));
        foreach (var (start, end) in BusSurveyCorridor.Legs)
        {
            var alongEast = start.Y == end.Y;
            var mid = (start + end) / 2;
            var length = (end - start).Length + 2 * BusSurveyCorridor.HalfWidth;
            var width = 2 * BusSurveyCorridor.HalfWidth;
            batch.AddBox(lane, _coordinates.ToGodot(mid - Vector3d.UnitZ * .09),
                new Vector3((float)(alongEast ? length : width), .025f, (float)(alongEast ? width : length)));
            var right = alongEast ? Vector3d.UnitY : Vector3d.UnitX;
            var dimensions = new Vector3(alongEast ? (float)length : .13f, .03f, alongEast ? .13f : (float)length);
            foreach (var side in new[] { -1, 1 })
                batch.AddBox(stripe, _coordinates.ToGodot(mid - Vector3d.UnitZ * .066 + right * (side * BusSurveyCorridor.HalfWidth)), dimensions);
        }
        if (batch.BuildSingleMeshInstance("BusSurveyLane", GeometryInstance3D.ShadowCastingSetting.Off) is { } mesh)
            parent.AddChild(mesh);
    }

    private bool CityMayEnterBusLane(Vector3d current, Vector3d candidate, WeatherAndTimeSnapshot weather) =>
        _isSandbox || BusSurveyCorridor.IsSurveyWindow(weather.HourOfDay) ||
        BusSurveyCorridor.Contains(current) || !BusSurveyCorridor.Contains(candidate);

    private void CaptureBusCorridorSweep(Vector3d position, double heading, double speed, WeatherAndTimeSnapshot weather,
        CameraEnvironmentSnapshot environment, TrajectoryEstimateSample? navigation, Func<Vector3d, Vector3d, string, bool> isOccluded)
    {
        var mode = CurrentActiveMode();
        if (mode < LocomotionMode.Car)
        { _feedbackText = Text("city.story.requires_platform"); return; }
        if (!BusSurveyCorridor.IsSurveyWindow(weather.HourOfDay))
        { _feedbackText = Text("city.bus.window_closed"); return; }
        if (!BusSurveyCorridor.Contains(position))
        { _feedbackText = Text("city.bus.use_lane"); return; }
        if (navigation is null)
        { _feedbackText = Text("city.hazards.wait_navigation"); return; }

        if (_panoramicCameras.Length == 0 || _panoramicMode != mode)
        {
            var sequence = NextCityCameraSequence;
            _panoramicCameras = Enum.GetValues<PanoramicFace>().Select(face =>
            {
                var profile = Panoramic360RigSimulator.GetProfileForPlatform(mode, face);
                return (new CameraCaptureScheduler(profile.Id, profile, sequence), Panoramic360RigSimulator.GetFaceMount(mode, face));
            }).ToArray();
            _panoramicMode = mode;
        }

        var requiredUnits = _panoramicCameras.Sum(camera => camera.Scheduler.Profile.StorageUnitsPerSample);
        if (_locomotion.EvaluateCaptureReadiness(requiredUnits) != CaptureStopReason.None)
        { _feedbackText = Text("city.bus.capture_resources"); return; }

        var time = weather.CalendarTimeSeconds;
        var body = Quaterniond.FromAxisAngle(Vector3d.UnitZ, heading + _cityHazards.RackYawErrorRadians);
        var velocity = new Vector3d(Math.Cos(heading) * speed, Math.Sin(heading) * speed, 0);
        var estimated = new EstimatedBodyPose(time, navigation.PositionEnu, navigation.BodyToMapRotation);
        EstimatedBodyPose Pose(double dt) => new(time + dt, position + velocity * dt, body);
        var visibleTargets = new HashSet<string>(StringComparer.Ordinal);
        var capturedCount = 0;

        foreach (var (camera, mount) in _panoramicCameras)
        {
            var profile = camera.Profile;
            var exposure = weather.IsNight ? profile.NightExposureSeconds : profile.DayExposureSeconds;
            var request = new CameraCaptureRequest(_tick, time, CameraGeometry.CameraToMap(Pose(0), mount),
                CameraGeometry.CameraToMap(Pose(exposure), mount), CameraGeometry.CameraToMap(Pose(profile.ReadoutTimeSeconds), mount),
                estimated, mount, environment, _liveCandidates, isOccluded);
            if (!camera.TryEnqueueSimulatedCapture(request, _locomotion, out var sample, out _) || sample is null)
                continue;
            camera.FlushBatchManifest("city_panorama");
            _fieldSamples.Add(sample);
            capturedCount++;
            if (sample.Quality.Passed)
            {
                visibleTargets.UnionWith(sample.ObservedFeatures.Select(feature => feature.TargetId));
                _autoCamera.RecordCaptureOutcome(position, heading, sample, _activeContract?.RequiredFeatureIds, minBaselineMetres: 4.5);
            }
        }

        if (capturedCount == 0) return;
        _observedTargetIds.UnionWith(visibleTargets);
        _recentPhotoSeconds = .6;
        _coverageSpots.Add(position);
        _audio.PlayShutter();
        var stopCount = _activeContract!.RequiredFeatureIds.Count(visibleTargets.Contains);
        _feedbackText = string.Format(Text("city.bus.sweep"), stopCount);
    }
}
