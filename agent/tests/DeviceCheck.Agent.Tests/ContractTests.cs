using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DeviceCheck.Agent.Export;
using DeviceCheck.Agent.Tests.Fixtures;

namespace DeviceCheck.Agent.Tests;

/// <summary>
/// Compatibility with the web contract: reads web/lib/types.ts from the repository and checks that every key
/// the site declares is present in the agent JSON (except browser-only keys, listed explicitly).
/// </summary>
public partial class ContractTests
{
    // Keys that only a browser can produce. The agent omits them; the site must treat them as optional for source = "agent".
    private static readonly HashSet<string> BrowserOnly =
    [
        "SystemInfo.browser_name", "SystemInfo.browser_version", "GpuInfo.renderer_raw",
        "DisplayInfo.css_resolution", "DisplayInfo.pixel_ratio", "DisplayInfo.color_gamut", "DisplayInfo.color_depth", "DisplayInfo.extended",
    ];

    // TypeScript interface → where its object lives in the report.
    private static readonly (string Interface, Func<JsonNode, JsonNode?> Select)[] Map =
    [
        ("HardwareReport", r => r),
        ("ScanInfo", r => r["scan"]),
        ("SystemInfo", r => r["system"]),
        ("CpuInfo", r => r["cpu"]),
        ("MotherboardInfo", r => r["motherboard"]),
        ("MemoryInfo", r => r["memory"]),
        ("MemoryModule", r => r["memory"]?["modules"]?[0]),
        ("GpuInfo", r => r["gpu"]?[0]),
        ("StorageDevice", r => r["storage"]?[0]),
        ("Volume", r => r["volumes"]?[0]),
        ("DisplayInfo", r => r["displays"]?[1]),
        ("Field", r => r["cpu"]?["model"]),
        ("Limitation", r => r["limitations"]?[0]),
        ("Resolution", r => r["displays"]?[1]?["resolution"]?["value"]),
    ];

    private static string TypesTs()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var p = Path.Combine(dir.FullName, "web", "lib", "types.ts");
            if (File.Exists(p)) return File.ReadAllText(p);
        }
        throw new FileNotFoundException("web/lib/types.ts not found above " + AppContext.BaseDirectory);
    }

    private static Dictionary<string, List<(string Key, bool Optional)>> Interfaces() =>
        InterfaceRx().Matches(TypesTs()).ToDictionary(
            m => m.Groups[1].Value,
            m => KeyRx().Matches(Regex.Replace(m.Groups[2].Value, @"/\*\*.*?\*/", "", RegexOptions.Singleline))
                .Select(k => (k.Groups[1].Value, k.Groups[2].Success)).ToList());

    [Fact]
    public async Task Agent_json_contains_every_web_contract_key()
    {
        var root = JsonNode.Parse(ReportJson.Serialize(await Machines.ScanAsync(Machines.AmdLaptop())))!;
        var interfaces = Interfaces();
        Assert.True(interfaces.Count >= 14, "failed to parse types.ts");

        var missing = new List<string>();
        foreach (var (name, select) in Map)
        {
            Assert.True(interfaces.ContainsKey(name), $"interface {name} not found in types.ts");
            var obj = select(root) as JsonObject;
            Assert.True(obj is not null, $"{name}: object not found in agent JSON");
            foreach (var (key, optional) in interfaces[name])
                if (!optional && !obj!.ContainsKey(key) && !BrowserOnly.Contains($"{name}.{key}"))
                    missing.Add($"{name}.{key}");
        }
        Assert.Empty(missing);
    }

    [Fact]
    public void Browser_only_list_is_not_stale()
    {
        var interfaces = Interfaces();
        foreach (var k in BrowserOnly)
        {
            var (iface, key) = (k.Split('.')[0], k.Split('.')[1]);
            Assert.Contains(interfaces[iface], x => x.Key == key);
        }
    }

    [Fact]
    public void Schema_version_matches_web()
    {
        Assert.Contains($"schema_version: '{Core.Model.SchemaInfo.Version}'", TypesTs());
    }

    [Fact]
    public void Confidence_values_match_web()
    {
        var m = Regex.Match(TypesTs(), @"type Confidence\s*=\s*([^\n]+)");
        var web = Regex.Matches(m.Groups[1].Value, "'(\\w+)'").Select(x => x.Groups[1].Value).Order();
        var agent = Enum.GetNames<Core.Model.Confidence>().Select(n => n.ToLowerInvariant()).Order();
        Assert.Equal(web, agent);
    }

    private static string WebFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(TypesTsPath())))!, .. parts]));

    private static string TypesTsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var p = Path.Combine(dir.FullName, "web", "lib", "types.ts");
            if (File.Exists(p)) return p;
        }
        throw new FileNotFoundException("web/lib/types.ts");
    }

    [Fact]
    public async Task Every_agent_code_is_translated_on_the_site()
    {
        var codes = new HashSet<string>(typeof(Core.Model.F.Reasons).GetFields().Select(f => "reason." + (string)f.GetValue(null)!));
        foreach (var r in new[] { await Machines.ScanAsync(Machines.AmdLaptop()), new Core.Model.HardwareReport() })
        {
            void Walk(JsonNode? n)
            {
                if (n is JsonObject o)
                {
                    if (o.ContainsKey("confidence"))
                    {
                        if ((string?)o["reason"] is { } reason) codes.Add("reason." + reason);
                        if ((string?)o["note"] is { } note) codes.Add("note." + note);
                    }
                    foreach (var (k, v) in o) if (k != "diagnostics") Walk(v);
                }
                else if (n is JsonArray a) foreach (var v in a) Walk(v);
            }
            var root = JsonNode.Parse(ReportJson.Serialize(r))!;
            Walk(root);
            foreach (var l in root["limitations"]!.AsArray()) codes.Add("lim." + (string?)l!["code"]);
        }

        foreach (var lang in new[] { "en", "ru" })
        {
            var dict = WebFile("web", "i18n", $"{lang}.ts");
            var missing = codes.Where(c => !dict.Contains($"'{c}':")).Order().ToList();
            Assert.True(missing.Count == 0, $"{lang}.ts is missing: {string.Join(", ", missing)}");
        }
    }

    [GeneratedRegex(@"export interface (\w+)(?:<[^>]*>)?\s*\{(.*?)\n\}", RegexOptions.Singleline)]
    private static partial Regex InterfaceRx();

    [GeneratedRegex(@"^\s*(\w+)(\?)?\s*:", RegexOptions.Multiline)]
    private static partial Regex KeyRx();
}
