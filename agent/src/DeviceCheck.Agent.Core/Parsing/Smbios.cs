using System.Text;

namespace DeviceCheck.Agent.Core.Parsing;

/// <summary>One SMBIOS structure: formatted area + string set.</summary>
public sealed class SmbiosStructure(byte type, byte[] formatted, IReadOnlyList<string> strings)
{
    public byte Type { get; } = type;
    public byte Length => (byte)Formatted.Length;
    public byte[] Formatted { get; } = formatted;
    public IReadOnlyList<string> Strings { get; } = strings;

    public bool Has(int offset, int size) => offset + size <= Formatted.Length;
    public byte? Byte(int o) => Has(o, 1) ? Formatted[o] : null;
    public ushort? Word(int o) => Has(o, 2) ? BitConverter.ToUInt16(Formatted, o) : null;
    public uint? DWord(int o) => Has(o, 4) ? BitConverter.ToUInt32(Formatted, o) : null;
    public ulong? QWord(int o) => Has(o, 8) ? BitConverter.ToUInt64(Formatted, o) : null;

    /// <summary>String referenced by the byte at <paramref name="o"/> (1-based index, 0 = none).</summary>
    public string? Str(int o)
    {
        var idx = Byte(o);
        if (idx is null or 0 || idx > Strings.Count) return null;
        var s = Strings[idx.Value - 1].Trim();
        return s.Length == 0 ? null : s;
    }
}

public static class SmbiosParser
{
    public static IReadOnlyList<SmbiosStructure> Parse(byte[] data)
    {
        var list = new List<SmbiosStructure>();
        int i = 0;
        while (i + 4 <= data.Length)
        {
            byte type = data[i];
            byte len = data[i + 1];
            if (len < 4 || i + len > data.Length) break;
            var formatted = data.AsSpan(i, len).ToArray();

            // String set: null-terminated strings, terminated by an extra null.
            int p = i + len;
            var strings = new List<string>();
            if (p + 1 < data.Length && data[p] == 0 && data[p + 1] == 0)
            {
                p += 2;
            }
            else
            {
                while (p < data.Length)
                {
                    int start = p;
                    while (p < data.Length && data[p] != 0) p++;
                    strings.Add(Encoding.ASCII.GetString(data, start, p - start));
                    p++; // terminator
                    if (p >= data.Length || data[p] == 0) { p++; break; }
                }
            }
            list.Add(new SmbiosStructure(type, formatted, strings));
            if (type == 127) break; // end-of-table
            i = p;
        }
        return list;
    }
}

// ---------------------------------------------------------------- typed views

public sealed record SmbiosBios(string? Vendor, string? Version, string? ReleaseDate, bool? UefiSupported);

public sealed record SmbiosSystem(string? Manufacturer, string? Product, string? Version, string? Family, string? Sku);

public sealed record SmbiosBaseboard(string? Manufacturer, string? Product, string? Version);

public sealed record SmbiosMemoryArray(int Location, int Use, int ErrorCorrection, long? MaxCapacityBytes, int DeviceCount);

public sealed record SmbiosMemoryDevice(
    string? DeviceLocator,
    string? BankLocator,
    long? SizeBytes,          // 0 = empty slot, null = unknown
    int FormFactor,
    int MemoryType,
    int? TotalWidth,
    int? DataWidth,
    int? SpeedMts,            // maximum capable speed
    int? ConfiguredSpeedMts,  // current speed
    string? Manufacturer,
    string? PartNumber,
    int? Rank,
    int? MinVoltageMv,
    int? MaxVoltageMv,
    int? ConfiguredVoltageMv,
    int? MemoryTechnology);

public static class SmbiosReader
{
    public static SmbiosBios? Bios(IReadOnlyList<SmbiosStructure> s)
    {
        var t = s.FirstOrDefault(x => x.Type == 0);
        if (t is null) return null;
        bool? uefi = null;
        // BIOS Characteristics Extension Byte 2 (offset 0x13), bit 3 = UEFI supported (SMBIOS 2.7+)
        if (t.Byte(0x13) is byte ext2) uefi = (ext2 & 0x08) != 0;
        return new SmbiosBios(t.Str(0x04), t.Str(0x05), t.Str(0x08), uefi);
    }

    public static SmbiosSystem? System(IReadOnlyList<SmbiosStructure> s)
    {
        var t = s.FirstOrDefault(x => x.Type == 1);
        return t is null ? null : new SmbiosSystem(t.Str(0x04), t.Str(0x05), t.Str(0x06), t.Str(0x1A), t.Str(0x19));
    }

    public static SmbiosBaseboard? Baseboard(IReadOnlyList<SmbiosStructure> s)
    {
        var t = s.FirstOrDefault(x => x.Type == 2);
        return t is null ? null : new SmbiosBaseboard(t.Str(0x04), t.Str(0x05), t.Str(0x06));
    }

    /// <summary>Type 16 arrays used as system memory (Use = 3).</summary>
    public static IReadOnlyList<SmbiosMemoryArray> MemoryArrays(IReadOnlyList<SmbiosStructure> s) =>
        s.Where(x => x.Type == 16).Select(t =>
        {
            long? max = null;
            if (t.DWord(0x07) is uint kb)
            {
                if (kb == 0x80000000 && t.QWord(0x0F) is ulong ext) max = (long)ext;
                else if (kb != 0x80000000) max = (long)kb * 1024;
            }
            return new SmbiosMemoryArray(t.Byte(0x04) ?? 0, t.Byte(0x05) ?? 0, t.Byte(0x06) ?? 0, max, t.Word(0x0D) ?? 0);
        }).Where(a => a.Use is 3 or 0).ToList();

    public static IReadOnlyList<SmbiosMemoryDevice> MemoryDevices(IReadOnlyList<SmbiosStructure> s) =>
        s.Where(x => x.Type == 17).Select(ParseType17).ToList();

    private static SmbiosMemoryDevice ParseType17(SmbiosStructure t)
    {
        long? size = null;
        if (t.Word(0x0C) is ushort sz)
        {
            if (sz == 0) size = 0;
            else if (sz == 0xFFFF) size = null;
            else if (sz == 0x7FFF && t.DWord(0x1C) is uint extMb) size = (long)(extMb & 0x7FFFFFFF) * 1024 * 1024;
            else size = (sz & 0x8000) != 0 ? (long)(sz & 0x7FFF) * 1024 : (long)sz * 1024 * 1024;
        }

        static int? Width(ushort? w) => w is null or 0 or 0xFFFF ? null : w;
        static int? Speed(ushort? w, uint? ext) => w switch
        {
            null or 0 => null,
            0xFFFF => ext is > 0 ? (int)(ext.Value & 0x7FFFFFFF) : null,
            _ => w,
        };
        static int? Volt(ushort? v) => v is null or 0 ? null : v;

        int? rank = t.Byte(0x1B) is byte attr && (attr & 0x0F) != 0 ? attr & 0x0F : null;

        return new SmbiosMemoryDevice(
            t.Str(0x10), t.Str(0x11), size,
            t.Byte(0x0E) ?? 0, t.Byte(0x12) ?? 0,
            Width(t.Word(0x08)), Width(t.Word(0x0A)),
            Speed(t.Word(0x15), t.DWord(0x54)),
            Speed(t.Word(0x20), t.DWord(0x58)),
            t.Str(0x17), t.Str(0x1A), rank,
            Volt(t.Word(0x22)), Volt(t.Word(0x24)), Volt(t.Word(0x26)),
            t.Byte(0x28));
    }
}
