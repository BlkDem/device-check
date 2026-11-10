using System.Text.RegularExpressions;
using DeviceCheck.Agent.Core.Collection;
using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Core.Normalization;
using DeviceCheck.Agent.Core.Parsing;
using DeviceCheck.Agent.Core.Sources;

namespace DeviceCheck.Agent.Collectors;

/// <summary>GPUs: DXGI (memory, PCI ids) + D3D12 (UMA → integrated/discrete) + WMI Win32_VideoController (driver).</summary>
public sealed partial class GpuCollector(IWmiClient wmi, IGraphicsSource graphics) : IHardwareCollector
{
    public string Id => "gpu";
    private const string DxgiSrc = "DXGI IDXGIAdapter1::GetDesc1";

    public async Task CollectAsync(CollectorContext ctx, CancellationToken ct)
    {
        var adapters = ctx.Try("DXGI", graphics.GetAdapters, (IReadOnlyList<GraphicsAdapterInfo>)[]);
        var controllers = await ctx.TryAsync("WMI Win32_VideoController", () => wmi.QueryAsync(Ns.Cimv2,
            "SELECT Name, PNPDeviceID, DriverVersion, DriverDate, AdapterCompatibility FROM Win32_VideoController", ct),
            (IReadOnlyList<WmiObject>)[]);

        var list = new List<GpuAdapter>();
        foreach (var a in adapters)
        {
            if (a.IsSoftware) { ctx.Warn("software_adapter_skipped"); continue; }
            var wmiRow = Match(controllers, a);
            list.Add(FromDxgi(a, wmiRow));
        }

        if (list.Count == 0 && controllers.Count > 0)
        {
            ctx.Warn("dxgi_unavailable_used_wmi");
            list.AddRange(controllers.Select(FromWmiOnly));
        }
        ctx.Report.Gpu = list;
    }

    internal static GpuAdapter FromDxgi(GraphicsAdapterInfo a, WmiObject? w)
    {
        var vendor = Vendors.PciVendor(a.VendorId);
        var g = new GpuAdapter
        {
            Vendor = vendor is not null ? F.Exact<string?>(vendor, "PCI vendor id (DXGI)") : F.Exact(w?.Str("AdapterCompatibility"), "WMI Win32_VideoController"),
            Model = F.Exact<string?>(a.Description, DxgiSrc),
            Description = F.Exact<string?>(a.Description, DxgiSrc),
            VendorId = F.Exact<string?>(Fw.Hex(a.VendorId, 4), DxgiSrc),
            DeviceId = F.Exact<string?>(Fw.Hex(a.DeviceId, 4), DxgiSrc),
            SubsystemId = a.SubsystemId != 0 ? F.Exact<string?>(Fw.Hex(a.SubsystemId, 8), DxgiSrc) : F.Unavailable<string?>(DxgiSrc, F.Reasons.NotReported),
            Revision = F.Exact<string?>(Fw.Hex(a.Revision, 2), DxgiSrc),
            PciDeviceId = F.Exact(PciId(w?.Str("PNPDeviceID")), "WMI Win32_VideoController.PNPDeviceID"),
            DedicatedVramBytes = F.Exact<long?>((long)a.DedicatedVideoMemory, DxgiSrc + " DedicatedVideoMemory", a.Uma == true ? "uma_carveout" : null),
            DedicatedSystemMemoryBytes = F.Exact<long?>((long)a.DedicatedSystemMemory, DxgiSrc + " DedicatedSystemMemory"),
            SharedMemoryBytes = F.Exact<long?>((long)a.SharedSystemMemory, DxgiSrc + " SharedSystemMemory", "system_ram_not_vram"),
            Uma = F.Exact(a.Uma, "D3D12 FEATURE_ARCHITECTURE.UMA"),
            DriverVersion = F.Exact(w?.Str("DriverVersion"), "WMI Win32_VideoController"),
            DriverDate = F.Exact(CimDate.Parse(w?.Str("DriverDate"))?.ToString("yyyy-MM-dd"), "WMI Win32_VideoController"),
            Apis = a.MaxFeatureLevel is not null
                ? F.Exact<List<string>?>([$"Direct3D 12 (feature level {a.MaxFeatureLevel})"], "D3D12CreateDevice / CheckFeatureSupport")
                : F.Unavailable<List<string>?>("D3D12CreateDevice", F.Reasons.NotReported),
        };
        g.AdapterType = AdapterType(a, vendor, a.Description);
        // A zero shared/dedicated value is a real value for DXGI; keep it but never treat it as "missing".
        return g;
    }

    internal static Field<string?> AdapterType(GraphicsAdapterInfo a, string? vendor, string model)
    {
        if (a.IsRemote) return F.Exact<string?>("remote", "DXGI_ADAPTER_FLAG_REMOTE");
        if (Vendors.IsVirtualGpuVendor(a.VendorId)) return F.Detected<string?>("virtual", "PCI vendor id", "virtual_gpu_vendor");
        if (a.Uma is bool uma) return F.Exact<string?>(uma ? "integrated" : "discrete", "D3D12 FEATURE_ARCHITECTURE.UMA");

        // Fallback heuristic by name (D3D12 unavailable).
        var m = model.ToLowerInvariant();
        string? guess = vendor switch
        {
            "Intel" => m.Contains(" arc ") && !m.EndsWith("arc graphics") ? "discrete" : "integrated",
            "NVIDIA" => "discrete",
            "AMD" when RadeonDiscrete().IsMatch(m) => "discrete",
            "AMD" when m.Contains("radeon(tm) graphics") || m.Contains("radeon graphics") || RadeonApu().IsMatch(m) => "integrated",
            "Qualcomm" or "Apple" or "ARM" => "integrated",
            _ => null,
        };
        return guess is null
            ? F.Unavailable<string?>("adapter name heuristic", F.Reasons.NoReliableSource)
            : F.Detected<string?>(guess, "adapter name heuristic", "heuristic");
    }

    private static GpuAdapter FromWmiOnly(WmiObject w)
    {
        const string src = "WMI Win32_VideoController";
        var pnp = w.Str("PNPDeviceID");
        var ven = pnp is null ? null : VenDev().Match(pnp);
        return new GpuAdapter
        {
            Vendor = F.Exact(w.Str("AdapterCompatibility"), src),
            Model = F.Exact(w.Str("Name"), src),
            Description = F.Exact(w.Str("Name"), src),
            VendorId = F.Exact(ven is { Success: true } ? "0x" + ven.Groups[1].Value.ToUpperInvariant() : null, src),
            DeviceId = F.Exact(ven is { Success: true } ? "0x" + ven.Groups[2].Value.ToUpperInvariant() : null, src),
            PciDeviceId = F.Exact(PciId(pnp), src),
            DriverVersion = F.Exact(w.Str("DriverVersion"), src),
            DriverDate = F.Exact(CimDate.Parse(w.Str("DriverDate"))?.ToString("yyyy-MM-dd"), src),
            // Win32_VideoController.AdapterRAM is a 32-bit value capped at 4 GiB: not used.
            DedicatedVramBytes = F.Unavailable<long?>(src, "wmi_adapter_ram_unreliable"),
            AdapterType = F.Unavailable<string?>(src, F.Reasons.NoReliableSource),
        };
    }

    /// <summary>Matches a DXGI adapter to its WMI row by VEN/DEV(/SUBSYS).</summary>
    internal static WmiObject? Match(IReadOnlyList<WmiObject> rows, GraphicsAdapterInfo a)
    {
        string ven = $"VEN_{a.VendorId:X4}", dev = $"DEV_{a.DeviceId:X4}", sub = $"SUBSYS_{a.SubsystemId:X8}";
        var candidates = rows.Where(r => r.Str("PNPDeviceID") is { } p
            && p.Contains(ven, StringComparison.OrdinalIgnoreCase) && p.Contains(dev, StringComparison.OrdinalIgnoreCase)).ToList();
        return candidates.FirstOrDefault(r => r.Str("PNPDeviceID")!.Contains(sub, StringComparison.OrdinalIgnoreCase)) ?? candidates.FirstOrDefault();
    }

    /// <summary>"PCI\VEN_1002&amp;DEV_15BF&amp;SUBSYS_...&amp;REV_C1\4&amp;..." → hardware id without the instance path (which is machine-specific).</summary>
    internal static string? PciId(string? pnp)
    {
        if (pnp is null || !pnp.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = pnp.Split('\\');
        return parts.Length >= 2 ? $"{parts[0]}\\{parts[1]}".ToUpperInvariant() : null;
    }

    [GeneratedRegex(@"\b(rx|pro w|w\d{4}|r9|r7|hd \d{4}|vii|vega (56|64)|fire)", RegexOptions.IgnoreCase)]
    private static partial Regex RadeonDiscrete();

    [GeneratedRegex(@"radeon\s+\d{3}m\b|vega \d+ graphics", RegexOptions.IgnoreCase)]
    private static partial Regex RadeonApu();

    [GeneratedRegex(@"VEN_([0-9A-F]{4})&DEV_([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VenDev();
}
