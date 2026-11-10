using DeviceCheck.Agent.Core.Collection;
using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Core.Normalization;
using DeviceCheck.Agent.Core.Sources;

namespace DeviceCheck.Agent.Collectors;

/// <summary>
/// Physical disks via Windows Storage Management API (MSFT_PhysicalDisk / MSFT_Disk), fallback Win32_DiskDrive.
/// Read-only: no benchmarks, no writes. Raw SMART attributes require admin and are not collected.
/// </summary>
public sealed class StorageCollector(IWmiClient wmi, IPlatformSource platform) : IHardwareCollector
{
    public string Id => "storage";
    private const string PdSrc = "Storage API MSFT_PhysicalDisk";

    public async Task CollectAsync(CollectorContext ctx, CancellationToken ct)
    {
        IReadOnlyList<WmiObject> Empty() => [];
        var pds = await ctx.TryAsync(PdSrc, () => wmi.QueryAsync(Ns.Storage,
            "SELECT DeviceId, FriendlyName, Model, Manufacturer, FirmwareVersion, Size, MediaType, BusType, SpindleSpeed, PhysicalSectorSize, LogicalSectorSize, HealthStatus FROM MSFT_PhysicalDisk", ct), Empty());
        var disks = await ctx.TryAsync("Storage API MSFT_Disk", () => wmi.QueryAsync(Ns.Storage,
            "SELECT Number, IsSystem, IsBoot FROM MSFT_Disk", ct), Empty());
        var drives = await ctx.TryAsync("WMI Win32_DiskDrive", () => wmi.QueryAsync(Ns.Cimv2,
            "SELECT Index, Model, InterfaceType, FirmwareRevision, Size, BytesPerSector FROM Win32_DiskDrive", ct), Empty());

        var list = new List<StorageDevice>();
        if (pds.Count > 0)
        {
            foreach (var p in pds)
            {
                int? number = int.TryParse(p.Str("DeviceId"), out var n) ? n : null;
                var disk = disks.FirstOrDefault(d => d.Int("Number") == number);
                var drive = drives.FirstOrDefault(d => d.Int("Index") == number);
                list.Add(FromPhysicalDisk(p, disk, drive, number));
            }
        }
        else if (drives.Count > 0)
        {
            ctx.Warn("storage_api_unavailable_used_win32_diskdrive");
            list.AddRange(drives.Select(FromDiskDrive));
        }
        ctx.Report.Storage = list.OrderBy(d => d.DiskNumber.Value ?? int.MaxValue).ToList();
        ctx.Limit("storage", "smart_attributes_require_admin");

        var volumes = ctx.Try("System.IO.DriveInfo", platform.GetVolumes, (IReadOnlyList<VolumeData>)[]);
        ctx.Report.Volumes = volumes.Select(v => new VolumeInfo
        {
            Letter = F.Exact<string?>(v.Name.TrimEnd('\\'), "System.IO.DriveInfo"),
            Filesystem = F.Exact(v.Format, "System.IO.DriveInfo"),
            DriveType = F.Exact<string?>(v.DriveType, "System.IO.DriveInfo"),
            SizeBytes = F.Exact<long?>(v.TotalBytes, "System.IO.DriveInfo"),
            FreeBytes = F.Exact<long?>(v.FreeBytes, "System.IO.DriveInfo"),
            UsedBytes = F.Exact<long?>(v.TotalBytes - v.FreeBytes, "System.IO.DriveInfo (size − free)", "derived"),
        }).ToList();
    }

    internal static StorageDevice FromPhysicalDisk(WmiObject p, WmiObject? disk, WmiObject? drive, int? number)
    {
        var model = FirmwareText.Clean(p.Str("Model")) ?? FirmwareText.Clean(p.Str("FriendlyName"));
        var bus = BusType(p.Int("BusType"));
        var media = MediaType(p.Int("MediaType"));
        var maker = FirmwareText.Clean(p.Str("Manufacturer"));
        var spindle = p.Long("SpindleSpeed");

        return new StorageDevice
        {
            Manufacturer = maker is not null
                ? F.Exact<string?>(maker, PdSrc)
                : F.Detected(Vendors.DiskManufacturerFromModel(model), "model name", "from_model_name"),
            Model = F.Exact(model, PdSrc),
            FirmwareVersion = F.Exact(FirmwareText.Clean(p.Str("FirmwareVersion")) ?? drive?.Str("FirmwareRevision"), PdSrc),
            SizeBytes = F.Exact(p.Long("Size") is > 0 and var sz ? sz : (long?)null, PdSrc),
            MediaType = media is not null
                ? F.Exact<string?>(media, PdSrc + ".MediaType")
                : bus == "nvme"
                    ? F.Detected<string?>("ssd", "BusType = NVMe", "nvme_implies_ssd")
                    : F.Unavailable<string?>(PdSrc + ".MediaType", "unspecified"),
            Interface = F.Exact(bus, PdSrc + ".BusType"),
            Rpm = spindle switch
            {
                0 => F.Unavailable<int?>(PdSrc + ".SpindleSpeed", F.Reasons.NotApplicable),
                null or >= uint.MaxValue => F.Unavailable<int?>(PdSrc + ".SpindleSpeed", F.Reasons.NotReported),
                _ => F.Exact<int?>((int)spindle, PdSrc + ".SpindleSpeed"),
            },
            PhysicalSectorBytes = F.Exact(p.Int("PhysicalSectorSize") is > 0 and var ps ? ps : (int?)null, PdSrc),
            LogicalSectorBytes = F.Exact(p.Int("LogicalSectorSize") is > 0 and var ls ? ls : (int?)null, PdSrc),
            FormFactor = F.Unavailable<string?>(PdSrc, F.Reasons.NotReported),
            SmartStatus = F.Exact(Health(p.Int("HealthStatus")), PdSrc + ".HealthStatus", "storage_health_not_raw_smart"),
            IsSystem = F.Exact(disk?.Bool("IsSystem"), "Storage API MSFT_Disk.IsSystem"),
            IsBoot = F.Exact(disk?.Bool("IsBoot"), "Storage API MSFT_Disk.IsBoot"),
            DiskNumber = F.Exact(number, PdSrc + ".DeviceId"),
            Serial = F.Private<string?>(),
        };
    }

    private static StorageDevice FromDiskDrive(WmiObject d)
    {
        const string src = "WMI Win32_DiskDrive";
        var model = FirmwareText.Clean(d.Str("Model"));
        return new StorageDevice
        {
            Manufacturer = F.Detected(Vendors.DiskManufacturerFromModel(model), "model name", "from_model_name"),
            Model = F.Exact(model, src),
            FirmwareVersion = F.Exact(d.Str("FirmwareRevision"), src),
            SizeBytes = F.Exact(d.Long("Size"), src, "rounded_to_cylinders"),
            MediaType = F.Unavailable<string?>(src, F.Reasons.NotReported),
            Interface = F.Exact(d.Str("InterfaceType")?.ToLowerInvariant(), src + ".InterfaceType"),
            LogicalSectorBytes = F.Exact(d.Int("BytesPerSector"), src),
            DiskNumber = F.Exact(d.Int("Index"), src),
            SmartStatus = F.Unavailable<string?>(src, F.Reasons.RequiresAdmin),
        };
    }

    internal static string? MediaType(int? v) => v switch { 3 => "hdd", 4 => "ssd", 5 => "scm", _ => null };

    internal static string? BusType(int? v) => v switch
    {
        1 => "scsi", 2 => "atapi", 3 => "ata", 4 => "ieee1394", 5 => "ssa", 6 => "fibre_channel", 7 => "usb", 8 => "raid",
        9 => "iscsi", 10 => "sas", 11 => "sata", 12 => "sd", 13 => "mmc", 14 => "virtual", 15 => "file_backed_virtual",
        16 => "storage_spaces", 17 => "nvme", 18 => "scm", 19 => "ufs",
        _ => null,
    };

    internal static string? Health(int? v) => v switch { 0 => "healthy", 1 => "warning", 2 => "unhealthy", 5 => "unknown", _ => null };
}
