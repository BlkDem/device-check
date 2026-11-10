using DeviceCheck.Agent.Collectors;
using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Core.Sources;
using DeviceCheck.Agent.Tests.Fixtures;
using static DeviceCheck.Agent.Tests.Fixtures.FakeWmi;
using static DeviceCheck.Agent.Tests.Fixtures.Machines;

namespace DeviceCheck.Agent.Tests;

public class MemoryCollectorTests
{
    private static async Task<(MemorySection Mem, HardwareReport Report, CollectorDiagnostics Diag)> Run(
        byte[]? smbios, FakeWmi? wmi = null, long? installed = null)
    {
        var report = new HardwareReport();
        var ctx = new Core.Collection.CollectorContext(report, new CollectorDiagnostics { Id = "memory" });
        var c = new MemoryCollector(wmi ?? new FakeWmi(), new FakeSmbios(smbios), new FakePlatform { Installed = installed });
        await c.CollectAsync(ctx, CancellationToken.None);
        return (report.Memory, report, ctx.Diagnostics);
    }

    [Fact]
    public async Task Desktop_ddr4_two_of_four_slots_dual_channel()
    {
        var (m, report, _) = await Run(SmbiosBuilder.Table(
            SmbiosBuilder.Type16(3, 128L * 1024 * 1024, 4),
            SmbiosBuilder.Type17("DIMM_A1", "BANK 0", 0),
            SmbiosBuilder.Type17("DIMM_A2", "BANK 1", 16 * 1024),
            SmbiosBuilder.Type17("DIMM_B1", "BANK 2", 0),
            SmbiosBuilder.Type17("DIMM_B2", "BANK 3", 16 * 1024)), installed: 32 * GiB);

        Assert.Equal(32 * GiB, m.TotalBytes.Value);
        Assert.Equal("DDR4", m.MemoryType.Value);
        Assert.Equal(Confidence.Exact, m.MemoryType.Confidence);
        Assert.Equal((4, 2, 2), (m.SlotsTotal.Value, m.SlotsUsed.Value, m.SlotsFree.Value));
        Assert.Equal(Confidence.Detected, m.SlotsTotal.Confidence); // SMBIOS slot count is never "exact"
        Assert.Equal(2, m.ModulesDetected.Value);
        Assert.Equal("dual", m.Channels.Value);
        Assert.Equal(128, m.BusWidthBits.Value);
        Assert.False(m.Ecc.Value);
        Assert.Equal(128 * GiB, m.MaxCapacityBytes.Value);
        Assert.Equal(4, m.SmbiosSlots.Count);
        Assert.Equal(2, m.SmbiosSlots.Count(s => !s.Populated));
        Assert.Contains(report.Limitations, l => l.Code == "smbios_slot_count_unverified");
    }

    [Fact]
    public async Task Ddr5_speed_is_mts_and_clock_is_half()
    {
        var (m, _, _) = await Run(SmbiosBuilder.Table(
            SmbiosBuilder.Type17("DIMM 0", "P0 CHANNEL A", 32 * 1024, formFactor: 0x0D, memType: 0x22, speed: 5600, configured: 5600, part: "CT32G56C46S5.M16D", maker: "Unknown"),
            SmbiosBuilder.Type17("DIMM 0", "P0 CHANNEL B", 32 * 1024, formFactor: 0x0D, memType: 0x22, speed: 5600, configured: 5600, part: "CT32G56C46S5.M16D", maker: "Unknown")));

        Assert.Equal("DDR5", m.MemoryType.Value);
        Assert.Equal(5600, m.SpeedMts.Value);
        Assert.Equal(2800, m.ClockMhz.Value);
        Assert.Equal(Confidence.Detected, m.ClockMhz.Confidence); // derived, never exact
        Assert.Equal("dual", m.Channels.Value);

        var mod = m.Modules[0];
        Assert.Equal(5600, mod.SpeedMts.Value);
        Assert.Equal(2800, mod.ClockMhz.Value);
        Assert.Equal("SODIMM", mod.FormFactor.Value);
        Assert.Equal(0x22, mod.SmbiosMemoryType.Value);
        // "Unknown" manufacturer → resolved from the part number, marked as detected
        Assert.Equal("Crucial", mod.Manufacturer.Value);
        Assert.Equal(Confidence.Detected, mod.Manufacturer.Confidence);
        Assert.Equal("from_part_number", mod.Manufacturer.Note);
        // total sum is used only when the OS API failed
        Assert.Equal(64 * GiB, m.TotalBytes.Value);
        Assert.Contains("sum of modules", m.TotalBytes.Source);
    }

    [Fact]
    public async Task Configured_speed_is_not_confused_with_rated_speed()
    {
        var (m, _, _) = await Run(SmbiosBuilder.Table(
            SmbiosBuilder.Type17("DIMM_A1", "BANK 0", 16 * 1024, memType: 0x22, speed: 6000, configured: 4800),
            SmbiosBuilder.Type17("DIMM_B1", "BANK 1", 16 * 1024, memType: 0x22, speed: 6000, configured: 4800)));

        Assert.Equal(4800, m.SpeedMts.Value);
        Assert.Equal(4800, m.Modules[0].SpeedMts.Value);
        Assert.Equal(6000, m.Modules[0].RatedSpeedMts.Value);
        Assert.False(m.MaxSpeedMts.HasValue);
    }

    [Fact]
    public async Task Two_modules_without_channel_labels_are_not_dual()
    {
        var (m, report, _) = await Run(SmbiosBuilder.Table(
            SmbiosBuilder.Type17("DIMM1", "BANK 0", 8 * 1024),
            SmbiosBuilder.Type17("DIMM2", "BANK 1", 8 * 1024)));

        Assert.False(m.Channels.HasValue);
        Assert.Null(m.Channels.Value);
        Assert.Equal("channel_mode_undetermined", m.Channels.Reason);
        Assert.Contains(report.Limitations, l => l.Code == "channel_mode_undetermined");
    }

    [Fact]
    public async Task Laptop_with_soldered_lpddr5_has_no_slots()
    {
        var chips = Enumerable.Range(0, 8).Select(i =>
            SmbiosBuilder.Type17($"DIMM {i}", $"P0 CHANNEL {(char)('A' + i)}", 2 * 1024, formFactor: 0x0B, memType: 0x23,
                dataWidth: 16, totalWidth: 16, speed: 6400, configured: 6400, maker: "Samsung", part: "K3KL8L80CM-MGCT")).ToArray();
        var (m, _, _) = await Run(SmbiosBuilder.Table(chips));

        Assert.Equal(0, m.SlotsTotal.Value);
        Assert.Equal(0, m.SlotsUsed.Value);
        Assert.Equal("all_memory_soldered", m.SlotsTotal.Note);
        Assert.Equal(16 * GiB, m.SolderedBytes.Value);
        Assert.Equal(8, m.ModulesDetected.Value);
        Assert.All(m.Modules, x => Assert.True(x.Soldered.Value));
        Assert.Equal("LPDDR5", m.MemoryType.Value);
        Assert.Equal("dual", m.Channels.Value); // 8 × 16-bit = 128-bit bus
    }

    [Fact]
    public async Task Soldered_plus_one_sodimm_counts_one_slot()
    {
        var (m, _, _) = await Run(SmbiosBuilder.Table(
            SmbiosBuilder.Type17("Onboard", "BANK 0", 8 * 1024, formFactor: 0x0B, memType: 0x1A),
            SmbiosBuilder.Type17("DIMM 1", "BANK 1", 8 * 1024, formFactor: 0x0D, memType: 0x1A)));

        Assert.Equal((1, 1, 0), (m.SlotsTotal.Value, m.SlotsUsed.Value, m.SlotsFree.Value));
        Assert.Equal(8 * GiB, m.SolderedBytes.Value);
        Assert.Equal(2, m.ModulesDetected.Value);
    }

    [Theory]
    [InlineData(3, false)]  // None
    [InlineData(5, true)]   // Single-bit ECC
    [InlineData(6, true)]   // Multi-bit ECC
    public async Task Ecc_from_type16(int errorCorrection, bool ecc)
    {
        var (m, _, _) = await Run(SmbiosBuilder.Table(
            SmbiosBuilder.Type16(errorCorrection, 0, 1),
            SmbiosBuilder.Type17("DIMM_A1", "BANK 0", 32 * 1024, totalWidth: ecc ? 72 : 64)));
        Assert.Equal(ecc, m.Ecc.Value);
        Assert.Equal(Confidence.Exact, m.Ecc.Confidence);
    }

    [Fact]
    public async Task Ecc_derived_from_widths_when_type16_unknown()
    {
        var (m, _, _) = await Run(SmbiosBuilder.Table(SmbiosBuilder.Type17("DIMM_A1", "BANK 0", 32 * 1024, dataWidth: 64, totalWidth: 72)));
        Assert.True(m.Ecc.Value);
        Assert.Equal(Confidence.Detected, m.Ecc.Confidence);
    }

    [Fact]
    public async Task Mixed_types_and_speeds_are_reported_not_averaged()
    {
        var (m, _, _) = await Run(SmbiosBuilder.Table(
            SmbiosBuilder.Type17("DIMM_A1", "BANK 0", 8 * 1024, configured: 2666),
            SmbiosBuilder.Type17("DIMM_B1", "BANK 1", 8 * 1024, configured: 3200)));
        Assert.Equal(2666, m.SpeedMts.Value);
        Assert.Equal("mixed_speeds_min", m.SpeedMts.Note);
    }

    [Fact]
    public async Task Falls_back_to_wmi_when_smbios_unavailable()
    {
        var wmi = new FakeWmi()
            .On("Win32_PhysicalMemory",
                Row(("DeviceLocator", "DIMM 0"), ("BankLabel", "P0 CHANNEL A"), ("Capacity", 16 * GiB), ("SMBIOSMemoryType", 26),
                    ("Speed", 3200), ("ConfiguredClockSpeed", 3200), ("FormFactor", 12), ("DataWidth", 64), ("TotalWidth", 64),
                    ("Manufacturer", "Kingston"), ("PartNumber", "KF432S20IB/16"), ("Attributes", 1), ("ConfiguredVoltage", 1200)),
                Row(("DeviceLocator", "DIMM 0"), ("BankLabel", "P0 CHANNEL B"), ("Capacity", 16 * GiB), ("SMBIOSMemoryType", 0), ("MemoryType", 26),
                    ("Speed", 3200), ("ConfiguredClockSpeed", 3200), ("FormFactor", 12), ("DataWidth", 64), ("TotalWidth", 64)))
            .On("Win32_PhysicalMemoryArray", Row(("MemoryDevices", 2), ("MemoryErrorCorrection", 3), ("MaxCapacityEx", 64L * 1024 * 1024), ("Use", 3)));

        var (m, _, diag) = await Run(null, wmi, 32 * GiB);

        Assert.Contains("smbios_type17_unavailable_used_wmi", diag.Warnings);
        Assert.Equal(2, m.Modules.Count);
        Assert.All(m.Modules, x => Assert.Equal("DDR4", x.MemoryType.Value)); // second row: legacy MemoryType enum
        Assert.Equal("WMI Win32_PhysicalMemoryArray.MemoryDevices", m.SlotsTotal.Source);
        Assert.Equal((2, 2, 0), (m.SlotsTotal.Value, m.SlotsUsed.Value, m.SlotsFree.Value));
        Assert.Equal("SODIMM", m.Modules[0].FormFactor.Value);
        Assert.Equal(1, m.Modules[0].Rank.Value);
        Assert.Equal("dual", m.Channels.Value);
        Assert.DoesNotContain(m.SmbiosSlots, s => !s.Populated); // WMI does not list empty slots
    }

    [Fact]
    public async Task Wmi_errors_are_isolated()
    {
        var wmi = new FakeWmi()
            .Throw("Win32_PhysicalMemory", new WmiException("Win32_PhysicalMemory", "0x80041003 access denied"))
            .Throw("Win32_PhysicalMemoryArray", new TimeoutException("WMI timeout"));

        var report = new HardwareReport();
        var ctx = new Core.Collection.CollectorContext(report, new CollectorDiagnostics { Id = "memory" });
        await new MemoryCollector(wmi, new ThrowingSmbios(), new FakePlatform { Installed = 8 * GiB }).CollectAsync(ctx, CancellationToken.None);

        Assert.Equal(3, ctx.Diagnostics.Errors.Count);
        Assert.Contains(ctx.Diagnostics.Errors, e => e.Contains("access denied"));
        Assert.Equal(8 * GiB, report.Memory.TotalBytes.Value);
        Assert.Empty(report.Memory.Modules);
        Assert.False(report.Memory.MemoryType.HasValue);
        Assert.False(report.Memory.SlotsTotal.HasValue);
        Assert.Equal("channel_mode_undetermined", report.Memory.Channels.Reason);
    }

    [Fact]
    public void Wmi_form_factor_enum_is_mapped_to_smbios()
    {
        Assert.Equal(0x09, MemoryCollector.FromWmi(Row(("FormFactor", 8))).FormFactor);  // DIMM
        Assert.Equal(0x0D, MemoryCollector.FromWmi(Row(("FormFactor", 12))).FormFactor); // SODIMM
        Assert.Equal(0x0B, MemoryCollector.FromWmi(Row(("FormFactor", 11))).FormFactor); // Row of chips
        Assert.Equal(0x05, MemoryCollector.FromWmi(Row(("FormFactor", 9))).FormFactor);  // Chip
    }

    [Fact]
    public void Serial_is_never_collected()
    {
        var mod = new MemoryModule();
        Assert.Equal(F.Reasons.Privacy, mod.Serial.Reason);
    }

}
