using System.Buffers.Binary;

namespace DeviceCheck.Agent.Monitoring.Smart;

public sealed record AtaAttributeEntry(int Id, int Flags, int Normalized, int Worst, long Raw, byte[] RawBytes, int? Threshold);

/// <summary>
/// ATA SMART READ DATA (512 bytes) and READ THRESHOLDS (512 bytes): 30 entries of 12 bytes from offset 2.
/// Entry: id, flags (2), normalized, worst, raw (6, little-endian), reserved.
/// </summary>
public static class AtaSmartParser
{
    public const int Size = 512;
    private const int Entries = 30;
    private const int EntrySize = 12;

    public static int Revision(ReadOnlySpan<byte> data) => data.Length >= 2 ? BinaryPrimitives.ReadUInt16LittleEndian(data) : 0;

    public static IReadOnlyList<AtaAttributeEntry> Parse(ReadOnlySpan<byte> data, ReadOnlySpan<byte> thresholds)
    {
        if (data.Length < Size) throw new ArgumentException($"SMART data must be {Size} bytes, got {data.Length}");
        var thr = new Dictionary<int, int>();
        if (thresholds.Length >= Size)
        {
            for (int i = 0; i < Entries; i++)
            {
                var e = thresholds.Slice(2 + i * EntrySize, EntrySize);
                if (e[0] != 0) thr[e[0]] = e[1];
            }
        }

        var list = new List<AtaAttributeEntry>();
        var seen = new HashSet<int>();
        for (int i = 0; i < Entries; i++)
        {
            var e = data.Slice(2 + i * EntrySize, EntrySize);
            int id = e[0];
            if (id == 0 || !seen.Add(id)) continue; // empty slot or duplicate (corrupt table)
            var raw = e.Slice(5, 6).ToArray();
            long value = 0;
            for (int b = 5; b >= 0; b--) value = (value << 8) | raw[b];
            list.Add(new AtaAttributeEntry(id, BinaryPrimitives.ReadUInt16LittleEndian(e[1..]), e[3], e[4], value, raw,
                thr.TryGetValue(id, out var t) ? t : null));
        }
        return list;
    }
}

/// <summary>How a SMART attribute may be read.</summary>
public enum AttributeSemantics
{
    /// <summary>Meaning and raw format are consistent across vendors (still marked "detected", never "exact").</summary>
    Standard,
    /// <summary>Name is common but raw format or meaning depends on the vendor: raw value only, no interpretation.</summary>
    VendorSpecific,
}

public sealed record AttributeInfo(string Name, AttributeSemantics Semantics, Func<long, byte[], long?>? Interpret = null);

/// <summary>
/// SMART attribute catalog. Only attributes with an established meaning are listed; unknown ids are reported
/// with raw/normalized values and no name. Interpretation exists only for <see cref="AttributeSemantics.Standard"/>.
/// </summary>
public static class AtaAttributeCatalog
{
    // Count-style attributes keep the full 48-bit raw value; some drives use the upper bytes, so only the low 32 bits are trusted.
    private static long? Low32(long raw, byte[] _) => raw & 0xFFFFFFFF;
    // Temperature: the low byte is the current temperature on every major vendor; upper bytes may hold min/max.
    private static long? LowByte(long raw, byte[] b) => b[0] is > 0 and < 128 ? b[0] : null;

    private static readonly Dictionary<int, AttributeInfo> Known = new()
    {
        [1] = new("Raw Read Error Rate", AttributeSemantics.VendorSpecific),
        [3] = new("Spin-Up Time", AttributeSemantics.VendorSpecific),
        [4] = new("Start/Stop Count", AttributeSemantics.Standard, Low32),
        [5] = new("Reallocated Sectors Count", AttributeSemantics.Standard, Low32),
        [7] = new("Seek Error Rate", AttributeSemantics.VendorSpecific),
        [9] = new("Power-On Hours", AttributeSemantics.Standard, Low32),
        [10] = new("Spin Retry Count", AttributeSemantics.Standard, Low32),
        [12] = new("Power Cycle Count", AttributeSemantics.Standard, Low32),
        [170] = new("Available Reserved Space", AttributeSemantics.VendorSpecific),
        [173] = new("Wear Leveling Count", AttributeSemantics.VendorSpecific),
        [174] = new("Unexpected Power Loss Count", AttributeSemantics.VendorSpecific),
        [177] = new("Wear Leveling Count", AttributeSemantics.VendorSpecific),
        [179] = new("Used Reserved Block Count", AttributeSemantics.VendorSpecific),
        [181] = new("Program Fail Count", AttributeSemantics.VendorSpecific),
        [182] = new("Erase Fail Count", AttributeSemantics.VendorSpecific),
        [183] = new("SATA Downshift Error Count", AttributeSemantics.VendorSpecific),
        [184] = new("End-to-End Error", AttributeSemantics.VendorSpecific),
        [187] = new("Reported Uncorrectable Errors", AttributeSemantics.VendorSpecific),
        [188] = new("Command Timeout", AttributeSemantics.VendorSpecific),
        [190] = new("Airflow Temperature", AttributeSemantics.VendorSpecific),
        [191] = new("G-Sense Error Rate", AttributeSemantics.VendorSpecific),
        [192] = new("Power-off Retract Count", AttributeSemantics.VendorSpecific),
        [193] = new("Load Cycle Count", AttributeSemantics.Standard, Low32),
        [194] = new("Temperature", AttributeSemantics.Standard, LowByte),
        [195] = new("Hardware ECC Recovered", AttributeSemantics.VendorSpecific),
        [196] = new("Reallocation Event Count", AttributeSemantics.Standard, Low32),
        [197] = new("Current Pending Sector Count", AttributeSemantics.Standard, Low32),
        [198] = new("Offline Uncorrectable Sector Count", AttributeSemantics.Standard, Low32),
        [199] = new("UDMA CRC Error Count", AttributeSemantics.Standard, Low32),
        [200] = new("Write Error Rate", AttributeSemantics.VendorSpecific),
        [202] = new("Percent Lifetime Remaining", AttributeSemantics.VendorSpecific),
        [231] = new("SSD Life Left", AttributeSemantics.VendorSpecific),
        [232] = new("Available Reserved Space", AttributeSemantics.VendorSpecific),
        [233] = new("Media Wearout Indicator", AttributeSemantics.VendorSpecific),
        [235] = new("Good Block Count / Power Loss Protection", AttributeSemantics.VendorSpecific),
        [240] = new("Head Flying Hours", AttributeSemantics.VendorSpecific),
        [241] = new("Total Host Writes", AttributeSemantics.VendorSpecific), // units: LBAs, 32 MiB, GiB... per vendor
        [242] = new("Total Host Reads", AttributeSemantics.VendorSpecific),
        [246] = new("Total Host Sector Writes", AttributeSemantics.VendorSpecific),
    };

    public static AttributeInfo? Lookup(int id) => Known.GetValueOrDefault(id);
}
