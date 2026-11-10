using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Core.Normalization;
using DeviceCheck.Agent.Core.Parsing;

namespace DeviceCheck.Agent.Tests;

public class CpuNormalizationTests
{
    [Theory]
    [InlineData("GenuineIntel", "Intel")]
    [InlineData("AuthenticAMD", "AMD")]
    [InlineData("HygonGenuine", "Hygon")]
    [InlineData(null, null)]
    public void Vendor_from_cpuid(string? raw, string? expected) => Assert.Equal(expected, CpuIdentification.NormalizeVendor(raw));

    [Theory]
    [InlineData("Intel(R) Core(TM) i7-8700K CPU @ 3.70GHz", "Intel Core i7-8700K @ 3.70GHz")]
    [InlineData("AMD Ryzen 9 5900X 12-Core Processor            ", "AMD Ryzen 9 5900X")]
    [InlineData("AMD Ryzen 7 7840HS w/ Radeon 780M Graphics        ", "AMD Ryzen 7 7840HS w/ Radeon 780M Graphics")]
    [InlineData("   ", null)]
    public void Brand_is_cleaned(string raw, string? expected) => Assert.Equal(expected, CpuIdentification.NormalizeBrand(raw));

    [Theory]
    [InlineData("12th Gen Intel(R) Core(TM) i7-12700K", "12th Gen Intel Core")]
    [InlineData("Intel(R) Core(TM) i5-8250U CPU @ 1.60GHz", "8th Gen Intel Core")]
    [InlineData("Intel(R) Core(TM) i7-1165G7 @ 2.80GHz", "11th Gen Intel Core")]
    [InlineData("13th Gen Intel(R) Core(TM) i9-13900K", "13th Gen Intel Core")]
    [InlineData("Intel(R) Core(TM) Ultra 7 155H", "Core Ultra Series 1")]
    [InlineData("Intel(R) Core(TM) Ultra 9 285K", "Core Ultra Series 2")]
    [InlineData("AMD Ryzen 7 7840HS w/ Radeon 780M Graphics", "Ryzen 7000 Series")]
    [InlineData("AMD Ryzen 5 PRO 5650U with Radeon Graphics", "Ryzen 5000 Series")]
    [InlineData("AMD Ryzen AI 9 HX 370 w/ Radeon 890M", "Ryzen AI 300 Series")]
    [InlineData("Intel(R) Xeon(R) Gold 6338 CPU @ 2.00GHz", null)]
    [InlineData("Intel(R) Core(TM)2 Duo CPU E8400", null)]
    public void Generation_from_brand(string brand, string? expected) => Assert.Equal(expected, CpuIdentification.Generation(brand));

    [Fact]
    public void Microarchitecture_lookup_amd_intel()
    {
        var phoenix = CpuIdentification.Lookup("AMD", 0x19, 0x74);
        Assert.Equal("Zen 4", phoenix?.Microarchitecture);
        Assert.Equal("Phoenix", phoenix?.Codename);
        Assert.Equal(4, phoenix?.ProcessNm);

        var adl = CpuIdentification.Lookup("Intel", 6, 0x97);
        Assert.Equal("Alder Lake", adl?.Codename);
        Assert.Null(adl?.ProcessNm); // "Intel 7" is a marketing node name, not a nm value
    }

    [Fact]
    public void Unknown_cpu_is_not_guessed()
    {
        Assert.Null(CpuIdentification.Lookup("Intel", 6, 0x01));
        Assert.Null(CpuIdentification.Lookup("AMD", 0x30, 0x00));
        Assert.Null(CpuIdentification.Lookup("Zhaoxin", 7, 0x3B));
    }
}

public class MemoryNormalizationTests
{
    [Theory]
    [InlineData(0x18, "DDR3")]
    [InlineData(0x1A, "DDR4")]
    [InlineData(0x22, "DDR5")]
    [InlineData(0x1D, "LPDDR3")]
    [InlineData(0x1E, "LPDDR4")]
    [InlineData(0x23, "LPDDR5")]
    [InlineData(0x02, null)]
    [InlineData(0x00, null)]
    public void Ddr_type_from_smbios(int code, string? expected) => Assert.Equal(expected, MemoryTypes.FromSmbios(code));

    [Theory]
    [InlineData(24, "DDR3")]
    [InlineData(26, "DDR4")]
    [InlineData(0, null)]
    public void Ddr_type_from_legacy_wmi(int code, string? expected) => Assert.Equal(expected, MemoryTypes.FromWmiLegacy(code));

    [Theory]
    [InlineData(0x05, true)]   // Chip
    [InlineData(0x0B, true)]   // Row of chips
    [InlineData(0x10, true)]   // Die
    [InlineData(0x09, false)]  // DIMM
    [InlineData(0x0D, false)]  // SODIMM
    [InlineData(0x11, false)]  // CAMM
    public void Soldered_form_factors(int ff, bool soldered) => Assert.Equal(soldered, MemoryTypes.IsSolderedFormFactor(ff));

    [Theory]
    [InlineData("DIMM_A1", null, "A")]
    [InlineData("DIMM 0", "P0 CHANNEL B", "B")]
    [InlineData("ChannelA-DIMM0", "BANK 0", "A")]
    [InlineData("DIMM1", "BANK 0", null)]
    [InlineData("XMM1", null, null)]
    public void Channel_from_locators(string device, string? bank, string? expected) => Assert.Equal(expected, MemoryTopology.Channel(device, bank));

    [Fact]
    public void Dual_channel_requires_labels_not_module_count()
    {
        Assert.Equal("unknown", MemoryTopology.Detect([(null, 64), (null, 64)]).Mode);
        Assert.Equal("dual", MemoryTopology.Detect([("A", 64), ("B", 64)]).Mode);
        // two modules on the same channel = single channel
        Assert.Equal("single", MemoryTopology.Detect([("A", 64), ("A", 64)]).Mode);
        Assert.Equal("unknown", MemoryTopology.Detect([]).Mode);
    }

    [Fact]
    public void Lpddr_narrow_channels_are_classified_by_bus_width()
    {
        // LPDDR5: 8 × 16-bit channels = 128-bit bus = "dual" in DDR terms
        var r = MemoryTopology.Detect(Enumerable.Range(0, 8).Select(i => ((string?)((char)('A' + i)).ToString(), (int?)16)).ToList());
        Assert.Equal("dual", r.Mode);
        Assert.Equal(128, r.BusWidthBits);
    }

    [Theory]
    [InlineData("80CE", "Samsung", true)]
    [InlineData("80AD000080AD", "SK hynix", true)]
    [InlineData("Micron Technology", "Micron Technology", false)]
    [InlineData("0000", null, false)]
    [InlineData("Unknown", null, false)]
    public void Memory_manufacturer(string raw, string? name, bool fromCode)
    {
        var (n, c) = Vendors.MemoryManufacturer(raw);
        Assert.Equal(name, n);
        Assert.Equal(fromCode, c);
    }

    [Theory]
    [InlineData("CT32G56C46S5.M16D", "Crucial")]
    [InlineData("M471A1K43DB1-CWE", "Samsung")]
    [InlineData("HMCG78AGBSA092N", "SK hynix")]
    [InlineData("F5-6000J3038F16G", "G.Skill")]
    [InlineData("XYZ123", null)]
    public void Memory_manufacturer_from_part_number(string part, string? expected) =>
        Assert.Equal(expected, Vendors.MemoryManufacturerFromPartNumber(part));
}

public class MissingValueTests
{
    [Fact]
    public void Empty_values_become_unavailable()
    {
        Assert.Equal(Confidence.Unavailable, F.Exact<string?>(null, "x").Confidence);
        Assert.Equal(Confidence.Unavailable, F.Exact<string?>("  ", "x").Confidence);
        Assert.Equal(Confidence.Unavailable, F.Exact<List<string>?>([], "x").Confidence);
        Assert.Equal(Confidence.Unavailable, F.Exact<double?>(double.NaN, "x").Confidence);
        Assert.Equal(F.Reasons.NotReported, F.Exact<int?>(null, "x").Reason);
        Assert.Null(F.Exact<int?>(null, "x").Value);
    }

    [Fact]
    public void Zero_is_a_value()
    {
        var f = F.Exact<long?>(0, "DXGI");
        Assert.True(f.HasValue);
        Assert.Equal(0, f.Value);
    }

    [Fact]
    public void First_available_and_weakest()
    {
        var f = F.FirstAvailable(F.Unavailable<int?>("a", "error"), F.Exact<int?>(5, "b"));
        Assert.Equal("b", f.Source);
        Assert.Equal("timeout", F.FirstAvailable(F.Unavailable<int?>("a", "error"), F.Unavailable<int?>("b", "timeout")).Reason);
        Assert.Equal(Confidence.Detected, F.Weakest(Confidence.Exact, Confidence.Detected));
        Assert.Equal(Confidence.Unavailable, F.Weakest(Confidence.Exact, Confidence.Unavailable));
    }

    [Theory]
    [InlineData("To be filled by O.E.M.")]
    [InlineData("Default string")]
    [InlineData("System Product Name")]
    [InlineData("0000000")]
    [InlineData("   ")]
    public void Firmware_placeholders_are_dropped(string s) => Assert.Null(FirmwareText.Clean(s));

    [Fact]
    public void Real_firmware_text_is_kept() => Assert.Equal("ROG STRIX B650E-F", FirmwareText.Clean("  ROG STRIX B650E-F\0"));
}

public class ParsingTests
{
    [Theory]
    [InlineData("20261110080000.000000+180", 2026, 11, 10, 8, 3)]
    [InlineData("20250101000000.000000-300", 2025, 1, 1, 0, -5)]
    public void Cim_datetime(string s, int y, int m, int d, int h, int offsetHours)
    {
        var dt = CimDate.Parse(s);
        Assert.Equal(new DateTimeOffset(y, m, d, h, 0, 0, TimeSpan.FromHours(offsetHours)), dt);
        Assert.Equal(TimeSpan.FromHours(offsetHours), dt!.Value.Offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026")]
    [InlineData("abcdefghijklmn.000000+000")]
    public void Cim_datetime_invalid(string? s) => Assert.Null(CimDate.Parse(s));

    [Theory]
    [InlineData("11/13/2023", "2023-11-13")]
    [InlineData("1/5/2020", "2020-01-05")]
    [InlineData("garbage", null)]
    public void Bios_date_to_iso(string s, string? iso) => Assert.Equal(iso, CimDate.BiosDateToIso(s));

    [Fact]
    public void Edid_identity_native_mode_and_size()
    {
        var e = EdidParser.Parse(Fixtures.EdidBuilder.Dell())!;
        Assert.Equal("DEL", e.ManufacturerCode);
        Assert.Equal(0xA0F2, e.ProductCode);
        Assert.Equal(2021, e.ManufactureYear);
        Assert.Equal("DELL U2723QE", e.MonitorName);
        Assert.Equal((2560, 1440), (e.NativeWidth, e.NativeHeight));
        Assert.Equal((597, 336), (e.ImageWidthMm, e.ImageHeightMm));
    }

    [Fact]
    public void Edid_invalid_header_is_rejected()
    {
        Assert.Null(EdidParser.Parse(new byte[128]));
        Assert.Null(EdidParser.Parse(null));
    }

    [Fact]
    public void Pnp_id_from_display_config() => Assert.Equal("DEL", EdidParser.DecodePnpId(0xAC10));

    [Fact]
    public void Smbios_type17_sizes_and_speeds()
    {
        var table = Fixtures.SmbiosBuilder.Table(
            Fixtures.SmbiosBuilder.Type17("DIMM_A1", "BANK 0", 16 * 1024, memType: 0x1A, speed: 3200, configured: 2933),
            Fixtures.SmbiosBuilder.Type17("DIMM_A2", "BANK 1", 0),
            Fixtures.SmbiosBuilder.Type17("DIMM_B1", "BANK 2", 64 * 1024, memType: 0x22)); // > 32 GiB → extended size field
        var devs = SmbiosReader.MemoryDevices(SmbiosParser.Parse(table));

        Assert.Equal(3, devs.Count);
        Assert.Equal(16L << 30, devs[0].SizeBytes);
        Assert.Equal(3200, devs[0].SpeedMts);
        Assert.Equal(2933, devs[0].ConfiguredSpeedMts);
        Assert.Equal("Samsung", devs[0].Manufacturer);
        Assert.Equal(0, devs[1].SizeBytes);
        Assert.Equal(64L << 30, devs[2].SizeBytes);
    }

    [Fact]
    public void Smbios_strings_and_end_of_table()
    {
        var s = SmbiosParser.Parse(Fixtures.SmbiosBuilder.Table(Fixtures.SmbiosBuilder.Type2("ASUSTeK COMPUTER INC.", "PRIME B650-PLUS", "Rev 1.xx")));
        Assert.Equal(2, s.Count); // Type 2 + Type 127
        var b = SmbiosReader.Baseboard(s)!;
        Assert.Equal(("ASUSTeK COMPUTER INC.", "PRIME B650-PLUS", "Rev 1.xx"), (b.Manufacturer, b.Product, b.Version));
    }

    [Fact]
    public void Smbios_truncated_data_does_not_throw()
    {
        var full = Fixtures.SmbiosBuilder.Table(Fixtures.SmbiosBuilder.Type17("DIMM", "B", 8192));
        for (int n = 0; n < full.Length; n++) SmbiosParser.Parse(full[..n]);
    }
}
