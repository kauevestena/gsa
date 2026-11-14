using Gsa.Core.Geometry;
using Gsa.Missions.Content;
using Godot;
using Dictionary = Godot.Collections.Dictionary;

namespace Gsa.Engine;

/// <summary>Engine-safe values for GDScript; domain objects remain private.</summary>
public partial class GameFacade : Node
{
    private readonly GodotCoordinateAdapter _coordinates = new();

    public Dictionary GetStatus() => new()
    {
        ["schema_version"] = 1,
        ["datum_id"] = LocalMapFrame.DatumId,
        ["physics_ticks_per_second"] = 60,
        ["missions_module"] = typeof(ContractValidator).Assembly.GetName().Name ?? "",
    };

    public Dictionary MapToLocal(double east, double north, double up)
    {
        try
        {
            return new Dictionary
            {
                ["status"] = "accepted",
                ["position"] = _coordinates.ToGodot(new Vector3d(east, north, up)),
            };
        }
        catch (ArgumentException exception)
        {
            return InvalidInput(exception.Message);
        }
    }

    public Dictionary LocalToMap(Vector3 position)
    {
        try
        {
            Vector3d enu = _coordinates.ToMap(position);
            return new Dictionary
            {
                ["status"] = "accepted",
                ["east"] = enu.X,
                ["north"] = enu.Y,
                ["up"] = enu.Z,
            };
        }
        catch (ArgumentException exception)
        {
            return InvalidInput(exception.Message);
        }
    }

    private static Dictionary InvalidInput(string reason) => new()
    {
        ["status"] = "invalid_input",
        ["reason"] = reason,
    };
}
