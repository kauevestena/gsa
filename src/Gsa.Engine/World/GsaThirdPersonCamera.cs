using Gsa.Core.Mobility;
using Godot;

namespace Gsa.Engine.World;

public enum GsaCameraMode
{
    Chase = 0,
    SurveyView = 1,
    Overhead = 2,
}

/// <summary>GTA III-style third-person chase camera with wall avoidance, speed pull-back, orbit, and Survey/Overhead toggles.</summary>
public sealed class GsaThirdPersonCamera
{
    private readonly Camera3D _camera;
    private readonly PhysicsRayQueryParameters3D _camRayQuery = new() { CollisionMask = 1 };
    private float _orbitYawOffset;
    private float _orbitPitch = 0.30f;
    private float _zoomDistance = 6.8f;
    private float _smoothedYaw;
    private bool _initialized;
    private bool _rightMouseOrbiting;
    private float _shakeTimer;
    private float _shakeIntensity;
    private float _shakePhase;

    public void TriggerShake(float intensity = 0.28f, float durationSeconds = 0.35f)
    {
        _shakeIntensity = Math.Max(_shakeIntensity, intensity);
        _shakeTimer = Math.Max(_shakeTimer, durationSeconds);
    }

    public GsaCameraMode Mode { get; private set; } = GsaCameraMode.Chase;
    public Camera3D Camera => _camera;

    public GsaThirdPersonCamera(Camera3D camera)
    {
        _camera = camera;
    }

    public void Snap(Vector3 playerGodotPos, double headingRadians, LocomotionMode platformMode)
    {
        _smoothedYaw = (float)headingRadians;
        _orbitYawOffset = 0f;
        _initialized = true;
        var (pivot, _, idealCamPos, lookTarget) = ComputeChaseTransform(playerGodotPos, _smoothedYaw, 0.0, platformMode);
        _ = pivot;
        _camera.Fov = 58f;
        _camera.Position = idealCamPos;
        _camera.LookAt(lookTarget, Vector3.Up);
    }

    public GsaCameraMode CycleMode()
    {
        Mode = (GsaCameraMode)(((int)Mode + 1) % 3);
        return Mode;
    }

    public void SetMode(GsaCameraMode mode) => Mode = mode;

    public bool HandleInput(InputEvent input)
    {
        if (input is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right)
            {
                _rightMouseOrbiting = mb.Pressed;
                return false;
            }
            if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp)
            {
                _zoomDistance = Math.Clamp(_zoomDistance - 0.6f, 3.6f, 12.5f);
                return true;
            }
            if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown)
            {
                _zoomDistance = Math.Clamp(_zoomDistance + 0.6f, 3.6f, 12.5f);
                return true;
            }
        }
        else if (input is InputEventMouseMotion motion && _rightMouseOrbiting)
        {
            _orbitYawOffset -= motion.Relative.X * 0.006f;
            _orbitPitch = Math.Clamp(_orbitPitch + motion.Relative.Y * 0.005f, 0.08f, 1.05f);
            return true;
        }
        return false;
    }

    public void Update(
        double dt,
        Vector3 playerGodotPos,
        double headingRadians,
        double speedMps,
        LocomotionMode platformMode,
        PhysicsDirectSpaceState3D? spaceState,
        double gimbalinaStress = 0.0,
        double clockSeconds = 0.0)
    {
        var fdt = (float)Math.Clamp(dt, 0.001, 0.1);
        var targetYaw = (float)headingRadians;
        if (!_initialized)
        {
            _smoothedYaw = targetYaw;
            _initialized = true;
        }

        // Q/E keyboard camera orbit
        var orbitDir = (Input.IsPhysicalKeyPressed(Key.Q) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(Key.E) ? 1f : 0f);
        if (MathF.Abs(orbitDir) > 0.01f)
            _orbitYawOffset += orbitDir * 1.8f * fdt;
        else if (MathF.Abs((float)speedMps) > 1.2f && !_rightMouseOrbiting)
            _orbitYawOffset = Mathf.Lerp(_orbitYawOffset, 0f, Math.Min(1f, fdt * 2.2f));

        // Smooth heading follow
        var yawDiff = MathF.Atan2(MathF.Sin(targetYaw - _smoothedYaw), MathF.Cos(targetYaw - _smoothedYaw));
        _smoothedYaw += yawDiff * Math.Min(1f, fdt * (MathF.Abs((float)speedMps) > 0.5f ? 5.5f : 2.5f));

        if (Mode == GsaCameraMode.SurveyView)
        {
            var camHeight = platformMode switch
            {
                LocomotionMode.Van => 2.3f,
                LocomotionMode.Kombi or LocomotionMode.Pickup => 1.95f,
                LocomotionMode.Car => 1.65f,
                LocomotionMode.OnFoot => 1.55f,
                _ => 1.05f,
            };
            var eye = playerGodotPos + new Vector3(0f, camHeight, 0f);
            var dir = new Vector3((float)Math.Cos(headingRadians), 0f, -(float)Math.Sin(headingRadians));
            var jitter = platformMode <= LocomotionMode.Bicycle && gimbalinaStress > 50.0
                ? new Vector3(0f, (float)(Math.Sin(clockSeconds * 28.0) * 0.025 * (gimbalinaStress / 100.0)), 0f)
                : Vector3.Zero;
            _camera.Fov = 62f;
            _camera.Position = eye + jitter;
            _camera.LookAt(eye + dir * 12f, Vector3.Up);
            return;
        }

        if (Mode == GsaCameraMode.Overhead)
        {
            var overheadPos = playerGodotPos + new Vector3(0f, 24f, 16f);
            _camera.Fov = 54f;
            _camera.Position = _camera.Position.Lerp(overheadPos, Math.Min(1f, fdt * 8f));
            _camera.LookAt(playerGodotPos + new Vector3(0f, 0.8f, 0f), Vector3.Up);
            return;
        }

        // Mode 0: Authentic GTA III Third-Person Chase Cam
        var viewYaw = _smoothedYaw + _orbitYawOffset;
        var (pivot, _, idealCamPos, lookTarget) = ComputeChaseTransform(playerGodotPos, viewYaw, speedMps, platformMode);

        // Collision push-forward so buildings don't block the player (ignore hits inside vehicle radius)
        if (spaceState is not null)
        {
            _camRayQuery.From = pivot;
            _camRayQuery.To = idealCamPos;
            var hit = spaceState.IntersectRay(_camRayQuery);
            if (hit.Count > 0)
            {
                var hitPos = hit["position"].AsVector3();
                var hitDist = (hitPos - pivot).Length();
                if (hitDist > 1.8f)
                {
                    var toCam = (idealCamPos - pivot).Normalized();
                    idealCamPos = pivot + toCam * Math.Max(2.0f, hitDist - 0.35f);
                }
            }
        }

        _camera.Position = _camera.Position.Lerp(idealCamPos, Math.Min(1f, fdt * 11f));
        if (_shakeTimer > 0f)
        {
            _shakeTimer = MathF.Max(0f, _shakeTimer - fdt);
            _shakePhase += fdt * 52f;
            var mag = _shakeIntensity * MathF.Min(1f, _shakeTimer * 4f);
            _camera.Position += new Vector3(MathF.Sin(_shakePhase) * mag, MathF.Cos(_shakePhase * 1.3f) * mag * 0.7f, 0f);
            if (_shakeTimer <= 0f) _shakeIntensity = 0f;
        }
        _camera.Fov = Mathf.Lerp(_camera.Fov, 58f + Math.Clamp((float)Math.Abs(speedMps) * 0.55f, 0f, 10f), Math.Min(1f, fdt * 6f));
        _camera.LookAt(lookTarget, Vector3.Up);
    }

    private (Vector3 Pivot, Vector3 ForwardXZ, Vector3 IdealCamPos, Vector3 LookTarget) ComputeChaseTransform(
        Vector3 playerGodotPos,
        float viewYaw,
        double speedMps,
        LocomotionMode platformMode)
    {
        var sizeScale = platformMode switch
        {
            LocomotionMode.Van or LocomotionMode.Pickup => 1.32f,
            LocomotionMode.Kombi or LocomotionMode.Car => 1.18f,
            LocomotionMode.Scooter or LocomotionMode.Bicycle => 0.94f,
            _ => 0.84f,
        };
        var speedBoostDist = Math.Clamp((float)Math.Abs(speedMps) * 0.14f, 0f, 2.6f);
        var desiredDist = _zoomDistance * sizeScale + speedBoostDist;
        var forwardXZ = new Vector3(MathF.Cos(viewYaw), 0f, -MathF.Sin(viewYaw));
        var pivot = playerGodotPos + new Vector3(0f, platformMode >= LocomotionMode.Car ? 1.85f : 1.40f, 0f);
        var horizDist = desiredDist * MathF.Cos(_orbitPitch);
        var vertDist = desiredDist * MathF.Sin(_orbitPitch) + 0.75f;
        var idealCamPos = pivot - forwardXZ * horizDist + new Vector3(0f, vertDist, 0f);
        var lookTarget = pivot + forwardXZ * 2.5f - new Vector3(0f, 0.25f, 0f);
        return (pivot, forwardXZ, idealCamPos, lookTarget);
    }
}

