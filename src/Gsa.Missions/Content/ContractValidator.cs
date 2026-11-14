using System.Text.Json;

namespace Gsa.Missions.Content;

/// <summary>
/// Validates contract structure and cross-field constraints, without consulting world truth.
/// Catalog references are resolved by the content tool after this validation succeeds.
/// </summary>
public static class ContractValidator
{
    private static readonly string[] Products = ["images", "feature_inventory", "point_cloud", "pavement_profile"];
    private static readonly Dictionary<string, Metric> Metrics = new(StringComparer.Ordinal)
    {
        ["area_coverage"] = new("threshold", "ratio", "min", Products, true),
        ["feature_completeness"] = new("threshold", "ratio", "min", ["feature_inventory"]),
        ["valid_image_fraction"] = new("threshold", "ratio", "min", ["images", "feature_inventory", "pavement_profile"]),
        ["absolute_rmse"] = new("threshold", "m", "max", Products),
        ["relative_rmse"] = new("threshold", "m", "max", ["feature_inventory", "point_cloud", "pavement_profile"]),
        ["density_coverage"] = new("threshold", "ratio", "min", ["point_cloud"], true),
        ["all_required_features"] = new("predicate", null, null, ["images", "feature_inventory"]),
        ["privacy_redacted"] = new("predicate", null, null, Products),
        ["format_valid"] = new("predicate", null, null, Products),
    };

    private sealed record Metric(string Kind, string? Unit, string? Direction, string[] Products, bool Spatial = false);

    public static IReadOnlyList<ContentDiagnostic> Validate(JsonElement contract, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var validator = new Validator(path);
        validator.Contract(contract);
        return validator.Diagnostics.AsReadOnly();
    }

    private sealed class Validator(string path)
    {
        public List<ContentDiagnostic> Diagnostics { get; } = [];

        private void Error(string field, string reason) => Diagnostics.Add(new(path, field, reason));

        private bool Object(JsonElement value, string field, params string[] properties)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                Error(field, "Expected an object.");
                return false;
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    Error(field + "." + property.Name, "Duplicate property is ambiguous.");
                if (!properties.Contains(property.Name, StringComparer.Ordinal))
                    Error(field + "." + property.Name, "Unknown field for schema version 1.");
            }
            return true;
        }

        private JsonElement Get(JsonElement value, string property) =>
            value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var result) ? result : default;

        private string? Text(JsonElement value, string field)
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            {
                Error(field, "Expected a non-empty string.");
                return null;
            }
            return value.GetString();
        }

        private string? Text(JsonElement parent, string property, string field) => Text(Get(parent, property), field + "." + property);

        private double? Number(JsonElement value, string field, double minimum = 0, double maximum = double.MaxValue)
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number))
            {
                Error(field, "Expected a finite number.");
                return null;
            }
            if (number < minimum || number > maximum)
                Error(field, $"Expected a value between {minimum:G} and {maximum:G}.");
            return number;
        }

        private double? Number(JsonElement parent, string property, string field, double minimum = 0, double maximum = double.MaxValue) =>
            Number(Get(parent, property), field + "." + property, minimum, maximum);

        private static bool TryInteger(JsonElement value, long minimum, long maximum, out long result)
        {
            return JsonNumbers.TryGetInt64(value, out result) && result >= minimum && result <= maximum;
        }

        private void Integer(JsonElement parent, string property, string field, long minimum)
        {
            var value = Get(parent, property);
            if (!TryInteger(value, minimum, long.MaxValue, out _))
                Error(field + "." + property, $"Expected an integer greater than or equal to {minimum}.");
        }

        private void Boolean(JsonElement parent, string property, string field)
        {
            if (Get(parent, property).ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                Error(field + "." + property, "Expected a boolean.");
        }

        private string? Enum(JsonElement parent, string property, string field, params string[] allowed)
        {
            var text = Text(parent, property, field);
            if (text is not null && !allowed.Contains(text, StringComparer.Ordinal))
                Error(field + "." + property, "Unknown value; expected one of: " + string.Join(", ", allowed) + ".");
            return text;
        }

        private HashSet<string> Ids(JsonElement value, string field, bool nonEmpty = false)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (value.ValueKind != JsonValueKind.Array)
            {
                Error(field, "Expected an array of unique IDs.");
                return ids;
            }
            if (nonEmpty && value.GetArrayLength() == 0)
                Error(field, "At least one ID is required.");
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                var itemField = $"{field}[{index++}]";
                var id = Text(item, itemField);
                if (id is not null && !ids.Add(id))
                    Error(itemField, "Duplicate ID.");
            }
            return ids;
        }

        public void Contract(JsonElement value)
        {
            if (!Object(value, "$", "schema_version", "id", "title_key", "client_id", "region_id", "area_id", "product",
                        "required_feature_ids", "requirements", "required_capabilities", "risk", "calm", "deadline", "reward",
                        "partial_policy", "delivery_format", "story_prerequisites"))
                return;
            var version = Get(value, "schema_version");
            if (!TryInteger(version, int.MinValue, int.MaxValue, out var schemaVersion) || schemaVersion != 1)
                Error("$.schema_version", "Only schema_version 1 is supported.");
            foreach (var property in new[] { "id", "title_key", "client_id", "region_id", "area_id" })
                Text(value, property, "$");
            var product = Enum(value, "product", "$", Products);
            var features = Ids(Get(value, "required_feature_ids"), "$.required_feature_ids");
            Requirements(Get(value, "requirements"), "$.requirements", product);
            var capabilities = Ids(Get(value, "required_capabilities"), "$.required_capabilities", true);
            var acquisition = product is "images" or "feature_inventory" ? "camera" :
                product is "point_cloud" or "pavement_profile" ? "lidar" : null;
            if (acquisition is not null && !capabilities.Contains(acquisition))
                Error("$.required_capabilities", $"Product {product} requires capability {acquisition}.");
            if (product == "pavement_profile" && !capabilities.Contains("camera"))
                Error("$.required_capabilities", "Pavement profiles require camera and lidar acquisition.");
            Enum(value, "risk", "$", "easy", "moderate", "hard", "dangerous");
            Boolean(value, "calm", "$");
            Enum(value, "delivery_format", "$", "gsa_bundle_v1");
            Ids(Get(value, "story_prerequisites"), "$.story_prerequisites");
            Deadline(Get(value, "deadline"));
            Reward(Get(value, "reward"));
            Partial(Get(value, "partial_policy"), product, features);
        }

        private void Deadline(JsonElement value)
        {
            const string field = "$.deadline";
            if (!Object(value, field, "due_after_s", "expires_after_s", "late_penalty_per_hour", "late_penalty_cap"))
                return;
            var due = Number(value, "due_after_s", field);
            var expires = Number(value, "expires_after_s", field);
            if (due is <= 0)
                Error(field + ".due_after_s", "Recommended deadline must be positive.");
            if (due.HasValue && expires.HasValue && expires <= due)
                Error(field + ".expires_after_s", "Final deadline must be strictly after the recommended deadline.");
            Number(value, "late_penalty_per_hour", field, 0, 1);
            Number(value, "late_penalty_cap", field, 0, 1);
        }

        private void Reward(JsonElement value)
        {
            const string field = "$.reward";
            if (!Object(value, field, "base_coords", "base_xp", "reputation", "story_unlocks"))
                return;
            Integer(value, "base_coords", field, 0);
            Integer(value, "base_xp", field, 0);
            Integer(value, "reputation", field, 0);
            Ids(Get(value, "story_unlocks"), field + ".story_unlocks");
        }

        private void Requirements(JsonElement value, string field, string? product)
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
            {
                Error(field, "Expected at least one requirement.");
                return;
            }
            var metrics = new HashSet<string>(StringComparer.Ordinal);
            var index = 0;
            foreach (var requirement in value.EnumerateArray())
            {
                var itemField = $"{field}[{index++}]";
                if (requirement.ValueKind != JsonValueKind.Object)
                {
                    Error(itemField, "Expected a requirement object.");
                    continue;
                }
                var kind = Enum(requirement, "kind", itemField, "threshold", "predicate");
                var metricName = Text(requirement, "metric", itemField);
                if (metricName is null)
                    continue;
                if (!metrics.Add(metricName))
                    Error(itemField + ".metric", "A metric may appear only once in each requirement set.");
                if (!Metrics.TryGetValue(metricName, out var metric))
                {
                    Error(itemField + ".metric", "Unknown metric.");
                    continue;
                }
                if (kind != metric.Kind)
                    Error(itemField + ".kind", $"Metric {metricName} requires kind {metric.Kind}.");
                if (product is not null && !metric.Products.Contains(product, StringComparer.Ordinal))
                    Error(itemField + ".metric", $"Metric {metricName} is incompatible with product {product}.");
                if (kind == "predicate")
                {
                    Object(requirement, itemField, "kind", "metric", "expected");
                    if (Get(requirement, "expected").ValueKind != JsonValueKind.True)
                        Error(itemField + ".expected", "Required predicates must expect true; global privacy/format invariants cannot be disabled.");
                }
                else if (kind == "threshold")
                {
                    var fields = new List<string> { "kind", "metric", "unit", "direction", "limit", "excellent" };
                    if (metric.Spatial)
                        fields.AddRange(["surface_id", "cell_size_m"]);
                    if (metricName == "density_coverage")
                        fields.Add("target_density_points_per_m2");
                    Object(requirement, itemField, fields.ToArray());
                    var unit = Text(requirement, "unit", itemField);
                    if (unit is not null && unit != metric.Unit)
                        Error(itemField + ".unit", $"Metric {metricName} uses unit {metric.Unit}.");
                    var direction = Enum(requirement, "direction", itemField, "min", "max");
                    if (direction is not null && direction != metric.Direction)
                        Error(itemField + ".direction", $"Metric {metricName} requires direction {metric.Direction}.");
                    var limit = Number(requirement, "limit", itemField, 0, metric.Unit == "ratio" ? 1 : double.MaxValue);
                    var excellent = Number(requirement, "excellent", itemField, 0, metric.Unit == "ratio" ? 1 : double.MaxValue);
                    if (limit.HasValue && excellent.HasValue &&
                        (direction == "min" && excellent <= limit || direction == "max" && excellent >= limit))
                        Error(itemField + ".excellent", "Excellence must be strictly better than the passing limit.");
                    if (metric.Spatial)
                    {
                        Text(requirement, "surface_id", itemField);
                        if (Number(requirement, "cell_size_m", itemField) is <= 0)
                            Error(itemField + ".cell_size_m", "Cell size must be positive.");
                    }
                    if (metricName == "density_coverage" && Number(requirement, "target_density_points_per_m2", itemField) is <= 0)
                        Error(itemField + ".target_density_points_per_m2", "Target density must be positive.");
                }
            }
        }

        private void Partial(JsonElement value, string? product, HashSet<string> contractFeatures)
        {
            const string field = "$.partial_policy";
            if (value.ValueKind != JsonValueKind.Object)
            {
                Error(field, "Expected a partial policy object.");
                return;
            }
            var mode = Enum(value, "mode", field, "disabled", "deliverables");
            Object(value, field, mode == "disabled" ? ["mode"] : ["mode", "deliverables"]);
            if (mode != "deliverables")
                return;
            var deliverables = Get(value, "deliverables");
            if (deliverables.ValueKind != JsonValueKind.Array || deliverables.GetArrayLength() == 0)
            {
                Error(field + ".deliverables", "At least one independent deliverable is required.");
                return;
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var features = new HashSet<string>(StringComparer.Ordinal);
            double totalWeight = 0;
            var index = 0;
            foreach (var deliverable in deliverables.EnumerateArray())
            {
                var itemField = $"{field}.deliverables[{index++}]";
                if (!Object(deliverable, itemField, "id", "weight", "area_id", "required_feature_ids", "requirements"))
                    continue;
                var id = Text(deliverable, "id", itemField);
                if (id is not null && !ids.Add(id))
                    Error(itemField + ".id", "A deliverable cannot be paid twice.");
                Text(deliverable, "area_id", itemField);
                var weight = Number(deliverable, "weight", itemField, 0, 1);
                if (weight is <= 0)
                    Error(itemField + ".weight", "Deliverable weights must be positive.");
                totalWeight += weight ?? 0;
                foreach (var feature in Ids(Get(deliverable, "required_feature_ids"), itemField + ".required_feature_ids"))
                {
                    if (!features.Add(feature))
                        Error(itemField + ".required_feature_ids", "A feature cannot be charged in more than one deliverable.");
                    if (!contractFeatures.Contains(feature))
                        Error(itemField + ".required_feature_ids", $"Feature {feature} is outside the contract requirements.");
                }
                Requirements(Get(deliverable, "requirements"), itemField + ".requirements", product);
            }
            if (Math.Abs(totalWeight - 1) > 1e-9)
                Error(field + ".deliverables", "Deliverable weights must sum to 1 (tolerance 1e-9).");
        }
    }
}
