using System.Text.RegularExpressions;

namespace DeviceCheck.Agent.Core.Normalization;

public static partial class FirmwareText
{
    // Values that OEMs leave in SMBIOS instead of real data.
    private static readonly HashSet<string> Placeholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "To be filled by O.E.M.", "To Be Filled By O.E.M.", "Default string", "System manufacturer", "System Product Name",
        "System Version", "Not Applicable", "Not Specified", "None", "N/A", "NA", "Unknown", "OEM", "O.E.M.", "x.x",
        "Base Board Product Name", "Base Board Version", "Default", "Undefined", "Type1ProductConfigId", "Type2 - Board Product Name",
        "Type2 - Board Version", "Board Version", "Invalid", "0", "00000000", "0000", "Not Available", "SKU", "Default_String",
    };

    /// <summary>Trims the value and returns null for empty strings and known OEM placeholders.</summary>
    public static string? Clean(string? s)
    {
        if (s is null) return null;
        s = s.Trim().Trim('\0').Trim();
        if (s.Length == 0 || Placeholders.Contains(s) || AllSame().IsMatch(s)) return null;
        return s;
    }

    public static bool IsPlaceholder(string? s) => s is not null && s.Trim().Length > 0 && Clean(s) is null;

    [GeneratedRegex(@"^(.)\1+$")]
    private static partial Regex AllSame();
}

public static class MemoryTypes
{
    /// <summary>SMBIOS Type 17 "Memory Type" (offset 0x12) → name.</summary>
    public static string? FromSmbios(int code) => code switch
    {
        0x0F => "SDRAM",
        0x12 => "DDR",
        0x13 => "DDR2",
        0x14 => "DDR2 FB-DIMM",
        0x18 => "DDR3",
        0x19 => "FBD2",
        0x1A => "DDR4",
        0x1B => "LPDDR",
        0x1C => "LPDDR2",
        0x1D => "LPDDR3",
        0x1E => "LPDDR4",
        0x1F => "Logical non-volatile device",
        0x20 => "HBM",
        0x21 => "HBM2",
        0x22 => "DDR5",
        0x23 => "LPDDR5",
        0x24 => "HBM3",
        _ => null,
    };

    /// <summary>Win32_PhysicalMemory.MemoryType (legacy CIM enum, used when SMBIOSMemoryType is 0).</summary>
    public static string? FromWmiLegacy(int code) => code switch
    {
        20 => "DDR",
        21 => "DDR2",
        22 => "DDR2 FB-DIMM",
        24 => "DDR3",
        26 => "DDR4",
        _ => null,
    };

    public static bool IsLowPower(string? type) => type?.StartsWith("LPDDR", StringComparison.Ordinal) == true;

    /// <summary>SMBIOS Type 17 Form Factor (offset 0x0E).</summary>
    public static string? FormFactor(int code) => code switch
    {
        0x01 => "Other",
        0x03 => "SIMM",
        0x04 => "SIP",
        0x05 => "Chip",
        0x06 => "DIP",
        0x07 => "ZIP",
        0x08 => "Proprietary Card",
        0x09 => "DIMM",
        0x0A => "TSOP",
        0x0B => "Row of chips",
        0x0C => "RIMM",
        0x0D => "SODIMM",
        0x0E => "SRIMM",
        0x0F => "FB-DIMM",
        0x10 => "Die",
        0x11 => "CAMM",
        _ => null,
    };

    /// <summary>Memory soldered to the board does not occupy a slot.</summary>
    public static bool IsSolderedFormFactor(int code) => code is 0x05 or 0x0B or 0x10;
}

public static class Vendors
{
    // JEDEC JEP106 ids as SMBIOS reports them ("80CE", "CE00", "Samsung" ...).
    private static readonly Dictionary<string, string> Jedec = new(StringComparer.OrdinalIgnoreCase)
    {
        ["80CE"] = "Samsung", ["CE00"] = "Samsung", ["00CE"] = "Samsung",
        ["80AD"] = "SK hynix", ["AD00"] = "SK hynix", ["00AD"] = "SK hynix",
        ["802C"] = "Micron", ["2C00"] = "Micron", ["002C"] = "Micron",
        ["859B"] = "Crucial", ["9B05"] = "Crucial",
        ["0198"] = "Kingston", ["9801"] = "Kingston",
        ["04CD"] = "G.Skill", ["CD04"] = "G.Skill",
        ["029E"] = "Corsair", ["9E02"] = "Corsair",
        ["04CB"] = "ADATA", ["CB04"] = "ADATA",
        ["04EF"] = "Team Group", ["EF04"] = "Team Group",
        ["0194"] = "Smart Modular",
        ["8551"] = "Qimonda",
        ["80B3"] = "Nanya", ["B300"] = "Nanya",
        ["8A76"] = "CXMT",
        ["0443"] = "Ramaxel", ["4304"] = "Ramaxel",
        ["8983"] = "Hewlett Packard Enterprise",
    };

    /// <summary>Returns (name, fromJedecCode). Unknown codes are returned as-is.</summary>
    public static (string? Name, bool FromCode) MemoryManufacturer(string? raw)
    {
        var s = FirmwareText.Clean(raw);
        if (s is null) return (null, false);
        var key = s.Replace(" ", "");
        if (key.Length >= 4 && Jedec.TryGetValue(key[..4], out var n) && key.Length <= 16 && IsHex(key)) return (n, true);
        if (IsHex(key) && key.Trim('0').Length == 0) return (null, false);
        return (s, false);
    }

    private static bool IsHex(string s) => s.All(Uri.IsHexDigit);

    // Module part-number prefixes of major vendors (documented naming schemes).
    private static readonly (string Prefix, string Name)[] MemoryPartPrefixes =
    [
        ("CT", "Crucial"), ("BL", "Crucial Ballistix"), ("MTA", "Micron"), ("MTC", "Micron"), ("KF", "Kingston"), ("KVR", "Kingston"),
        ("KCP", "Kingston"), ("KSM", "Kingston"), ("F3-", "G.Skill"), ("F4-", "G.Skill"), ("F5-", "G.Skill"), ("CM", "Corsair"),
        ("M471", "Samsung"), ("M378", "Samsung"), ("M393", "Samsung"), ("M425", "Samsung"), ("M321", "Samsung"), ("M323", "Samsung"),
        ("HMA", "SK hynix"), ("HMC", "SK hynix"), ("HMCG", "SK hynix"), ("HMT", "SK hynix"), ("AD4", "ADATA"), ("AD5", "ADATA"),
        ("AX4", "ADATA XPG"), ("AX5", "ADATA XPG"), ("TED", "Team Group"), ("TF", "Team Group"), ("FF", "Team Group"),
    ];

    public static string? MemoryManufacturerFromPartNumber(string? part)
    {
        var p = FirmwareText.Clean(part);
        if (p is null) return null;
        return MemoryPartPrefixes.Where(x => p.StartsWith(x.Prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Prefix.Length).Select(x => x.Name).FirstOrDefault();
    }

    public static string? PciVendor(uint id) => id switch
    {
        0x10DE => "NVIDIA",
        0x1002 or 0x1022 => "AMD",
        0x8086 => "Intel",
        0x5143 => "Qualcomm",
        0x1414 => "Microsoft",
        0x15AD => "VMware",
        0x80EE => "Oracle (VirtualBox)",
        0x1AF4 => "Red Hat (virtio)",
        0x1234 => "QEMU",
        0x1AB8 => "Parallels",
        0x106B => "Apple",
        0x13B5 => "ARM",
        0x1A03 => "ASPEED",
        0x102B => "Matrox",
        _ => null,
    };

    public static bool IsVirtualGpuVendor(uint id) => id is 0x15AD or 0x80EE or 0x1AF4 or 0x1234 or 0x1AB8 or 0x1414;

    private static readonly Dictionary<string, string> Pnp = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SAM"] = "Samsung", ["SEC"] = "Samsung", ["SDC"] = "Samsung Display", ["DEL"] = "Dell", ["GSM"] = "LG", ["LGD"] = "LG Display",
        ["AUS"] = "ASUS", ["ACR"] = "Acer", ["BNQ"] = "BenQ", ["AOC"] = "AOC", ["HWP"] = "HP", ["HPN"] = "HP", ["LEN"] = "Lenovo",
        ["PHL"] = "Philips", ["VSC"] = "ViewSonic", ["MSI"] = "MSI", ["BOE"] = "BOE", ["AUO"] = "AU Optronics", ["CMN"] = "Innolux",
        ["SHP"] = "Sharp", ["APP"] = "Apple", ["GBT"] = "Gigabyte", ["HKC"] = "HKC", ["IVM"] = "iiyama", ["EIZ"] = "EIZO",
        ["XMI"] = "Xiaomi", ["NEC"] = "NEC", ["SNY"] = "Sony", ["HSD"] = "HannStar", ["CSO"] = "CSOT", ["TMX"] = "Tianma",
        ["IVO"] = "InfoVision", ["MEI"] = "Panasonic", ["HIQ"] = "Hyundai ImageQuest", ["FUS"] = "Fujitsu", ["GGL"] = "Google",
        ["MS_"] = "Microsoft", ["MSF"] = "Microsoft", ["RHT"] = "Red Hat", ["VSN"] = "ViewSonic", ["PNP"] = "Generic",
    };

    public static string? MonitorManufacturer(string? pnpCode) =>
        pnpCode is not null && Pnp.TryGetValue(pnpCode, out var n) ? n : null;

    // Storage: manufacturer from model name prefix (detected, not exact).
    private static readonly (string Prefix, string Name)[] DiskPrefixes =
    [
        ("Samsung", "Samsung"), ("MZ", "Samsung"), ("WDC", "Western Digital"), ("WD", "Western Digital"), ("Western Digital", "Western Digital"),
        ("Seagate", "Seagate"), ("ST", "Seagate"), ("Kingston", "Kingston"), ("KINGSTON", "Kingston"), ("Crucial", "Crucial"), ("CT", "Crucial"),
        ("Micron", "Micron"), ("SK hynix", "SK hynix"), ("SKHynix", "SK hynix"), ("HFM", "SK hynix"), ("KIOXIA", "KIOXIA"), ("KXG", "KIOXIA"),
        ("TOSHIBA", "Toshiba"), ("INTEL", "Intel"), ("SSDPE", "Intel"), ("SanDisk", "SanDisk"), ("ADATA", "ADATA"), ("Lexar", "Lexar"),
        ("Corsair", "Corsair"), ("Netac", "Netac"), ("Patriot", "Patriot"), ("TEAM", "Team Group"), ("Transcend", "Transcend"),
        ("HGST", "HGST"), ("Hitachi", "Hitachi"), ("PNY", "PNY"), ("Sabrent", "Sabrent"), ("Phison", "Phison"), ("Solidigm", "Solidigm"),
        ("Apacer", "Apacer"), ("Silicon Power", "Silicon Power"), ("SPCC", "Silicon Power"), ("Goodram", "Goodram"), ("Verbatim", "Verbatim"),
        ("Msft", "Microsoft"), ("Virtual", "Virtual"), ("VMware", "VMware"), ("QEMU", "QEMU"), ("Generic", "Generic"),
    ];

    public static string? DiskManufacturerFromModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return null;
        var m = model.Trim();
        foreach (var (prefix, name) in DiskPrefixes)
        {
            if (!m.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            // Short alphanumeric prefixes (ST, CT, WD, MZ) must be followed by a digit or separator.
            if (prefix.Length <= 3 && m.Length > prefix.Length && char.IsLetter(m[prefix.Length]) && prefix.All(char.IsUpper)) continue;
            return name;
        }
        return null;
    }
}
