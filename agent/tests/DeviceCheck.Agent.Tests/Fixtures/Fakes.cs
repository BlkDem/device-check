using System.Text;
using DeviceCheck.Agent.Core.Collection;
using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Core.Sources;

namespace DeviceCheck.Agent.Tests.Fixtures;

// Test fixtures only. They are never used by the real scan.

/// <summary>WMI fake: maps a class name found in the WQL to rows, or to an exception.</summary>
public sealed class FakeWmi : IWmiClient
{
    private readonly Dictionary<string, Func<IReadOnlyList<WmiObject>>> _map = new(StringComparer.OrdinalIgnoreCase);

    public FakeWmi On(string className, params WmiObject[] rows) { _map[className] = () => rows; return this; }
    public FakeWmi Throw(string className, Exception e) { _map[className] = () => throw e; return this; }

    public Task<IReadOnlyList<WmiObject>> QueryAsync(string ns, string wql, CancellationToken ct)
    {
        var cls = System.Text.RegularExpressions.Regex.Match(wql, @"FROM\s+(\w+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value;
        return Task.FromResult(_map.TryGetValue(cls, out var f) ? f() : (IReadOnlyList<WmiObject>)[]);
    }

    public static WmiObject Row(params (string Key, object? Value)[] props)
    {
        var o = new WmiObject();
        foreach (var (k, v) in props) o[k] = v;
        return o;
    }
}

public sealed class FakeSmbios(byte[]? data) : ISmbiosSource
{
    public SmbiosTable? Read() => data is null ? null : new SmbiosTable(3, 5, data);
}

public sealed class ThrowingSmbios : ISmbiosSource
{
    public SmbiosTable? Read() => throw new InvalidOperationException("firmware table unavailable");
}

public sealed class FakePlatform : IPlatformSource
{
    public long? Installed { get; init; } = 16L << 30;
    public string? FirmwareType() => "uefi";
    public long UptimeMilliseconds() => 3_600_000;
    public string OsArchitecture() => "x86-64";
    public object? CurrentVersionValue(string name) => name switch { "DisplayVersion" => "24H2", "UBR" => 2314, "EditionID" => "Professional", _ => null };
    public MemoryStatus GetMemoryStatus() => new(Installed, Installed - (512L << 20));
    public IReadOnlyList<VolumeData> GetVolumes() => [new("C:\\", "NTFS", "fixed", 500L << 30, 100L << 30)];
    public bool IsElevated() => false;
}

public sealed class FakeProcessor(ProcessorTopology? topo, CpuidInfo? cpuid) : IProcessorSource
{
    public ProcessorTopology? GetTopology() => topo;
    public CpuidInfo? GetCpuid() => cpuid;
}

public sealed class FakeGraphics(params GraphicsAdapterInfo[] adapters) : IGraphicsSource
{
    public IReadOnlyList<GraphicsAdapterInfo> GetAdapters() => adapters;
}

public sealed class FakeDisplays(params DisplayPathInfo[] paths) : IDisplaySource
{
    public IReadOnlyList<DisplayPathInfo> GetActiveDisplays() => paths;
}

public sealed class DelayCollector(TimeSpan delay) : IHardwareCollector
{
    public string Id => "delay";
    public Task CollectAsync(CollectorContext ctx, CancellationToken ct) => Task.Delay(delay, ct);
}

public static class Ctx
{
    public static CollectorContext New(HardwareReport? r = null) => new(r ?? new HardwareReport(), new CollectorDiagnostics { Id = "test" });
}

/// <summary>Builds raw SMBIOS structure tables.</summary>
public static class SmbiosBuilder
{
    public static byte[] Table(params byte[][] structures) =>
        structures.SelectMany(x => x).Concat(Struct(127, new byte[0])).ToArray();

    /// <param name="body">Formatted area after the 4-byte header.</param>
    public static byte[] Struct(byte type, byte[] body, params string[] strings)
    {
        var bytes = new List<byte> { type, (byte)(4 + body.Length), 0x00, 0x10 };
        bytes.AddRange(body);
        if (strings.Length == 0) bytes.AddRange([0, 0]);
        else
        {
            foreach (var s in strings) { bytes.AddRange(Encoding.ASCII.GetBytes(s)); bytes.Add(0); }
            bytes.Add(0);
        }
        return bytes.ToArray();
    }

    public static byte[] Type0(string vendor, string version, string date, bool uefi = true) =>
        Struct(0, Body(0x18, (0x04, [1]), (0x05, [2]), (0x08, [3]), (0x13, [(byte)(uefi ? 0x08 : 0x00)])), vendor, version, date);

    public static byte[] Type2(string maker, string product, string version) =>
        Struct(2, Body(0x0F, (0x04, [1]), (0x05, [2]), (0x06, [3])), maker, product, version);

    public static byte[] Type1(string maker, string product) =>
        Struct(1, Body(0x1B, (0x04, [1]), (0x05, [2])), maker, product);

    public static byte[] Type16(int errorCorrection, long maxKb, int devices) =>
        Struct(16, Body(0x17, (0x04, [3]), (0x05, [3]), (0x06, [(byte)errorCorrection]),
            (0x07, BitConverter.GetBytes((uint)maxKb)), (0x0B, [0xFE, 0xFF]), (0x0D, BitConverter.GetBytes((ushort)devices))));

    /// <summary>SMBIOS 3.x Type 17 (length 0x5C). sizeMb = 0 → empty slot.</summary>
    public static byte[] Type17(string locator, string bank, int sizeMb, int formFactor = 0x09, int memType = 0x1A,
        int dataWidth = 64, int totalWidth = 64, int speed = 3200, int configured = 3200,
        string maker = "Samsung", string part = "M378A1K43EB2-CWE", int rank = 1, int mv = 1200)
    {
        bool empty = sizeMb == 0;
        ushort size = sizeMb >= 0x7FFF ? (ushort)0x7FFF : (ushort)sizeMb;
        return Struct(17, Body(0x5C,
                (0x08, BitConverter.GetBytes((ushort)(empty ? 0xFFFF : totalWidth))),
                (0x0A, BitConverter.GetBytes((ushort)(empty ? 0xFFFF : dataWidth))),
                (0x0C, BitConverter.GetBytes(size)),
                (0x0E, [(byte)formFactor]),
                (0x10, [1]), (0x11, [2]),
                (0x12, [(byte)(empty ? 0x02 : memType)]),
                (0x15, BitConverter.GetBytes((ushort)(empty ? 0 : speed))),
                (0x17, [3]), (0x18, [0]), (0x1A, [4]),
                (0x1B, [(byte)(empty ? 0 : rank)]),
                (0x1C, BitConverter.GetBytes((uint)(sizeMb >= 0x7FFF ? sizeMb : 0))),
                (0x20, BitConverter.GetBytes((ushort)(empty ? 0 : configured))),
                (0x22, BitConverter.GetBytes((ushort)(empty ? 0 : mv))),
                (0x24, BitConverter.GetBytes((ushort)(empty ? 0 : mv))),
                (0x26, BitConverter.GetBytes((ushort)(empty ? 0 : mv)))),
            locator, bank, empty ? "Unknown" : maker, empty ? "Unknown" : part);
    }

    /// <summary>Formatted body of total structure length <paramref name="length"/>; offsets are relative to the structure start.</summary>
    private static byte[] Body(int length, params (int Offset, byte[] Data)[] fields)
    {
        var b = new byte[length - 4];
        foreach (var (o, d) in fields) d.CopyTo(b, o - 4);
        return b;
    }
}

public static class EdidBuilder
{
    /// <summary>Dell-like EDID: "DEL", product 0xA0F2, 2021, 2560×1440, 597×336 mm, name "DELL U2723QE".</summary>
    public static byte[] Dell()
    {
        var e = new byte[128];
        new byte[] { 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 }.CopyTo(e, 0);
        e[8] = 0x10; e[9] = 0xAC;            // DEL
        e[10] = 0xF2; e[11] = 0xA0;          // product 0xA0F2
        e[12] = 0x12; e[13] = 0x34;          // serial (must never be exported)
        e[17] = 31;                          // 2021
        e[21] = 60; e[22] = 34;
        e[54] = 0x56; e[55] = 0x5E;          // pixel clock
        e[56] = 0x00; e[58] = 0xA0;          // h active 2560
        e[59] = 0xA0; e[61] = 0x50;          // v active 1440
        e[66] = 0x55; e[67] = 0x50; e[68] = 0x21; // 597 × 336 mm
        e[72] = 0; e[73] = 0; e[74] = 0; e[75] = 0xFC; e[76] = 0;
        var name = Encoding.ASCII.GetBytes("DELL U2723QE\n");
        name.CopyTo(e, 77);
        return e;
    }
}
