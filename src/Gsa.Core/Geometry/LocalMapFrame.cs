namespace Gsa.Core.Geometry;

/// <summary>
/// Converts absolute ENU metres to local east/up/south metres in double precision.
/// The engine adapter alone converts this neutral local vector to engine numeric types.
/// </summary>
public sealed class LocalMapFrame
{
    public const string DatumId = "SAO_DATUM_LOCAL_V1";
    public static LocalMapFrame Default { get; } = new(new Vector3d(500000, 7400000, 100));

    public Vector3d Origin { get; }

    public LocalMapFrame(Vector3d origin) => Origin = origin;

    public Vector3d ToLocal(Vector3d mapPosition)
    {
        var enu = mapPosition - Origin;
        return new(enu.X, enu.Z, -enu.Y);
    }

    public Vector3d ToMap(Vector3d localPosition) =>
        Origin + new Vector3d(localPosition.X, -localPosition.Z, localPosition.Y);
}
