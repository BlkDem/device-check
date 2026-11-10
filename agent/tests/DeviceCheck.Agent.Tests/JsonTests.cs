using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Export;
using DeviceCheck.Agent.Tests.Fixtures;

namespace DeviceCheck.Agent.Tests;

public class JsonTests
{
    private static Task<HardwareReport> Report() => Machines.ScanAsync(Machines.AmdLaptop());

    [Fact]
    public async Task Collected_report_is_valid()
    {
        var json = ReportJson.Serialize(await Report());
        Assert.Empty(ReportValidator.Validate(json));
    }

    [Fact]
    public async Task Format_is_snake_case_with_lowercase_confidence()
    {
        var root = JsonNode.Parse(ReportJson.Serialize(await Report()))!;

        Assert.Equal("1.0", (string?)root["schema_version"]);
        Assert.Equal("agent", (string?)root["scan"]!["source"]);
        var cores = root["cpu"]!["cores_physical"]!;
        Assert.Equal(8, (int?)cores["value"]);
        Assert.Equal("exact", (string?)cores["confidence"]);
        Assert.Null(cores["reason"]); // only written for unavailable fields
        Assert.False(cores.AsObject().ContainsKey("reason"));
        Assert.Equal(512L * 1024 * 1024, (long?)root["gpu"]![0]!["dedicated_vram_bytes"]!["value"]);
        Assert.Equal(2560, (int?)root["displays"]![1]!["resolution"]!["value"]!["width"]);

        var boost = root["cpu"]!["max_clock_mhz"]!.AsObject();
        Assert.True(boost.ContainsKey("value"));
        Assert.Null(boost["value"]);
        Assert.Equal("unavailable", (string?)boost["confidence"]);
        Assert.Equal("no_reliable_source", (string?)boost["reason"]);
    }

    [Fact]
    public async Task Round_trip_is_lossless()
    {
        var json = ReportJson.Serialize(await Report());
        var back = ReportJson.Deserialize(json)!;

        Assert.Equal("AMD Ryzen 7 7840HS w/ Radeon 780M Graphics", back.Cpu.Model.Value);
        Assert.Equal(Confidence.Detected, back.Cpu.Microarchitecture.Confidence);
        Assert.Equal(2, back.Memory.Modules.Count);
        Assert.Equal(5600, back.Memory.Modules[0].SpeedMts.Value);
        Assert.Equal(new Resolution(2560, 1440), back.Displays[1].Resolution.Value);
        Assert.Contains(back.Limitations, l => l.Section == "memory");
        Assert.Equal(json, ReportJson.Serialize(back));
    }

    [Fact]
    public async Task Identifiers_are_removed_even_if_a_collector_filled_them()
    {
        var r = await Report();
        r.System.Hostname = F.Exact<string?>("MY-WORKSTATION", "Environment.MachineName");
        r.Memory.Modules[0].Serial = F.Exact<string?>("E1A2B3C4", "SMBIOS");
        r.Motherboard.Serial = F.Exact<string?>("MB-123456", "SMBIOS");

        var json = ReportJson.Serialize(r);
        Assert.DoesNotContain("MY-WORKSTATION", json);
        Assert.DoesNotContain("E1A2B3C4", json);
        Assert.DoesNotContain("MB-123456", json);
        var host = JsonNode.Parse(json)!["system"]!["hostname"]!;
        Assert.Equal("privacy", (string?)host["reason"]);
        Assert.Empty(ReportValidator.Validate(json));
    }

    [Fact]
    public void Validator_rejects_broken_reports()
    {
        Assert.NotEmpty(ReportValidator.Validate("{not json"));
        Assert.NotEmpty(ReportValidator.Validate("[]"));
        Assert.Contains(ReportValidator.Validate("{}"), e => e.Contains("schema_version"));

        var root = JsonNode.Parse(ReportJson.Serialize(new HardwareReport()))!;
        root["schema_version"] = "2.0";
        root["cpu"]!["model"]!["confidence"] = "probably";
        root["cpu"]!["vendor"]!["value"] = "AMD"; // value on an unavailable field
        root["gpu"] = new JsonObject();
        var errors = ReportValidator.Validate(root.ToJsonString());

        Assert.Contains(errors, e => e.Contains("schema_version"));
        Assert.Contains(errors, e => e.Contains("invalid confidence 'probably'"));
        Assert.Contains(errors, e => e.Contains("$.cpu.vendor") && e.Contains("value null"));
        Assert.Contains(errors, e => e.Contains("'gpu' must be an array"));
    }

    [Fact]
    public void Empty_report_is_valid()
    {
        // Every field defaults to unavailable/not_collected: still a valid document.
        Assert.Empty(ReportValidator.Validate(ReportJson.Serialize(new HardwareReport())));
    }

    [Fact]
    public void File_name_pattern()
    {
        var r = new HardwareReport();
        r.Scan.CreatedAt = new DateTimeOffset(2026, 3, 7, 9, 5, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 3, 7, 9, 5, 0)));
        Assert.Equal("device-check-report-2026-03-07-09-05.json", ReportJson.FileName(r));
        Assert.Matches(new Regex(@"^device-check-report-\d{4}-\d{2}-\d{2}-\d{2}-\d{2}\.json$"), ReportJson.FileName(new HardwareReport()));
    }

    [Fact]
    public async Task Written_file_is_utf8_without_bom_and_keeps_cyrillic()
    {
        var r = await Report();
        r.System.OsName = F.Exact<string?>("Windows 11 Pro для рабочих станций", "test");
        var dir = Directory.CreateTempSubdirectory("dc-agent-test-");
        try
        {
            var path = await ReportJson.WriteAsync(r, dir.FullName);
            Assert.Equal(ReportJson.FileName(r), Path.GetFileName(path));
            var bytes = await File.ReadAllBytesAsync(path);
            Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..], "BOM must not be written");
            var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            Assert.Contains("для рабочих станций", text);
            Assert.Empty(ReportValidator.Validate(text));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task Invalid_report_is_not_written()
    {
        var r = new HardwareReport { SchemaVersion = "9.9" };
        var dir = Directory.CreateTempSubdirectory("dc-agent-test-");
        try
        {
            await Assert.ThrowsAsync<ReportValidationException>(() => ReportJson.WriteAsync(r, dir.FullName));
            Assert.Empty(dir.GetFiles());
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task Units_are_part_of_numeric_field_names()
    {
        // Numeric hardware quantities must carry a unit suffix; plain counts are listed explicitly.
        string[] units = ["_bytes", "_mhz", "_mts", "_mv", "_mm", "_in", "_seconds", "_kb", "_bits", "_hz", "_nm", "_w"];
        string[] counts = ["sockets", "cores_physical", "cores_performance", "cores_efficiency", "threads_logical", "slots_total",
            "slots_used", "slots_free", "modules_detected", "rank", "smbios_memory_type", "disk_number", "manufacture_year",
            "physical_sector_bytes", "logical_sector_bytes", "rpm"];
        var root = JsonNode.Parse(ReportJson.Serialize(await Report()))!;

        var offenders = new List<string>();
        void Walk(JsonNode? n, string key)
        {
            switch (n)
            {
                case JsonObject o when o.ContainsKey("confidence"):
                    if (o["value"] is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.Number
                        && !units.Any(key.EndsWith) && !counts.Contains(key)) offenders.Add(key);
                    break;
                case JsonObject o:
                    foreach (var (k, c) in o) if (k != "diagnostics") Walk(c, k);
                    break;
                case JsonArray a:
                    foreach (var c in a) Walk(c, key);
                    break;
            }
        }
        Walk(root, "$");
        Assert.Empty(offenders);
    }
}
