using DeviceCheck.Agent.Collectors;
using DeviceCheck.Agent.Core.Collection;
using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Core.Sources;
using static DeviceCheck.Agent.Tests.Fixtures.FakeWmi;

namespace DeviceCheck.Agent.Tests.Fixtures;

/// <summary>Complete fake machines (fixtures) for end-to-end collector runs.</summary>
public static class Machines
{
    public const long GiB = 1L << 30;
    public const long MiB = 1L << 20;

    public static CpuidInfo AmdCpuid() => new("AuthenticAMD", "AMD Ryzen 7 7840HS w/ Radeon 780M Graphics        ",
        0x19, 0x74, 1, ["SSE4.2", "AVX2", "AVX-512F", "AMD-V"], false, false);

    public static CpuidInfo IntelHybridCpuid() => new("GenuineIntel", "12th Gen Intel(R) Core(TM) i7-12700K",
        6, 0x97, 2, ["SSE4.2", "AVX2", "VT-x"], false, true);

    public static ProcessorTopology Topology(int cores, int threads, IReadOnlyList<int>? classes = null) => new(1, cores, threads,
        classes ?? Enumerable.Repeat(0, cores).ToList(),
        [
            .. Enumerable.Repeat(new CacheInfo(1, "data", 32 * 1024), cores),
            .. Enumerable.Repeat(new CacheInfo(1, "instruction", 32 * 1024), cores),
            .. Enumerable.Repeat(new CacheInfo(2, "unified", 1024 * 1024), cores),
            new CacheInfo(3, "unified", 16 * 1024 * 1024),
        ]);

    public static WmiObject ProcessorRow(string name = "AMD Ryzen 7 7840HS w/ Radeon 780M Graphics", int maxClock = 3801) => Row(
        ("Name", name), ("Manufacturer", "AuthenticAMD"), ("MaxClockSpeed", maxClock), ("NumberOfCores", 8),
        ("NumberOfLogicalProcessors", 16), ("L2CacheSize", 8192), ("L3CacheSize", 16384), ("Architecture", 9),
        ("VirtualizationFirmwareEnabled", true));

    /// <summary>AMD laptop: DDR5 2 × 32 GiB SODIMM, iGPU + dGPU, NVMe + SATA HDD + USB, two monitors.</summary>
    public static IReadOnlyList<IHardwareCollector> AmdLaptop(FakeWmi? wmi = null)
    {
        wmi ??= new FakeWmi();
        wmi.On("Win32_OperatingSystem", Row(("Caption", "Microsoft Windows 11 Pro"), ("BuildNumber", "26100"),
                ("LastBootUpTime", "20261110080000.000000+180"), ("InstallDate", "20250101120000.000000+180")))
            .On("Win32_ComputerSystem", Row(("PCSystemType", 2), ("Manufacturer", "LENOVO"), ("Model", "21K5")))
            .On("Win32_Processor", ProcessorRow())
            .On("Win32_PerfFormattedData_Counters_ProcessorInformation", Row(("PercentProcessorPerformance", 120L)))
            .On("Win32_VideoController",
                Row(("Name", "AMD Radeon 780M Graphics"), ("PNPDeviceID", @"PCI\VEN_1002&DEV_15BF&SUBSYS_380217AA&REV_C1\4&1234"),
                    ("DriverVersion", "31.0.24027.1012"), ("DriverDate", "20250315000000.000000-000"), ("AdapterCompatibility", "Advanced Micro Devices, Inc.")),
                Row(("Name", "NVIDIA GeForce RTX 4060 Laptop GPU"), ("PNPDeviceID", @"PCI\VEN_10DE&DEV_28A0&SUBSYS_380217AA&REV_A1\4&5678"),
                    ("DriverVersion", "32.0.15.6094"), ("DriverDate", "20250901000000.000000-000"), ("AdapterCompatibility", "NVIDIA")))
            .On("MSFT_PhysicalDisk",
                Row(("DeviceId", "1"), ("Model", "ST2000DM008-2FR102"), ("Size", 2_000_398_934_016L), ("MediaType", 3), ("BusType", 11),
                    ("SpindleSpeed", 7200L), ("PhysicalSectorSize", 4096), ("LogicalSectorSize", 512), ("HealthStatus", 0)),
                Row(("DeviceId", "0"), ("Model", "Samsung SSD 990 PRO 2TB"), ("FirmwareVersion", "4B2QJXD7"), ("Size", 2_000_398_934_016L),
                    ("MediaType", 0), ("BusType", 17), ("SpindleSpeed", 0L), ("PhysicalSectorSize", 512), ("LogicalSectorSize", 512), ("HealthStatus", 0)),
                Row(("DeviceId", "2"), ("FriendlyName", "SanDisk Ultra USB 3.0"), ("Size", 61_530_439_680L), ("MediaType", 0), ("BusType", 7),
                    ("SpindleSpeed", (long)uint.MaxValue), ("HealthStatus", 0)))
            .On("MSFT_Disk", Row(("Number", 0), ("IsSystem", true), ("IsBoot", true)), Row(("Number", 1), ("IsSystem", false), ("IsBoot", false)))
            .On("Win32_PnPEntity", Row(("Name", "AMD PCI")), Row(("Name", "AMD SMBus")));

        var smbios = new FakeSmbios(SmbiosBuilder.Table(
            SmbiosBuilder.Type1("LENOVO", "21K5"),
            SmbiosBuilder.Type2("LENOVO", "21K5CTO1WW", "SDK0T76530 WIN"),
            SmbiosBuilder.Type16(3, 64L * 1024 * 1024, 2),
            SmbiosBuilder.Type17("DIMM 0", "P0 CHANNEL A", 32 * 1024, formFactor: 0x0D, memType: 0x22, speed: 5600, configured: 5600,
                maker: "Micron Technology", part: "CT32G56C46S5.M16D", rank: 2, mv: 1100),
            SmbiosBuilder.Type17("DIMM 0", "P0 CHANNEL B", 32 * 1024, formFactor: 0x0D, memType: 0x22, speed: 5600, configured: 5600,
                maker: "Micron Technology", part: "CT32G56C46S5.M16D", rank: 2, mv: 1100)));

        var gpu = new FakeGraphics(
            new GraphicsAdapterInfo("AMD Radeon 780M Graphics", 0x1002, 0x15BF, 0x380217AA, 0xC1, 512 * (ulong)MiB, 0, 32 * (ulong)GiB, false, false, true, "12_1"),
            new GraphicsAdapterInfo("NVIDIA GeForce RTX 4060 Laptop GPU", 0x10DE, 0x28A0, 0x380217AA, 0xA1, 8 * (ulong)GiB, 0, 32 * (ulong)GiB, false, false, false, "12_2"),
            new GraphicsAdapterInfo("Microsoft Basic Render Driver", 0x1414, 0x8C, 0, 0, 0, 0, 32 * (ulong)GiB, true, false, null, null));

        var displays = new FakeDisplays(
            new DisplayPathInfo(2880, 1800, 120_000, 1000, int.MinValue, null, 0xE509, 0x2A5C, true, false, true, null),
            new DisplayPathInfo(2560, 1440, 143_998, 1000, 10, "DELL U2723QE", 0, 0, false, false, false, EdidBuilder.Dell()));

        return CollectorSet.Create(wmi, smbios, new FakeProcessor(Topology(8, 16), AmdCpuid()), gpu, displays,
            new FakePlatform { Installed = 64 * GiB });
    }

    public static Task<HardwareReport> ScanAsync(IReadOnlyList<IHardwareCollector> collectors, ScanOptions? options = null) =>
        new ScanOrchestrator(collectors, options ?? new ScanOptions { AgentVersion = "0.1.0-test" }).RunAsync();
}
