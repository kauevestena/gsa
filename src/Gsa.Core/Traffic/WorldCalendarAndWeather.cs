using Gsa.Core.Sensors.Camera;

namespace Gsa.Core.Traffic;

public sealed record WeatherAndTimeSnapshot(
    int SchemaVersion,
    double CalendarTimeSeconds,
    int DayIndex,
    double HourOfDay,
    bool IsNight,
    bool IsRushHour,
    bool IsWeekend,
    bool IsRaining,
    double RainIntensity,
    double SunElevationRadians,
    double SurfaceGripMultiplier,
    double TrafficDensityMultiplier,
    bool Paused)
{
    public CameraEnvironmentSnapshot ToCameraEnvironment(
        double cameraViewSunAngleRadians = Math.PI / 3.0,
        double lensObstructionFraction = 0.0)
    {
        return new CameraEnvironmentSnapshot(
            isNight: IsNight,
            sunAngleRadians: Math.Clamp(cameraViewSunAngleRadians, 0.0, Math.PI),
            lensObstructionFraction: Math.Clamp(lensObstructionFraction, 0.0, 1.0));
    }
}

/// <summary>Persistent accelerated calendar (1 physical s = 60 calendar s), weather, adhesion, and sensor environment snapshot.</summary>
public sealed class WorldCalendarAndWeatherController
{
    public const double CalendarSecondsPerPhysicalSecond = 60.0;
    public const double SecondsPerHour = 3600.0;
    public const double SecondsPerDay = 86400.0;
    public const double DefaultStartCalendarSeconds = 8.0 * SecondsPerHour;
    public const double DryGripMultiplier = 1.0;
    public const double RainGripMultiplier = 0.6;

    public double CalendarTimeSeconds { get; private set; }
    public bool Paused { get; private set; }
    public bool IsRaining { get; private set; }
    public double RainIntensity { get; private set; }

    public int DayIndex => (int)Math.Floor(CalendarTimeSeconds / SecondsPerDay);
    public double HourOfDay => (CalendarTimeSeconds % SecondsPerDay) / SecondsPerHour;
    public bool IsNight => HourOfDay >= 20.0 || HourOfDay < 6.0;
    public bool IsRushHour => (HourOfDay >= 7.0 && HourOfDay < 9.0) || (HourOfDay >= 17.0 && HourOfDay < 19.0);
    public bool IsWeekend => (DayIndex % 7) >= 5;
    public double SurfaceGripMultiplier => IsRaining ? RainGripMultiplier : DryGripMultiplier;

    public WorldCalendarAndWeatherController(
        double initialCalendarTimeSeconds = DefaultStartCalendarSeconds,
        bool isRaining = false,
        double rainIntensity = 0.0,
        bool paused = false)
    {
        if (!double.IsFinite(initialCalendarTimeSeconds) || initialCalendarTimeSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(initialCalendarTimeSeconds));
        if (!double.IsFinite(rainIntensity) || rainIntensity < 0.0 || rainIntensity > 1.0)
            throw new ArgumentOutOfRangeException(nameof(rainIntensity));

        CalendarTimeSeconds = initialCalendarTimeSeconds;
        IsRaining = isRaining;
        RainIntensity = isRaining ? Math.Max(0.1, rainIntensity) : 0.0;
        Paused = paused;
    }

    public void SetPaused(bool paused) => Paused = paused;

    public void SetWeather(bool isRaining, double rainIntensity = 0.7)
    {
        if (!double.IsFinite(rainIntensity) || rainIntensity < 0.0 || rainIntensity > 1.0)
            throw new ArgumentOutOfRangeException(nameof(rainIntensity));

        IsRaining = isRaining;
        RainIntensity = isRaining ? Math.Max(0.1, rainIntensity) : 0.0;
    }

    public WeatherAndTimeSnapshot StepPhysical(
        double physicalDtSeconds,
        Action<double>? onClockAdvanced = null)
    {
        if (!double.IsFinite(physicalDtSeconds) || physicalDtSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(physicalDtSeconds));

        if (!Paused && physicalDtSeconds > 0.0)
        {
            CalendarTimeSeconds += physicalDtSeconds * CalendarSecondsPerPhysicalSecond;
            onClockAdvanced?.Invoke(CalendarTimeSeconds);
        }

        return ExportSnapshot();
    }

    public WeatherAndTimeSnapshot AdvanceCalendarSeconds(
        double deltaCalendarSeconds,
        Action<double>? onClockAdvanced = null)
    {
        if (!double.IsFinite(deltaCalendarSeconds) || deltaCalendarSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(deltaCalendarSeconds));

        if (deltaCalendarSeconds > 0.0)
        {
            CalendarTimeSeconds += deltaCalendarSeconds;
            onClockAdvanced?.Invoke(CalendarTimeSeconds);
        }

        return ExportSnapshot();
    }

    public WeatherAndTimeSnapshot ExportSnapshot()
    {
        var hour = HourOfDay;
        var sunElevation = IsNight
            ? 0.0
            : Math.Clamp(Math.Sin(Math.PI * (hour - 6.0) / 14.0) * (Math.PI * 0.42), 0.05, Math.PI * 0.45);
        var densityMultiplier = IsRushHour ? 1.35 : (IsNight ? 0.45 : 1.0);
        if (IsRaining)
            densityMultiplier *= 0.9;

        return new WeatherAndTimeSnapshot(
            SchemaVersion: 1,
            CalendarTimeSeconds: CalendarTimeSeconds,
            DayIndex: DayIndex,
            HourOfDay: hour,
            IsNight: IsNight,
            IsRushHour: IsRushHour,
            IsWeekend: IsWeekend,
            IsRaining: IsRaining,
            RainIntensity: RainIntensity,
            SunElevationRadians: sunElevation,
            SurfaceGripMultiplier: SurfaceGripMultiplier,
            TrafficDensityMultiplier: densityMultiplier,
            Paused: Paused);
    }

    public void Restore(WeatherAndTimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!double.IsFinite(snapshot.CalendarTimeSeconds) || snapshot.CalendarTimeSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(snapshot));
        if (!double.IsFinite(snapshot.RainIntensity) || snapshot.RainIntensity < 0.0 || snapshot.RainIntensity > 1.0)
            throw new ArgumentOutOfRangeException(nameof(snapshot));

        CalendarTimeSeconds = snapshot.CalendarTimeSeconds;
        IsRaining = snapshot.IsRaining;
        RainIntensity = snapshot.IsRaining ? snapshot.RainIntensity : 0.0;
        Paused = snapshot.Paused;
    }
}

