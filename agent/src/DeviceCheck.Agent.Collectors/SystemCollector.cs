using System.Globalization;
using DeviceCheck.Agent.Core.Collection;
using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Core.Parsing;
using DeviceCheck.Agent.Core.Sources;

namespace DeviceCheck.Agent.Collectors;

public sealed class SystemCollector(IWmiClient wmi, IPlatformSource platform) : IHardwareCollector
{
    public string Id => "system";

    public async Task CollectAsync(CollectorContext ctx, CancellationToken ct)
    {
        const string osSrc = "WMI Win32_OperatingSystem";
        const string regSrc = @"Registry HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        var s = ctx.Report.System;

        var os = (await ctx.TryAsync(osSrc,
            () => wmi.QueryAsync(Ns.Cimv2, "SELECT Caption, Version, BuildNumber, InstallDate, LastBootUpTime FROM Win32_OperatingSystem", ct),
            (IReadOnlyList<WmiObject>)[])).FirstOrDefault();
        var cs = (await ctx.TryAsync("WMI Win32_ComputerSystem",
            () => wmi.QueryAsync(Ns.Cimv2, "SELECT PCSystemType FROM Win32_ComputerSystem", ct),
            (IReadOnlyList<WmiObject>)[])).FirstOrDefault();

        var reg = (string name) => ctx.Try(regSrc, () => platform.CurrentVersionValue(name), null);

        int? build = int.TryParse(os?.Str("BuildNumber") ?? reg("CurrentBuildNumber") as string, out var b) ? b : null;
        var ubr = reg("UBR") is int u ? u : (int?)null;

        s.OsName = F.Exact(CleanCaption(os?.Str("Caption")), osSrc);
        s.OsVersion = F.Exact((reg("DisplayVersion") ?? reg("ReleaseId")) as string, regSrc + @"\DisplayVersion");
        s.OsBuild = build is null
            ? F.Unavailable<string?>(osSrc, F.Reasons.NotReported)
            : F.Exact<string?>(ubr is null ? $"{build}" : $"{build}.{ubr}", ubr is null ? osSrc : "Win32_OperatingSystem.BuildNumber + registry UBR");
        s.OsEdition = F.Exact(reg("EditionID") as string, regSrc + @"\EditionID");
        s.Architecture = F.Exact<string?>(ctx.Try("RuntimeInformation.OSArchitecture", platform.OsArchitecture, null), "RuntimeInformation.OSArchitecture");
        s.UptimeSeconds = F.Exact<long?>(ctx.Try("GetTickCount64", platform.UptimeMilliseconds, -1) is var ms and >= 0 ? ms / 1000 : null,
            "GetTickCount64", "fast_startup_may_extend_uptime");
        s.BootTime = F.Exact(CimDate.Parse(os?.Str("LastBootUpTime")), osSrc + ".LastBootUpTime");
        s.InstallDate = F.Exact(CimDate.Parse(os?.Str("InstallDate")), osSrc + ".InstallDate", "resets_on_feature_update");
        s.Hostname = F.Private<string?>();
        s.DeviceType = F.Exact(DeviceType(cs?.Int("PCSystemType")), "Win32_ComputerSystem.PCSystemType");
        s.Language = F.Exact<string?>(CultureInfo.CurrentUICulture.Name, "CultureInfo.CurrentUICulture");
        s.Timezone = F.Exact<string?>(TimeZoneInfo.Local.Id, "TimeZoneInfo.Local");

        ctx.Report.Scan.Platform = "Windows";
        ctx.Report.Scan.PlatformVersion = build switch
        {
            >= 22000 => "Windows 11",
            >= 10240 => "Windows 10",
            null => null,
            _ => "Windows",
        };
        ctx.Report.Diagnostics.Elevated = ctx.Try("IsElevated", platform.IsElevated, false);
    }

    internal static string? CleanCaption(string? caption)
    {
        if (caption is null) return null;
        foreach (var p in new[] { "Microsoft ", "Майкрософт " })
            if (caption.StartsWith(p, StringComparison.OrdinalIgnoreCase)) caption = caption[p.Length..];
        return caption.Trim();
    }

    internal static string? DeviceType(int? pcSystemType) => pcSystemType switch
    {
        1 => "desktop",
        2 => "laptop",
        3 => "workstation",
        4 or 5 or 7 => "server",
        6 => "appliance",
        8 => "tablet",
        _ => null,
    };
}
