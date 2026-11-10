using System.Text.RegularExpressions;
using DeviceCheck.Agent.Core.Collection;
using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Core.Parsing;
using DeviceCheck.Agent.Core.Sources;

namespace DeviceCheck.Agent.Collectors;

public sealed partial class MotherboardCollector(IWmiClient wmi, ISmbiosSource smbios, IPlatformSource platform) : IHardwareCollector
{
    public string Id => "motherboard";

    public async Task CollectAsync(CollectorContext ctx, CancellationToken ct)
    {
        var m = ctx.Report.Motherboard;
        var table = ctx.Try("SMBIOS (GetSystemFirmwareTable RSMB)", smbios.Read, null);
        var structs = table is null ? [] : SmbiosParser.Parse(table.Data);
        var bios = SmbiosReader.Bios(structs);
        var sys = SmbiosReader.System(structs);
        var board = SmbiosReader.Baseboard(structs);

        IReadOnlyList<WmiObject> Empty() => [];
        var wBoard = (await ctx.TryAsync("WMI Win32_BaseBoard", () => wmi.QueryAsync(Ns.Cimv2, "SELECT Manufacturer, Product, Version FROM Win32_BaseBoard", ct), Empty())).FirstOrDefault();
        var wBios = (await ctx.TryAsync("WMI Win32_BIOS", () => wmi.QueryAsync(Ns.Cimv2, "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS", ct), Empty())).FirstOrDefault();
        var wSys = (await ctx.TryAsync("WMI Win32_ComputerSystem", () => wmi.QueryAsync(Ns.Cimv2, "SELECT Manufacturer, Model, SystemFamily FROM Win32_ComputerSystem", ct), Empty())).FirstOrDefault();

        m.Manufacturer = Fw.Str(board?.Manufacturer, "SMBIOS Type 2", wBoard?.Str("Manufacturer"), "WMI Win32_BaseBoard");
        m.Model = Fw.Str(board?.Product, "SMBIOS Type 2", wBoard?.Str("Product"), "WMI Win32_BaseBoard");
        m.Version = Fw.Str(board?.Version, "SMBIOS Type 2", wBoard?.Str("Version"), "WMI Win32_BaseBoard");
        m.BiosVendor = Fw.Str(bios?.Vendor, "SMBIOS Type 0", wBios?.Str("Manufacturer"), "WMI Win32_BIOS");
        m.BiosVersion = Fw.Str(bios?.Version, "SMBIOS Type 0", wBios?.Str("SMBIOSBIOSVersion"), "WMI Win32_BIOS");
        var biosDate = CimDate.BiosDateToIso(bios?.ReleaseDate);
        m.BiosDate = biosDate is not null
            ? F.Exact<string?>(biosDate, "SMBIOS Type 0")
            : F.Exact(CimDate.Parse(wBios?.Str("ReleaseDate"))?.ToString("yyyy-MM-dd"), "WMI Win32_BIOS");
        m.SmbiosVersion = table is null
            ? F.Unavailable<string?>("SMBIOS", F.Reasons.NotReported)
            : F.Exact<string?>($"{table.MajorVersion}.{table.MinorVersion}", "SMBIOS entry point");
        m.SystemManufacturer = Fw.Str(sys?.Manufacturer, "SMBIOS Type 1", wSys?.Str("Manufacturer"), "WMI Win32_ComputerSystem");
        m.SystemModel = Fw.Str(sys?.Product, "SMBIOS Type 1", wSys?.Str("Model"), "WMI Win32_ComputerSystem");
        m.SystemFamily = Fw.Str(sys?.Family, "SMBIOS Type 1", wSys?.Str("SystemFamily"), "WMI Win32_ComputerSystem");

        var fwType = ctx.Try("GetFirmwareType", platform.FirmwareType, null);
        m.FirmwareType = fwType is not null
            ? F.Exact<string?>(fwType, "GetFirmwareType")
            : bios?.UefiSupported == true
                ? F.Detected<string?>("uefi", "SMBIOS Type 0 characteristics", "uefi_supported_not_necessarily_active")
                : F.Unavailable<string?>("GetFirmwareType", F.Reasons.NotReported);

        // Chipset: only when a PCI device name explicitly names it (Intel PCH LPC/eSPI controller). No guessing.
        var pci = await ctx.TryAsync("WMI Win32_PnPEntity (PCI)", () => wmi.QueryAsync(Ns.Cimv2,
            "SELECT Name FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'PCI\\\\%'", ct), Empty());
        var chipset = pci.Select(p => ChipsetFromDeviceName(p.Str("Name"))).FirstOrDefault(x => x is not null);
        m.Chipset = chipset is not null
            ? F.Detected<string?>(chipset, "PCI device name (Win32_PnPEntity)", "from_pci_device_name")
            : F.Unavailable<string?>("PCI device names", F.Reasons.NoReliableSource);
        if (chipset is null) ctx.Limit("motherboard", "chipset_not_identified");

        if (structs.Count == 0) ctx.Warn("smbios_unavailable_used_wmi");
    }

    internal static string? ChipsetFromDeviceName(string? name)
    {
        if (name is null) return null;
        var m = ChipsetRx().Match(name);
        if (!m.Success) return null;
        var series = m.Groups["model"].Success ? m.Groups["model"].Value : m.Groups["series"].Value + " Series";
        return $"{m.Groups["vendor"].Value} {series}".Trim();
    }

    // "Intel(R) B660 Chipset LPC/eSPI Controller - 7A86", "Intel(R) 300 Series Chipset Family LPC Controller (H370) - A304"
    // The LPC/eSPI check is a lookahead so that it cannot steal the "(H370)" part while backtracking.
    [GeneratedRegex(@"^(?=.*\b(?:LPC|eSPI)\b)(?<vendor>Intel|AMD)(?:\(R\))?\s+(?:(?<model>[A-Z]\d{3}[A-Z]?|[A-Z]{1,2}\d{2,3})\s+Chipset|(?<series>\d{3})\s+Series\s+Chipset[^()]*(?:\((?<model>[A-Z]\d{3}[A-Z]?)\))?)", RegexOptions.IgnoreCase)]
    private static partial Regex ChipsetRx();
}
