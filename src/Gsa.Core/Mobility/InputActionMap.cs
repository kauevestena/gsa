using System.Text.Json;
using Gsa.Core.Serialization;

namespace Gsa.Core.Mobility;

public enum GameInputAction
{
    Accelerate,
    Brake,
    SteerLeft,
    SteerRight,
    Interact,
    Capture,
    Map,
    Horn,
    Pause,
}

public sealed record InputActionBinding
{
    public string Keyboard { get; }
    public string Gamepad { get; }

    public InputActionBinding(string keyboard, string gamepad)
    {
        if (string.IsNullOrWhiteSpace(keyboard))
            throw new ArgumentException("Keyboard binding must be a nonempty string.", nameof(keyboard));
        if (string.IsNullOrWhiteSpace(gamepad))
            throw new ArgumentException("Gamepad binding must be a nonempty string.", nameof(gamepad));
        Keyboard = keyboard.Trim();
        Gamepad = gamepad.Trim();
    }
}

/// <summary>Remappable keyboard/mouse and gamepad bindings persisted as schema_version 1 JSON.</summary>
public sealed class InputActionMap
{
    private static readonly (GameInputAction Action, string Key)[] ActionNames =
    [
        (GameInputAction.Accelerate, "accelerate"),
        (GameInputAction.Brake, "brake"),
        (GameInputAction.SteerLeft, "steer_left"),
        (GameInputAction.SteerRight, "steer_right"),
        (GameInputAction.Interact, "interact"),
        (GameInputAction.Capture, "capture"),
        (GameInputAction.Map, "map"),
        (GameInputAction.Horn, "horn"),
        (GameInputAction.Pause, "pause"),
    ];

    private static readonly Dictionary<GameInputAction, string> ToWire = ActionNames.ToDictionary(x => x.Action, x => x.Key);
    private static readonly Dictionary<string, GameInputAction> FromWire =
        ActionNames.ToDictionary(x => x.Key, x => x.Action, StringComparer.Ordinal);

    private readonly Dictionary<GameInputAction, InputActionBinding> _bindings;

    private InputActionMap(Dictionary<GameInputAction, InputActionBinding> bindings)
    {
        ValidateBindings(bindings);
        _bindings = new Dictionary<GameInputAction, InputActionBinding>(bindings);
    }

    public int SchemaVersion => 1;

    public static InputActionMap CreateDefault() => new(new Dictionary<GameInputAction, InputActionBinding>
    {
        [GameInputAction.Accelerate] = new("W", "gamepad_rt"),
        [GameInputAction.Brake] = new("S", "gamepad_lt"),
        [GameInputAction.SteerLeft] = new("A", "gamepad_ls_left"),
        [GameInputAction.SteerRight] = new("D", "gamepad_ls_right"),
        [GameInputAction.Interact] = new("E", "gamepad_a"),
        [GameInputAction.Capture] = new("Space", "gamepad_x"),
        [GameInputAction.Map] = new("M", "gamepad_back"),
        [GameInputAction.Horn] = new("H", "gamepad_b"),
        [GameInputAction.Pause] = new("Escape", "gamepad_start"),
    });

    public static string WireName(GameInputAction action) => ToWire[action];

    public InputActionBinding GetBinding(GameInputAction action) => _bindings[action];

    public IReadOnlyDictionary<string, InputActionBinding> GetAllByWireName() =>
        ActionNames.ToDictionary(pair => pair.Key, pair => _bindings[pair.Action], StringComparer.Ordinal);

    public InputActionMap RemapKeyboard(GameInputAction action, string control)
    {
        var updated = new Dictionary<GameInputAction, InputActionBinding>(_bindings)
        {
            [action] = new InputActionBinding(control, _bindings[action].Gamepad),
        };
        return new InputActionMap(updated);
    }

    public InputActionMap RemapGamepad(GameInputAction action, string control)
    {
        var updated = new Dictionary<GameInputAction, InputActionBinding>(_bindings)
        {
            [action] = new InputActionBinding(_bindings[action].Keyboard, control),
        };
        return new InputActionMap(updated);
    }

    public LocomotionInput EvaluateLocomotion(IReadOnlySet<string> activeControls)
    {
        ArgumentNullException.ThrowIfNull(activeControls);
        bool IsActive(GameInputAction action)
        {
            var binding = _bindings[action];
            return activeControls.Contains(binding.Keyboard) || activeControls.Contains(binding.Gamepad);
        }

        var throttle = IsActive(GameInputAction.Accelerate) ? 1.0 : 0.0;
        var brake = IsActive(GameInputAction.Brake) ? 1.0 : 0.0;
        var steer = (IsActive(GameInputAction.SteerLeft) ? 1.0 : 0.0) -
                    (IsActive(GameInputAction.SteerRight) ? 1.0 : 0.0);
        return new LocomotionInput(throttle, brake, steer, IsActive(GameInputAction.Interact));
    }

    public string ToJson()
    {
        var actions = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var (action, name) in ActionNames)
        {
            var binding = _bindings[action];
            actions[name] = new
            {
                keyboard = binding.Keyboard,
                gamepad = binding.Gamepad,
            };
        }
        return JsonSerializer.Serialize(new { schema_version = SchemaVersion, actions },
            new JsonSerializerOptions { WriteIndented = true });
    }

    public static InputActionMap FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("$: Expected JSON object.");
        CheckAllowedFields(root, "$", "schema_version", "actions");
        if (!JsonNumbers.TryGetInt64(root.GetProperty("schema_version"), out var version) || version != 1)
            throw new FormatException("$.schema_version: Only schema_version 1 is supported.");

        var actionsNode = root.GetProperty("actions");
        if (actionsNode.ValueKind != JsonValueKind.Object)
            throw new FormatException("$.actions: Expected object.");

        var parsed = new Dictionary<GameInputAction, InputActionBinding>();
        foreach (var prop in actionsNode.EnumerateObject())
        {
            if (!FromWire.TryGetValue(prop.Name, out var action))
                throw new FormatException($"$.actions.{prop.Name}: Unknown input action.");
            if (parsed.ContainsKey(action))
                throw new FormatException($"$.actions.{prop.Name}: Duplicate action.");
            var field = $"$.actions.{prop.Name}";
            CheckAllowedFields(prop.Value, field, "keyboard", "gamepad");
            var keyboard = ReadNonEmptyText(prop.Value, "keyboard", field);
            var gamepad = ReadNonEmptyText(prop.Value, "gamepad", field);
            parsed[action] = new InputActionBinding(keyboard, gamepad);
        }

        foreach (var (action, wire) in ActionNames)
        {
            if (!parsed.ContainsKey(action))
                throw new FormatException($"$.actions.{wire}: Required input action is missing.");
        }

        try
        {
            return new InputActionMap(parsed);
        }
        catch (ArgumentException exception)
        {
            throw new FormatException($"$.actions: {exception.Message}", exception);
        }
    }

    private static void ValidateBindings(IReadOnlyDictionary<GameInputAction, InputActionBinding> bindings)
    {
        var seenKeyboard = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenGamepad = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (action, wire) in ActionNames)
        {
            if (!bindings.TryGetValue(action, out var binding))
                throw new ArgumentException($"Missing action '{wire}'.");
            if (!seenKeyboard.Add(binding.Keyboard))
                throw new ArgumentException($"Duplicate keyboard binding '{binding.Keyboard}'.");
            if (!seenGamepad.Add(binding.Gamepad))
                throw new ArgumentException($"Duplicate gamepad binding '{binding.Gamepad}'.");
        }
    }

    private static void CheckAllowedFields(JsonElement obj, string field, params string[] allowed)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            throw new FormatException($"{field}: Expected object.");
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in obj.EnumerateObject())
        {
            if (!present.Add(property.Name))
                throw new FormatException($"{field}.{property.Name}: Duplicate property.");
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
                throw new FormatException($"{field}.{property.Name}: Unknown property.");
        }
        foreach (var required in allowed)
        {
            if (!present.Contains(required))
                throw new FormatException($"{field}.{required}: Required property is absent.");
        }
    }

    private static string ReadNonEmptyText(JsonElement obj, string property, string field)
    {
        var element = obj.GetProperty(property);
        if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
            throw new FormatException($"{field}.{property}: Expected nonempty string.");
        return element.GetString()!;
    }
}

