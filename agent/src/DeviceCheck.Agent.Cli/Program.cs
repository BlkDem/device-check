using System.Globalization;
using System.Reflection;
using System.Text;
using DeviceCheck.Agent.Collectors;
using DeviceCheck.Agent.Core.Collection;
using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Export;
using DeviceCheck.Agent.Windows;

namespace DeviceCheck.Agent.Cli;

internal static class Program
{
    private const string Usage = """
        Device Check Agent (console) — reads hardware information on this PC.
        Nothing is sent anywhere. A JSON file is written only when you ask for it.

        Usage: DeviceCheck.Agent.Cli [options]
               DeviceCheck.Agent.Cli storage [--json] [--out <file|dir>] [--quick]   storage health (storage --help)
          --out <file|dir>   Save the HardwareReport JSON (a directory gets device-check-report-YYYY-MM-DD-HH-mm.json)
          --json             Print the JSON to stdout instead of the summary
          --compact          Compact JSON (with --json/--out)
          --timeout <sec>    Per-collector timeout, default 20
          --quiet            No progress output
          -h, --help         Show this help
        """;

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        if (args is ["storage", ..]) return await StorageCommand.RunAsync(args[1..]);
        string? outPath = null;
        bool json = false, compact = false, quiet = false;
        int timeoutSec = 20;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" when i + 1 < args.Length: outPath = args[++i]; break;
                case "--json": json = true; break;
                case "--compact": compact = true; break;
                case "--quiet": quiet = true; break;
                case "--timeout" when i + 1 < args.Length && int.TryParse(args[i + 1], out var t) && t > 0: timeoutSec = t; i++; break;
                case "-h" or "--help" or "/?": Console.WriteLine(Usage); return 0;
                default: Console.Error.WriteLine($"Unknown option: {args[i]}\n\n{Usage}"); return 2;
            }
        }

        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var wmi = new WmiClient(TimeSpan.FromSeconds(Math.Max(3, timeoutSec / 2)));
        var collectors = CollectorSet.Create(wmi, new SmbiosSource(), new ProcessorSource(), new GraphicsSource(), new DisplaySource(), new PlatformSource());
        var orchestrator = new ScanOrchestrator(collectors, new ScanOptions
        {
            AgentVersion = version,
            CollectorTimeout = TimeSpan.FromSeconds(timeoutSec),
            MaxParallelism = 3,
        });

        var log = json ? Console.Error : Console.Out;
        var progress = new Progress<ScanProgress>(p =>
        {
            if (!quiet && p.Status != "running") log.WriteLine($"  [{p.Completed}/{p.Total}] {p.CollectorId,-12} {p.Status}");
        });

        if (!quiet) log.WriteLine($"Device Check Agent {version} — scanning…");
        HardwareReport report;
        try
        {
            report = await orchestrator.RunAsync(progress, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Scan cancelled.");
            return 130;
        }
        await Task.Delay(50); // let Progress<T> flush its last callbacks

        if (json) Console.WriteLine(ReportJson.Serialize(report, indented: !compact));
        else Summary.Print(report);

        if (outPath is not null)
        {
            try
            {
                var full = await ReportJson.WriteAsync(report, outPath, cts.Token);
                log.WriteLine($"\nSaved: {full}");
            }
            catch (ReportValidationException e)
            {
                Console.Error.WriteLine(e.Message);
                return 3;
            }
        }
        return report.Diagnostics.Collectors.Any(c => c.Status is "failed" or "timeout") ? 1 : 0;
    }
}

internal static class Summary
{
    public static void Print(HardwareReport r)
    {
        static string V<T>(Field<T> f, Func<T, string>? fmt = null) =>
            f.HasValue ? (fmt is null ? Convert.ToString(f.Value, CultureInfo.InvariantCulture) ?? "" : fmt(f.Value)) + $"  ({f.Confidence.ToString().ToLowerInvariant()})" : $"— ({f.Reason})";
        static string Gib(long? b) => b is null ? "—" : $"{b.Value / 1024.0 / 1024 / 1024:0.##} GiB";

        Console.WriteLine();
        Console.WriteLine($"OS        {V(r.System.OsName)}  {r.System.OsVersion.Value} build {r.System.OsBuild.Value}");
        Console.WriteLine($"CPU       {V(r.Cpu.Model)}");
        Console.WriteLine($"          {r.Cpu.CoresPhysical.Value} cores / {r.Cpu.ThreadsLogical.Value} threads, base {r.Cpu.BaseClockMhz.Value} MHz, {r.Cpu.Microarchitecture.Value ?? "uarch ?"}");
        Console.WriteLine($"Board     {V(r.Motherboard.Manufacturer)} {r.Motherboard.Model.Value}  BIOS {r.Motherboard.BiosVersion.Value} ({r.Motherboard.BiosDate.Value}, {r.Motherboard.FirmwareType.Value})");
        Console.WriteLine($"System    {r.Motherboard.SystemManufacturer.Value} {r.Motherboard.SystemModel.Value}");
        Console.WriteLine($"Memory    {Gib(r.Memory.TotalBytes.Value)} {r.Memory.MemoryType.Value} @ {r.Memory.SpeedMts.Value} MT/s, slots {r.Memory.SlotsUsed.Value}/{r.Memory.SlotsTotal.Value}, channels {r.Memory.Channels.Value ?? "unknown"}");
        foreach (var m in r.Memory.Modules)
            Console.WriteLine($"          {m.Slot.Value,-14} {Gib(m.SizeBytes.Value),-10} {m.MemoryType.Value} {m.SpeedMts.Value}/{m.RatedSpeedMts.Value} MT/s {m.Manufacturer.Value} {m.PartNumber.Value}{(m.Soldered.Value == true ? " [soldered]" : "")}");
        foreach (var g in r.Gpu)
            Console.WriteLine($"GPU       {g.Model.Value} [{g.AdapterType.Value}] VRAM {Gib(g.DedicatedVramBytes.Value)}, shared {Gib(g.SharedMemoryBytes.Value)}, driver {g.DriverVersion.Value}");
        foreach (var d in r.Storage)
            Console.WriteLine($"Disk {d.DiskNumber.Value,-4} {d.Model.Value} {Gib(d.SizeBytes.Value)} {d.MediaType.Value}/{d.Interface.Value} health {d.SmartStatus.Value}{(d.IsSystem.Value == true ? " [system]" : "")}");
        foreach (var v in r.Volumes)
            Console.WriteLine($"Volume    {v.Letter.Value} {v.Filesystem.Value} {Gib(v.SizeBytes.Value)} free {Gib(v.FreeBytes.Value)}");
        foreach (var d in r.Displays)
            Console.WriteLine($"Display   {d.Manufacturer.Value} {d.Model.Value} {d.Resolution.Value?.Width}x{d.Resolution.Value?.Height} @ {d.RefreshHz.Value} Hz, {d.DiagonalIn.Value}\" {d.Connection.Value}, HDR {d.Hdr.Value}");

        Console.WriteLine($"\nCollectors ({r.Diagnostics.DurationMs} ms, elevated: {r.Diagnostics.Elevated}):");
        foreach (var c in r.Diagnostics.Collectors)
        {
            Console.WriteLine($"  {c.Id,-12} {c.Status,-8} {c.DurationMs,6} ms");
            foreach (var e in c.Errors) Console.WriteLine($"      error: {e}");
            foreach (var w in c.Warnings) Console.WriteLine($"      warn:  {w}");
        }
        if (r.Limitations.Count > 0)
            Console.WriteLine("Limitations: " + string.Join(", ", r.Limitations.Select(l => $"{l.Section}:{l.Code}")));
    }
}
