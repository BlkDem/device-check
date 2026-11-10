using System.Buffers.Binary;

namespace DeviceCheck.Agent.Monitoring.Smart;

/// <summary>A 128-bit NVMe counter. <see cref="AsLong"/> is null when it does not fit into a signed 64-bit value.</summary>
public readonly record struct NvmeCounter(ulong Low, ulong High)
{
    public long? AsLong => High == 0 && Low <= long.MaxValue ? (long)Low : null;
}

/// <summary>NVMe Health Information log page 02h (NVMe Base Specification 1.4, figure 194). All fields little-endian.</summary>
public sealed record NvmeHealthLog(
    byte CriticalWarning,
    int CompositeTemperatureKelvin,
    int AvailableSpare,
    int AvailableSpareThreshold,
    int PercentageUsed,
    NvmeCounter DataUnitsRead,
    NvmeCounter DataUnitsWritten,
    NvmeCounter HostReadCommands,
    NvmeCounter HostWriteCommands,
    NvmeCounter ControllerBusyTime,
    NvmeCounter PowerCycles,
    NvmeCounter PowerOnHours,
    NvmeCounter UnsafeShutdowns,
    NvmeCounter MediaErrors,
    NvmeCounter ErrorLogEntries,
    uint WarningTemperatureTime,
    uint CriticalTemperatureTime,
    IReadOnlyList<int> SensorKelvin)
{
    public const int Size = 512;

    /// <summary>One data unit = 1000 units of 512 bytes (spec: "reported in thousands of 512-byte units, rounded up").</summary>
    public const long BytesPerDataUnit = 512_000;

    public static NvmeHealthLog Parse(ReadOnlySpan<byte> d)
    {
        if (d.Length < Size) throw new ArgumentException($"NVMe health log must be {Size} bytes, got {d.Length}");
        static NvmeCounter Counter(ReadOnlySpan<byte> s, int o) => new(BinaryPrimitives.ReadUInt64LittleEndian(s[o..]), BinaryPrimitives.ReadUInt64LittleEndian(s[(o + 8)..]));
        var sensors = new int[8];
        for (int i = 0; i < 8; i++) sensors[i] = BinaryPrimitives.ReadUInt16LittleEndian(d[(200 + i * 2)..]);
        return new NvmeHealthLog(
            d[0],
            BinaryPrimitives.ReadUInt16LittleEndian(d[1..]),
            d[3], d[4], d[5],
            Counter(d, 32), Counter(d, 48), Counter(d, 64), Counter(d, 80), Counter(d, 96),
            Counter(d, 112), Counter(d, 128), Counter(d, 144), Counter(d, 160), Counter(d, 176),
            BinaryPrimitives.ReadUInt32LittleEndian(d[192..]),
            BinaryPrimitives.ReadUInt32LittleEndian(d[196..]),
            sensors);
    }

    /// <summary>Kelvin → °C. The spec reports whole kelvins; 0 means "not reported".</summary>
    public static int? Celsius(int kelvin) => kelvin is > 0 and < 0xFFFF ? kelvin - 273 : null;

    /// <summary>Critical Warning bits (figure 194, byte 0).</summary>
    public static IReadOnlyList<string> WarningFlags(byte cw)
    {
        string[] names = ["available_spare_below_threshold", "temperature_threshold", "reliability_degraded", "read_only",
            "volatile_backup_failed", "persistent_memory_read_only", "reserved_bit_6", "reserved_bit_7"];
        return Enumerable.Range(0, 8).Where(b => (cw & (1 << b)) != 0).Select(b => names[b]).ToList();
    }

    /// <summary>Bytes = data units × 512 000; null on overflow.</summary>
    public static long? ToBytes(NvmeCounter units)
    {
        if (units.AsLong is not long u) return null;
        try { return checked(u * BytesPerDataUnit); }
        catch (OverflowException) { return null; }
    }
}

/// <summary>Fields of the NVMe Identify Controller structure (CNS 01h) used for health.</summary>
public sealed record NvmeIdentify(int WarningTempKelvin, int CriticalTempKelvin, string? Model, string? Firmware)
{
    public const int Size = 4096;

    public static NvmeIdentify Parse(ReadOnlySpan<byte> d)
    {
        if (d.Length < 512) throw new ArgumentException($"NVMe identify data too short: {d.Length}");
        static string? Ascii(ReadOnlySpan<byte> s)
        {
            var t = System.Text.Encoding.ASCII.GetString(s).Trim('\0', ' ');
            return t.Length == 0 ? null : t;
        }
        return new NvmeIdentify(
            BinaryPrimitives.ReadUInt16LittleEndian(d[266..]), // WCTEMP
            BinaryPrimitives.ReadUInt16LittleEndian(d[268..]), // CCTEMP
            Ascii(d[24..64]),                                  // MN (SN at 4..23 is deliberately not read)
            Ascii(d[64..72]));                                 // FR
    }
}
