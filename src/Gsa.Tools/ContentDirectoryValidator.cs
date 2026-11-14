using System.Text.Json;
using Gsa.Missions.Content;

namespace Gsa.Tools;

/// <summary>Validates neutral JSON catalogs; never opens an engine or executes mission events.</summary>
public static class ContentDirectoryValidator
{
    public static IReadOnlyList<ContentDiagnostic> Validate(string directory)
    {
        var validation = new Validation(directory);
        validation.Run();
        return validation.Diagnostics;
    }

    private sealed record Document(string Path, JsonElement Json);
    private sealed record Area(string Region, double[][] Polygon);
    private sealed record Surface(string Region, string Area);
    private sealed record Feature(string Region, string Area, string Surface);
    private sealed record Mission(string Id, Document Document, string[] Preconditions, string[] Unlocks, bool Calm, string? CalmVariant);

    private sealed class Validation(string directory)
    {
        public List<ContentDiagnostic> Diagnostics { get; } = [];
        private readonly Dictionary<string, Document> _contracts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Document> _profiles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Document> _regions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Document> _missions = new(StringComparer.Ordinal);
        private readonly HashSet<string> _clients = new(StringComparer.Ordinal);
        private readonly HashSet<string> _capabilities = new(StringComparer.Ordinal);
        private readonly HashSet<string> _providedCapabilities = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _slots = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Area> _areas = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Surface> _surfaces = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Feature> _features = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Mission> _missionData = new(StringComparer.Ordinal);

        public void Run()
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    Add(directory, "$", "Content directory does not exist.");
                    return;
                }
                if (IsLink(directory))
                {
                    Add(directory, "$", "Symbolic links are not followed.");
                    return;
                }
                var catalog = Read(Path.Combine(directory, "catalog.json"));
                if (catalog is not null)
                    ValidateCatalog(catalog);
                Load("contracts", _contracts);
                Load("profiles", _profiles);
                Load("regions", _regions);
                Load("missions", _missions);
                foreach (var profile in _profiles.Values) ValidateProfile(profile);
                foreach (var region in _regions.Values) ValidateRegion(region);
                foreach (var mission in _missions.Values) ValidateMission(mission);
                foreach (var contract in _contracts.Values)
                {
                    Diagnostics.AddRange(ContractValidator.Validate(contract.Json, contract.Path));
                    ValidateContractReferences(contract);
                }
                ValidateMissionReferences();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                Add(directory, "$", $"Cannot read content: {error.Message}");
            }
        }

        private void Load(string name, Dictionary<string, Document> destination)
        {
            var folder = Path.Combine(directory, name);
            if (!Directory.Exists(folder))
            {
                Add(folder, "$", "Required content folder is missing.");
                return;
            }
            var count = 0;
            var pending = new Stack<string>();
            pending.Push(folder);
            while (pending.TryPop(out var current))
            {
                if (IsLink(current))
                {
                    Add(current, "$", "Symbolic links are not followed.");
                    continue;
                }
                foreach (var child in Directory.GetDirectories(current).Order(StringComparer.Ordinal).Reverse())
                    pending.Push(child);
                foreach (var file in Directory.GetFiles(current, "*.json").Order(StringComparer.Ordinal))
                {
                    count++;
                    var document = Read(file);
                    if (document is null) continue;
                    Version(document);
                    var id = String(document, document.Json, "id", "$.id");
                    if (id is not null && !destination.TryAdd(id, document))
                        Add(document, "$.id", $"Duplicate {name} ID '{id}'.");
                }
            }
            if (count == 0) Add(folder, "$", "Content folder must contain at least one JSON document.");
        }

        private Document? Read(string path)
        {
            if (!File.Exists(path))
            {
                Add(path, "$", "Required JSON document is missing.");
                return null;
            }
            if (IsLink(path))
            {
                Add(path, "$", "Symbolic links are not followed.");
                return null;
            }
            try
            {
                using var parsed = JsonDocument.Parse(File.ReadAllBytes(path));
                var result = new Document(path, parsed.RootElement.Clone());
                if (result.Json.ValueKind != JsonValueKind.Object)
                {
                    Add(result, "$", "Document root must be an object.");
                    return null;
                }
                CheckJson(result, result.Json, "$");
                return result;
            }
            catch (JsonException error)
            {
                Add(path, error.Path ?? "$", $"Invalid JSON at line {error.LineNumber}, byte {error.BytePositionInLine}: {error.Message}");
                return null;
            }
        }

        private void CheckJson(Document d, JsonElement value, string field)
        {
            if (value.ValueKind == JsonValueKind.Number && (!value.TryGetDouble(out var n) || !double.IsFinite(n)))
                Add(d, field, "Number must be finite.");
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    if (!names.Add(property.Name)) Add(d, $"{field}.{property.Name}", "Duplicate JSON property.");
                    CheckJson(d, property.Value, $"{field}.{property.Name}");
                }
            }
            if (value.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in value.EnumerateArray()) CheckJson(d, item, $"{field}[{index++}]");
            }
        }

        private void Version(Document d)
        {
            if (!d.Json.TryGetProperty("schema_version", out var value) || !value.TryGetInt32Safe(out var version) || version != 1)
                Add(d, "$.schema_version", "Only schema_version 1 is supported.");
        }

        private void ValidateCatalog(Document d)
        {
            Version(d);
            foreach (var id in Strings(d, d.Json, "clients", "$.clients")) _clients.Add(id);
            foreach (var id in Strings(d, d.Json, "capabilities", "$.capabilities")) _capabilities.Add(id);
            foreach (var (slot, index) in Array(d, d.Json, "mounting_slots", "$.mounting_slots"))
            {
                var prefix = $"$.mounting_slots[{index}]";
                var id = String(d, slot, "id", prefix + ".id");
                var capacity = Integer(d, slot, "capacity", prefix + ".capacity", 1, int.MaxValue);
                if (id is not null && capacity is not null && !_slots.TryAdd(id, (int)capacity.Value))
                    Add(d, prefix + ".id", $"Duplicate mounting slot ID '{id}'.");
            }
        }

        private void ValidateProfile(Document d)
        {
            var initialErrors = Diagnostics.Count;
            var type = String(d, d.Json, "type", "$.type");
            if (type is not null && type is not ("camera" or "gnss" or "lidar" or "imu" or "dmi"))
                Add(d, "$.type", "Unknown sensor type.");
            Integer(d, d.Json, "grade", "$.grade", 0, 6);
            Number(d, d.Json, "rate_hz", "$.rate_hz", true);
            Number(d, d.Json, "range_m", "$.range_m", true);
            Number(d, d.Json, "power_w", "$.power_w", false);
            var sample = Integer(d, d.Json, "storage_bytes_per_sample", "$.storage_bytes_per_sample", 1, long.MaxValue);
            var capacity = Integer(d, d.Json, "buffer_capacity_bytes", "$.buffer_capacity_bytes", 1, long.MaxValue);
            if (sample is not null && capacity < sample)
                Add(d, "$.buffer_capacity_bytes", "Buffer cannot hold one complete sample.");
            NumericObject(d, "resolution", true);
            NumericObject(d, "noise", false);
            var capabilities = Strings(d, d.Json, "capabilities", "$.capabilities");
            foreach (var capability in capabilities)
            {
                if (!_capabilities.Contains(capability)) Add(d, "$.capabilities", $"Unknown capability '{capability}'.");
                var requiredType = capability switch { "camera" => "camera", "gnss_basic" => "gnss", "lidar" => "lidar", _ => null };
                if (requiredType is not null && type != requiredType)
                    Add(d, "$.capabilities", $"Capability '{capability}' requires sensor type '{requiredType}'.");
            }
            var mounting = Object(d, d.Json, "mounting_requirements", "$.mounting_requirements");
            var slot = String(d, mounting, "slot_id", "$.mounting_requirements.slot_id");
            var count = Integer(d, mounting, "count", "$.mounting_requirements.count", 1, int.MaxValue);
            if (slot is not null)
            {
                if (!_slots.TryGetValue(slot, out var available)) Add(d, "$.mounting_requirements.slot_id", $"Unknown mounting slot '{slot}'.");
                else if (count > available) Add(d, "$.mounting_requirements.count", "Required mounting positions exceed slot capacity.");
            }
            if (Diagnostics.Count == initialErrors)
                foreach (var capability in capabilities) _providedCapabilities.Add(capability);
        }

        private void NumericObject(Document d, string name, bool positive)
        {
            var obj = Object(d, d.Json, name, "$." + name);
            if (obj.ValueKind != JsonValueKind.Object) return;
            if (!obj.EnumerateObject().Any()) Add(d, "$." + name, "At least one numeric parameter is required.");
            foreach (var item in obj.EnumerateObject()) Number(d, obj, item.Name, $"$.{name}.{item.Name}", positive);
        }

        private void ValidateRegion(Document d)
        {
            var id = GetString(d.Json, "id");
            if (id is null) return;
            var bounds = Object(d, d.Json, "bounds_enu", "$.bounds_enu");
            var min = Coordinates(d, bounds, "min", "$.bounds_enu.min", 3);
            var max = Coordinates(d, bounds, "max", "$.bounds_enu.max", 3);
            if (min is not null && max is not null && Enumerable.Range(0, 3).Any(i => min[i] >= max[i]))
                Add(d, "$.bounds_enu", "Region bounds must increase on all axes.");
            foreach (var (area, index) in Array(d, d.Json, "areas", "$.areas"))
            {
                var prefix = $"$.areas[{index}]";
                var areaId = String(d, area, "id", prefix + ".id");
                var polygon = Polygon(d, area, prefix + ".polygon_enu");
                if (areaId is not null && polygon is not null && !_areas.TryAdd(areaId, new Area(id, polygon)))
                    Add(d, prefix + ".id", $"Duplicate area ID '{areaId}'.");
                if (polygon is not null && min is not null && max is not null)
                    foreach (var point in polygon)
                        if (point[0] < min[0] || point[0] > max[0] || point[1] < min[1] || point[1] > max[1])
                            Add(d, prefix + ".polygon_enu", "Area vertex lies outside region bounds.");
            }
            foreach (var (surface, index) in Array(d, d.Json, "surfaces", "$.surfaces"))
            {
                var prefix = $"$.surfaces[{index}]";
                var surfaceId = String(d, surface, "id", prefix + ".id");
                var areaId = String(d, surface, "area_id", prefix + ".area_id");
                if (areaId is not null && (!_areas.TryGetValue(areaId, out var area) || area.Region != id))
                    Add(d, prefix + ".area_id", "Surface area must resolve in the same region.");
                if (surfaceId is not null && areaId is not null && !_surfaces.TryAdd(surfaceId, new Surface(id, areaId)))
                    Add(d, prefix + ".id", $"Duplicate surface ID '{surfaceId}'.");
            }
            foreach (var (feature, index) in Array(d, d.Json, "features", "$.features"))
            {
                var prefix = $"$.features[{index}]";
                var featureId = String(d, feature, "id", prefix + ".id");
                var areaId = String(d, feature, "area_id", prefix + ".area_id");
                var surfaceId = String(d, feature, "surface_id", prefix + ".surface_id");
                String(d, feature, "class", prefix + ".class");
                String(d, feature, "material", prefix + ".material");
                Strings(d, feature, "tags", prefix + ".tags", allowEmpty: true);
                Boolean(d, feature, "dynamic", prefix + ".dynamic");
                if (areaId is not null && (!_areas.TryGetValue(areaId, out var area) || area.Region != id))
                    Add(d, prefix + ".area_id", "Feature area must resolve in the same region.");
                if (surfaceId is not null && (!_surfaces.TryGetValue(surfaceId, out var surface) || surface.Region != id || surface.Area != areaId))
                    Add(d, prefix + ".surface_id", "Feature surface must resolve in its area.");
                var position = Coordinates(d, feature, "position_enu", prefix + ".position_enu", 3);
                if (position is not null && min is not null && max is not null && Enumerable.Range(0, 3).Any(i => position[i] < min[i] || position[i] > max[i]))
                    Add(d, prefix + ".position_enu", "Feature position lies outside region bounds.");
                if (featureId is not null && areaId is not null && surfaceId is not null && !_features.TryAdd(featureId, new Feature(id, areaId, surfaceId)))
                    Add(d, prefix + ".id", $"Duplicate feature ID '{featureId}'.");
            }
        }

        private void ValidateContractReferences(Document d)
        {
            var root = d.Json;
            Reference(d, GetString(root, "client_id"), _clients.Contains, "$.client_id", "client");
            var region = GetString(root, "region_id");
            var area = GetString(root, "area_id");
            Reference(d, region, _regions.ContainsKey, "$.region_id", "region");
            AreaReference(d, area, region, "$.area_id");
            foreach (var (feature, index) in StringItems(root, "required_feature_ids"))
                FeatureReference(d, feature, region, area, $"$.required_feature_ids[{index}]");
            foreach (var (capability, index) in StringItems(root, "required_capabilities"))
            {
                Reference(d, capability, _capabilities.Contains, $"$.required_capabilities[{index}]", "capability");
                if (!_providedCapabilities.Contains(capability))
                    Add(d, $"$.required_capabilities[{index}]", $"Capability '{capability}' has no valid profile provider.");
            }
            RequirementSurfaces(d, root, region, area, "$");
            foreach (var (precondition, index) in StringItems(root, "story_prerequisites"))
                Reference(d, precondition, _missions.ContainsKey, $"$.story_prerequisites[{index}]", "mission");
            if (TryProperty(root, "reward", out var reward))
                foreach (var (unlock, index) in StringItems(reward, "story_unlocks"))
                    Reference(d, unlock, _missions.ContainsKey, $"$.reward.story_unlocks[{index}]", "mission");
            if (TryProperty(root, "partial_policy", out var partial) && TryProperty(partial, "deliverables", out var deliverables) && deliverables.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in deliverables.EnumerateArray())
                {
                    var prefix = $"$.partial_policy.deliverables[{index++}]";
                    var subArea = GetString(item, "area_id");
                    AreaReference(d, subArea, region, prefix + ".area_id");
                    foreach (var (feature, i) in StringItems(item, "required_feature_ids"))
                        FeatureReference(d, feature, region, subArea, $"{prefix}.required_feature_ids[{i}]");
                    RequirementSurfaces(d, item, region, subArea, prefix);
                }
            }
        }

        private void AreaReference(Document d, string? area, string? region, string field)
        {
            if (area is not null && (!_areas.TryGetValue(area, out var found) || found.Region != region))
                Add(d, field, $"Area '{area}' does not resolve in contract region '{region}'.");
        }

        private void FeatureReference(Document d, string feature, string? region, string? area, string field)
        {
            if (!_features.TryGetValue(feature, out var found) || found.Region != region || found.Area != area)
                Add(d, field, $"Feature '{feature}' does not resolve in contract region/area.");
        }

        private void RequirementSurfaces(Document d, JsonElement parent, string? region, string? area, string prefix)
        {
            if (!TryProperty(parent, "requirements", out var requirements) || requirements.ValueKind != JsonValueKind.Array) return;
            var index = 0;
            foreach (var item in requirements.EnumerateArray())
            {
                var surface = GetString(item, "surface_id");
                if (surface is not null && (!_surfaces.TryGetValue(surface, out var found) || found.Region != region || found.Area != area))
                    Add(d, $"{prefix}.requirements[{index}].surface_id", $"Surface '{surface}' does not resolve in contract region/area.");
                index++;
            }
        }

        private void ValidateMission(Document d)
        {
            var id = GetString(d.Json, "id");
            if (id is null) return;
            Integer(d, d.Json, "act", "$.act", 1, 3);
            var objectives = Array(d, d.Json, "objectives", "$.objectives");
            var objectiveIds = new HashSet<string>(StringComparer.Ordinal);
            if (objectives.Count == 0) Add(d, "$.objectives", "Mission must have at least one objective.");
            foreach (var (objective, index) in objectives)
            {
                var prefix = $"$.objectives[{index}]";
                var objectiveId = String(d, objective, "id", prefix + ".id");
                if (objectiveId is not null && !objectiveIds.Add(objectiveId)) Add(d, prefix + ".id", "Duplicate objective ID.");
                var contract = String(d, objective, "contract_id", prefix + ".contract_id");
                Reference(d, contract, _contracts.ContainsKey, prefix + ".contract_id", "contract");
                if (contract is not null && _contracts.TryGetValue(contract, out var definition) &&
                    TryProperty(definition.Json, "partial_policy", out var partial) && GetString(partial, "mode") != "disabled")
                    Add(d, prefix + ".contract_id", "Story mission objectives cannot use contracts with partial delivery.");
            }
            Array(d, d.Json, "events", "$.events");
            var preconditions = Strings(d, d.Json, "preconditions", "$.preconditions", allowEmpty: true);
            var unlocks = Strings(d, d.Json, "unlocks", "$.unlocks", allowEmpty: true);
            Strings(d, d.Json, "dialogue_keys", "$.dialogue_keys", allowEmpty: true);
            var calm = Boolean(d, d.Json, "calm", "$.calm") ?? false;
            string? calmVariant = null;
            if (!TryProperty(d.Json, "calm_variant", out var variant)) Add(d, "$.calm_variant", "Required field is missing.");
            else if (variant.ValueKind != JsonValueKind.Null) calmVariant = String(d, d.Json, "calm_variant", "$.calm_variant");
            _missionData[id] = new Mission(id, d, preconditions, unlocks, calm, calmVariant);
        }

        private void ValidateMissionReferences()
        {
            foreach (var mission in _missionData.Values)
            {
                foreach (var id in mission.Preconditions) Reference(mission.Document, id, _missions.ContainsKey, "$.preconditions", "mission");
                foreach (var id in mission.Unlocks) Reference(mission.Document, id, _missions.ContainsKey, "$.unlocks", "mission");
                if (mission.CalmVariant is not null)
                {
                    if (!_missionData.TryGetValue(mission.CalmVariant, out var variant) || !variant.Calm || variant.Id == mission.Id)
                        Add(mission.Document, "$.calm_variant", "Calm variant must resolve to a different calm mission.");
                }
                else if (!mission.Calm) Add(mission.Document, "$.calm_variant", "A non-calm mission requires a calm variant.");
            }
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var done = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mission in _missionData.Values) Visit(mission);
            void Visit(Mission mission)
            {
                if (done.Contains(mission.Id)) return;
                if (!visiting.Add(mission.Id))
                {
                    Add(mission.Document, "$.preconditions", "Mission preconditions contain a cycle.");
                    return;
                }
                foreach (var id in mission.Preconditions)
                    if (_missionData.TryGetValue(id, out var dependency)) Visit(dependency);
                visiting.Remove(mission.Id);
                done.Add(mission.Id);
            }
        }

        private double[][]? Polygon(Document d, JsonElement area, string field)
        {
            var values = Array(d, area, "polygon_enu", field);
            var points = new List<double[]>();
            foreach (var (value, index) in values)
            {
                var point = CoordinateArray(d, value, $"{field}[{index}]", 2);
                if (point is not null) points.Add(point);
            }
            if (points.Count != values.Count) return null;
            if (points.Count > 1 && Same(points[0], points[^1])) points.RemoveAt(points.Count - 1);
            if (points.Count < 3 || points.Distinct(new PointComparer()).Count() < 3)
            {
                Add(d, field, "Polygon needs at least three distinct vertices.");
                return null;
            }
            var signedArea = 0.0;
            var origin = points[0];
            for (var i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                if (Same(a, b)) Add(d, field, "Polygon has a zero-length edge.");
                signedArea += (a[0] - origin[0]) * (b[1] - origin[1]) - (b[0] - origin[0]) * (a[1] - origin[1]);
                for (var j = i + 1; j < points.Count; j++)
                {
                    if (j == i + 1 || (i == 0 && j == points.Count - 1)) continue;
                    if (Intersects(a, b, points[j], points[(j + 1) % points.Count]))
                        Add(d, field, "Polygon has intersecting or overlapping non-adjacent edges.");
                }
            }
            if (!double.IsFinite(signedArea) || Math.Abs(signedArea) <= 2e-10)
                Add(d, field, "Polygon area must exceed 1e-10 square metres.");
            return points.ToArray();
        }

        private static bool Same(double[] a, double[] b) => a[0] == b[0] && a[1] == b[1];
        private sealed class PointComparer : IEqualityComparer<double[]>
        {
            public bool Equals(double[]? a, double[]? b) => a is not null && b is not null && Same(a, b);
            public int GetHashCode(double[] p) => HashCode.Combine(p[0], p[1]);
        }
        private static double Cross(double[] a, double[] b, double[] c) => (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);
        private static bool Intersects(double[] a, double[] b, double[] c, double[] d)
        {
            var ac = Cross(a, b, c); var ad = Cross(a, b, d);
            var ca = Cross(c, d, a); var cb = Cross(c, d, b);
            if (((ac > 0 && ad < 0) || (ac < 0 && ad > 0)) && ((ca > 0 && cb < 0) || (ca < 0 && cb > 0))) return true;
            return (ac == 0 && On(a, b, c)) || (ad == 0 && On(a, b, d)) || (ca == 0 && On(c, d, a)) || (cb == 0 && On(c, d, b));
        }
        private static bool On(double[] a, double[] b, double[] p) => p[0] >= Math.Min(a[0], b[0]) && p[0] <= Math.Max(a[0], b[0]) && p[1] >= Math.Min(a[1], b[1]) && p[1] <= Math.Max(a[1], b[1]);

        private double[]? Coordinates(Document d, JsonElement parent, string name, string field, int dimensions)
        {
            if (!TryProperty(parent, name, out var value))
            {
                Add(d, field, "Required coordinate field is missing.");
                return null;
            }
            return CoordinateArray(d, value, field, dimensions);
        }
        private double[]? CoordinateArray(Document d, JsonElement value, string field, int dimensions)
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != dimensions)
            {
                Add(d, field, $"Expected {dimensions} coordinate numbers.");
                return null;
            }
            var result = new double[dimensions];
            var index = 0;
            foreach (var part in value.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Number || !part.TryGetDouble(out var n) || !double.IsFinite(n))
                {
                    Add(d, $"{field}[{index}]", "Coordinate must be finite.");
                    return null;
                }
                result[index++] = n;
            }
            return result;
        }
        private string? String(Document d, JsonElement parent, string name, string field)
        {
            var value = GetString(parent, name);
            if (string.IsNullOrWhiteSpace(value)) { Add(d, field, "Expected a non-empty string."); return null; }
            return value;
        }
        private static string? GetString(JsonElement parent, string name) => TryProperty(parent, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        private static bool TryProperty(JsonElement parent, string name, out JsonElement value)
        {
            value = default;
            return parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out value);
        }
        private JsonElement Object(Document d, JsonElement parent, string name, string field)
        {
            if (!TryProperty(parent, name, out var value) || value.ValueKind != JsonValueKind.Object) { Add(d, field, "Expected an object."); return default; }
            return value;
        }
        private List<(JsonElement Value, int Index)> Array(Document d, JsonElement parent, string name, string field)
        {
            var result = new List<(JsonElement, int)>();
            if (!TryProperty(parent, name, out var value) || value.ValueKind != JsonValueKind.Array) { Add(d, field, "Expected an array."); return result; }
            var index = 0;
            foreach (var item in value.EnumerateArray()) result.Add((item, index++));
            return result;
        }
        private string[] Strings(Document d, JsonElement parent, string name, string field, bool allowEmpty = false)
        {
            var array = Array(d, parent, name, field);
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (!allowEmpty && array.Count == 0) Add(d, field, "Array must not be empty.");
            foreach (var (value, index) in array)
            {
                if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) Add(d, $"{field}[{index}]", "Expected a non-empty string.");
                else if (!result.Add(value.GetString()!)) Add(d, $"{field}[{index}]", "Duplicate ID.");
            }
            return result.ToArray();
        }
        private static IEnumerable<(string Value, int Index)> StringItems(JsonElement parent, string name)
        {
            if (!TryProperty(parent, name, out var value) || value.ValueKind != JsonValueKind.Array) yield break;
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String) yield return (item.GetString()!, index);
                index++;
            }
        }
        private long? Integer(Document d, JsonElement parent, string name, string field, long min, long max)
        {
            if (!TryProperty(parent, name, out var value) || !value.TryGetInt64Safe(out var n) || n < min || n > max)
            { Add(d, field, $"Expected an integer between {min} and {max}."); return null; }
            return n;
        }
        private double? Number(Document d, JsonElement parent, string name, string field, bool positive)
        {
            if (!TryProperty(parent, name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var n) || !double.IsFinite(n) || (positive ? n <= 0 : n < 0))
            { Add(d, field, positive ? "Expected a finite positive number." : "Expected a finite non-negative number."); return null; }
            return n;
        }
        private bool? Boolean(Document d, JsonElement parent, string name, string field)
        {
            if (!TryProperty(parent, name, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            { Add(d, field, "Expected a boolean."); return null; }
            return value.GetBoolean();
        }
        private void Reference(Document d, string? id, Func<string, bool> exists, string field, string kind)
        {
            if (id is not null && !exists(id)) Add(d, field, $"Unknown {kind} ID '{id}'.");
        }
        private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        private void Add(Document d, string field, string reason) => Add(d.Path, field, reason);
        private void Add(string path, string field, string reason) => Diagnostics.Add(new ContentDiagnostic(path, field, reason));
    }
}

internal static class JsonElementExtensions
{
    internal static bool TryGetInt32Safe(this JsonElement element, out int value)
    {
        value = default;
        if (!element.TryGetInt64Safe(out var integer) || integer < int.MinValue || integer > int.MaxValue)
            return false;
        value = (int)integer;
        return true;
    }

    internal static bool TryGetInt64Safe(this JsonElement element, out long value)
        => JsonNumbers.TryGetInt64(element, out value);
}
