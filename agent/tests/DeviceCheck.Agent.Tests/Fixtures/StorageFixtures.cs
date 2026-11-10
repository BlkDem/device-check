using System.Buffers.Binary;
using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Core.Sources;
using DeviceCheck.Agent.Monitoring.Core;
using DeviceCheck.Agent.Monitoring.Storage;
using static DeviceCheck.Agent.Tests.Fixtures.FakeWmi;

namespace DeviceCheck.Agent.Tests.Fixtures;

// Storage health fixtures. Test-only: the real monitor never sees these.

public static class NvmeLogBuilder
{
    public static byte[] Health(int tempC = 42, int spare = 100, int spareThr = 10, int used = 15, byte criticalWarning = 0,
        ulong unitsRead = 234_567, ulong unitsWritten = 123_456, ulong poh = 1200, ulong cycles = 50, ulong unsafeShutdowns = 3,
        ulong mediaErrors = 0, ulong errorLog = 2, ulong highWrittenBits = 0, int[]? sensorsC = null)
    {
        var d = new byte[512];
        d[0] = criticalWarning;
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(1), (ushort)(tempC + 273));
        d[3] = (byte)spare; d[4] = (byte)spareThr; d[5] = (byte)used;
        void C(int o, ulong low, ulong high = 0)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(d.AsSpan(o), low);
            BinaryPrimitives.WriteUInt64LittleEndian(d.AsSpan(o + 8), high);
        }
        C(32, unitsRead); C(48, unitsWritten, highWrittenBits); C(64, 9_000_000); C(80, 7_000_000); C(96, 321);
        C(112, cycles); C(128, poh); C(144, unsafeShutdowns); C(160, mediaErrors); C(176, errorLog);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(192), 5);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(196), 1);
        var sensors = sensorsC ?? [tempC, tempC + 8];
        for (int i = 0; i < sensors.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(200 + i * 2), (ushort)(sensors[i] == 0 ? 0 : sensors[i] + 273)); // 0 = sensor not implemented
        return d;
    }

    public static byte[] Identify(int warnC = 85, int critC = 95)
    {
        var d = new byte[4096];
        System.Text.Encoding.ASCII.GetBytes("S6B0NL0W123456      ").CopyTo(d, 4); // serial: must never be read
        System.Text.Encoding.ASCII.GetBytes("Samsung SSD 990 PRO 2TB".PadRight(40)).CopyTo(d, 24);
        System.Text.Encoding.ASCII.GetBytes("4B2QJXD7").CopyTo(d, 64);
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(266), (ushort)(warnC == 0 ? 0 : warnC + 273));
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(268), (ushort)(critC == 0 ? 0 : critC + 273));
        return d;
    }
}

public static class AtaSmartBuilder
{
    public sealed record Attr(int Id, int Normalized, int Worst, long Raw, int Threshold = 0, int Flags = 0x0033, byte[]? RawBytes = null);

    public static (byte[] Data, byte[] Thresholds) Build(params Attr[] attrs)
    {
        var data = new byte[512];
        var thr = new byte[512];
        BinaryPrimitives.WriteUInt16LittleEndian(data, 16);
        for (int i = 0; i < attrs.Length; i++)
        {
            var a = attrs[i];
            int o = 2 + i * 12;
            data[o] = (byte)a.Id;
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(o + 1), (ushort)a.Flags);
            data[o + 3] = (byte)a.Normalized;
            data[o + 4] = (byte)a.Worst;
            var raw = a.RawBytes ?? BitConverter.GetBytes(a.Raw)[..6];
            raw.CopyTo(data, o + 5);
            thr[o] = (byte)a.Id;
            thr[o + 1] = (byte)a.Threshold;
        }
        return (data, thr);
    }

    public static (byte[] Data, byte[] Thresholds) SataSsd() => Build(
        new(5, 100, 100, 0, 10), new(9, 98, 98, 8760), new(12, 99, 99, 300), new(177, 95, 95, 52, 0x13),
        new(187, 100, 100, 0), new(194, 65, 52, 0, 0, 0x22, [35, 0, 15, 0, 48, 0]), new(199, 100, 100, 0), new(241, 99, 99, 41_234_567_890));

    public static (byte[] Data, byte[] Thresholds) Hdd() => Build(
        new(1, 200, 200, 0, 51), new(3, 175, 172, 4258, 21), new(5, 198, 198, 8, 140), new(9, 60, 60, 29_415),
        new(12, 100, 100, 812), new(194, 106, 92, 0, 0, 0x22, [41, 0, 0, 0, 0, 0]), new(197, 200, 200, 2), new(198, 100, 253, 1),
        new(199, 200, 200, 0), new(250, 100, 100, 77));
}

/// <summary>IOCTL fake: per disk, a value or an exception for each query.</summary>
public sealed class FakeStorageDevices : IStorageDeviceSource
{
    public Dictionary<int, Func<byte[]>> Health { get; } = [];
    public Dictionary<int, Func<byte[]>> Identify { get; } = [];
    public Dictionary<int, Func<DeviceTemperatureData?>> Temperature { get; } = [];
    public int HealthQueries { get; private set; }

    private static StorageAccessException NotSupported(int disk) => new(F.Reasons.Unsupported, $"disk {disk}: not supported", 1);

    public byte[] NvmeHealthLog(int diskNumber)
    {
        HealthQueries++;
        return Health.TryGetValue(diskNumber, out var f) ? f() : throw NotSupported(diskNumber);
    }

    public byte[] NvmeIdentifyController(int diskNumber) =>
        Identify.TryGetValue(diskNumber, out var f) ? f() : throw NotSupported(diskNumber);

    public DeviceTemperatureData? DeviceTemperature(int diskNumber) =>
        Temperature.TryGetValue(diskNumber, out var f) ? f() : throw NotSupported(diskNumber);
}

public sealed class FakeVolumes(params VolumeData[] volumes) : IPlatformSource
{
    public string? FirmwareType() => "uefi";
    public long UptimeMilliseconds() => 0;
    public string OsArchitecture() => "x86-64";
    public object? CurrentVersionValue(string name) => null;
    public MemoryStatus GetMemoryStatus() => new(null, null);
    public IReadOnlyList<VolumeData> GetVolumes() => volumes;
    public bool Elevated { get; init; }
    public bool IsElevated() => Elevated;
}

/// <summary>
/// Workstation: 0 NVMe (system), 1 SATA SSD (two partitions), 2 SATA HDD, 3 USB stick, 4 virtual disk;
/// volume S: spans disks 1 and 2; Z: is a subst drive known only to DriveInfo.
/// </summary>
public static class StorageMachines
{
    public const long GB = 1_000_000_000;

    public static WmiObject Pd(int n, string model, int bus, int media, long size, int health = 0, string? maker = null) => Row(
        ("DeviceId", n.ToString()), ("FriendlyName", model), ("Model", model), ("Manufacturer", maker), ("FirmwareVersion", "FW1"),
        ("Size", size), ("BusType", bus), ("MediaType", media), ("HealthStatus", health), ("OperationalStatus", new ushort[] { 2 }),
        ("UniqueId", $"UNIQ-{n}-{model}"), ("SerialNumber", $"SERIAL{n}XYZ987"));

    public static FakeWmi Wmi()
    {
        var wmi = new FakeWmi()
            .On("MSFT_PhysicalDisk",
                Pd(0, "Samsung SSD 990 PRO 2TB", 17, 0, 2000 * GB),
                Pd(1, "Samsung SSD 870 EVO 1TB", 11, 4, 1000 * GB),
                Pd(2, "WDC WD40EZRZ-00GXCB0", 11, 3, 4000 * GB, 1),
                Pd(3, "SanDisk Ultra", 7, 0, 64 * GB),
                Pd(4, "Msft Virtual Disk", 14, 0, 128 * GB))
            .On("MSFT_Disk", Row(("Number", 0), ("IsSystem", true), ("IsBoot", true)), Row(("Number", 1), ("IsSystem", false), ("IsBoot", false)))
            .On("Win32_DiskDrive",
                Row(("Index", 0), ("PNPDeviceID", @"SCSI\DISK&VEN_NVME&PROD_SAMSUNG_SSD_990\5&1&0&000000")),
                Row(("Index", 1), ("PNPDeviceID", @"SCSI\DISK&VEN_&PROD_SAMSUNG_SSD_870\4&2A&0&010000")),
                Row(("Index", 2), ("PNPDeviceID", @"SCSI\DISK&VEN_WDC&PROD_WD40EZRZ\4&2A&0&020000")),
                Row(("Index", 3), ("PNPDeviceID", @"USBSTOR\DISK&VEN_SANDISK&PROD_ULTRA\0101&0")))
            .On("MSFT_Volume",
                Vol(@"\\?\Volume{c}\", 'C', "Windows", "NTFS", 3, 1900 * GB, 1400 * GB),
                Vol(@"\\?\Volume{rec}\", '\0', "Recovery", "NTFS", 3, 1 * GB, (long)(0.5 * GB)),
                Vol(@"\\?\Volume{d}\", 'D', "Data", "NTFS", 3, 600 * GB, 90 * GB),
                Vol(@"\\?\Volume{e}\", 'E', "Games", "NTFS", 3, 400 * GB, 15 * GB),
                Vol(@"\\?\Volume{f}\", 'F', "Archive", "NTFS", 3, 4000 * GB, 150 * GB, health: 1),
                Vol(@"\\?\Volume{g}\", 'G', "STICK", "FAT32", 2, 64 * GB, 60 * GB),
                Vol(@"\\?\Volume{s}\", 'S', "Span", "NTFS", 3, 200 * GB, 100 * GB))
            .On("MSFT_Partition",
                Part(0, null, @"\\?\Volume{efi}\"), Part(0, 'C', @"C:\", @"\\?\Volume{c}\"), Part(0, null, @"\\?\Volume{rec}\"),
                Part(1, 'D', @"D:\", @"\\?\Volume{d}\"), Part(1, 'E', @"E:\", @"\\?\Volume{e}\"), Part(1, null, @"\\?\Volume{s}\"),
                Part(2, 'F', @"F:\", @"\\?\Volume{f}\"), Part(2, null, @"\\?\Volume{s}\"), Part(3, 'G', @"G:\", @"\\?\Volume{g}\"))
            .On("MSStorageDriver_ATAPISmartData",
                Row(("InstanceName", @"SCSI\Disk&Ven_&Prod_Samsung_SSD_870\4&2a&0&010000_0"), ("VendorSpecific", AtaSmartBuilder.SataSsd().Data)),
                Row(("InstanceName", @"SCSI\Disk&Ven_WDC&Prod_WD40EZRZ\4&2a&0&020000_0"), ("VendorSpecific", AtaSmartBuilder.Hdd().Data)))
            .On("MSStorageDriver_FailurePredictThresholds",
                Row(("InstanceName", @"SCSI\Disk&Ven_&Prod_Samsung_SSD_870\4&2a&0&010000_0"), ("VendorSpecific", AtaSmartBuilder.SataSsd().Thresholds)),
                Row(("InstanceName", @"SCSI\Disk&Ven_WDC&Prod_WD40EZRZ\4&2a&0&020000_0"), ("VendorSpecific", AtaSmartBuilder.Hdd().Thresholds)))
            .On("MSStorageDriver_FailurePredictStatus",
                Row(("InstanceName", @"SCSI\Disk&Ven_&Prod_Samsung_SSD_870\4&2a&0&010000_0"), ("PredictFailure", false)),
                Row(("InstanceName", @"SCSI\Disk&Ven_WDC&Prod_WD40EZRZ\4&2a&0&020000_0"), ("PredictFailure", false)))
            .On("MSFT_StorageReliabilityCounter",
                Row(("DeviceId", "2"), ("Temperature", 41), ("TemperatureMax", 55), ("PowerOnHours", 29_415L), ("ReadErrorsUncorrected", 1L)),
                Row(("DeviceId", "3"), ("Temperature", 0), ("Wear", 7), ("PowerOnHours", 120L)));
        return wmi;
    }

    public static WmiObject Vol(string path, char letter, string label, string fs, int driveType, long size, long free, int health = 0) => Row(
        ("Path", path), ("DriveLetter", letter), ("FileSystemLabel", label), ("FileSystem", fs), ("DriveType", driveType),
        ("HealthStatus", health), ("Size", size), ("SizeRemaining", free));

    public static WmiObject Part(int disk, char? letter, params string[] paths) =>
        Row(("DiskNumber", disk), ("DriveLetter", letter ?? '\0'), ("AccessPaths", paths));

    public static FakeStorageDevices Devices()
    {
        var d = new FakeStorageDevices();
        d.Health[0] = () => NvmeLogBuilder.Health();
        d.Identify[0] = () => NvmeLogBuilder.Identify();
        d.Temperature[0] = () => new DeviceTemperatureData(41, 82, 90); // driver view; NVMe log / identify win
        d.Temperature[1] = () => new DeviceTemperatureData(38, 70, 80);
        // 2: HDD without driver temperature → SMART 194; 3: USB unsupported; 4: virtual unsupported
        return d;
    }

    public static FakeVolumes Platform(bool elevated = true) => new(
        new VolumeData(@"C:\", "NTFS", "fixed", 1900 * GB, 1400 * GB),
        new VolumeData(@"Z:\", "NTFS", "fixed", 1900 * GB, 1400 * GB)) { Elevated = elevated };

    public static StorageHealthMonitor Monitor(FakeWmi? wmi = null, FakeStorageDevices? devices = null, FakeVolumes? platform = null,
        IReadOnlyList<IStorageHealthCollector>? collectors = null, TimeSpan? timeout = null) =>
        new(wmi ?? Wmi(), devices ?? Devices(), platform ?? Platform(), new StorageMonitorOptions
        {
            AgentVersion = "0.2.0-test",
            CollectorTimeout = timeout ?? TimeSpan.FromSeconds(10),
        }, collectors);
}
