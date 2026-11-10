using System.Diagnostics;
using DeviceCheck.Agent.Core.Collection;
using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Core.Sources;
using DeviceCheck.Agent.Monitoring.Core;
using DeviceCheck.Agent.Monitoring.Filesystems;

namespace DeviceCheck.Agent.Monitoring.Storage;

public sealed class StorageMonitorOptions
{
    public string AgentVersion { get; init; } = "0.0.0";
    public TimeSpan CollectorTimeout { get; init; } = TimeSpan.FromSeconds(20);
}

/// <summary>
/// Storage part of the System Health Monitor (stage 1: collection only, no health classification yet).
/// Inventory first, then each collector in order with its own timeout; a failing collector never stops the others.
/// Strictly read-only: no SMART commands that change state, no self-tests, no writes.
/// </summary>
public sealed class StorageHealthMonitor(
    IWmiClient wmi, IStorageDeviceSource devices, IPlatformSource platform, StorageMonitorOptions options,
    IReadOnlyList<IStorageHealthCollector>? collectors = null) : IHealthMonitor<StorageHealthReport>
{
    public string Id => "storage";

    // Order matters: driver temperature, then NVMe (overrides it), ATA, reliability counters (fallback), volumes.
    private readonly IReadOnlyList<IStorageHealthCollector> _collectors = collectors ??
    [
        new DeviceTemperatureCollector(devices),
        new NvmeHealthCollector(devices),
        new AtaSmartCollector(wmi),
        new ReliabilityCounterCollector(wmi),
        new VolumeCollector(wmi, platform),
    ];

    public async Task<StorageHealthReport> SampleAsync(SampleKind kind, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var report = new StorageHealthReport();
        report.Scan.AgentVersion = options.AgentVersion;
        report.Scan.Collector.Version = options.AgentVersion;
        report.Scan.Platform = "Windows";
        report.Diagnostics.Elevated = TryElevated();

        var invDiag = new CollectorDiagnostics { Id = "inventory" };
        report.Diagnostics.Collectors.Add(invDiag);
        var disks = await Run(invDiag, c => new DiskInventory(wmi).CollectAsync(new CollectionContext(report.Limitations, c), ct), ct) ?? [];
        report.StorageHealth.Devices = disks.Select(d => d.Device).ToList();

        foreach (var c in _collectors)
        {
            if (c.FullOnly && kind == SampleKind.Quick) continue;
            var diag = new CollectorDiagnostics { Id = c.Id };
            report.Diagnostics.Collectors.Add(diag);
            await Run<bool>(diag, async d => { await c.CollectAsync(new StorageHealthContext(report, d, disks), ct); return true; }, ct);
        }

        if (kind == SampleKind.Full) Finish(report, disks);
        else MarkQuick(disks);
        report.CollectedAt = DateTimeOffset.UtcNow;
        report.Diagnostics.DurationMs = sw.ElapsedMilliseconds;
        return report;
    }

    private static void MarkQuick(List<DiskEntry> disks)
    {
        foreach (var d in disks) d.Device.Smart.Interface = F.Unavailable<string?>("—", "not_sampled_quick");
    }

    /// <summary>Explains every missing SMART interface. A missing SMART never implies a bad disk.</summary>
    private static void Finish(StorageHealthReport report, List<DiskEntry> disks)
    {
        foreach (var d in disks.Where(d => d.Bus is not ("sata" or "ata" or "atapi") && d.Device.Health.PredictFailure.Reason == F.Reasons.NotCollected))
            d.Device.Health.PredictFailure = F.Unavailable<bool?>("ATA SMART RETURN STATUS", "ata_only");
        foreach (var d in disks.Where(d => !d.Device.Smart.Interface.HasValue))
        {
            var reason = d.SmartReason ?? d.Bus switch
            {
                "usb" => "usb_bridge_no_passthrough",
                "virtual" or "file_backed_virtual" or "storage_spaces" => F.Reasons.NotApplicable,
                "raid" => "raid_controller_hides_disks",
                "nvme" or "sata" or "ata" or "atapi" => F.Reasons.NotReported,
                _ => "no_smart_interface",
            };
            d.Device.Smart.Interface = F.Unavailable<string?>("SMART", reason);
            if (d.Bus == "usb") report.Limitations.Add(new Limitation("storage", "usb_smart_unavailable"));
        }
        report.Limitations = report.Limitations.DistinctBy(l => (l.Section, l.Code)).ToList();
    }

    private async Task<T?> Run<T>(CollectorDiagnostics diag, Func<CollectorDiagnostics, Task<T>> fn, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Task.Run: device IOCTLs and WMI are blocking calls; WaitAsync enforces the timeout even then.
            var r = await Task.Run(() => fn(diag), ct).WaitAsync(options.CollectorTimeout, ct).ConfigureAwait(false);
            diag.Status = diag.Errors.Count == 0 ? "ok" : "partial";
            return r;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { diag.Status = "cancelled"; throw; }
        catch (TimeoutException)
        {
            diag.Status = "timeout";
            diag.Errors.Add($"collector timed out after {options.CollectorTimeout.TotalSeconds:0} s");
        }
        catch (Exception e)
        {
            diag.Status = "failed";
            diag.Errors.Add($"{e.GetType().Name}: {e.Message}");
        }
        finally { diag.DurationMs = sw.ElapsedMilliseconds; }
        return default;
    }

    private bool TryElevated()
    {
        try { return platform.IsElevated(); } catch { return false; }
    }
}
