namespace Gsa.Core.Mobility;

public enum LocomotionMode
{
    OnFoot = 0,
    Handcart = 1,
}

/// <summary>Physical and resource parameters for the initial walking and handcart modes.</summary>
public sealed record PlatformProfile
{
    public int Level { get; }
    public string Id { get; }
    public LocomotionMode Mode { get; }
    public double MaxSpeedMetresPerSecond { get; }
    public double AccelerationMetresPerSecondSquared { get; }
    public double DecelerationMetresPerSecondSquared { get; }
    public double TurnRateRadiansPerSecond { get; }
    public double CollisionRadiusMetres { get; }
    public double CollisionHeightMetres { get; }
    public int VirtualStorageCapacityUnits { get; }
    public bool HasFiniteAutonomy { get; }
    public double PhysicalAutonomySeconds { get; }
    public int PriceCoords { get; }
    public bool SupportsLidarSlots => Level >= 4;

    public PlatformProfile(
        int level,
        string id,
        LocomotionMode mode,
        double maxSpeedMetresPerSecond,
        double accelerationMetresPerSecondSquared,
        double decelerationMetresPerSecondSquared,
        double turnRateRadiansPerSecond,
        double collisionRadiusMetres,
        double collisionHeightMetres,
        int virtualStorageCapacityUnits,
        bool hasFiniteAutonomy,
        double physicalAutonomySeconds,
        int priceCoords)
    {
        if (level < 0)
            throw new ArgumentOutOfRangeException(nameof(level), "Platform level cannot be negative.");
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Platform ID is required.", nameof(id));
        EnsurePositiveFinite(maxSpeedMetresPerSecond, nameof(maxSpeedMetresPerSecond));
        EnsurePositiveFinite(accelerationMetresPerSecondSquared, nameof(accelerationMetresPerSecondSquared));
        EnsurePositiveFinite(decelerationMetresPerSecondSquared, nameof(decelerationMetresPerSecondSquared));
        EnsurePositiveFinite(turnRateRadiansPerSecond, nameof(turnRateRadiansPerSecond));
        EnsurePositiveFinite(collisionRadiusMetres, nameof(collisionRadiusMetres));
        EnsurePositiveFinite(collisionHeightMetres, nameof(collisionHeightMetres));
        if (virtualStorageCapacityUnits <= 0)
            throw new ArgumentOutOfRangeException(nameof(virtualStorageCapacityUnits), "Virtual storage must be positive.");
        if (!double.IsFinite(physicalAutonomySeconds) || (hasFiniteAutonomy && physicalAutonomySeconds <= 0) ||
            (!hasFiniteAutonomy && physicalAutonomySeconds != 0))
            throw new ArgumentOutOfRangeException(nameof(physicalAutonomySeconds), "Autonomy must match HasFiniteAutonomy.");
        if (priceCoords < 0)
            throw new ArgumentOutOfRangeException(nameof(priceCoords), "Price cannot be negative.");

        Level = level;
        Id = id;
        Mode = mode;
        MaxSpeedMetresPerSecond = maxSpeedMetresPerSecond;
        AccelerationMetresPerSecondSquared = accelerationMetresPerSecondSquared;
        DecelerationMetresPerSecondSquared = decelerationMetresPerSecondSquared;
        TurnRateRadiansPerSecond = turnRateRadiansPerSecond;
        CollisionRadiusMetres = collisionRadiusMetres;
        CollisionHeightMetres = collisionHeightMetres;
        VirtualStorageCapacityUnits = virtualStorageCapacityUnits;
        HasFiniteAutonomy = hasFiniteAutonomy;
        PhysicalAutonomySeconds = physicalAutonomySeconds;
        PriceCoords = priceCoords;
    }

    public static PlatformProfile OnFoot { get; } = new(
        level: 0,
        id: "on_foot",
        mode: LocomotionMode.OnFoot,
        maxSpeedMetresPerSecond: 3.0,
        accelerationMetresPerSecondSquared: 12.0,
        decelerationMetresPerSecondSquared: 16.0,
        turnRateRadiansPerSecond: 3.8,
        collisionRadiusMetres: 0.35,
        collisionHeightMetres: 1.75,
        virtualStorageCapacityUnits: 200,
        hasFiniteAutonomy: false,
        physicalAutonomySeconds: 0.0,
        priceCoords: 0);

    public static PlatformProfile Handcart { get; } = new(
        level: 0,
        id: "handcart",
        mode: LocomotionMode.Handcart,
        maxSpeedMetresPerSecond: 2.0,
        accelerationMetresPerSecondSquared: 4.5,
        decelerationMetresPerSecondSquared: 7.0,
        turnRateRadiansPerSecond: 2.2,
        collisionRadiusMetres: 0.55,
        collisionHeightMetres: 1.25,
        virtualStorageCapacityUnits: 200,
        hasFiniteAutonomy: true,
        physicalAutonomySeconds: 1800.0,
        priceCoords: 0);

    private static void EnsurePositiveFinite(double value, string paramName)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(paramName, "Value must be finite and positive.");
    }
}

