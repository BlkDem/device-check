using System.Security.Cryptography;
using System.Text;
using DeviceCheck.Agent.Core.Collection;
using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Core.Normalization;
using DeviceCheck.Agent.Core.Sources;
using DeviceCheck.Agent.Monitoring.Core;

namespace DeviceCheck.Agent.Monitoring.Storage;

/// <summary>
/// Physical disk inventory: Windows Storage Management API (MSFT_PhysicalDisk + MSFT_Disk), Win32_DiskDrive for
/// PnP ids. Falls back to Win32_DiskDrive alone when the Storage API is unavailable (no health status then).
/// </summary>
public sealed class DiskInventory(IWmiClient wmi)
{
    public const string StorageNs = @"root\Microsoft\Windows\Storage";
    public const string Cimv2 = @"root\cimv2";
    private const string PdSrc = "Storage API MSFT_PhysicalDisk";

    public async Task<List<DiskEntry>> CollectAsync(CollectionContext ctx, CancellationToken ct)
    {
        IReadOnlyList<WmiObject> Empty() => [];
        var pds = await ctx.TryAsync(PdSrc, () => wmi.QueryAsync(StorageNs,
            "SELECT DeviceId, FriendlyName, Model, Manufacturer, FirmwareVersion, Size, MediaType, BusType, HealthStatus, OperationalStatus, UniqueId, SerialNumber FROM MSFT_PhysicalDisk", ct), Empty());
        var disks = await ctx.TryAsync("Storage API MSFT_Disk", () => wmi.QueryAsync(StorageNs,
            "SELECT Number, IsSystem, IsBoot FROM MSFT_Disk", ct), Empty());
        var drives = await ctx.TryAsync("WMI Win32_DiskDrive", () => wmi.QueryAsync(Cimv2,
            "SELECT Index, Model, PNPDeviceID, InterfaceType, Size, FirmwareRevision FROM Win32_DiskDrive", ct), Empty());

        var list = new List<DiskEntry>();
        if (pds.Count > 0)
        {
            foreach (var p in pds)
            {
                if (!int.TryParse(p.Str("DeviceId"), out var n)) continue; // pooled disks without a disk number
                list.Add(FromPhysicalDisk(p, n, disks.FirstOrDefault(d => d.Int("Number") == n), drives.FirstOrDefault(d => d.Int("Index") == n)));
            }
        }
        else if (drives.Count > 0)
        {
            ctx.Warn("storage_api_unavailable_used_win32_diskdrive");
            ctx.Limit("storage", "storage_api_unavailable");
            list.AddRange(drives.Where(d => d.Int("Index") is not null).Select(FromDiskDrive));
        }
        return list.OrderBy(d => d.Number).ToList();
    }

    internal static DiskEntry FromPhysicalDisk(WmiObject p, int number, WmiObject? disk, WmiObject? drive)
    {
        var model = FirmwareText.Clean(p.Str("Model")) ?? FirmwareText.Clean(p.Str("FriendlyName"));
        var bus = StorageEnums.BusType(p.Int("BusType"));
        var media = StorageEnums.MediaType(p.Int("MediaType"));
        var maker = FirmwareText.Clean(p.Str("Manufacturer"));
        var device = new StorageHealthDevice
        {
            Id = StableId("disk", p.Str("UniqueId") ?? p.Str("SerialNumber") ?? $"{model}|{p.Long("Size")}|{number}"),
            DiskNumber = F.Exact<int?>(number, PdSrc + ".DeviceId"),
            Model = F.Exact(model, PdSrc),
            Manufacturer = maker is not null
                ? F.Exact<string?>(maker, PdSrc)
                : F.Detected(Vendors.DiskManufacturerFromModel(model), "model name", "from_model_name"),
            FirmwareVersion = F.Exact(FirmwareText.Clean(p.Str("FirmwareVersion")) ?? drive?.Str("FirmwareRevision"), PdSrc),
            BusType = F.Exact(bus, PdSrc + ".BusType"),
            MediaType = media is not null
                ? F.Exact<string?>(media, PdSrc + ".MediaType")
                : bus == "nvme" ? F.Detected<string?>("ssd", "BusType = NVMe", "nvme_implies_ssd") : F.Unavailable<string?>(PdSrc + ".MediaType", "unspecified"),
            CapacityBytes = F.Exact(p.Long("Size") is > 0 and var sz ? sz : (long?)null, PdSrc + ".Size"),
            IsSystem = F.Exact(disk?.Bool("IsSystem"), "Storage API MSFT_Disk.IsSystem"),
            IsBoot = F.Exact(disk?.Bool("IsBoot"), "Storage API MSFT_Disk.IsBoot"),
        };
        device.Type = Classify(bus, media ?? (bus == "nvme" ? "ssd" : null), PdSrc + ".BusType + MediaType");
        device.Health.OsStatus = F.Exact(StorageEnums.Health(p.Int("HealthStatus")), PdSrc + ".HealthStatus", "windows_health_not_smart");
        var ops = p.Longs("OperationalStatus").Select(StorageEnums.OperationalStatus).ToList();
        device.Health.OperationalStatus = F.Exact<List<string>?>(ops, PdSrc + ".OperationalStatus");
        return new DiskEntry(number, device) { Bus = bus, StorageDeviceId = p.Str("DeviceId"), PnpDeviceId = drive?.Str("PNPDeviceID") };
    }

    private static DiskEntry FromDiskDrive(WmiObject d)
    {
        const string src = "WMI Win32_DiskDrive";
        int number = d.Int("Index")!.Value;
        var model = FirmwareText.Clean(d.Str("Model"));
        var iface = d.Str("InterfaceType")?.ToLowerInvariant(); // IDE / SCSI / USB: controller class, not the bus
        var device = new StorageHealthDevice
        {
            Id = StableId("disk", d.Str("PNPDeviceID") ?? $"{model}|{number}"),
            DiskNumber = F.Exact<int?>(number, src + ".Index"),
            Model = F.Exact(model, src),
            Manufacturer = F.Detected(Vendors.DiskManufacturerFromModel(model), "model name", "from_model_name"),
            FirmwareVersion = F.Exact(d.Str("FirmwareRevision"), src),
            BusType = F.Unavailable<string?>(src + ".InterfaceType", "controller_class_only"),
            MediaType = F.Unavailable<string?>(src, F.Reasons.NotReported),
            CapacityBytes = F.Exact(d.Long("Size"), src, "rounded_to_cylinders"),
            Type = iface == "usb" ? F.Detected<string?>("usb", src + ".InterfaceType", "from_interface_type") : F.Unavailable<string?>(src, F.Reasons.NotReported),
        };
        device.Health.OsStatus = F.Unavailable<string?>(PdSrc, "storage_api_unavailable");
        return new DiskEntry(number, device) { Bus = iface == "usb" ? "usb" : null, PnpDeviceId = d.Str("PNPDeviceID") };
    }

    internal static Field<string?> Classify(string? bus, string? media, string source)
    {
        string? type = bus switch
        {
            "nvme" => "nvme",
            "sata" or "ata" or "atapi" => media switch { "ssd" => "sata_ssd", "hdd" => "sata_hdd", _ => "sata" },
            "usb" => "usb",
            "sas" => "sas",
            "scsi" or "iscsi" or "fibre_channel" => "scsi",
            "raid" => "raid",
            "storage_spaces" => "storage_spaces",
            "virtual" or "file_backed_virtual" => "virtual",
            "sd" or "mmc" or "ufs" => "sd",
            null => null,
            _ => "unknown",
        };
        return F.Detected(type, source, "from_bus_and_media_type");
    }

    /// <summary>Non-reversible, stable id: the serial / unique id never leaves the agent.</summary>
    public static string StableId(string prefix, string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("device-check/" + prefix + "/" + key.Trim().ToUpperInvariant()));
        return $"{prefix}-{Convert.ToHexStringLower(hash)[..12]}";
    }
}
