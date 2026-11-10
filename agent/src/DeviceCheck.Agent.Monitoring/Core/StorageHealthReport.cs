using DeviceCheck.Agent.Core.Model;

namespace DeviceCheck.Agent.Monitoring.Core;

// StorageHealthReport v1.0 — System Health Monitor, storage part.
// Built from the same blocks as HardwareReport: Field<T> (value/source/confidence/collected_at/reason/note),
// ScanInfo, Limitation and per-collector diagnostics. Serialized in snake_case; every value may be null.
// Units are part of the names: *_bytes, *_celsius, *_percent, *_hours, *_minutes.

public static class StorageHealthSchema
{
    public const string Version = "1.0";
    public const string Kind = "storage_health";
}

public sealed class StorageHealthReport
{
    public string SchemaVersion { get; init; } = StorageHealthSchema.Version;
    public string Kind { get; init; } = StorageHealthSchema.Kind;
    public DateTimeOffset CollectedAt { get; set; } = DateTimeOffset.UtcNow;
    public ScanInfo Scan { get; set; } = new();
    public StorageHealthSection StorageHealth { get; set; } = new();
    public List<Limitation> Limitations { get; set; } = [];
    public ScanDiagnostics Diagnostics { get; set; } = new();
}

public sealed class StorageHealthSection
{
    public List<StorageHealthDevice> Devices { get; set; } = [];

    /// <summary>Volumes that do not belong to a reported physical disk (network, virtual file systems, subst).</summary>
    public List<VolumeHealth> OtherVolumes { get; set; } = [];

    /// <summary>Alert Engine output (stage 2). Always empty in stage 1.</summary>
    public List<HealthAlert> Alerts { get; set; } = [];
}

public sealed class StorageHealthDevice
{
    /// <summary>Stable internal id: hash of the disk's unique id. Never the serial number itself.</summary>
    public string Id { get; set; } = "";
    public Field<int?> DiskNumber { get; set; } = F.None<int?>();
    public Field<string?> Model { get; set; } = F.None<string?>();
    public Field<string?> Manufacturer { get; set; } = F.None<string?>();
    public Field<string?> FirmwareVersion { get; set; } = F.None<string?>();
    /// <summary>"nvme" | "sata_ssd" | "sata_hdd" | "sata" | "usb" | "sas" | "scsi" | "raid" | "storage_spaces" | "virtual" | "sd" | "unknown".</summary>
    public Field<string?> Type { get; set; } = F.None<string?>();
    public Field<string?> BusType { get; set; } = F.None<string?>();
    /// <summary>"ssd" | "hdd" | "scm".</summary>
    public Field<string?> MediaType { get; set; } = F.None<string?>();
    public Field<long?> CapacityBytes { get; set; } = F.None<long?>();
    public Field<bool?> IsSystem { get; set; } = F.None<bool?>();
    public Field<bool?> IsBoot { get; set; } = F.None<bool?>();
    public Field<string?> Serial { get; set; } = F.Private<string?>();
    public DeviceHealth Health { get; set; } = new();
    public DeviceTemperature Temperature { get; set; } = new();
    public SmartInfo Smart { get; set; } = new();
    public List<VolumeHealth> Volumes { get; set; } = [];
}

public sealed class DeviceHealth
{
    /// <summary>Device Check assessment: healthy | good | warning | critical | unknown. Produced by the Health Engine (stage 2).</summary>
    public Field<string?> Status { get; set; } = F.Unavailable<string?>("health engine", "not_evaluated");
    /// <summary>Health reported by Windows Storage Management: healthy | warning | unhealthy | unknown.</summary>
    public Field<string?> OsStatus { get; set; } = F.None<string?>();
    public Field<List<string>?> OperationalStatus { get; set; } = F.None<List<string>?>();
    /// <summary>ATA SMART RETURN STATUS through the storage driver: true = a threshold is exceeded.</summary>
    public Field<bool?> PredictFailure { get; set; } = F.None<bool?>();
}

public sealed class DeviceTemperature
{
    public Field<int?> CurrentCelsius { get; set; } = F.None<int?>();
    /// <summary>Manufacturer warning threshold (NVMe WCTEMP or driver-reported). Never a generic guess.</summary>
    public Field<int?> WarningCelsius { get; set; } = F.None<int?>();
    /// <summary>Manufacturer critical threshold (NVMe CCTEMP or driver-reported).</summary>
    public Field<int?> CriticalCelsius { get; set; } = F.None<int?>();
    /// <summary>Highest temperature recorded by the drive (reliability counter).</summary>
    public Field<int?> MaxRecordedCelsius { get; set; } = F.None<int?>();
    public List<TemperatureSensor> Sensors { get; set; } = [];
}

public sealed record TemperatureSensor(string Name, Field<int?> Celsius);

public sealed class SmartInfo
{
    /// <summary>Where the SMART data came from: nvme_health_log | ata_smart | storage_reliability_counter.</summary>
    public Field<string?> Interface { get; set; } = F.None<string?>();

    // Normalized key indicators, each taken from the most direct source available.
    /// <summary>NVMe "Percentage Used": share of rated endurance consumed (may exceed 100). Not "life remaining".</summary>
    public Field<int?> PercentageUsed { get; set; } = F.None<int?>();
    public Field<int?> AvailableSparePercent { get; set; } = F.None<int?>();
    public Field<int?> AvailableSpareThresholdPercent { get; set; } = F.None<int?>();
    public Field<long?> PowerOnHours { get; set; } = F.None<long?>();
    public Field<long?> PowerCycles { get; set; } = F.None<long?>();
    public Field<long?> UnsafeShutdowns { get; set; } = F.None<long?>();
    public Field<long?> MediaErrors { get; set; } = F.None<long?>();
    public Field<long?> DataReadBytes { get; set; } = F.None<long?>();
    public Field<long?> DataWrittenBytes { get; set; } = F.None<long?>();
    public Field<long?> ReallocatedSectors { get; set; } = F.None<long?>();
    public Field<long?> PendingSectors { get; set; } = F.None<long?>();
    public Field<long?> UncorrectableSectors { get; set; } = F.None<long?>();
    public Field<long?> CrcErrors { get; set; } = F.None<long?>();

    // Raw views, as returned by each interface.
    public NvmeHealthInfo? Nvme { get; set; }
    public AtaSmartInfo? Ata { get; set; }
    public ReliabilityCounters? Reliability { get; set; }
}

/// <summary>NVMe Health Information log page (02h), NVMe Base Specification 1.4+.</summary>
public sealed class NvmeHealthInfo
{
    public Field<int?> CriticalWarning { get; set; } = F.None<int?>();
    public Field<List<string>?> CriticalWarningFlags { get; set; } = F.None<List<string>?>();
    public Field<int?> CompositeTemperatureCelsius { get; set; } = F.None<int?>();
    public Field<int?> AvailableSparePercent { get; set; } = F.None<int?>();
    public Field<int?> AvailableSpareThresholdPercent { get; set; } = F.None<int?>();
    public Field<int?> PercentageUsed { get; set; } = F.None<int?>();
    /// <summary>In units of 1000 × 512 bytes, as reported. See <see cref="SmartInfo.DataReadBytes"/> for bytes.</summary>
    public Field<long?> DataUnitsRead { get; set; } = F.None<long?>();
    public Field<long?> DataUnitsWritten { get; set; } = F.None<long?>();
    public Field<long?> HostReadCommands { get; set; } = F.None<long?>();
    public Field<long?> HostWriteCommands { get; set; } = F.None<long?>();
    public Field<long?> ControllerBusyTimeMinutes { get; set; } = F.None<long?>();
    public Field<long?> PowerCycles { get; set; } = F.None<long?>();
    public Field<long?> PowerOnHours { get; set; } = F.None<long?>();
    public Field<long?> UnsafeShutdowns { get; set; } = F.None<long?>();
    public Field<long?> MediaErrors { get; set; } = F.None<long?>();
    public Field<long?> ErrorLogEntries { get; set; } = F.None<long?>();
    public Field<long?> WarningTemperatureTimeMinutes { get; set; } = F.None<long?>();
    public Field<long?> CriticalTemperatureTimeMinutes { get; set; } = F.None<long?>();
}

/// <summary>ATA SMART READ DATA + READ THRESHOLDS as passed through by the storage driver.</summary>
public sealed class AtaSmartInfo
{
    public Field<int?> Revision { get; set; } = F.None<int?>();
    public List<SmartAttribute> Attributes { get; set; } = [];
}

public sealed class SmartAttribute
{
    public int Id { get; set; }
    /// <summary>Common name; null when the id has no established meaning.</summary>
    public string? Name { get; set; }
    /// <summary>"standard" (same meaning across vendors) | "vendor_specific" | "unknown".</summary>
    public string Semantics { get; set; } = "unknown";
    public int Flags { get; set; }
    public bool PreFailure { get; set; }
    public Field<int?> Normalized { get; set; } = F.None<int?>();
    public Field<int?> Worst { get; set; } = F.None<int?>();
    public Field<int?> Threshold { get; set; } = F.None<int?>();
    /// <summary>48-bit raw value as stored by the drive.</summary>
    public Field<long?> Raw { get; set; } = F.None<long?>();
    /// <summary>Raw value interpreted by the attribute's standard meaning; unavailable for vendor-specific attributes.</summary>
    public Field<long?> Value { get; set; } = F.None<long?>();
}

/// <summary>Windows MSFT_StorageReliabilityCounter (driver-normalized SMART / NVMe counters).</summary>
public sealed class ReliabilityCounters
{
    public Field<int?> TemperatureCelsius { get; set; } = F.None<int?>();
    public Field<int?> TemperatureMaxCelsius { get; set; } = F.None<int?>();
    /// <summary>Percentage of rated endurance used, as reported by Windows.</summary>
    public Field<int?> WearPercent { get; set; } = F.None<int?>();
    public Field<long?> PowerOnHours { get; set; } = F.None<long?>();
    public Field<long?> StartStopCycles { get; set; } = F.None<long?>();
    public Field<long?> LoadUnloadCycles { get; set; } = F.None<long?>();
    public Field<long?> ReadErrorsTotal { get; set; } = F.None<long?>();
    public Field<long?> ReadErrorsCorrected { get; set; } = F.None<long?>();
    public Field<long?> ReadErrorsUncorrected { get; set; } = F.None<long?>();
    public Field<long?> WriteErrorsTotal { get; set; } = F.None<long?>();
    public Field<long?> WriteErrorsCorrected { get; set; } = F.None<long?>();
    public Field<long?> WriteErrorsUncorrected { get; set; } = F.None<long?>();
    public Field<long?> ReadLatencyMaxMs { get; set; } = F.None<long?>();
    public Field<long?> WriteLatencyMaxMs { get; set; } = F.None<long?>();
    public Field<long?> FlushLatencyMaxMs { get; set; } = F.None<long?>();
}

public sealed class VolumeHealth
{
    /// <summary>Stable id: hash of the volume GUID path, or the drive letter when only that is known.</summary>
    public string Id { get; set; } = "";
    public Field<string?> Letter { get; set; } = F.None<string?>();
    public Field<string?> Label { get; set; } = F.None<string?>();
    public Field<string?> Filesystem { get; set; } = F.None<string?>();
    /// <summary>"fixed" | "removable" | "remote" | "cdrom" | "ramdisk".</summary>
    public Field<string?> DriveType { get; set; } = F.None<string?>();
    public Field<long?> TotalBytes { get; set; } = F.None<long?>();
    public Field<long?> FreeBytes { get; set; } = F.None<long?>();
    public Field<long?> UsedBytes { get; set; } = F.None<long?>();
    public Field<double?> FreePercent { get; set; } = F.None<double?>();
    public Field<double?> UsedPercent { get; set; } = F.None<double?>();
    /// <summary>Physical disks the volume lives on (several for spanned / Storage Spaces volumes).</summary>
    public Field<List<int>?> DiskNumbers { get; set; } = F.None<List<int>?>();
    /// <summary>File system health reported by Windows: healthy | warning | unhealthy.</summary>
    public Field<string?> HealthStatus { get; set; } = F.None<string?>();
}

/// <summary>Alert Engine record (stage 2). Declared now so the schema slot is stable.</summary>
public sealed class HealthAlert
{
    public string Id { get; set; } = "";
    /// <summary>low_disk_space | critical_disk_space | high_temperature | smart_warning | smart_critical | ssd_wear | media_errors | smart_unavailable.</summary>
    public string Type { get; set; } = "";
    /// <summary>info | warning | critical | emergency.</summary>
    public string Severity { get; set; } = "";
    public DateTimeOffset DetectedAt { get; set; }
    public string Source { get; set; } = "";
    public string? DeviceId { get; set; }
    public string? VolumeId { get; set; }
    public string Description { get; set; } = "";
    public string Recommendation { get; set; } = "";
    /// <summary>active | resolved | acknowledged.</summary>
    public string Status { get; set; } = "active";
}
