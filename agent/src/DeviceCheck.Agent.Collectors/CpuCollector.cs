using DeviceCheck.Agent.Core.Collection;
using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Core.Normalization;
using DeviceCheck.Agent.Core.Sources;

namespace DeviceCheck.Agent.Collectors;

public sealed class CpuCollector(IWmiClient wmi, IProcessorSource processor) : IHardwareCollector
{
    public string Id => "cpu";

    private const string CpuidSrc = "CPUID";
    private const string TopoSrc = "GetLogicalProcessorInformationEx";
    private const string WmiSrc = "WMI Win32_Processor";

    public async Task CollectAsync(CollectorContext ctx, CancellationToken ct)
    {
        var c = ctx.Report.Cpu;
        var cpuid = ctx.Try(CpuidSrc, processor.GetCpuid, null);
        var topo = ctx.Try(TopoSrc, processor.GetTopology, null);
        var rows = await ctx.TryAsync(WmiSrc, () => wmi.QueryAsync(Ns.Cimv2,
            "SELECT Name, Manufacturer, MaxClockSpeed, CurrentClockSpeed, NumberOfCores, NumberOfLogicalProcessors, L2CacheSize, L3CacheSize, VirtualizationFirmwareEnabled, Architecture FROM Win32_Processor", ct),
            (IReadOnlyList<WmiObject>)[]);
        var w = rows.FirstOrDefault();

        // Identity
        var vendor = CpuIdentification.NormalizeVendor(cpuid?.Vendor);
        c.Vendor = vendor is not null ? F.Exact<string?>(vendor, CpuidSrc) : F.Exact(w?.Str("Manufacturer"), WmiSrc);
        var brand = CpuIdentification.NormalizeBrand(cpuid?.Brand);
        c.Model = brand is not null ? F.Exact<string?>(brand, CpuidSrc + " brand string") : F.Exact(CpuIdentification.NormalizeBrand(w?.Str("Name")), WmiSrc);
        c.Architecture = F.Exact(Arch(w?.Int("Architecture")), WmiSrc + ".Architecture");

        if (cpuid is not null)
        {
            c.Family = F.Exact<string?>($"{vendor} Family {cpuid.Family:X}h", CpuidSrc);
            c.CpuidSignature = F.Exact<string?>($"Family {cpuid.Family:X}h, Model {cpuid.Model:X}h, Stepping {cpuid.Stepping}", CpuidSrc);
            c.Instructions = F.Exact<List<string>?>(cpuid.Features.ToList(), CpuidSrc, "hardware_flags");

            var arch = vendor is null ? null : CpuIdentification.Lookup(vendor, cpuid.Family, cpuid.Model);
            if (arch is not null)
            {
                c.Microarchitecture = F.Detected<string?>(arch.Microarchitecture, "reference table by CPUID family/model", "reference_table");
                c.Codename = F.Detected(arch.Codename, "reference table by CPUID family/model", "reference_table");
                c.ProcessNode = F.Detected(arch.ProcessNode, "reference table by CPUID family/model", "reference_table");
                c.ProcessNm = arch.ProcessNm is null
                    ? F.Unavailable<int?>("reference table", "see_process_node")
                    : F.Detected<int?>(arch.ProcessNm, "reference table by CPUID family/model", "reference_table");
            }
            else
            {
                foreach (var set in new Action<Field<string?>>[] { f => c.Microarchitecture = f, f => c.Codename = f, f => c.ProcessNode = f })
                    set(F.Unavailable<string?>("reference table", "not_in_reference_table"));
                c.ProcessNm = F.Unavailable<int?>("reference table", "not_in_reference_table");
            }

            bool vt = cpuid.Features.Contains("VT-x") || cpuid.Features.Contains("AMD-V");
            c.Virtualization = vt
                ? F.Exact<bool?>(true, CpuidSrc)
                : cpuid.HypervisorPresent
                    ? F.Detected<bool?>(true, "hypervisor present", "hypervisor_hides_vmx")
                    : F.Exact<bool?>(false, CpuidSrc);
        }
        else
        {
            c.Family = F.Unavailable<string?>(CpuidSrc, F.Reasons.Unsupported);
            c.Instructions = F.Unavailable<List<string>?>(CpuidSrc, F.Reasons.Unsupported);
        }
        c.Generation = F.Detected(CpuIdentification.Generation(brand ?? w?.Str("Name")), "brand string", "from_brand_string");

        // When a hypervisor (Hyper-V/VBS) runs, Windows reports firmware virtualization as false although it must be on.
        var fwVirt = w?.Bool("VirtualizationFirmwareEnabled");
        c.VirtualizationEnabled = cpuid?.HypervisorPresent == true
            ? F.Detected<bool?>(true, "hypervisor present", "hypervisor_running")
            : F.Exact(fwVirt, WmiSrc + ".VirtualizationFirmwareEnabled");

        // Topology
        if (topo is not null)
        {
            c.Sockets = F.Exact<int?>(topo.Packages, TopoSrc);
            c.CoresPhysical = F.Exact<int?>(topo.Cores, TopoSrc);
            c.ThreadsLogical = F.Exact<int?>(topo.LogicalProcessors, TopoSrc);
            var classes = topo.CoreEfficiencyClasses;
            if (classes.Distinct().Count() > 1)
            {
                int max = classes.Max();
                c.CoresPerformance = F.Exact<int?>(classes.Count(x => x == max), TopoSrc + " (EfficiencyClass)");
                c.CoresEfficiency = F.Exact<int?>(classes.Count(x => x != max), TopoSrc + " (EfficiencyClass)");
            }
            else
            {
                c.CoresPerformance = F.Unavailable<int?>(TopoSrc, F.Reasons.NotApplicable);
                c.CoresEfficiency = F.Unavailable<int?>(TopoSrc, F.Reasons.NotApplicable);
            }
            ApplyCaches(c, topo.Caches);
        }
        else
        {
            c.Sockets = F.Exact<int?>(rows.Count > 0 ? rows.Count : null, WmiSrc);
            c.CoresPhysical = F.Exact<int?>(rows.Sum(r => r.Int("NumberOfCores") ?? 0) is > 0 and var n ? n : null, WmiSrc);
            c.ThreadsLogical = F.Exact<int?>(rows.Sum(r => r.Int("NumberOfLogicalProcessors") ?? 0) is > 0 and var t ? t : null, WmiSrc);
            c.CacheL2Kb = F.Exact<long?>(w?.Long("L2CacheSize") is > 0 and var l2 ? l2 : null, WmiSrc);
            c.CacheL3Kb = F.Exact<long?>(w?.Long("L3CacheSize") is > 0 and var l3 ? l3 : null, WmiSrc);
        }

        // Clocks. Win32_Processor.MaxClockSpeed is the nominal (base) frequency on modern CPUs, not the boost clock.
        var nominal = w?.Int("MaxClockSpeed");
        c.BaseClockMhz = F.Detected(nominal is > 0 ? nominal : null, WmiSrc + ".MaxClockSpeed", "nominal_clock");
        c.MaxClockMhz = F.Unavailable<int?>("—", F.Reasons.NoReliableSource);
        ctx.Limit("cpu", "boost_clock_unavailable");
        c.TdpW = F.Unavailable<int?>("—", F.Reasons.NoReliableSource);

        // Current effective clock = nominal × "% Processor Performance" (the same method Task Manager uses).
        const string perfSrc = "WMI Win32_PerfFormattedData_Counters_ProcessorInformation";
        var perf = (await ctx.TryAsync(perfSrc, () => wmi.QueryAsync(Ns.Cimv2,
            "SELECT PercentProcessorPerformance FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='_Total'", ct),
            (IReadOnlyList<WmiObject>)[])).FirstOrDefault()?.Long("PercentProcessorPerformance");
        c.CurrentClockMhz = nominal is > 0 && perf is > 0
            ? F.Approx<int?>((int)Math.Round(nominal.Value * perf.Value / 100.0), perfSrc + " × MaxClockSpeed", "instantaneous_average")
            : F.Unavailable<int?>(perfSrc, F.Reasons.NotReported);
    }

    private static void ApplyCaches(CpuSection c, IReadOnlyList<CacheInfo> caches)
    {
        long Sum(int level) => caches.Where(x => x.Level == level).Sum(x => x.SizeBytes) / 1024;
        c.CacheL1Kb = F.Exact<long?>(Sum(1) is > 0 and var l1 ? l1 : null, TopoSrc, "total_all_cores");
        c.CacheL2Kb = F.Exact<long?>(Sum(2) is > 0 and var l2 ? l2 : null, TopoSrc, "total_all_cores");
        c.CacheL3Kb = F.Exact<long?>(Sum(3) is > 0 and var l3 ? l3 : null, TopoSrc, "total_all_cores");

        var layout = caches
            .GroupBy(x => (x.Level, x.Type, x.SizeBytes))
            .OrderBy(g => g.Key.Level).ThenBy(g => g.Key.Type).ThenByDescending(g => g.Key.SizeBytes)
            .Select(g => $"L{g.Key.Level}{TypeSuffix(g.Key.Type)} {g.Count()} × {FormatSize(g.Key.SizeBytes)}")
            .ToList();
        c.CacheLayout = F.Exact<List<string>?>(layout, TopoSrc);
    }

    private static string TypeSuffix(string t) => t switch { "data" => "d", "instruction" => "i", _ => "" };

    private static string FormatSize(long bytes) => bytes >= 1024 * 1024 && bytes % (1024 * 1024) == 0
        ? $"{bytes / 1024 / 1024} MB"
        : $"{bytes / 1024} KB";

    internal static string? Arch(int? code) => code switch
    {
        0 => "x86",
        5 => "ARM",
        9 => "x86-64",
        12 => "ARM64",
        _ => null,
    };
}
