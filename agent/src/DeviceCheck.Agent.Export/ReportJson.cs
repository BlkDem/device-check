using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using DeviceCheck.Agent.Core.Model;

namespace DeviceCheck.Agent.Export;

/// <summary>Serialization of HardwareReport in the web contract format (snake_case, lowercase enums, UTF-8).</summary>
public static class ReportJson
{
    public static readonly JsonSerializerOptions Options = Create(indented: true);
    public static readonly JsonSerializerOptions CompactOptions = Create(indented: false);

    private static JsonSerializerOptions Create(bool indented) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = indented,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        // Keep Cyrillic and other text readable; output is still valid UTF-8 JSON.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        NumberHandling = JsonNumberHandling.Strict,
    };

    /// <summary>Serializes, sanitizes (removes personal identifiers) and returns the JSON text.</summary>
    public static string Serialize(HardwareReport report, bool indented = true)
    {
        var node = JsonSerializer.SerializeToNode(report, indented ? Options : CompactOptions)!;
        ReportSanitizer.Sanitize(node);
        return node.ToJsonString(indented ? Options : CompactOptions);
    }

    public static HardwareReport? Deserialize(string json) => JsonSerializer.Deserialize<HardwareReport>(json, Options);

    /// <summary>StorageHealthReport → JSON, with the same identifier sanitizing as the hardware report.</summary>
    public static string Serialize(Monitoring.Core.StorageHealthReport report, bool indented = true)
    {
        var node = JsonSerializer.SerializeToNode(report, indented ? Options : CompactOptions)!;
        ReportSanitizer.Sanitize(node);
        return node.ToJsonString(indented ? Options : CompactOptions);
    }

    public static Monitoring.Core.StorageHealthReport? DeserializeStorageHealth(string json) =>
        JsonSerializer.Deserialize<Monitoring.Core.StorageHealthReport>(json, Options);

    /// <summary>device-check-storage-health-YYYY-MM-DD-HH-mm.json (local time).</summary>
    public static string FileName(Monitoring.Core.StorageHealthReport r) =>
        $"device-check-storage-health-{r.CollectedAt.ToLocalTime().ToString("yyyy-MM-dd-HH-mm", CultureInfo.InvariantCulture)}.json";

    /// <summary>Validates, then writes UTF-8 (no BOM). Throws <see cref="ReportValidationException"/> if invalid.</summary>
    public static async Task<string> WriteAsync(Monitoring.Core.StorageHealthReport report, string path, CancellationToken ct = default)
    {
        var json = Serialize(report);
        var errors = ReportValidator.ValidateStorageHealth(json);
        if (errors.Count > 0) throw new ReportValidationException(errors);
        if (Directory.Exists(path)) path = Path.Combine(path, FileName(report));
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false), ct).ConfigureAwait(false);
        return Path.GetFullPath(path);
    }

    /// <summary>device-check-report-YYYY-MM-DD-HH-mm.json (local time of the scan).</summary>
    public static string FileName(HardwareReport r) =>
        $"device-check-report-{r.Scan.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd-HH-mm", CultureInfo.InvariantCulture)}.json";

    /// <summary>Validates, then writes UTF-8 (no BOM). Throws <see cref="ReportValidationException"/> if invalid.</summary>
    public static async Task<string> WriteAsync(HardwareReport report, string path, CancellationToken ct = default)
    {
        var json = Serialize(report);
        var errors = ReportValidator.Validate(json);
        if (errors.Count > 0) throw new ReportValidationException(errors);
        if (Directory.Exists(path)) path = Path.Combine(path, FileName(report));
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false), ct).ConfigureAwait(false);
        return Path.GetFullPath(path);
    }
}

/// <summary>
/// Removes identifiers that must never be exported without separate consent, whatever a collector put there.
/// Any key in <see cref="ForbiddenKeys"/> is replaced with an unavailable field (reason "privacy").
/// </summary>
public static class ReportSanitizer
{
    public static readonly IReadOnlySet<string> ForbiddenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "serial", "serial_number", "hostname", "computer_name", "processor_id", "mac", "mac_address", "uuid", "unique_id", "asset_tag", "user_name",
    };

    public static void Sanitize(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (ForbiddenKeys.Contains(key))
                    {
                        var collectedAt = (obj[key] as JsonObject)?["collected_at"]?.DeepClone() ?? JsonValue.Create(DateTimeOffset.UtcNow);
                        obj[key] = new JsonObject
                        {
                            ["value"] = null,
                            ["source"] = "—",
                            ["confidence"] = "unavailable",
                            ["collected_at"] = collectedAt,
                            ["reason"] = "privacy",
                        };
                    }
                    else if (obj[key] is { } child) Sanitize(child);
                }
                break;
            case JsonArray arr:
                foreach (var item in arr) if (item is not null) Sanitize(item);
                break;
        }
    }
}
