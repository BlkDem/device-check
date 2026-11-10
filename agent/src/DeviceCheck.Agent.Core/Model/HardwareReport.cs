namespace DeviceCheck.Agent.Core.Model;

// HardwareReport v1.0 — the same contract as web/lib/types.ts.
// Property names are serialized in snake_case. Keys shared with the web contract keep their exact names;
// everything else is an additive extension (the web UI ignores keys it does not know).
// Units are always part of the name: *_bytes (binary bytes), *_mhz, *_mts (mega-transfers/s), *_mv, *_mm, *_in, *_seconds.

public sealed class HardwareReport
{
    public string SchemaVersion { get; init; } = SchemaInfo.Version;
    public ScanInfo Scan { get; set; } = new();
    public SystemSection System { get; set; } = new();
    public CpuSection Cpu { get; set; } = new();
    public MotherboardSection Motherboard { get; set; } = new();
    public MemorySection Memory { get; set; } = new();
    public List<GpuAdapter> Gpu { get; set; } = [];
    public List<StorageDevice> Storage { get; set; } = [];
    public List<VolumeInfo> Volumes { get; set; } = [];
    public List<DisplayDevice> Displays { get; set; } = [];
    /// <summary>Browser environment — only produced by the web collector, always null for the agent.</summary>
    public object? Browser { get; set; }
    public List<Limitation> Limitations { get; set; } = [];
    public ScanDiagnostics Diagnostics { get; set; } = new();
}

public static class SchemaInfo
{
    public const string Version = "1.0";
}

public sealed class ScanInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>"browser" | "agent" (web contract).</summary>
    public string Source { get; set; } = "agent";
    /// <summary>OS family: Windows | Linux | macOS ...</summary>
    public string? Platform { get; set; }
    /// <summary>e.g. "Windows 11".</summary>
    public string? PlatformVersion { get; set; }
    public string? AgentVersion { get; set; }
    public CollectorInfo Collector { get; set; } = new();
}

public sealed class CollectorInfo
{
    public string Name { get; set; } = "device-check-agent-windows";
    public string Version { get; set; } = "0.0.0";
}

public sealed record Resolution(int Width, int Height);

public sealed record Limitation(string Section, string Code);

public sealed class SystemSection
{
    public Field<string?> OsName { get; set; } = F.None<string?>();
    public Field<string?> OsVersion { get; set; } = F.None<string?>();
    public Field<string?> OsBuild { get; set; } = F.None<string?>();
    public Field<string?> OsEdition { get; set; } = F.None<string?>();
    public Field<string?> Architecture { get; set; } = F.None<string?>();
    public Field<long?> UptimeSeconds { get; set; } = F.None<long?>();
    public Field<DateTimeOffset?> BootTime { get; set; } = F.None<DateTimeOffset?>();
    public Field<DateTimeOffset?> InstallDate { get; set; } = F.None<DateTimeOffset?>();
    public Field<string?> Hostname { get; set; } = F.Private<string?>();
    public Field<string?> DeviceType { get; set; } = F.None<string?>();
    public Field<string?> Language { get; set; } = F.None<string?>();
    public Field<string?> Timezone { get; set; } = F.None<string?>();
}

public sealed class CpuSection
{
    public Field<string?> Vendor { get; set; } = F.None<string?>();
    public Field<string?> Model { get; set; } = F.None<string?>();
    public Field<string?> Family { get; set; } = F.None<string?>();
    public Field<string?> Generation { get; set; } = F.None<string?>();
    public Field<string?> Microarchitecture { get; set; } = F.None<string?>();
    public Field<string?> Codename { get; set; } = F.None<string?>();
    public Field<string?> CpuidSignature { get; set; } = F.None<string?>();
    public Field<string?> Architecture { get; set; } = F.None<string?>();
    public Field<int?> Sockets { get; set; } = F.None<int?>();
    public Field<int?> CoresPhysical { get; set; } = F.None<int?>();
    public Field<int?> CoresPerformance { get; set; } = F.None<int?>();
    public Field<int?> CoresEfficiency { get; set; } = F.None<int?>();
    public Field<int?> ThreadsLogical { get; set; } = F.None<int?>();
    public Field<int?> BaseClockMhz { get; set; } = F.None<int?>();
    public Field<int?> MaxClockMhz { get; set; } = F.None<int?>();
    public Field<int?> CurrentClockMhz { get; set; } = F.None<int?>();
    public Field<long?> CacheL1Kb { get; set; } = F.None<long?>();
    public Field<long?> CacheL2Kb { get; set; } = F.None<long?>();
    public Field<long?> CacheL3Kb { get; set; } = F.None<long?>();
    /// <summary>Human-readable cache layout, e.g. "L1d 8 × 32 KB".</summary>
    public Field<List<string>?> CacheLayout { get; set; } = F.None<List<string>?>();
    public Field<List<string>?> Instructions { get; set; } = F.None<List<string>?>();
    /// <summary>Hardware virtualization (VT-x / AMD-V) supported by the CPU.</summary>
    public Field<bool?> Virtualization { get; set; } = F.None<bool?>();
    /// <summary>Virtualization enabled in firmware.</summary>
    public Field<bool?> VirtualizationEnabled { get; set; } = F.None<bool?>();
    public Field<int?> TdpW { get; set; } = F.None<int?>();
    public Field<int?> ProcessNm { get; set; } = F.None<int?>();
    /// <summary>Process node label as marketed: "TSMC N4", "Intel 7".</summary>
    public Field<string?> ProcessNode { get; set; } = F.None<string?>();
    /// <summary>Never collected (unique identifier).</summary>
    public Field<string?> ProcessorId { get; set; } = F.Private<string?>();
}

public sealed class MotherboardSection
{
    public Field<string?> Manufacturer { get; set; } = F.None<string?>();
    public Field<string?> Model { get; set; } = F.None<string?>();
    public Field<string?> Version { get; set; } = F.None<string?>();
    public Field<string?> Chipset { get; set; } = F.None<string?>();
    /// <summary>"uefi" | "bios".</summary>
    public Field<string?> FirmwareType { get; set; } = F.None<string?>();
    public Field<string?> BiosVendor { get; set; } = F.None<string?>();
    public Field<string?> BiosVersion { get; set; } = F.None<string?>();
    /// <summary>ISO date (yyyy-MM-dd).</summary>
    public Field<string?> BiosDate { get; set; } = F.None<string?>();
    public Field<string?> SmbiosVersion { get; set; } = F.None<string?>();
    public Field<string?> SystemManufacturer { get; set; } = F.None<string?>();
    public Field<string?> SystemModel { get; set; } = F.None<string?>();
    public Field<string?> SystemFamily { get; set; } = F.None<string?>();
    public Field<string?> Serial { get; set; } = F.Private<string?>();
}

public sealed class MemorySection
{
    /// <summary>Physically installed RAM (firmware view).</summary>
    public Field<long?> TotalBytes { get; set; } = F.None<long?>();
    /// <summary>RAM visible to Windows (installed minus hardware reserved).</summary>
    public Field<long?> UsableBytes { get; set; } = F.None<long?>();
    public Field<string?> MemoryType { get; set; } = F.None<string?>();
    /// <summary>Current (configured) data rate in MT/s.</summary>
    public Field<int?> SpeedMts { get; set; } = F.None<int?>();
    /// <summary>I/O clock in MHz = data rate / 2 (DDR). Derived.</summary>
    public Field<int?> ClockMhz { get; set; } = F.None<int?>();
    public Field<int?> MaxSpeedMts { get; set; } = F.None<int?>();
    /// <summary>Socketed slots per SMBIOS (soldered memory excluded). May include non-existent slots.</summary>
    public Field<int?> SlotsTotal { get; set; } = F.None<int?>();
    public Field<int?> SlotsUsed { get; set; } = F.None<int?>();
    public Field<int?> SlotsFree { get; set; } = F.None<int?>();
    public Field<int?> ModulesDetected { get; set; } = F.None<int?>();
    public Field<long?> SolderedBytes { get; set; } = F.None<long?>();
    /// <summary>"single" | "dual" | "quad" | "unknown".</summary>
    public Field<string?> Channels { get; set; } = F.None<string?>();
    public Field<int?> BusWidthBits { get; set; } = F.None<int?>();
    public Field<bool?> Ecc { get; set; } = F.None<bool?>();
    public Field<long?> MaxCapacityBytes { get; set; } = F.None<long?>();
    /// <summary>Populated memory devices.</summary>
    public List<MemoryModule> Modules { get; set; } = [];
    /// <summary>Raw SMBIOS Type 17 view: every memory device record, populated or not.</summary>
    public List<SmbiosMemorySlot> SmbiosSlots { get; set; } = [];
}

public sealed class MemoryModule
{
    /// <summary>SMBIOS Device Locator, e.g. "DIMM_A1".</summary>
    public Field<string?> Slot { get; set; } = F.None<string?>();
    public Field<string?> BankLabel { get; set; } = F.None<string?>();
    public Field<bool?> Soldered { get; set; } = F.None<bool?>();
    public Field<string?> Manufacturer { get; set; } = F.None<string?>();
    public Field<string?> Model { get; set; } = F.None<string?>();
    public Field<string?> Serial { get; set; } = F.Private<string?>();
    public Field<long?> SizeBytes { get; set; } = F.None<long?>();
    public Field<string?> MemoryType { get; set; } = F.None<string?>();
    public Field<int?> SmbiosMemoryType { get; set; } = F.None<int?>();
    /// <summary>Configured (current) data rate, MT/s.</summary>
    public Field<int?> SpeedMts { get; set; } = F.None<int?>();
    /// <summary>Current I/O clock MHz (derived = speed_mts / 2).</summary>
    public Field<int?> ClockMhz { get; set; } = F.None<int?>();
    /// <summary>Maximum rated data rate of the module, MT/s.</summary>
    public Field<int?> RatedSpeedMts { get; set; } = F.None<int?>();
    public Field<int?> DataWidthBits { get; set; } = F.None<int?>();
    public Field<int?> TotalWidthBits { get; set; } = F.None<int?>();
    public Field<int?> Rank { get; set; } = F.None<int?>();
    public Field<string?> FormFactor { get; set; } = F.None<string?>();
    public Field<string?> PartNumber { get; set; } = F.None<string?>();
    /// <summary>Configured voltage, mV.</summary>
    public Field<int?> VoltageMv { get; set; } = F.None<int?>();
    public Field<int?> MinVoltageMv { get; set; } = F.None<int?>();
    public Field<int?> MaxVoltageMv { get; set; } = F.None<int?>();
}

public sealed class SmbiosMemorySlot
{
    public string? Locator { get; set; }
    public string? BankLabel { get; set; }
    public bool Populated { get; set; }
    public long? SizeBytes { get; set; }
    public string? FormFactor { get; set; }
    public bool Soldered { get; set; }
    public string? Channel { get; set; }
}

public sealed class GpuAdapter
{
    public Field<string?> Vendor { get; set; } = F.None<string?>();
    public Field<string?> Model { get; set; } = F.None<string?>();
    public Field<string?> Description { get; set; } = F.None<string?>();
    /// <summary>GPU architecture (RDNA 3, Ada Lovelace...): no reliable OS source, kept for the web contract.</summary>
    public Field<string?> Architecture { get; set; } = F.Unavailable<string?>("—", F.Reasons.NoReliableSource);
    /// <summary>"integrated" | "discrete" | "virtual" | "remote" | "software".</summary>
    public Field<string?> AdapterType { get; set; } = F.None<string?>();
    public Field<string?> VendorId { get; set; } = F.None<string?>();
    public Field<string?> DeviceId { get; set; } = F.None<string?>();
    public Field<string?> SubsystemId { get; set; } = F.None<string?>();
    public Field<string?> Revision { get; set; } = F.None<string?>();
    public Field<string?> PciDeviceId { get; set; } = F.None<string?>();
    /// <summary>Memory local to the GPU (or BIOS-carved UMA frame buffer on integrated GPUs).</summary>
    public Field<long?> DedicatedVramBytes { get; set; } = F.None<long?>();
    public Field<long?> DedicatedSystemMemoryBytes { get; set; } = F.None<long?>();
    /// <summary>System RAM the GPU may borrow. Not VRAM.</summary>
    public Field<long?> SharedMemoryBytes { get; set; } = F.None<long?>();
    public Field<bool?> Uma { get; set; } = F.None<bool?>();
    public Field<string?> DriverVersion { get; set; } = F.None<string?>();
    public Field<string?> DriverDate { get; set; } = F.None<string?>();
    public Field<List<string>?> Apis { get; set; } = F.None<List<string>?>();
}

public sealed class StorageDevice
{
    public Field<string?> Manufacturer { get; set; } = F.None<string?>();
    public Field<string?> Model { get; set; } = F.None<string?>();
    public Field<string?> FirmwareVersion { get; set; } = F.None<string?>();
    public Field<long?> SizeBytes { get; set; } = F.None<long?>();
    /// <summary>"ssd" | "hdd" | "scm" | "unknown".</summary>
    public Field<string?> MediaType { get; set; } = F.None<string?>();
    /// <summary>Bus: "nvme" | "sata" | "usb" | "sas" | "raid" | "virtual" | ...</summary>
    public Field<string?> Interface { get; set; } = F.None<string?>();
    public Field<int?> Rpm { get; set; } = F.None<int?>();
    public Field<int?> PhysicalSectorBytes { get; set; } = F.None<int?>();
    public Field<int?> LogicalSectorBytes { get; set; } = F.None<int?>();
    public Field<string?> FormFactor { get; set; } = F.None<string?>();
    /// <summary>"healthy" | "warning" | "unhealthy" | "unknown".</summary>
    public Field<string?> SmartStatus { get; set; } = F.None<string?>();
    public Field<bool?> IsSystem { get; set; } = F.None<bool?>();
    public Field<bool?> IsBoot { get; set; } = F.None<bool?>();
    public Field<int?> DiskNumber { get; set; } = F.None<int?>();
    public Field<string?> Serial { get; set; } = F.Private<string?>();
}

public sealed class VolumeInfo
{
    public Field<string?> Letter { get; set; } = F.None<string?>();
    public Field<string?> Filesystem { get; set; } = F.None<string?>();
    public Field<string?> DriveType { get; set; } = F.None<string?>();
    public Field<long?> SizeBytes { get; set; } = F.None<long?>();
    public Field<long?> FreeBytes { get; set; } = F.None<long?>();
    public Field<long?> UsedBytes { get; set; } = F.None<long?>();
}

public sealed class DisplayDevice
{
    public Field<string?> Manufacturer { get; set; } = F.None<string?>();
    public Field<string?> ManufacturerCode { get; set; } = F.None<string?>();
    public Field<string?> Model { get; set; } = F.None<string?>();
    public Field<string?> ProductCode { get; set; } = F.None<string?>();
    public Field<Resolution?> Resolution { get; set; } = F.None<Resolution?>();
    public Field<Resolution?> NativeResolution { get; set; } = F.None<Resolution?>();
    public Field<double?> RefreshHz { get; set; } = F.None<double?>();
    public Field<int?> PhysicalWidthMm { get; set; } = F.None<int?>();
    public Field<int?> PhysicalHeightMm { get; set; } = F.None<int?>();
    public Field<double?> DiagonalIn { get; set; } = F.None<double?>();
    public Field<bool?> Hdr { get; set; } = F.None<bool?>();
    public Field<bool?> HdrEnabled { get; set; } = F.None<bool?>();
    public Field<string?> Connection { get; set; } = F.None<string?>();
    public Field<bool?> IsPrimary { get; set; } = F.None<bool?>();
    public Field<int?> ManufactureYear { get; set; } = F.None<int?>();
    public Field<string?> Serial { get; set; } = F.Private<string?>();
}

public sealed class ScanDiagnostics
{
    public long DurationMs { get; set; }
    public bool Elevated { get; set; }
    public List<CollectorDiagnostics> Collectors { get; set; } = [];
}

public sealed class CollectorDiagnostics
{
    public string Id { get; set; } = "";
    /// <summary>"ok" | "partial" | "failed" | "timeout" | "cancelled".</summary>
    public string Status { get; set; } = "ok";
    public long DurationMs { get; set; }
    public List<string> Sources { get; set; } = [];
    /// <summary>Wall time per source call, ms (performance diagnostics).</summary>
    public Dictionary<string, long> SourceTimingsMs { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}
