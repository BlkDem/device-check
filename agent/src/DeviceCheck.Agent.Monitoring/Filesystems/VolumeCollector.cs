using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Core.Normalization;
using DeviceCheck.Agent.Core.Sources;
using DeviceCheck.Agent.Monitoring.Core;
using DeviceCheck.Agent.Monitoring.Storage;

namespace DeviceCheck.Agent.Monitoring.Filesystems;

/// <summary>
/// Local volumes and free space: MSFT_Volume (label, file system, health), MSFT_Partition (volume → disk mapping),
/// System.IO.DriveInfo for drives the Storage API does not list (virtual file systems, subst) and as a fallback.
/// Read-only: nothing is cleaned, fixed or written.
/// </summary>
public sealed class VolumeCollector(IWmiClient wmi, IPlatformSource platform) : IStorageHealthCollector
{
    public string Id => "volumes";
    public bool FullOnly => false;
    private const string VolSrc = "Storage API MSFT_Volume";
    private const string DriveInfoSrc = "System.IO.DriveInfo";

    public async Task CollectAsync(StorageHealthContext ctx, CancellationToken ct)
    {
        IReadOnlyList<WmiObject> Empty() => [];
        var vols = await ctx.TryAsync(VolSrc, () => wmi.QueryAsync(DiskInventory.StorageNs,
            "SELECT Path, DriveLetter, FileSystemLabel, FileSystem, DriveType, HealthStatus, Size, SizeRemaining FROM MSFT_Volume", ct), Empty());
        var parts = await ctx.TryAsync("Storage API MSFT_Partition", () => wmi.QueryAsync(DiskInventory.StorageNs,
            "SELECT DiskNumber, DriveLetter, AccessPaths FROM MSFT_Partition", ct), Empty());
        var drives = ctx.Try(DriveInfoSrc, platform.GetVolumes, (IReadOnlyList<VolumeData>)[]);

        var volumes = new List<VolumeHealth>();
        foreach (var v in vols)
        {
            if (v.Long("Size") is not > 0) continue;
            volumes.Add(FromStorageApi(v, DisksOf(v, parts)));
        }
        // Drive letters the Storage API does not know about (or all of them, if it failed).
        var known = volumes.Select(v => Letter(v.Letter.Value)).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var d in drives)
        {
            var letter = d.Name.TrimEnd('\\', ':');
            if (known.Contains(letter)) continue;
            volumes.Add(FromDriveInfo(d, letter, LetterDisks(letter, parts)));
        }

        Attach(ctx, volumes);
    }

    /// <summary>Single-disk volumes go to their disk; spanned and unassociated ones to <see cref="StorageHealthSection.OtherVolumes"/>.</summary>
    internal static void Attach(StorageHealthContext ctx, List<VolumeHealth> volumes)
    {
        var byNumber = ctx.Disks.ToDictionary(d => d.Number);
        foreach (var d in ctx.Disks) d.Device.Volumes = [];
        ctx.Report.StorageHealth.OtherVolumes = [];
        foreach (var v in volumes.OrderBy(v => v.Letter.Value ?? "~", StringComparer.Ordinal)) // lettered first, then unlettered
        {
            if (v.DiskNumbers.Value is [var n] && byNumber.TryGetValue(n, out var disk)) disk.Device.Volumes.Add(v);
            else ctx.Report.StorageHealth.OtherVolumes.Add(v);
        }
    }

    internal static VolumeHealth FromStorageApi(WmiObject v, List<int> disks)
    {
        var letter = Letter(v.Str("DriveLetter"));
        long? total = v.Long("Size"), free = v.Long("SizeRemaining");
        var vol = new VolumeHealth
        {
            Id = DiskInventory.StableId("vol", v.Str("Path") ?? letter ?? Guid.NewGuid().ToString()),
            Letter = F.Exact(letter is null ? null : letter + ":", VolSrc + ".DriveLetter"),
            Label = F.Exact(v.Str("FileSystemLabel"), VolSrc + ".FileSystemLabel"),
            Filesystem = F.Exact(v.Str("FileSystem"), VolSrc + ".FileSystem"),
            DriveType = F.Exact(StorageEnums.DriveType(v.Int("DriveType")), VolSrc + ".DriveType"),
            HealthStatus = F.Exact(StorageEnums.Health(v.Int("HealthStatus")), VolSrc + ".HealthStatus"),
            DiskNumbers = disks.Count > 0
                ? F.Exact<List<int>?>(disks, "Storage API MSFT_Partition.AccessPaths")
                : F.Unavailable<List<int>?>("Storage API MSFT_Partition", "no_partition_mapping"),
        };
        Space(vol, total, free, VolSrc);
        return vol;
    }

    internal static VolumeHealth FromDriveInfo(VolumeData d, string letter, List<int> disks)
    {
        var vol = new VolumeHealth
        {
            Id = DiskInventory.StableId("vol", letter),
            Letter = F.Exact<string?>(letter + ":", DriveInfoSrc),
            Label = F.Unavailable<string?>(DriveInfoSrc, F.Reasons.NotCollected),
            Filesystem = F.Exact(d.Format, DriveInfoSrc),
            DriveType = F.Exact<string?>(d.DriveType, DriveInfoSrc),
            HealthStatus = F.Unavailable<string?>(VolSrc, "not_listed_by_storage_api"),
            DiskNumbers = disks.Count > 0
                ? F.Exact<List<int>?>(disks, "Storage API MSFT_Partition.DriveLetter")
                : F.Unavailable<List<int>?>("Storage API MSFT_Partition", "no_partition_mapping"),
        };
        Space(vol, d.TotalBytes, d.FreeBytes, DriveInfoSrc);
        return vol;
    }

    /// <summary>Used = total − free; percentages rounded to 0.1. All derived from exact inputs.</summary>
    internal static void Space(VolumeHealth vol, long? total, long? free, string src)
    {
        vol.TotalBytes = F.Exact(total, src);
        vol.FreeBytes = F.Exact(free, src);
        if (total is > 0 && free is >= 0 && free <= total)
        {
            vol.UsedBytes = F.Exact<long?>(total - free, src + " (size − free)", "derived");
            vol.FreePercent = F.Exact<double?>(Math.Round(free.Value * 100.0 / total.Value, 1), src + " (free / size)", "derived");
            vol.UsedPercent = F.Exact<double?>(Math.Round(100 - free.Value * 100.0 / total.Value, 1), src + " (used / size)", "derived");
        }
        else
        {
            vol.UsedBytes = F.Unavailable<long?>(src, F.Reasons.NotReported);
            vol.FreePercent = F.Unavailable<double?>(src, F.Reasons.NotReported);
            vol.UsedPercent = F.Unavailable<double?>(src, F.Reasons.NotReported);
        }
    }

    private static List<int> DisksOf(WmiObject volume, IReadOnlyList<WmiObject> parts)
    {
        var path = volume.Str("Path");
        var letter = Letter(volume.Str("DriveLetter"));
        return parts.Where(p => (path is not null && Paths(p).Contains(path, StringComparer.OrdinalIgnoreCase))
                                || (letter is not null && Letter(p.Str("DriveLetter")) == letter))
            .Select(p => p.Int("DiskNumber")).OfType<int>().Distinct().Order().ToList();
    }

    private static List<int> LetterDisks(string letter, IReadOnlyList<WmiObject> parts) =>
        parts.Where(p => string.Equals(Letter(p.Str("DriveLetter")), letter, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Int("DiskNumber")).OfType<int>().Distinct().ToList();

    private static IEnumerable<string> Paths(WmiObject p) =>
        p.TryGetValue("AccessPaths", out var v) && v is string[] a ? a : [];

    /// <summary>MSFT_* DriveLetter is a char; "\0" means none.</summary>
    internal static string? Letter(string? s)
    {
        var t = s?.Trim('\0', ' ', ':', '\\');
        return string.IsNullOrEmpty(t) ? null : t.ToUpperInvariant();
    }
}
