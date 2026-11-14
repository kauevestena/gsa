namespace Gsa.Core.Sensors.Camera;

public enum ProjectionStatus
{
    Uninitialized,
    Visible,
    BehindCamera,
    OutsideImage,
    NonFiniteProjection,
    TrajectoryUnavailable,
}

/// <summary>An image coordinate, in pixels; it carries no depth.</summary>
public readonly record struct PixelPoint
{
    public double U { get; }
    public double V { get; }

    public PixelPoint(double u, double v)
    {
        if (!double.IsFinite(u) || !double.IsFinite(v))
            throw new ArgumentOutOfRangeException(nameof(u), "Pixel coordinates must be finite.");
        (U, V) = (u, v);
    }
}

/// <summary>Only a measured pixel and validity leave the geometric camera model.</summary>
public readonly record struct ProjectionResult
{
    public ProjectionStatus Status { get; }
    public PixelPoint? Pixel { get; }
    public bool IsVisible => Status == ProjectionStatus.Visible && Pixel.HasValue;

    private ProjectionResult(ProjectionStatus status, PixelPoint? pixel) => (Status, Pixel) = (status, pixel);

    internal static ProjectionResult Success(PixelPoint pixel) => new(ProjectionStatus.Visible, pixel);
    internal static ProjectionResult Failure(ProjectionStatus status) => new(status, null);
}
