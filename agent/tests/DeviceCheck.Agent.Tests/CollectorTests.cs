using DeviceCheck.Agent.Collectors;
using DeviceCheck.Agent.Core.Collection;
using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Core.Sources;
using DeviceCheck.Agent.Tests.Fixtures;
using static DeviceCheck.Agent.Tests.Fixtures.FakeWmi;
using static DeviceCheck.Agent.Tests.Fixtures.Machines;

namespace DeviceCheck.Agent.Tests;

internal static class Run
{
    public static async Task<CollectorContext> Collector(IHardwareCollector c)
    {
        var ctx = new CollectorContext(new HardwareReport(), new CollectorDiagnostics { Id = c.Id });
        await c.CollectAsync(ctx, CancellationToken.None);
        return ctx;
    }

    public static FakeWmi LaptopWmi()
    {
        var wmi = new FakeWmi();
        AmdLaptop(wmi);
        return wmi;
    }
}

public class CpuCollectorTests
{
    private static FakeWmi Wmi() => new FakeWmi()
        .On("Win32_Processor", ProcessorRow())
        .On("Win32_PerfFormattedData_Counters_ProcessorInformation", Row(("PercentProcessorPerformance", 120L)));

    [Fact]
    public async Task Amd_zen4_from_cpuid_and_topology()
    {
        var ctx = await Run.Collector(new CpuCollector(Wmi(), new FakeProcessor(Topology(8, 16), AmdCpuid())));
        var c = ctx.Report.Cpu;

        Assert.Equal("AMD", c.Vendor.Value);
        Assert.Equal("CPUID", c.Vendor.Source);
        Assert.Equal("AMD Ryzen 7 7840HS w/ Radeon 780M Graphics", c.Model.Value);
        Assert.Equal("AMD Family 19h", c.Family.Value);
        Assert.Equal("Zen 4", c.Microarchitecture.Value);
        Assert.Equal(Confidence.Detected, c.Microarchitecture.Confidence);
        Assert.Equal("Phoenix", c.Codename.Value);
        Assert.Equal(4, c.ProcessNm.Value);
        Assert.Equal("Ryzen 7000 Series", c.Generation.Value);
        Assert.Equal("x86-64", c.Architecture.Value);
        Assert.Equal((1, 8, 16), (c.Sockets.Value, c.CoresPhysical.Value, c.ThreadsLogical.Value));
        Assert.Equal(F.Reasons.NotApplicable, c.CoresPerformance.Reason);
        Assert.Equal(512, c.CacheL1Kb.Value);   // 8 × (32 KB data + 32 KB instruction)
        Assert.Equal(8192, c.CacheL2Kb.Value);
        Assert.Equal(16384, c.CacheL3Kb.Value);
        Assert.Contains("L1d 8 × 32 KB", c.CacheLayout.Value!);
        Assert.Contains("L3 1 × 16 MB", c.CacheLayout.Value!);
        Assert.Contains("AVX-512F", c.Instructions.Value!);
        Assert.True(c.Virtualization.Value);
        Assert.True(c.VirtualizationEnabled.Value);
        Assert.False(c.ProcessorId.HasValue);
        Assert.Equal(F.Reasons.Privacy, c.ProcessorId.Reason);
    }

    [Fact]
    public async Task Clocks_are_not_overstated()
    {
        var ctx = await Run.Collector(new CpuCollector(Wmi(), new FakeProcessor(Topology(8, 16), AmdCpuid())));
        var c = ctx.Report.Cpu;

        // Win32_Processor.MaxClockSpeed is the nominal clock, so it is the base clock, not the boost clock.
        Assert.Equal(3801, c.BaseClockMhz.Value);
        Assert.Equal(Confidence.Detected, c.BaseClockMhz.Confidence);
        Assert.False(c.MaxClockMhz.HasValue);
        Assert.Equal(4561, c.CurrentClockMhz.Value); // 3801 × 120 %
        Assert.Equal(Confidence.Approximate, c.CurrentClockMhz.Confidence);
        Assert.False(c.TdpW.HasValue);
        Assert.Contains(ctx.Report.Limitations, l => l.Code == "boost_clock_unavailable");
    }

    [Fact]
    public async Task Intel_hybrid_counts_p_and_e_cores()
    {
        var classes = Enumerable.Repeat(1, 8).Concat(Enumerable.Repeat(0, 4)).ToList();
        var wmi = new FakeWmi().On("Win32_Processor", ProcessorRow("12th Gen Intel(R) Core(TM) i7-12700K", 3600));
        var ctx = await Run.Collector(new CpuCollector(wmi, new FakeProcessor(Topology(12, 20, classes), IntelHybridCpuid())));
        var c = ctx.Report.Cpu;

        Assert.Equal("Intel", c.Vendor.Value);
        Assert.Equal("12th Gen Intel Core i7-12700K", c.Model.Value);
        Assert.Equal("Alder Lake", c.Codename.Value);
        Assert.Equal("Intel 7", c.ProcessNode.Value);
        Assert.Equal("see_process_node", c.ProcessNm.Reason); // "Intel 7" is not 7 nm
        Assert.Equal("12th Gen Intel Core", c.Generation.Value);
        Assert.Equal((8, 4), (c.CoresPerformance.Value, c.CoresEfficiency.Value));
        Assert.Equal(F.Reasons.NotReported, c.CurrentClockMhz.Reason); // no perf counter row
    }

    [Fact]
    public async Task Unknown_model_is_not_guessed()
    {
        var cpuid = AmdCpuid() with { Family = 0x1F, Model = 0x01 };
        var ctx = await Run.Collector(new CpuCollector(Wmi(), new FakeProcessor(Topology(8, 16), cpuid)));
        Assert.Equal("not_in_reference_table", ctx.Report.Cpu.Microarchitecture.Reason);
        Assert.Null(ctx.Report.Cpu.ProcessNm.Value);
    }

    [Fact]
    public async Task Without_cpuid_and_topology_uses_wmi()
    {
        var ctx = await Run.Collector(new CpuCollector(Wmi(), new FakeProcessor(null, null)));
        var c = ctx.Report.Cpu;

        Assert.Equal("AuthenticAMD", c.Vendor.Value);
        Assert.Equal("WMI Win32_Processor", c.Vendor.Source);
        Assert.Equal("AMD Ryzen 7 7840HS w/ Radeon 780M Graphics", c.Model.Value);
        Assert.Equal((1, 8, 16), (c.Sockets.Value, c.CoresPhysical.Value, c.ThreadsLogical.Value));
        Assert.Equal(8192, c.CacheL2Kb.Value);
        Assert.Equal(F.Reasons.Unsupported, c.Family.Reason);
        Assert.Equal(F.Reasons.Unsupported, c.Instructions.Reason);
    }

    [Fact]
    public async Task Wmi_failure_keeps_cpuid_data()
    {
        var wmi = new FakeWmi().Throw("Win32_Processor", new WmiException("Win32_Processor", "RPC server unavailable"))
            .Throw("Win32_PerfFormattedData_Counters_ProcessorInformation", new TimeoutException());
        var ctx = await Run.Collector(new CpuCollector(wmi, new FakeProcessor(Topology(8, 16), AmdCpuid())));

        Assert.Equal(2, ctx.Diagnostics.Errors.Count);
        Assert.Equal("AMD Ryzen 7 7840HS w/ Radeon 780M Graphics", ctx.Report.Cpu.Model.Value);
        Assert.Equal(8, ctx.Report.Cpu.CoresPhysical.Value);
        Assert.False(ctx.Report.Cpu.BaseClockMhz.HasValue);
        Assert.False(ctx.Report.Cpu.Architecture.HasValue);
    }

    [Fact]
    public async Task Hypervisor_hides_vmx_flag()
    {
        var cpuid = IntelHybridCpuid() with { Features = ["SSE4.2", "AVX2"], HypervisorPresent = true };
        var ctx = await Run.Collector(new CpuCollector(new FakeWmi(), new FakeProcessor(Topology(4, 8), cpuid)));
        Assert.True(ctx.Report.Cpu.Virtualization.Value);
        Assert.Equal(Confidence.Detected, ctx.Report.Cpu.Virtualization.Confidence);
        Assert.Equal("hypervisor_running", ctx.Report.Cpu.VirtualizationEnabled.Note);
    }
}

public class GpuCollectorTests
{
    private static GraphicsAdapterInfo Adapter(string name, uint ven, uint dev, bool? uma, ulong vram = 0, bool remote = false, bool software = false) =>
        new(name, ven, dev, 0x12345678, 1, vram, 0, 16 * (ulong)GiB, software, remote, uma, "12_1");

    [Fact]
    public async Task Integrated_and_discrete_gpu_on_one_machine()
    {
        var wmi = Run.LaptopWmi();
        var collectors = AmdLaptop(wmi);
        var ctx = await Run.Collector(collectors.Single(c => c.Id == "gpu"));
        var g = ctx.Report.Gpu;

        Assert.Equal(2, g.Count); // Microsoft Basic Render Driver skipped
        Assert.Contains("software_adapter_skipped", ctx.Diagnostics.Warnings);

        Assert.Equal(("AMD", "integrated"), (g[0].Vendor.Value, g[0].AdapterType.Value));
        Assert.Equal(Confidence.Exact, g[0].AdapterType.Confidence);
        Assert.Equal(512 * MiB, g[0].DedicatedVramBytes.Value);
        Assert.Equal("uma_carveout", g[0].DedicatedVramBytes.Note);
        Assert.Equal(@"PCI\VEN_1002&DEV_15BF&SUBSYS_380217AA&REV_C1", g[0].PciDeviceId.Value);
        Assert.Equal("31.0.24027.1012", g[0].DriverVersion.Value);
        Assert.Equal("2025-03-15", g[0].DriverDate.Value);

        Assert.Equal(("NVIDIA", "discrete"), (g[1].Vendor.Value, g[1].AdapterType.Value));
        Assert.Equal(8 * GiB, g[1].DedicatedVramBytes.Value);
        Assert.Equal("32.0.15.6094", g[1].DriverVersion.Value);
        Assert.Equal("0x10DE", g[1].VendorId.Value);
        Assert.Equal("0x28A0", g[1].DeviceId.Value);

        // Shared memory is system RAM, never presented as VRAM.
        Assert.All(g, x => Assert.Equal("system_ram_not_vram", x.SharedMemoryBytes.Note));
        Assert.All(g, x => Assert.NotEqual(x.SharedMemoryBytes.Value, x.DedicatedVramBytes.Value));
    }

    [Fact]
    public async Task Remote_and_virtual_adapters()
    {
        var gpu = new FakeGraphics(
            Adapter("Microsoft Remote Display Adapter", 0x1414, 0x8E, null, remote: true),
            Adapter("VMware SVGA 3D", 0x15AD, 0x0405, false));
        var ctx = await Run.Collector(new GpuCollector(new FakeWmi(), gpu));
        Assert.Equal("remote", ctx.Report.Gpu[0].AdapterType.Value);
        Assert.Equal("virtual", ctx.Report.Gpu[1].AdapterType.Value);
    }

    [Theory]
    [InlineData("NVIDIA GeForce GTX 1650", 0x10DE, "discrete")]
    [InlineData("AMD Radeon RX 7900 XTX", 0x1002, "discrete")]
    [InlineData("AMD Radeon(TM) Graphics", 0x1002, "integrated")]
    [InlineData("Intel(R) UHD Graphics 630", 0x8086, "integrated")]
    [InlineData("Intel(R) Arc(TM) Graphics", 0x8086, "integrated")]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", 0x8086, "discrete")]
    public void Name_heuristic_when_d3d12_unavailable(string name, uint ven, string expected)
    {
        var a = Adapter(name, ven, 1, uma: null);
        var t = GpuCollector.AdapterType(a, Core.Normalization.Vendors.PciVendor(ven), name.Replace("(R)", "").Replace("(TM)", ""));
        Assert.Equal(expected, t.Value);
        Assert.Equal(Confidence.Detected, t.Confidence);
    }

    [Fact]
    public void Unknown_vendor_type_is_unavailable() =>
        Assert.False(GpuCollector.AdapterType(Adapter("Mystery GPU", 0xABCD, 1, null), null, "Mystery GPU").HasValue);

    [Fact]
    public void Identical_gpus_are_matched_by_subsystem()
    {
        var rows = new[]
        {
            Row(("PNPDeviceID", @"PCI\VEN_10DE&DEV_2684&SUBSYS_11111111&REV_A1\4&1"), ("DriverVersion", "a")),
            Row(("PNPDeviceID", @"PCI\VEN_10DE&DEV_2684&SUBSYS_12345678&REV_A1\4&2"), ("DriverVersion", "b")),
        };
        Assert.Equal("b", GpuCollector.Match(rows, Adapter("RTX 4090", 0x10DE, 0x2684, false))?.Str("DriverVersion"));
    }

    [Fact]
    public async Task Dxgi_failure_falls_back_to_wmi()
    {
        var ctx = await Run.Collector(new GpuCollector(Run.LaptopWmi(), new ThrowingGraphics()));
        var g = ctx.Report.Gpu;
        Assert.Equal(2, g.Count);
        Assert.Contains("dxgi_unavailable_used_wmi", ctx.Diagnostics.Warnings);
        Assert.Single(ctx.Diagnostics.Errors);
        Assert.Equal("0x10DE", g[1].VendorId.Value);
        Assert.Equal("wmi_adapter_ram_unreliable", g[1].DedicatedVramBytes.Reason); // AdapterRAM is capped at 4 GiB
    }

    private sealed class ThrowingGraphics : IGraphicsSource
    {
        public IReadOnlyList<GraphicsAdapterInfo> GetAdapters() => throw new DllNotFoundException("dxgi.dll");
    }
}

public class StorageCollectorTests
{
    [Fact]
    public async Task Nvme_sata_hdd_and_usb()
    {
        var ctx = await Run.Collector(new StorageCollector(Run.LaptopWmi(), new FakePlatform()));
        var d = ctx.Report.Storage;

        Assert.Equal(new int[] { 0, 1, 2 }, d.Select(x => x.DiskNumber.Value ?? -1));

        Assert.Equal("Samsung", d[0].Manufacturer.Value);
        Assert.Equal("from_model_name", d[0].Manufacturer.Note);
        Assert.Equal(("ssd", "nvme"), (d[0].MediaType.Value, d[0].Interface.Value));
        Assert.Equal("nvme_implies_ssd", d[0].MediaType.Note);
        Assert.Equal(F.Reasons.NotApplicable, d[0].Rpm.Reason);
        Assert.True(d[0].IsSystem.Value);
        Assert.Equal("4B2QJXD7", d[0].FirmwareVersion.Value);
        Assert.Equal("healthy", d[0].SmartStatus.Value);

        Assert.Equal("Seagate", d[1].Manufacturer.Value);
        Assert.Equal(("hdd", "sata"), (d[1].MediaType.Value, d[1].Interface.Value));
        Assert.Equal(Confidence.Exact, d[1].MediaType.Confidence);
        Assert.Equal(7200, d[1].Rpm.Value);
        Assert.Equal((4096, 512), (d[1].PhysicalSectorBytes.Value, d[1].LogicalSectorBytes.Value));
        Assert.False(d[1].IsSystem.Value);

        Assert.Equal("SanDisk Ultra USB 3.0", d[2].Model.Value);
        Assert.Equal("usb", d[2].Interface.Value);
        Assert.False(d[2].MediaType.HasValue);
        Assert.Equal(F.Reasons.NotReported, d[2].Rpm.Reason);

        Assert.All(d, x => Assert.Equal(F.Reasons.Privacy, x.Serial.Reason));
        Assert.Contains(ctx.Report.Limitations, l => l.Code == "smart_attributes_require_admin");
    }

    [Fact]
    public async Task Volumes_used_space_is_derived()
    {
        var ctx = await Run.Collector(new StorageCollector(new FakeWmi(), new FakePlatform()));
        var v = Assert.Single(ctx.Report.Volumes);
        Assert.Equal("C:", v.Letter.Value);
        Assert.Equal(400 * GiB, v.UsedBytes.Value);
        Assert.Equal("derived", v.UsedBytes.Note);
        Assert.Empty(ctx.Report.Storage);
    }

    [Fact]
    public async Task Storage_api_failure_falls_back_to_win32_diskdrive()
    {
        var wmi = new FakeWmi()
            .Throw("MSFT_PhysicalDisk", new WmiException("MSFT_PhysicalDisk", "invalid namespace"))
            .On("Win32_DiskDrive",
                Row(("Index", 1), ("Model", "WDC WD10EZEX-08WN4A0"), ("InterfaceType", "IDE"), ("Size", 1_000_202_273_280L), ("BytesPerSector", 512)),
                Row(("Index", 0), ("Model", "KINGSTON SA400S37240G"), ("InterfaceType", "IDE"), ("Size", 240_054_796_800L)));
        var ctx = await Run.Collector(new StorageCollector(wmi, new FakePlatform()));
        var d = ctx.Report.Storage;

        Assert.Contains("storage_api_unavailable_used_win32_diskdrive", ctx.Diagnostics.Warnings);
        Assert.Equal(new string?[] { "Kingston", "Western Digital" }, d.Select(x => x.Manufacturer.Value));
        Assert.All(d, x => Assert.Equal(F.Reasons.RequiresAdmin, x.SmartStatus.Reason));
    }

    [Theory]
    [InlineData(17, "nvme")]
    [InlineData(11, "sata")]
    [InlineData(7, "usb")]
    [InlineData(8, "raid")]
    [InlineData(14, "virtual")]
    [InlineData(16, "storage_spaces")]
    [InlineData(99, null)]
    public void Bus_types(int code, string? expected) => Assert.Equal(expected, StorageCollector.BusType(code));
}

public class DisplayCollectorTests
{
    [Fact]
    public async Task Two_monitors_internal_panel_and_dell()
    {
        var collectors = AmdLaptop();
        var ctx = await Run.Collector(collectors.Single(c => c.Id == "display"));
        var d = ctx.Report.Displays;
        Assert.Equal(2, d.Count);

        var panel = d[0];
        Assert.Equal("BOE", panel.ManufacturerCode.Value);
        Assert.Equal("BOE", panel.Manufacturer.Value);
        Assert.Equal(new Resolution(2880, 1800), panel.Resolution.Value);
        Assert.Equal(120.0, panel.RefreshHz.Value);
        Assert.Equal("internal", panel.Connection.Value);
        Assert.True(panel.Hdr.Value);
        Assert.True(panel.IsPrimary.Value);
        Assert.False(panel.NativeResolution.HasValue); // no EDID → no native mode
        Assert.Contains("edid_not_found", ctx.Diagnostics.Warnings);

        var dell = d[1];
        Assert.Equal("Dell", dell.Manufacturer.Value);
        Assert.Equal("DELL U2723QE", dell.Model.Value);
        Assert.Equal("0xA0F2", dell.ProductCode.Value);
        Assert.Equal(new Resolution(2560, 1440), dell.NativeResolution.Value);
        Assert.Equal(143.998, dell.RefreshHz.Value); // real active mode, not a value from EDID
        Assert.StartsWith("QueryDisplayConfig", dell.RefreshHz.Source);
        Assert.Equal((597, 336), (dell.PhysicalWidthMm.Value, dell.PhysicalHeightMm.Value));
        Assert.Equal(27.0, dell.DiagonalIn.Value);
        Assert.Equal(Confidence.Detected, dell.DiagonalIn.Confidence);
        Assert.Equal("displayport", dell.Connection.Value);
        Assert.Equal(2021, dell.ManufactureYear.Value);
        Assert.Equal(F.Reasons.Privacy, dell.Serial.Reason);
    }

    [Fact]
    public async Task Display_api_failure_is_isolated()
    {
        var ctx = await Run.Collector(new DisplayCollector(new ThrowingDisplays()));
        Assert.Empty(ctx.Report.Displays);
        Assert.Single(ctx.Diagnostics.Errors);
    }

    private sealed class ThrowingDisplays : IDisplaySource
    {
        public IReadOnlyList<DisplayPathInfo> GetActiveDisplays() => throw new InvalidOperationException("QueryDisplayConfig: 0x57");
    }
}

public class MotherboardCollectorTests
{
    [Fact]
    public async Task Smbios_values_and_placeholders()
    {
        var smbios = new FakeSmbios(SmbiosBuilder.Table(
            SmbiosBuilder.Type0("American Megatrends Inc.", "DXH6.13", "11/13/2023"),
            SmbiosBuilder.Type1("To be filled by O.E.M.", "To be filled by O.E.M."),
            SmbiosBuilder.Type2("Micro-Star International Co., Ltd.", "MAG B650 TOMAHAWK WIFI (MS-7D75)", "Default string")));
        var wmi = new FakeWmi().On("Win32_PnPEntity", Row(("Name", "AMD GPIO Controller")));
        var ctx = await Run.Collector(new MotherboardCollector(wmi, smbios, new FakePlatform()));
        var m = ctx.Report.Motherboard;

        Assert.Equal("Micro-Star International Co., Ltd.", m.Manufacturer.Value);
        Assert.Equal("SMBIOS Type 2", m.Manufacturer.Source);
        Assert.Equal("MAG B650 TOMAHAWK WIFI (MS-7D75)", m.Model.Value);
        Assert.Equal(F.Reasons.FirmwarePlaceholder, m.Version.Reason);
        Assert.Equal(F.Reasons.FirmwarePlaceholder, m.SystemManufacturer.Reason);
        Assert.Equal("American Megatrends Inc.", m.BiosVendor.Value);
        Assert.Equal("DXH6.13", m.BiosVersion.Value);
        Assert.Equal("2023-11-13", m.BiosDate.Value);
        Assert.Equal("3.5", m.SmbiosVersion.Value);
        Assert.Equal("uefi", m.FirmwareType.Value);
        Assert.Equal(F.Reasons.NoReliableSource, m.Chipset.Reason);
        Assert.Equal(F.Reasons.Privacy, m.Serial.Reason);
    }

    [Fact]
    public async Task Wmi_fallback_without_smbios()
    {
        var wmi = new FakeWmi()
            .On("Win32_BaseBoard", Row(("Manufacturer", "ASUSTeK COMPUTER INC."), ("Product", "PRIME Z790-P"), ("Version", "Rev 1.xx")))
            .On("Win32_BIOS", Row(("Manufacturer", "American Megatrends Inc."), ("SMBIOSBIOSVersion", "1663"), ("ReleaseDate", "20240105000000.000000+000")))
            .On("Win32_ComputerSystem", Row(("Manufacturer", "ASUS"), ("Model", "System Product Name")))
            .On("Win32_PnPEntity", Row(("Name", "Intel(R) Z790 Chipset LPC/eSPI Controller - 7A04")));
        var ctx = await Run.Collector(new MotherboardCollector(wmi, new FakeSmbios(null), new FakePlatform()));
        var m = ctx.Report.Motherboard;

        Assert.Contains("smbios_unavailable_used_wmi", ctx.Diagnostics.Warnings);
        Assert.Equal("PRIME Z790-P", m.Model.Value);
        Assert.Equal("WMI Win32_BaseBoard", m.Model.Source);
        Assert.Equal("2024-01-05", m.BiosDate.Value);
        Assert.Equal("ASUS", m.SystemManufacturer.Value);
        Assert.Equal(F.Reasons.FirmwarePlaceholder, m.SystemModel.Reason);
        Assert.False(m.SmbiosVersion.HasValue);
        Assert.Equal("Intel Z790", m.Chipset.Value);
        Assert.Equal(Confidence.Detected, m.Chipset.Confidence);
    }

    [Theory]
    [InlineData("Intel(R) B660 Chipset LPC/eSPI Controller - 7A86", "Intel B660")]
    [InlineData("Intel(R) 300 Series Chipset Family LPC Controller (H370) - A304", "Intel H370")]
    [InlineData("Intel(R) 100 Series/C230 Series Chipset Family LPC Controller - A143", null)]
    [InlineData("Intel(R) 400 Series Chipset Family LPC Controller - 0284", "Intel 400 Series")]
    [InlineData("Intel(R) Z790 Chipset SMBus - 7A23", null)]
    [InlineData("AMD SMBus", null)]
    [InlineData(null, null)]
    public void Chipset_from_pci_name(string? name, string? expected) =>
        Assert.Equal(expected, MotherboardCollector.ChipsetFromDeviceName(name));
}

public class SystemCollectorTests
{
    [Fact]
    public async Task Windows_11_identity_and_privacy()
    {
        var ctx = await Run.Collector(AmdLaptop().Single(c => c.Id == "system"));
        var s = ctx.Report.System;

        Assert.Equal("Windows 11 Pro", s.OsName.Value);
        Assert.Equal("24H2", s.OsVersion.Value);
        Assert.Equal("26100.2314", s.OsBuild.Value);
        Assert.Equal("Professional", s.OsEdition.Value);
        Assert.Equal("x86-64", s.Architecture.Value);
        Assert.Equal(3600, s.UptimeSeconds.Value);
        Assert.Equal("laptop", s.DeviceType.Value);
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.FromHours(3)), s.BootTime.Value);
        Assert.Equal(F.Reasons.Privacy, s.Hostname.Reason);
        Assert.Equal(("Windows", "Windows 11"), (ctx.Report.Scan.Platform, ctx.Report.Scan.PlatformVersion));
        Assert.False(ctx.Report.Diagnostics.Elevated);
    }

    [Fact]
    public async Task Windows_10_by_build_number()
    {
        var wmi = new FakeWmi().On("Win32_OperatingSystem", Row(("Caption", "Майкрософт Windows 10 Pro"), ("BuildNumber", "19045")));
        var ctx = await Run.Collector(new SystemCollector(wmi, new FakePlatform()));
        Assert.Equal("Windows 10 Pro", ctx.Report.System.OsName.Value);
        Assert.Equal("Windows 10", ctx.Report.Scan.PlatformVersion);
    }

    [Fact]
    public async Task Wmi_unavailable_does_not_throw()
    {
        var wmi = new FakeWmi().Throw("Win32_OperatingSystem", new TimeoutException("WMI query timed out"))
            .Throw("Win32_ComputerSystem", new TimeoutException("WMI query timed out"));
        var ctx = await Run.Collector(new SystemCollector(wmi, new FakePlatform()));
        Assert.False(ctx.Report.System.OsName.HasValue);
        Assert.Equal("24H2", ctx.Report.System.OsVersion.Value); // registry still works
        Assert.Equal(2, ctx.Diagnostics.Errors.Count);
    }
}
