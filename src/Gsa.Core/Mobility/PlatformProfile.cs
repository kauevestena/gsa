namespace Gsa.Core.Mobility;

public enum LocomotionMode
{
    OnFoot = 0,
    Handcart = 1,
    Bicycle = 2,
    Scooter = 3,
    Car = 4,
    Kombi = 5,
    Pickup = 6,
    Van = 7,
}

/// <summary>Physical and resource parameters for the initial walking, handcart, bicycle, and scooter modes.</summary>
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

    public static PlatformProfile Bicycle { get; } = new(
        level: 1,
        id: "bicycle",
        mode: LocomotionMode.Bicycle,
        maxSpeedMetresPerSecond: 6.0,
        accelerationMetresPerSecondSquared: 6.0,
        decelerationMetresPerSecondSquared: 10.0,
        turnRateRadiansPerSecond: 2.8,
        collisionRadiusMetres: 0.45,
        collisionHeightMetres: 1.65,
        virtualStorageCapacityUnits: 400,
        hasFiniteAutonomy: true,
        physicalAutonomySeconds: 2400.0,
        priceCoords: 400);

    public static PlatformProfile Scooter { get; } = new(
        level: 2,
        id: "scooter",
        mode: LocomotionMode.Scooter,
        maxSpeedMetresPerSecond: 12.0,
        accelerationMetresPerSecondSquared: 8.5,
        decelerationMetresPerSecondSquared: 12.0,
        turnRateRadiansPerSecond: 2.5,
        collisionRadiusMetres: 0.55,
        collisionHeightMetres: 1.50,
        virtualStorageCapacityUnits: 800,
        hasFiniteAutonomy: true,
        physicalAutonomySeconds: 3000.0,
        priceCoords: 1200);

    public static PlatformProfile Car { get; } = new(
        level: 3,
        id: "car",
        mode: LocomotionMode.Car,
        maxSpeedMetresPerSecond: 18.0,
        accelerationMetresPerSecondSquared: 6.5,
        decelerationMetresPerSecondSquared: 10.5,
        turnRateRadiansPerSecond: 1.8,
        collisionRadiusMetres: 1.15,
        collisionHeightMetres: 1.55,
        virtualStorageCapacityUnits: 1600,
        hasFiniteAutonomy: true,
        physicalAutonomySeconds: 3600.0,
        priceCoords: 4500);

    public static PlatformProfile Kombi { get; } = new(
        level: 4,
        id: "kombi",
        mode: LocomotionMode.Kombi,
        maxSpeedMetresPerSecond: 16.0,
        accelerationMetresPerSecondSquared: 5.0,
        decelerationMetresPerSecondSquared: 9.0,
        turnRateRadiansPerSecond: 1.5,
        collisionRadiusMetres: 1.30,
        collisionHeightMetres: 2.05,
        virtualStorageCapacityUnits: 4000,
        hasFiniteAutonomy: true,
        physicalAutonomySeconds: 3600.0,
        priceCoords: 14000);

    public static PlatformProfile Pickup { get; } = new(
        level: 5,
        id: "pickup",
        mode: LocomotionMode.Pickup,
        maxSpeedMetresPerSecond: 20.0,
        accelerationMetresPerSecondSquared: 6.0,
        decelerationMetresPerSecondSquared: 9.5,
        turnRateRadiansPerSecond: 1.6,
        collisionRadiusMetres: 1.35,
        collisionHeightMetres: 1.90,
        virtualStorageCapacityUnits: 8000,
        hasFiniteAutonomy: true,
        physicalAutonomySeconds: 4200.0,
        priceCoords: 30000);

    public static PlatformProfile Van { get; } = new(
        level: 6,
        id: "van",
        mode: LocomotionMode.Van,
        maxSpeedMetresPerSecond: 18.0,
        accelerationMetresPerSecondSquared: 4.8,
        decelerationMetresPerSecondSquared: 8.8,
        turnRateRadiansPerSecond: 1.4,
        collisionRadiusMetres: 1.40,
        collisionHeightMetres: 2.35,
        virtualStorageCapacityUnits: 16000,
        hasFiniteAutonomy: true,
        physicalAutonomySeconds: 5400.0,
        priceCoords: 60000);

    public static PlatformProfile ForMode(LocomotionMode mode) => mode switch
    {
        LocomotionMode.OnFoot => OnFoot,
        LocomotionMode.Handcart => Handcart,
        LocomotionMode.Bicycle => Bicycle,
        LocomotionMode.Scooter => Scooter,
        LocomotionMode.Car => Car,
        LocomotionMode.Kombi => Kombi,
        LocomotionMode.Pickup => Pickup,
        LocomotionMode.Van => Van,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown locomotion mode."),
    };

    private static void EnsurePositiveFinite(double value, string paramName)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(paramName, "Value must be finite and positive.");
    }
}

