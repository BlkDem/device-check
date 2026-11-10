namespace DeviceCheck.Agent.Core.Sources;

// Abstractions over platform data sources. Collectors depend only on these interfaces,
// so they can be unit-tested with fixtures and ported to other OSes by adding implementations.

/// <summary>A WMI/CIM row: case-insensitive property bag.</summary>
public sealed class WmiObject : Dictionary<string, object?>
{
    public WmiObject() : base(StringComparer.OrdinalIgnoreCase) { }

    public string? Str(string name)
    {
        if (!TryGetValue(name, out var v) || v is null) return null;
        var s = Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    public long? Long(string name)
    {
        if (!TryGetValue(name, out var v) || v is null) return null;
        try { return Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    public int? Int(string name)
    {
        var l = Long(name);
        return l is >= int.MinValue and <= int.MaxValue ? (int)l : null;
    }

    public bool? Bool(string name) => TryGetValue(name, out var v) && v is bool b ? b : null;

    public IReadOnlyList<long> Longs(string name)
    {
        if (!TryGetValue(name, out var v) || v is not Array arr) return [];
        var r = new List<long>();
        foreach (var x in arr) { try { r.Add(Convert.ToInt64(x)); } catch { } }
        return r;
    }
}

public sealed class WmiException(string query, string message, Exception? inner = null)
    : Exception($"WMI query failed ({query}): {message}", inner)
{
    public string Query { get; } = query;
}

public interface IWmiClient
{
    /// <summary>Runs a WQL query. Throws <see cref="WmiException"/> or <see cref="TimeoutException"/> on failure.</summary>
    Task<IReadOnlyList<WmiObject>> QueryAsync(string wmiNamespace, string wql, CancellationToken ct);
}

public sealed record SmbiosTable(byte MajorVersion, byte MinorVersion, byte[] Data);

public interface ISmbiosSource
{
    /// <summary>Raw SMBIOS structure table, or null if firmware does not expose one.</summary>
    SmbiosTable? Read();
}

public sealed record CacheInfo(int Level, string Type, long SizeBytes);

public sealed record ProcessorTopology(
    int Packages,
    int Cores,
    int LogicalProcessors,
    IReadOnlyList<int> CoreEfficiencyClasses,
    IReadOnlyList<CacheInfo> Caches);

public sealed record CpuidInfo(
    string Vendor,
    string? Brand,
    int Family,
    int Model,
    int Stepping,
    IReadOnlyList<string> Features,
    bool HypervisorPresent,
    bool HybridFlag);

public interface IProcessorSource
{
    ProcessorTopology? GetTopology();
    /// <summary>CPUID (x86/x64 only), null on other architectures.</summary>
    CpuidInfo? GetCpuid();
}

public sealed record GraphicsAdapterInfo(
    string Description,
    uint VendorId,
    uint DeviceId,
    uint SubsystemId,
    uint Revision,
    ulong DedicatedVideoMemory,
    ulong DedicatedSystemMemory,
    ulong SharedSystemMemory,
    bool IsSoftware,
    bool IsRemote,
    /// <summary>D3D12 FEATURE_ARCHITECTURE.UMA; null if a D3D12 device could not be created.</summary>
    bool? Uma,
    /// <summary>Highest D3D feature level, e.g. "12_1"; null if unknown.</summary>
    string? MaxFeatureLevel);

public interface IGraphicsSource
{
    IReadOnlyList<GraphicsAdapterInfo> GetAdapters();
}

public sealed record DisplayPathInfo(
    int Width,
    int Height,
    uint RefreshNumerator,
    uint RefreshDenominator,
    int OutputTechnology,
    string? FriendlyName,
    ushort EdidManufacturerId,
    ushort EdidProductCode,
    bool? AdvancedColorSupported,
    bool? AdvancedColorEnabled,
    bool IsPrimary,
    byte[]? Edid);

public interface IDisplaySource
{
    IReadOnlyList<DisplayPathInfo> GetActiveDisplays();
}

public sealed record VolumeData(string Name, string? Format, string DriveType, long TotalBytes, long FreeBytes);

public sealed record MemoryStatus(long? InstalledBytes, long? UsableBytes);

/// <summary>Miscellaneous OS facts that need direct API / registry access.</summary>
public interface IPlatformSource
{
    /// <summary>"uefi" | "bios" | null.</summary>
    string? FirmwareType();
    long UptimeMilliseconds();
    string OsArchitecture();
    /// <summary>Value from HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion.</summary>
    object? CurrentVersionValue(string name);
    MemoryStatus GetMemoryStatus();
    IReadOnlyList<VolumeData> GetVolumes();
    bool IsElevated();
}
