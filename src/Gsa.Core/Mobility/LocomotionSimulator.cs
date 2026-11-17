using Gsa.Core.Geometry;
using Gsa.Core.World;

namespace Gsa.Core.Mobility;

public readonly record struct ObstacleBoxEnu(string Id, Vector3d CenterEnu, Vector3d SizeEnu, bool Collision = true)
{
    public double MinX => CenterEnu.X - SizeEnu.X / 2.0;
    public double MaxX => CenterEnu.X + SizeEnu.X / 2.0;
    public double MinY => CenterEnu.Y - SizeEnu.Y / 2.0;
    public double MaxY => CenterEnu.Y + SizeEnu.Y / 2.0;
    public double MinZ => CenterEnu.Z - SizeEnu.Z / 2.0;
    public double MaxZ => CenterEnu.Z + SizeEnu.Z / 2.0;

    public static IReadOnlyList<ObstacleBoxEnu> FromWorldObjects(IEnumerable<WorldBoxObject> objects) =>
        objects.Select(item => new ObstacleBoxEnu(item.Id, item.PositionEnu, item.SizeEnu, item.Collision)).ToArray();
}

public readonly record struct InteractiveTargetEnu(string Id, string ObjectClass, Vector3d PositionEnu)
{
    public static IReadOnlyList<InteractiveTargetEnu> FromWorldObjects(IEnumerable<WorldBoxObject> objects) =>
        objects.Where(item => item.ObjectClass is "office_entry" or "unload_point" or "survey_control")
               .Select(item => new InteractiveTargetEnu(item.Id, item.ObjectClass, item.PositionEnu))
               .ToArray();
}

public readonly record struct LocomotionInput
{
    public double Throttle { get; }
    public double Brake { get; }
    public double Steer { get; }
    public bool InteractPressed { get; }

    public LocomotionInput(double throttle, double brake, double steer, bool interactPressed = false)
    {
        if (!double.IsFinite(throttle) || !double.IsFinite(brake) || !double.IsFinite(steer))
            throw new ArgumentOutOfRangeException(nameof(throttle), "Locomotion inputs must be finite.");
        Throttle = Math.Clamp(throttle, -1.0, 1.0);
        Brake = Math.Clamp(brake, 0.0, 1.0);
        Steer = Math.Clamp(steer, -1.0, 1.0);
        InteractPressed = interactPressed;
    }
}

public enum InteractionKind
{
    None,
    MountHandcart,
    DismountHandcart,
    EnterOffice,
    UnloadBatch,
    OccupySurveyControl,
    BlockedMustDismount,
    OutOfRange,
}

public readonly record struct InteractionOutcome(InteractionKind Kind, string? TargetId, string? ReasonKey);

public enum CaptureStopReason
{
    None,
    AutonomyExhausted,
    VirtualStorageFull,
    GimbalinaFled,
}

public sealed record LocomotionStepResult(
    Vector3d PositionEnu,
    double HeadingRadians,
    Vector3d VelocityEnu,
    double ForwardSpeedMetresPerSecond,
    LocomotionMode Mode,
    bool Collided,
    string? CollidedObjectId,
    double ImpactDeltaVMetresPerSecond,
    double RemainingAutonomySeconds,
    int UsedVirtualStorageUnits,
    bool CanCapture,
    CaptureStopReason CaptureStopReason);

/// <summary>Deterministic kinematic locomotion for on-foot (3 m/s) and handcart (2 m/s) modes in ENU.</summary>
public sealed record LocomotionSaveSnapshot(
    LocomotionMode Mode,
    LocomotionMode MountedVehicleMode,
    Vector3d PositionEnu,
    double HeadingRadians,
    Vector3d ParkedVehiclePositionEnu,
    double ParkedVehicleHeadingRadians,
    double RemainingAutonomySeconds,
    int UsedVirtualStorageUnits,
    bool GimbalinaFled);

public sealed class LocomotionSimulator
{
    public const double MaxMountDistanceMetres = 1.8;
    public const double MaxInteractDistanceMetres = 2.5;
    public const double MaxSpeedToToggleMount = 0.1;

    private readonly WorldBounds? _sceneBounds;

    public LocomotionMode Mode { get; private set; }
    public LocomotionMode MountedVehicleMode { get; private set; } = LocomotionMode.Handcart;
    public PlatformProfile CurrentProfile => PlatformProfile.ForMode(Mode);
    public Vector3d PositionEnu { get; private set; }
    public double HeadingRadians { get; private set; }
    public double ForwardSpeedMetresPerSecond { get; private set; }
    public Vector3d VelocityEnu { get; private set; }
    public Vector3d HandcartPositionEnu { get; private set; }
    public double HandcartHeadingRadians { get; private set; }
    public double RemainingAutonomySeconds { get; private set; }
    public int UsedVirtualStorageUnits { get; private set; }
    public bool GimbalinaFled { get; private set; }

    public LocomotionSimulator(
        Vector3d initialPositionEnu,
        double initialHeadingRadians = 0.0,
        LocomotionMode initialMode = LocomotionMode.Handcart,
        WorldBounds? sceneBounds = null)
    {
        if (!double.IsFinite(initialHeadingRadians))
            throw new ArgumentOutOfRangeException(nameof(initialHeadingRadians), "Heading must be finite.");
        _sceneBounds = sceneBounds;
        Mode = initialMode;
        MountedVehicleMode = initialMode == LocomotionMode.OnFoot ? LocomotionMode.Handcart : initialMode;
        PositionEnu = initialPositionEnu;
        HeadingRadians = NormalizeAngle(initialHeadingRadians);
        HandcartPositionEnu = initialPositionEnu;
        HandcartHeadingRadians = HeadingRadians;
        RemainingAutonomySeconds = PlatformProfile.ForMode(MountedVehicleMode).PhysicalAutonomySeconds;
    }

    public Quaterniond BodyToMap => Quaterniond.FromAxisAngle(Vector3d.UnitZ, HeadingRadians);

    public LocomotionSaveSnapshot ExportSaveSnapshot() => new(
        Mode, MountedVehicleMode, PositionEnu, HeadingRadians, HandcartPositionEnu,
        HandcartHeadingRadians, RemainingAutonomySeconds, UsedVirtualStorageUnits, GimbalinaFled);

    /// <summary>Restores player/parked vehicle and resources. Motion resumes at rest.</summary>
    public static LocomotionSimulator FromSaveSnapshot(LocomotionSaveSnapshot snapshot, WorldBounds? bounds = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Enum.IsDefined(snapshot.Mode) || !Enum.IsDefined(snapshot.MountedVehicleMode) ||
            snapshot.MountedVehicleMode == LocomotionMode.OnFoot ||
            (snapshot.Mode != LocomotionMode.OnFoot && snapshot.Mode != snapshot.MountedVehicleMode) ||
            !double.IsFinite(snapshot.HeadingRadians) || !double.IsFinite(snapshot.ParkedVehicleHeadingRadians) ||
            !double.IsFinite(snapshot.RemainingAutonomySeconds) || snapshot.RemainingAutonomySeconds < 0 ||
            snapshot.UsedVirtualStorageUnits < 0)
            throw new ArgumentException("Invalid saved locomotion state.", nameof(snapshot));
        if (bounds is { } extent && (!extent.ContainsBox(snapshot.PositionEnu, Vector3d.Zero) ||
            !extent.ContainsBox(snapshot.ParkedVehiclePositionEnu, Vector3d.Zero)))
            throw new ArgumentException("Saved locomotion lies outside this world.", nameof(snapshot));
        var result = new LocomotionSimulator(snapshot.PositionEnu, snapshot.HeadingRadians, snapshot.MountedVehicleMode, bounds)
        {
            Mode = snapshot.Mode,
            HandcartPositionEnu = snapshot.ParkedVehiclePositionEnu,
            HandcartHeadingRadians = NormalizeAngle(snapshot.ParkedVehicleHeadingRadians),
            RemainingAutonomySeconds = snapshot.RemainingAutonomySeconds,
            UsedVirtualStorageUnits = snapshot.UsedVirtualStorageUnits,
            GimbalinaFled = snapshot.GimbalinaFled,
        };
        return result;
    }

    public CaptureStopReason EvaluateCaptureReadiness(int pendingUnits = 1)
    {
        if (pendingUnits <= 0)
            throw new ArgumentOutOfRangeException(nameof(pendingUnits), "Pending storage units must be positive.");
        if (GimbalinaFled)
            return CaptureStopReason.GimbalinaFled;
        if (CurrentProfile.HasFiniteAutonomy && RemainingAutonomySeconds <= 0)
            return CaptureStopReason.AutonomyExhausted;
        if (UsedVirtualStorageUnits + pendingUnits > CurrentProfile.VirtualStorageCapacityUnits)
            return CaptureStopReason.VirtualStorageFull;
        return CaptureStopReason.None;
    }

    public bool TryConsumeCaptureUnits(int units, out CaptureStopReason stopReason)
    {
        stopReason = EvaluateCaptureReadiness(units);
        if (stopReason != CaptureStopReason.None)
            return false;
        UsedVirtualStorageUnits += units;
        return true;
    }

    public void MarkGimbalinaFled() => GimbalinaFled = true;

    public void SwitchVehiclePlatform(LocomotionMode vehicleMode, bool refillAutonomy = true, bool clearStorage = false)
    {
        if (vehicleMode == LocomotionMode.OnFoot)
            throw new ArgumentException("Vehicle platform mode cannot be OnFoot.", nameof(vehicleMode));

        MountedVehicleMode = vehicleMode;
        if (Mode != LocomotionMode.OnFoot)
            Mode = vehicleMode;
        if (refillAutonomy)
            RemainingAutonomySeconds = PlatformProfile.ForMode(vehicleMode).PhysicalAutonomySeconds;
        if (clearStorage)
            UsedVirtualStorageUnits = 0;
    }

    public void RefillHandcartResources(bool refillAutonomy = true, bool clearStorage = true)
    {
        if (refillAutonomy)
            RemainingAutonomySeconds = PlatformProfile.ForMode(MountedVehicleMode).PhysicalAutonomySeconds;
        if (clearStorage)
            UsedVirtualStorageUnits = 0;
        GimbalinaFled = false;
    }

    public void RepositionSafeAfterRecovery(
        Vector3d safePositionEnu,
        LocomotionMode? vehicleMode = null,
        double headingRadians = 0.0,
        bool refillAutonomy = true,
        bool clearStorage = true)
    {
        if (vehicleMode is { } explicitMode && explicitMode != LocomotionMode.OnFoot)
            MountedVehicleMode = explicitMode;

        Mode = MountedVehicleMode;
        PositionEnu = safePositionEnu;
        HeadingRadians = NormalizeAngle(headingRadians);
        ForwardSpeedMetresPerSecond = 0.0;
        VelocityEnu = Vector3d.Zero;
        HandcartPositionEnu = PositionEnu;
        HandcartHeadingRadians = HeadingRadians;
        RefillHandcartResources(refillAutonomy, clearStorage);
    }

    public LocomotionStepResult Step(
        double dtSeconds,
        LocomotionInput input,
        double slopeGrade = 0.0,
        double surfaceGrip = 1.0,
        IReadOnlyList<ObstacleBoxEnu>? obstacles = null,
        Func<Vector3d, double>? groundElevationAt = null)
    {
        if (!double.IsFinite(dtSeconds) || dtSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(dtSeconds), "Time step must be finite and positive.");
        if (!double.IsFinite(slopeGrade) || slopeGrade < -0.5 || slopeGrade > 0.5)
            throw new ArgumentOutOfRangeException(nameof(slopeGrade), "Slope grade must be finite and within [-0.5, 0.5].");
        if (!double.IsFinite(surfaceGrip) || surfaceGrip <= 0 || surfaceGrip > 1.0)
            throw new ArgumentOutOfRangeException(nameof(surfaceGrip), "Surface grip must be within (0, 1].");

        var profile = CurrentProfile;
        var gripFactor = Math.Clamp(surfaceGrip, 0.25, 1.0);
        var turnAuthority = Mode == LocomotionMode.OnFoot
            ? 1.0
            : 0.45 + 0.55 * Math.Min(1.0, Math.Abs(ForwardSpeedMetresPerSecond) / 0.5);
        HeadingRadians = NormalizeAngle(HeadingRadians + input.Steer * profile.TurnRateRadiansPerSecond * gripFactor * turnAuthority * dtSeconds);

        var slopeAngle = Math.Atan(slopeGrade);
        var slopeAccel = -9.80665 * Math.Sin(slopeAngle) * 0.15;
        var maxForward = profile.MaxSpeedMetresPerSecond * (slopeGrade > 0 ? Math.Max(0.65, 1.0 - 0.6 * slopeGrade) : 1.0);
        var maxReverse = 0.5 * profile.MaxSpeedMetresPerSecond;

        var targetSpeed = ForwardSpeedMetresPerSecond;
        if (input.Brake > 0)
        {
            var brakeStep = profile.DecelerationMetresPerSecondSquared * gripFactor * input.Brake * dtSeconds;
            targetSpeed = MoveToward(targetSpeed, 0.0, brakeStep);
        }
        else if (Math.Abs(input.Throttle) > 1e-6)
        {
            var accel = profile.AccelerationMetresPerSecondSquared * gripFactor * input.Throttle + slopeAccel;
            targetSpeed = Math.Clamp(targetSpeed + accel * dtSeconds, -maxReverse, maxForward);
        }
        else
        {
            var coastDrag = profile.DecelerationMetresPerSecondSquared * 0.45 * dtSeconds;
            targetSpeed = MoveToward(targetSpeed, 0.0, coastDrag);
        }

        var cosH = Math.Cos(HeadingRadians);
        var sinH = Math.Sin(HeadingRadians);
        var cosSlope = Math.Cos(slopeAngle);
        var horizontalSpeed = targetSpeed * cosSlope;
        var deltaX = cosH * horizontalSpeed * dtSeconds;
        var deltaY = sinH * horizontalSpeed * dtSeconds;
        var deltaZ = targetSpeed * Math.Sin(slopeAngle) * dtSeconds;

        var candidate = PositionEnu;
        var collided = false;
        string? collidedId = null;
        var preVx = cosH * horizontalSpeed;
        var preVy = sinH * horizontalSpeed;
        var radius = profile.CollisionRadiusMetres;

        candidate = ResolveAxis(candidate, deltaX, 0, radius, profile.CollisionHeightMetres, obstacles,
            ref collided, ref collidedId, out var blockedX);
        candidate = ResolveAxis(candidate, 0, deltaY, radius, profile.CollisionHeightMetres, obstacles,
            ref collided, ref collidedId, out var blockedY);

        var nextZ = candidate.Z + deltaZ;
        if (groundElevationAt is not null)
            nextZ = groundElevationAt(new Vector3d(candidate.X, candidate.Y, nextZ));
        candidate = new Vector3d(candidate.X, candidate.Y, nextZ);

        if (_sceneBounds is { } bounds)
        {
            var minX = bounds.Min.X + radius;
            var maxX = bounds.Max.X - radius;
            var minY = bounds.Min.Y + radius;
            var maxY = bounds.Max.Y - radius;
            var clampedX = Math.Clamp(candidate.X, minX, maxX);
            var clampedY = Math.Clamp(candidate.Y, minY, maxY);
            if (clampedX != candidate.X)
                blockedX = true;
            if (clampedY != candidate.Y)
                blockedY = true;
            candidate = new Vector3d(clampedX, clampedY, candidate.Z);
        }

        var postVx = blockedX ? 0.0 : preVx;
        var postVy = blockedY ? 0.0 : preVy;
        var impactDeltaV = Math.Sqrt((preVx - postVx) * (preVx - postVx) + (preVy - postVy) * (preVy - postVy));
        var postHorizontalSpeed = postVx * cosH + postVy * sinH;

        var movedHorizontal = Math.Sqrt(
            (candidate.X - PositionEnu.X) * (candidate.X - PositionEnu.X) +
            (candidate.Y - PositionEnu.Y) * (candidate.Y - PositionEnu.Y));

        PositionEnu = candidate;
        ForwardSpeedMetresPerSecond = Math.Clamp(postHorizontalSpeed / Math.Max(0.5, cosSlope), -maxReverse, maxForward);
        VelocityEnu = new Vector3d(postVx, postVy, deltaZ / dtSeconds);

        if (Mode != LocomotionMode.OnFoot)
        {
            HandcartPositionEnu = PositionEnu;
            HandcartHeadingRadians = HeadingRadians;
            if (movedHorizontal > 1e-9)
            {
                RemainingAutonomySeconds = Math.Max(0.0, RemainingAutonomySeconds - dtSeconds);
                if (RemainingAutonomySeconds < 1e-9)
                    RemainingAutonomySeconds = 0.0;
            }
        }

        var stopReason = EvaluateCaptureReadiness();
        return new LocomotionStepResult(
            PositionEnu,
            HeadingRadians,
            VelocityEnu,
            ForwardSpeedMetresPerSecond,
            Mode,
            collided,
            collidedId,
            impactDeltaV,
            RemainingAutonomySeconds,
            UsedVirtualStorageUnits,
            stopReason == CaptureStopReason.None,
            stopReason);
    }

    public bool TryToggleMount(IReadOnlyList<ObstacleBoxEnu>? obstacles, out string? reasonKey)
    {
        if (Math.Abs(ForwardSpeedMetresPerSecond) >= MaxSpeedToToggleMount)
        {
            reasonKey = "mobility.mount.vehicle_moving";
            return false;
        }

        if (Mode == LocomotionMode.OnFoot)
        {
            var dx = PositionEnu.X - HandcartPositionEnu.X;
            var dy = PositionEnu.Y - HandcartPositionEnu.Y;
            if (Math.Sqrt(dx * dx + dy * dy) > MaxMountDistanceMetres)
            {
                reasonKey = "mobility.mount.too_far";
                return false;
            }
            Mode = MountedVehicleMode;
            PositionEnu = HandcartPositionEnu;
            HeadingRadians = HandcartHeadingRadians;
            ForwardSpeedMetresPerSecond = 0.0;
            VelocityEnu = Vector3d.Zero;
            reasonKey = null;
            return true;
        }

        var footRadius = PlatformProfile.OnFoot.CollisionRadiusMetres;
        var footHeight = PlatformProfile.OnFoot.CollisionHeightMetres;
        var candidateAngles = new[] { Math.PI / 2, -Math.PI / 2, Math.PI, 0.0 };
        foreach (var offsetAngle in candidateAngles)
        {
            var angle = HeadingRadians + offsetAngle;
            var exitPosition = new Vector3d(
                PositionEnu.X + Math.Cos(angle) * 1.1,
                PositionEnu.Y + Math.Sin(angle) * 1.1,
                PositionEnu.Z);
            if (!IsBlocked(exitPosition, footRadius, footHeight, obstacles) && IsInsideBounds(exitPosition, footRadius))
            {
                HandcartPositionEnu = PositionEnu;
                HandcartHeadingRadians = HeadingRadians;
                Mode = LocomotionMode.OnFoot;
                PositionEnu = exitPosition;
                ForwardSpeedMetresPerSecond = 0.0;
                VelocityEnu = Vector3d.Zero;
                reasonKey = null;
                return true;
            }
        }

        reasonKey = "mobility.mount.exit_blocked";
        return false;
    }

    public InteractionOutcome TryInteract(
        IEnumerable<InteractiveTargetEnu> targets,
        IReadOnlyList<ObstacleBoxEnu>? obstacles = null)
    {
        ArgumentNullException.ThrowIfNull(targets);
        InteractiveTargetEnu? best = null;
        var bestDistance = double.PositiveInfinity;
        foreach (var target in targets)
        {
            var dx = target.PositionEnu.X - PositionEnu.X;
            var dy = target.PositionEnu.Y - PositionEnu.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = target;
            }
        }

        if (best is { } hit && bestDistance <= MaxInteractDistanceMetres)
        {
            switch (hit.ObjectClass)
            {
                case "office_entry":
                    return new InteractionOutcome(InteractionKind.EnterOffice, hit.Id, null);
                case "unload_point":
                    return new InteractionOutcome(InteractionKind.UnloadBatch, hit.Id, null);
                case "survey_control":
                    if (Mode != LocomotionMode.OnFoot)
                        return new InteractionOutcome(InteractionKind.BlockedMustDismount, hit.Id, "mobility.interact.dismount_required");
                    return new InteractionOutcome(InteractionKind.OccupySurveyControl, hit.Id, null);
            }
        }

        var handcartDistance = Math.Sqrt(
            (HandcartPositionEnu.X - PositionEnu.X) * (HandcartPositionEnu.X - PositionEnu.X) +
            (HandcartPositionEnu.Y - PositionEnu.Y) * (HandcartPositionEnu.Y - PositionEnu.Y));
        if (Mode != LocomotionMode.OnFoot || handcartDistance <= MaxMountDistanceMetres)
        {
            var previousMode = Mode;
            if (TryToggleMount(obstacles, out var reason))
                return new InteractionOutcome(
                    previousMode == LocomotionMode.OnFoot ? InteractionKind.MountHandcart : InteractionKind.DismountHandcart,
                    "handcart",
                    null);
            return new InteractionOutcome(InteractionKind.None, "handcart", reason);
        }

        return new InteractionOutcome(InteractionKind.OutOfRange, best?.Id, "mobility.interact.out_of_range");
    }

    private Vector3d ResolveAxis(
        Vector3d current,
        double dx,
        double dy,
        double radius,
        double actorHeight,
        IReadOnlyList<ObstacleBoxEnu>? obstacles,
        ref bool collided,
        ref string? collidedId,
        out bool blocked)
    {
        blocked = false;
        var candidate = new Vector3d(current.X + dx, current.Y + dy, current.Z);
        if (obstacles is null)
            return candidate;

        foreach (var box in obstacles)
        {
            if (!box.Collision || !OverlapsVertically(current.Z, actorHeight, box))
                continue;
            if (!OverlapsHorizontal(candidate.X, candidate.Y, radius, box))
                continue;

            collided = true;
            collidedId ??= box.Id;
            blocked = true;
            const double skin = 1e-4;
            if (Math.Abs(dx) > 0)
            {
                var resolvedX = dx > 0 ? box.MinX - radius - skin : box.MaxX + radius + skin;
                candidate = new Vector3d(resolvedX, candidate.Y, candidate.Z);
            }
            if (Math.Abs(dy) > 0)
            {
                var resolvedY = dy > 0 ? box.MinY - radius - skin : box.MaxY + radius + skin;
                candidate = new Vector3d(candidate.X, resolvedY, candidate.Z);
            }
        }
        return candidate;
    }

    private bool IsBlocked(Vector3d position, double radius, double actorHeight, IReadOnlyList<ObstacleBoxEnu>? obstacles)
    {
        if (obstacles is null)
            return false;
        foreach (var box in obstacles)
        {
            if (box.Collision && OverlapsVertically(position.Z, actorHeight, box) &&
                OverlapsHorizontal(position.X, position.Y, radius, box))
                return true;
        }
        return false;
    }

    private bool IsInsideBounds(Vector3d position, double radius)
    {
        if (_sceneBounds is not { } bounds)
            return true;
        return position.X - radius >= bounds.Min.X && position.X + radius <= bounds.Max.X &&
               position.Y - radius >= bounds.Min.Y && position.Y + radius <= bounds.Max.Y;
    }

    private static bool OverlapsVertically(double footZ, double actorHeight, ObstacleBoxEnu box) =>
        box.MaxZ > footZ + 0.05 && box.MinZ < footZ + actorHeight;

    private static bool OverlapsHorizontal(double x, double y, double radius, ObstacleBoxEnu box) =>
        x + radius > box.MinX && x - radius < box.MaxX &&
        y + radius > box.MinY && y - radius < box.MaxY;

    private static double MoveToward(double value, double target, double maxDelta) =>
        Math.Abs(target - value) <= maxDelta ? target : value + Math.Sign(target - value) * maxDelta;

    private static double NormalizeAngle(double radians)
    {
        var wrapped = (radians + Math.PI) % (2.0 * Math.PI);
        if (wrapped < 0)
            wrapped += 2.0 * Math.PI;
        return wrapped - Math.PI;
    }
}
