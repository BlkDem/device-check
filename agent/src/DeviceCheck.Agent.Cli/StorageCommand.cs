using System.Globalization;
using System.Reflection;
using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Export;
using DeviceCheck.Agent.Monitoring.Core;
using DeviceCheck.Agent.Monitoring.Storage;
using DeviceCheck.Agent.Windows;

namespace DeviceCheck.Agent.Cli;

/// <summary><c>storage</c>: System Health Monitor, storage part — console diagnostics and StorageHealthReport JSON.</summary>
internal static class StorageCommand
{
    private const string Usage = """
        Storage health (read-only: no SMART commands that change state, no self-tests, no writes).

        Usage: DeviceCheck.Agent.Cli storage [options]
          --json             Print the StorageHealthReport JSON instead of the summary
          --out <file|dir>   Save the JSON (a directory gets device-check-storage-health-YYYY-MM-DD-HH-mm.json)
          --quick            Free space and temperature only (no SMART queries)
          --timeout <sec>    Per-collector timeout, default 20
          --quiet            No progress output

        SMART of SATA drives and Windows reliability counters require "Run as administrator".
        NVMe health is read without elevation where the driver allows it.
        """;

    public static async Task<int> RunAsync(string[] args)
    {
        string? outPath = null;
        bool json = false, quick = false, quiet = false;
        int timeoutSec = 20;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" when i + 1 < args.Length: outPath = args[++i]; break;
                case "--json": json = true; break;
                case "--quick": quick = true; break;
                case "--quiet": quiet = true; break;
                case "--timeout" when i + 1 < args.Length && int.TryParse(args[i + 1], out var t) && t > 0: timeoutSec = t; i++; break;
                case "-h" or "--help" or "/?": Console.WriteLine(Usage); return 0;
                default: Console.Error.WriteLine($"Unknown option: {args[i]}\n\n{Usage}"); return 2;
            }
        }

        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        var log = json ? Console.Error : Console.Out;
        if (!quiet) log.WriteLine($"Device Check Agent {version} — storage health ({(quick ? "quick" : "full")})…");

        var monitor = new StorageHealthMonitor(new WmiClient(TimeSpan.FromSeconds(Math.Max(3, timeoutSec / 2))), new StorageDeviceSource(),
            new PlatformSource(), new StorageMonitorOptions { AgentVersion = version, CollectorTimeout = TimeSpan.FromSeconds(timeoutSec) });
        StorageHealthReport report;
        try
        {
            report = await monitor.SampleAsync(quick ? SampleKind.Quick : SampleKind.Full, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }

        if (json) Console.WriteLine(ReportJson.Serialize(report));
        else Print(report);

        if (outPath is not null)
        {
            try
            {
                log.WriteLine($"\nSaved: {await ReportJson.WriteAsync(report, outPath, cts.Token)}");
            }
            catch (ReportValidationException e)
            {
                Console.Error.WriteLine(e.Message);
                return 3;
            }
        }
        return report.Diagnostics.Collectors.Any(c => c.Status is "failed" or "timeout") ? 1 : 0;
    }

    private static string V<T>(Field<T> f, Func<T, string>? fmt = null) =>
        f.HasValue ? (fmt is null ? Convert.ToString(f.Value, CultureInfo.InvariantCulture) ?? "" : fmt(f.Value)) : $"— ({f.Reason})";

    private static string Size(long? b)
    {
        if (b is null) return "—";
        double v = b.Value;
        string[] u = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return v.ToString(v >= 100 ? "0" : "0.#", CultureInfo.InvariantCulture) + " " + u[i];
    }

    private static void Print(StorageHealthReport r)
    {
        var sh = r.StorageHealth;
        int volumes = sh.Devices.Sum(d => d.Volumes.Count) + sh.OtherVolumes.Count;
        Console.WriteLine($"\n{sh.Devices.Count} disk(s), {volumes} volume(s), elevated: {r.Diagnostics.Elevated}");
        foreach (var d in sh.Devices)
        {
            var s = d.Smart;
            var t = d.Temperature;
            Console.WriteLine($"\nDisk {d.DiskNumber.Value}  {V(d.Model)}  [{V(d.Type)}]  {Size(d.CapacityBytes.Value)}  id {d.Id}");
            Console.WriteLine($"  Windows health  {V(d.Health.OsStatus)}  ({V(d.Health.OperationalStatus, x => string.Join(",", x!))})   predict failure: {V(d.Health.PredictFailure)}");
            Console.WriteLine($"  Temperature     {V(t.CurrentCelsius, c => $"{c} °C")}   warning {V(t.WarningCelsius, c => $"{c} °C")}   critical {V(t.CriticalCelsius, c => $"{c} °C")}");
            Console.WriteLine($"  SMART           {V(s.Interface)}");
            if (s.Interface.HasValue)
            {
                Console.WriteLine($"    used {V(s.PercentageUsed, x => $"{x}%")}  spare {V(s.AvailableSparePercent, x => $"{x}%")} (threshold {V(s.AvailableSpareThresholdPercent, x => $"{x}%")})  power-on {V(s.PowerOnHours, x => $"{x} h")}  cycles {V(s.PowerCycles)}");
                Console.WriteLine($"    unsafe shutdowns {V(s.UnsafeShutdowns)}  media errors {V(s.MediaErrors)}  read {V(s.DataReadBytes, x => Size(x))}  written {V(s.DataWrittenBytes, x => Size(x))}");
                if (s.Nvme is { } n)
                    Console.WriteLine($"    NVMe critical warning {V(n.CriticalWarning)} {V(n.CriticalWarningFlags, x => x!.Count == 0 ? "(none)" : string.Join(",", x))}  error log entries {V(n.ErrorLogEntries)}");
                if (s.Ata is { } a)
                {
                    Console.WriteLine($"    reallocated {V(s.ReallocatedSectors)}  pending {V(s.PendingSectors)}  uncorrectable {V(s.UncorrectableSectors)}  CRC {V(s.CrcErrors)}");
                    Console.WriteLine("    ID  Name                                  Norm Worst  Thr  Raw");
                    foreach (var x in a.Attributes)
                        Console.WriteLine($"    {x.Id,3} {x.Name ?? "?",-37} {x.Normalized.Value,4} {x.Worst.Value,5} {x.Threshold.Value?.ToString(CultureInfo.InvariantCulture) ?? "—",4}  {x.Raw.Value}");
                }
            }
            foreach (var v in d.Volumes) PrintVolume(v);
        }
        if (sh.OtherVolumes.Count > 0)
        {
            Console.WriteLine("\nOther volumes (not on a reported physical disk):");
            foreach (var v in sh.OtherVolumes) PrintVolume(v);
        }

        Console.WriteLine($"\nCollectors ({r.Diagnostics.DurationMs} ms):");
        foreach (var c in r.Diagnostics.Collectors)
        {
            Console.WriteLine($"  {c.Id,-12} {c.Status,-8} {c.DurationMs,6} ms");
            foreach (var e in c.Errors) Console.WriteLine($"      error: {e}");
            foreach (var w in c.Warnings) Console.WriteLine($"      warn:  {w}");
        }
        if (r.Limitations.Count > 0) Console.WriteLine("Limitations: " + string.Join(", ", r.Limitations.Select(l => $"{l.Section}:{l.Code}")));
    }

    private static void PrintVolume(VolumeHealth v) =>
        Console.WriteLine($"  {v.Letter.Value ?? "(no letter)",-11} {v.Label.Value ?? "",-14} {v.Filesystem.Value ?? "",-6} {Size(v.TotalBytes.Value),10}  free {Size(v.FreeBytes.Value),10} ({V(v.FreePercent, x => $"{x?.ToString(CultureInfo.InvariantCulture)}%")})  disks {V(v.DiskNumbers, x => string.Join(",", x!))}");
}
