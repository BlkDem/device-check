using System.Text.Json;
using System.Text.Json.Nodes;
using DeviceCheck.Agent.Core.Model;

namespace DeviceCheck.Agent.Export;

public sealed class ReportValidationException(IReadOnlyList<string> errors)
    : Exception("HardwareReport validation failed:\n" + string.Join("\n", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>Structural validation of a HardwareReport JSON document against the v1.0 contract.</summary>
public static class ReportValidator
{
    private static readonly string[] RequiredTopLevel =
        ["schema_version", "scan", "system", "cpu", "motherboard", "memory", "gpu", "storage", "volumes", "displays", "limitations"];

    private static readonly string[] ArraySections = ["gpu", "storage", "volumes", "displays", "limitations"];
    private static readonly HashSet<string> Confidences = ["exact", "approximate", "detected", "unavailable"];

    public static IReadOnlyList<string> Validate(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException e) { return [$"invalid JSON: {e.Message}"]; }
        if (root is not JsonObject obj) return ["root must be an object"];

        var errors = new List<string>();
        foreach (var k in RequiredTopLevel)
            if (!obj.ContainsKey(k)) errors.Add($"missing top-level key '{k}'");
        foreach (var k in ArraySections)
            if (obj[k] is not null and not JsonArray) errors.Add($"'{k}' must be an array");

        if (obj["schema_version"]?.GetValueKind() != JsonValueKind.String || (string?)obj["schema_version"] != SchemaInfo.Version)
            errors.Add($"schema_version must be \"{SchemaInfo.Version}\"");

        if (obj["scan"] is JsonObject scan)
        {
            if (!Guid.TryParse((string?)scan["id"], out _)) errors.Add("scan.id must be a UUID");
            if (!DateTimeOffset.TryParse((string?)scan["created_at"], out _)) errors.Add("scan.created_at must be ISO-8601");
            if ((string?)scan["source"] is not ("agent" or "browser")) errors.Add("scan.source must be 'agent' or 'browser'");
        }
        else errors.Add("scan must be an object");

        if (obj["memory"] is JsonObject mem && mem["modules"] is not JsonArray) errors.Add("memory.modules must be an array");

        Walk(obj, "$", errors);
        return errors;
    }

    /// <summary>Structural validation of a StorageHealthReport JSON document (v1.0).</summary>
    public static IReadOnlyList<string> ValidateStorageHealth(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException e) { return [$"invalid JSON: {e.Message}"]; }
        if (root is not JsonObject obj) return ["root must be an object"];

        var errors = new List<string>();
        foreach (var k in new[] { "schema_version", "kind", "collected_at", "scan", "storage_health", "limitations" })
            if (!obj.ContainsKey(k)) errors.Add($"missing top-level key '{k}'");
        if ((string?)obj["schema_version"] != Monitoring.Core.StorageHealthSchema.Version) errors.Add($"schema_version must be \"{Monitoring.Core.StorageHealthSchema.Version}\"");
        if ((string?)obj["kind"] != Monitoring.Core.StorageHealthSchema.Kind) errors.Add("kind must be 'storage_health'");
        if (!DateTimeOffset.TryParse((string?)obj["collected_at"], out _)) errors.Add("collected_at must be ISO-8601");
        if (obj["storage_health"] is JsonObject sh)
        {
            foreach (var k in new[] { "devices", "other_volumes", "alerts" })
                if (sh[k] is not JsonArray) errors.Add($"storage_health.{k} must be an array");
            foreach (var d in sh["devices"] as JsonArray ?? [])
                if (d?["id"] is null || d["volumes"] is not JsonArray) errors.Add("every device needs 'id' and 'volumes'");
        }
        else errors.Add("storage_health must be an object");

        Walk(obj, "$", errors);
        return errors;
    }

    /// <summary>Every object that looks like a Field must have value/source/confidence/collected_at and be consistent.</summary>
    private static void Walk(JsonNode node, string path, List<string> errors)
    {
        if (errors.Count > 50) return;
        switch (node)
        {
            case JsonObject o when o.ContainsKey("confidence"):
            {
                foreach (var k in new[] { "value", "source", "collected_at" })
                    if (!o.ContainsKey(k)) errors.Add($"{path}: field missing '{k}'");
                var c = (string?)o["confidence"];
                if (c is null || !Confidences.Contains(c)) errors.Add($"{path}: invalid confidence '{c}'");
                if (c == "unavailable" && o["value"] is not null) errors.Add($"{path}: unavailable field must have value null");
                if (c != "unavailable" && o["value"] is null) errors.Add($"{path}: '{c}' field must have a value");
                if (!DateTimeOffset.TryParse((string?)o["collected_at"], out _)) errors.Add($"{path}: collected_at must be ISO-8601");
                break;
            }
            case JsonObject o:
                foreach (var (k, v) in o) if (v is not null) Walk(v, $"{path}.{k}", errors);
                break;
            case JsonArray a:
                for (int i = 0; i < a.Count; i++) if (a[i] is { } v) Walk(v, $"{path}[{i}]", errors);
                break;
        }
    }
}
