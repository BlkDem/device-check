using DeviceCheck.Agent.Core.Collection;
using DeviceCheck.Agent.Core.Model;

namespace DeviceCheck.Agent.Monitoring.Core;

/// <summary>How much to collect: <see cref="Quick"/> for frequent sampling (free space, temperature), <see cref="Full"/> adds SMART.</summary>
public enum SampleKind
{
    Quick,
    Full,
}

/// <summary>A health monitor of one subsystem (storage now; CPU, battery... later). Read-only by contract.</summary>
public interface IHealthMonitor<TReport>
{
    string Id { get; }
    Task<TReport> SampleAsync(SampleKind kind, CancellationToken ct = default);
}

/// <summary>
/// One storage data source family. Runs after the disk inventory, fills its part of the devices and records
/// its own errors; anything that escapes is caught by the monitor and never stops the other collectors.
/// </summary>
public interface IStorageHealthCollector
{
    string Id { get; }

    /// <summary>Collected on <see cref="SampleKind.Full"/> only (e.g. SMART: queried every 5–15 minutes at most).</summary>
    bool FullOnly { get; }

    Task CollectAsync(StorageHealthContext ctx, CancellationToken ct);
}

/// <summary>Disk as found by the inventory, with the internal facts other collectors need (never serialized).</summary>
public sealed class DiskEntry(int number, StorageHealthDevice device)
{
    public int Number { get; } = number;
    public StorageHealthDevice Device { get; } = device;
    public string? Bus { get; init; }
    /// <summary>MSFT_PhysicalDisk.DeviceId (key of MSFT_StorageReliabilityCounter).</summary>
    public string? StorageDeviceId { get; init; }
    /// <summary>Win32_DiskDrive.PNPDeviceID (key of the root\wmi SMART classes).</summary>
    public string? PnpDeviceId { get; init; }
    /// <summary>Why no SMART interface answered (requires_admin, api_unsupported...), set by the SMART collectors.</summary>
    public string? SmartReason { get; set; }
}

public sealed class StorageHealthContext(StorageHealthReport report, CollectorDiagnostics diagnostics, IReadOnlyList<DiskEntry> disks)
    : CollectionContext(report.Limitations, diagnostics)
{
    public StorageHealthReport Report { get; } = report;
    public IReadOnlyList<DiskEntry> Disks { get; } = disks;
}

public sealed record DeviceTemperatureData(int? CurrentCelsius, int? WarningCelsius, int? CriticalCelsius);

/// <summary>
/// Low-level, read-only queries on a physical disk (IOCTL_STORAGE_QUERY_PROPERTY on Windows).
/// Every method throws <see cref="StorageAccessException"/> when the device cannot be queried.
/// </summary>
public interface IStorageDeviceSource
{
    /// <summary>NVMe Health Information log page (02h), 512 bytes.</summary>
    byte[] NvmeHealthLog(int diskNumber);

    /// <summary>NVMe Identify Controller data structure, 4096 bytes.</summary>
    byte[] NvmeIdentifyController(int diskNumber);

    /// <summary>Temperature reported by the storage driver (StorageDeviceTemperatureProperty), if supported.</summary>
    DeviceTemperatureData? DeviceTemperature(int diskNumber);
}

/// <summary>A device query failed; <see cref="Reason"/> is a Field reason code (requires_admin, api_unsupported, ...).</summary>
public sealed class StorageAccessException(string reason, string message, int win32Error = 0) : Exception(message)
{
    public string Reason { get; } = reason;
    public int Win32Error { get; } = win32Error;

    public static string ReasonFor(int win32Error) => win32Error switch
    {
        5 => F.Reasons.RequiresAdmin,                // ERROR_ACCESS_DENIED
        1 or 50 or 87 or 1117 => F.Reasons.Unsupported, // INVALID_FUNCTION, NOT_SUPPORTED, INVALID_PARAMETER, IO_DEVICE
        2 or 3 => "device_not_accessible",           // FILE/PATH_NOT_FOUND (sandboxed or removed)
        21 => "device_not_ready",
        _ => F.Reasons.Error,
    };
}
