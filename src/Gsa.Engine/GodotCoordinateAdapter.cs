using Gsa.Core.Geometry;
using Godot;

namespace Gsa.Engine;

/// <summary>The only boundary between double map coordinates and Godot floats.</summary>
public sealed class GodotCoordinateAdapter
{
    private readonly LocalMapFrame _frame;

    public GodotCoordinateAdapter(LocalMapFrame? frame = null)
    {
        _frame = frame ?? LocalMapFrame.Default;
    }

    public Vector3 ToGodot(Vector3d enu)
    {
        Vector3d local = _frame.ToLocal(enu);
        var result = new Vector3((float)local.X, (float)local.Y, (float)local.Z);
        if (!result.IsFinite())
            throw new ArgumentOutOfRangeException(nameof(enu), "Local position exceeds float range.");
        return result;
    }

    public Vector3d ToMap(Vector3 local)
    {
        if (!local.IsFinite())
            throw new ArgumentOutOfRangeException(nameof(local), "Position must be finite.");
        return _frame.ToMap(new Vector3d(local.X, local.Y, local.Z));
    }
}
