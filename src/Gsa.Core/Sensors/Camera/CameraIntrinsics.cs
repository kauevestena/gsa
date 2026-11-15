namespace Gsa.Core.Sensors.Camera;

/// <summary>Finite pinhole intrinsics in pixels and normalized radial distortion.</summary>
public sealed class CameraIntrinsics
{
    public int Width { get; }
    public int Height { get; }
    public double Fx { get; }
    public double Fy { get; }
    public double Cx { get; }
    public double Cy { get; }
    public double K1 { get; }
    public double K2 { get; }

    public CameraIntrinsics(int width, int height, double fx, double fy,
                            double cx, double cy, double k1 = 0, double k2 = 0)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be positive.");
        if (!double.IsFinite(fx) || !double.IsFinite(fy) || fx <= 0 || fy <= 0)
            throw new ArgumentOutOfRangeException(nameof(fx), "Focal lengths must be finite and positive.");
        if (!double.IsFinite(cx) || !double.IsFinite(cy) || !double.IsFinite(k1) || !double.IsFinite(k2))
            throw new ArgumentOutOfRangeException(nameof(cx), "Principal point and distortion must be finite.");
        (Width, Height, Fx, Fy, Cx, Cy, K1, K2) = (width, height, fx, fy, cx, cy, k1, k2);
    }

    public static CameraIntrinsics Initial { get; } = FromHorizontalFieldOfView(1280, 720, Math.PI / 2);

    public static CameraIntrinsics FromHorizontalFieldOfView(int width, int height, double radians)
    {
        if (!double.IsFinite(radians) || radians <= 0 || radians >= Math.PI)
            throw new ArgumentOutOfRangeException(nameof(radians), "Horizontal field of view must be between zero and pi.");
        var focal = (width / 2.0) / Math.Tan(radians / 2);
        return new(width, height, focal, focal, width / 2.0, height / 2.0);
    }
}
