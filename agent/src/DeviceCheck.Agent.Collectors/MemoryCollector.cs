using DeviceCheck.Agent.Core.Collection;
using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Core.Normalization;
using DeviceCheck.Agent.Core.Parsing;
using DeviceCheck.Agent.Core.Sources;

namespace DeviceCheck.Agent.Collectors;

/// <summary>
/// RAM. Primary source: raw SMBIOS Type 16/17 (lists empty slots too); fallback: WMI Win32_PhysicalMemory(+Array).
/// Rules: speeds are data rates in MT/s (clock MHz is derived = MT/s / 2); soldered memory never counts as a slot;
/// channel mode only from explicit channel labels, otherwise unknown.
/// </summary>
public sealed class MemoryCollector(IWmiClient wmi, ISmbiosSource smbios, IPlatformSource platform) : IHardwareCollector
{
    public string Id => "memory";

    public async Task CollectAsync(CollectorContext ctx, CancellationToken ct)
    {
        var mem = ctx.Report.Memory;

        var status = ctx.Try("GetPhysicallyInstalledSystemMemory / GlobalMemoryStatusEx", platform.GetMemoryStatus, new MemoryStatus(null, null));
        mem.TotalBytes = F.Exact(status.InstalledBytes, "GetPhysicallyInstalledSystemMemory");
        mem.UsableBytes = F.Exact(status.UsableBytes, "GlobalMemoryStatusEx");

        var table = ctx.Try("SMBIOS (GetSystemFirmwareTable RSMB)", smbios.Read, null);
        var structs = table is null ? [] : SmbiosParser.Parse(table.Data);
        IReadOnlyList<SmbiosMemoryDevice> devices = SmbiosReader.MemoryDevices(structs);
        IReadOnlyList<SmbiosMemoryArray> arrays = SmbiosReader.MemoryArrays(structs);
        string src = "SMBIOS Type 17";
        bool listsEmptySlots = true;

        if (devices.Count == 0)
        {
            ctx.Warn("smbios_type17_unavailable_used_wmi");
            src = "WMI Win32_PhysicalMemory";
            listsEmptySlots = false;
            var rows = await ctx.TryAsync(src, () => wmi.QueryAsync(Ns.Cimv2,
                "SELECT BankLabel, DeviceLocator, Manufacturer, PartNumber, Capacity, MemoryType, SMBIOSMemoryType, Speed, ConfiguredClockSpeed, DataWidth, TotalWidth, FormFactor, MinVoltage, MaxVoltage, ConfiguredVoltage, Attributes FROM Win32_PhysicalMemory", ct),
                (IReadOnlyList<WmiObject>)[]);
            devices = rows.Select(FromWmi).ToList();
            var arr = await ctx.TryAsync("WMI Win32_PhysicalMemoryArray", () => wmi.QueryAsync(Ns.Cimv2,
                "SELECT MemoryDevices, MemoryErrorCorrection, MaxCapacityEx, Use FROM Win32_PhysicalMemoryArray", ct),
                (IReadOnlyList<WmiObject>)[]);
            arrays = arr.Select(a => new SmbiosMemoryArray(0, a.Int("Use") ?? 3, a.Int("MemoryErrorCorrection") ?? 2,
                a.Long("MaxCapacityEx") is > 0 and var kb ? kb * 1024 : null, a.Int("MemoryDevices") ?? 0)).ToList();
        }

        Build(ctx, mem, devices, arrays, src, listsEmptySlots);
    }

    internal static void Build(CollectorContext ctx, MemorySection mem, IReadOnlyList<SmbiosMemoryDevice> devices,
        IReadOnlyList<SmbiosMemoryArray> arrays, string src, bool listsEmptySlots)
    {
        var entries = devices.Select(d =>
        {
            var type = MemoryTypes.FromSmbios(d.MemoryType);
            bool soldered = MemoryTypes.IsSolderedFormFactor(d.FormFactor)
                            || (MemoryTypes.IsLowPower(type) && d.FormFactor is not (0x09 or 0x0D or 0x11));
            return (Dev: d, Type: type, Soldered: soldered, Populated: d.SizeBytes is > 0,
                Channel: MemoryTopology.Channel(d.DeviceLocator, d.BankLocator));
        }).ToList();
        var populated = entries.Where(e => e.Populated).ToList();

        mem.SmbiosSlots = entries.Select(e => new SmbiosMemorySlot
        {
            Locator = e.Dev.DeviceLocator,
            BankLabel = e.Dev.BankLocator,
            Populated = e.Populated,
            SizeBytes = e.Dev.SizeBytes,
            FormFactor = MemoryTypes.FormFactor(e.Dev.FormFactor),
            Soldered = e.Soldered,
            Channel = e.Channel,
        }).ToList();

        mem.Modules = populated.Select(e => Module(e.Dev, e.Type, e.Soldered, src)).ToList();
        mem.ModulesDetected = F.Exact<int?>(populated.Count, src);
        mem.SolderedBytes = F.Exact<long?>(populated.Where(e => e.Soldered).Sum(e => e.Dev.SizeBytes ?? 0), src);

        // Fallback total when GetPhysicallyInstalledSystemMemory failed.
        if (!mem.TotalBytes.HasValue && populated.Count > 0)
            mem.TotalBytes = F.Exact<long?>(populated.Sum(e => e.Dev.SizeBytes ?? 0), src + " (sum of modules)");

        // Slots: socketed devices only. SMBIOS may list slots that do not physically exist.
        var socketed = entries.Where(e => !e.Soldered).ToList();
        if (listsEmptySlots)
        {
            int total = socketed.Count;
            int used = socketed.Count(e => e.Populated);
            string note = total == 0 && populated.Count > 0 ? "all_memory_soldered" : "smbios_slots_unverified";
            mem.SlotsTotal = F.Detected<int?>(total, src, note);
            mem.SlotsUsed = F.Detected<int?>(used, src, note);
            mem.SlotsFree = F.Detected<int?>(total - used, src + " (derived)", note);
            ctx.Limit("memory", "smbios_slot_count_unverified");
        }
        else
        {
            int? arrayDevices = arrays.Sum(a => a.DeviceCount) is > 0 and var n ? n : null;
            int used = socketed.Count(e => e.Populated);
            mem.SlotsUsed = F.Detected<int?>(used, src, "smbios_slots_unverified");
            mem.SlotsTotal = arrayDevices is not null
                ? F.Detected<int?>(arrayDevices, "WMI Win32_PhysicalMemoryArray.MemoryDevices", "smbios_slots_unverified")
                : F.Unavailable<int?>("WMI Win32_PhysicalMemoryArray", F.Reasons.NotReported);
            mem.SlotsFree = arrayDevices is not null && populated.All(e => !e.Soldered)
                ? F.Detected<int?>(Math.Max(0, arrayDevices.Value - used), "derived", "smbios_slots_unverified")
                : F.Unavailable<int?>("derived", F.Reasons.NotReported);
        }

        var arrayDeviceCount = arrays.Sum(a => a.DeviceCount);
        if (listsEmptySlots && arrayDeviceCount > 0 && arrayDeviceCount != entries.Count)
            ctx.Warn("smbios_type16_type17_count_mismatch");

        // Type and speed (configured = current). Mixed values are reported, not averaged.
        var types = populated.Select(e => e.Type).Where(t => t is not null).Distinct().ToList();
        mem.MemoryType = types.Count switch
        {
            0 => F.Unavailable<string?>(src, F.Reasons.NotReported),
            1 => F.Exact(types[0], src),
            _ => F.Detected<string?>(string.Join(" / ", types), src, "mixed_types"),
        };
        var speeds = populated.Select(e => e.Dev.ConfiguredSpeedMts).Where(s => s is > 0).Select(s => s!.Value).Distinct().ToList();
        if (speeds.Count > 0)
        {
            int speed = speeds.Min();
            mem.SpeedMts = speeds.Count == 1 ? F.Exact<int?>(speed, src + " Configured Memory Speed") : F.Detected<int?>(speed, src, "mixed_speeds_min");
            mem.ClockMhz = F.Detected<int?>(speed / 2, "derived from speed_mts", "ddr_clock_is_half_data_rate");
        }
        else
        {
            mem.SpeedMts = F.Unavailable<int?>(src, F.Reasons.NotReported);
            mem.ClockMhz = F.Unavailable<int?>(src, F.Reasons.NotReported);
        }
        mem.MaxSpeedMts = F.Unavailable<int?>("—", F.Reasons.NoReliableSource);

        // Channels
        var ch = MemoryTopology.Detect(populated.Select(e => (e.Channel, e.Dev.DataWidth)).ToList());
        mem.Channels = ch.Mode == "unknown"
            ? F.Unavailable<string?>(src + " locators", "channel_mode_undetermined")
            : F.Detected<string?>(ch.Mode, src + " locators", "from_channel_labels");
        mem.BusWidthBits = ch.Mode != "unknown" && ch.BusWidthBits is not null
            ? F.Detected<int?>(ch.BusWidthBits, src + " data width per channel", "derived")
            : F.Unavailable<int?>(src, "channel_mode_undetermined");
        if (ch.Mode == "unknown") ctx.Limit("memory", "channel_mode_undetermined");

        // ECC: firmware array declaration first, else data width vs total width.
        int ec = arrays.Select(a => a.ErrorCorrection).FirstOrDefault(x => x >= 3);
        if (ec >= 3)
            mem.Ecc = F.Exact<bool?>(ec != 3, "SMBIOS Type 16 Memory Error Correction");
        else if (populated.Count > 0 && populated.All(e => e.Dev.DataWidth > 0 && e.Dev.TotalWidth > 0))
            mem.Ecc = F.Detected<bool?>(populated.Any(e => e.Dev.TotalWidth > e.Dev.DataWidth), src + " total vs data width", "derived");
        else
            mem.Ecc = F.Unavailable<bool?>(src, F.Reasons.NotReported);

        long? maxCap = arrays.All(a => a.MaxCapacityBytes is not null) && arrays.Count > 0 ? arrays.Sum(a => a.MaxCapacityBytes!.Value) : null;
        mem.MaxCapacityBytes = F.Exact(maxCap, "SMBIOS Type 16 Maximum Capacity", "firmware_reported");
    }

    private static MemoryModule Module(SmbiosMemoryDevice d, string? type, bool soldered, string src)
    {
        var (maker, fromCode) = Vendors.MemoryManufacturer(d.Manufacturer);
        return new MemoryModule
        {
            Slot = F.Exact(FirmwareText.Clean(d.DeviceLocator), src + " Device Locator"),
            BankLabel = F.Exact(FirmwareText.Clean(d.BankLocator), src + " Bank Locator"),
            Soldered = F.Detected<bool?>(soldered, src + " Form Factor / Memory Type", "from_form_factor"),
            Manufacturer = ModuleManufacturer(d, maker, fromCode, src),
            Model = F.Unavailable<string?>(src, F.Reasons.NotReported),
            Serial = F.Private<string?>(),
            SizeBytes = F.Exact(d.SizeBytes, src + " Size"),
            MemoryType = F.Exact(type, src + " Memory Type"),
            SmbiosMemoryType = F.Exact<int?>(d.MemoryType > 0 ? d.MemoryType : null, src + " Memory Type"),
            SpeedMts = F.Exact(d.ConfiguredSpeedMts, src + " Configured Memory Speed"),
            ClockMhz = d.ConfiguredSpeedMts is > 0
                ? F.Detected<int?>(d.ConfiguredSpeedMts / 2, "derived from speed_mts", "ddr_clock_is_half_data_rate")
                : F.Unavailable<int?>(src, F.Reasons.NotReported),
            RatedSpeedMts = F.Exact(d.SpeedMts, src + " Speed (maximum capable)"),
            DataWidthBits = F.Exact(d.DataWidth, src + " Data Width"),
            TotalWidthBits = F.Exact(d.TotalWidth, src + " Total Width"),
            Rank = F.Exact(d.Rank, src + " Attributes"),
            FormFactor = F.Exact(MemoryTypes.FormFactor(d.FormFactor), src + " Form Factor"),
            PartNumber = F.Exact(FirmwareText.Clean(d.PartNumber), src + " Part Number"),
            VoltageMv = F.Exact(d.ConfiguredVoltageMv, src + " Configured Voltage"),
            MinVoltageMv = F.Exact(d.MinVoltageMv, src + " Minimum Voltage"),
            MaxVoltageMv = F.Exact(d.MaxVoltageMv, src + " Maximum Voltage"),
        };
    }

    private static Field<string?> ModuleManufacturer(SmbiosMemoryDevice d, string? maker, bool fromCode, string src)
    {
        if (maker is not null)
            return fromCode ? F.Detected<string?>(maker, src + " Manufacturer", "jedec_id_lookup") : F.Exact<string?>(maker, src + " Manufacturer");
        var byPart = Vendors.MemoryManufacturerFromPartNumber(d.PartNumber);
        if (byPart is not null) return F.Detected<string?>(byPart, src + " Part Number prefix", "from_part_number");
        return F.Unavailable<string?>(src + " Manufacturer",
            FirmwareText.IsPlaceholder(d.Manufacturer) ? F.Reasons.FirmwarePlaceholder : F.Reasons.NotReported);
    }

    /// <summary>Win32_PhysicalMemory row → SMBIOS-like record (WMI FormFactor enum is different from SMBIOS).</summary>
    internal static SmbiosMemoryDevice FromWmi(WmiObject r)
    {
        int ff = r.Int("FormFactor") switch { 7 => 0x03, 8 => 0x09, 12 => 0x0D, 11 => 0x0B, 0 or null => 0x02, var x => x == 9 ? 0x05 : 0x01 };
        int smbiosType = r.Int("SMBIOSMemoryType") ?? 0;
        if (MemoryTypes.FromSmbios(smbiosType) is null)
        {
            // Older systems: legacy CIM enum only
            var legacy = MemoryTypes.FromWmiLegacy(r.Int("MemoryType") ?? 0);
            smbiosType = legacy switch { "DDR" => 0x12, "DDR2" => 0x13, "DDR3" => 0x18, "DDR4" => 0x1A, _ => smbiosType };
        }
        int? Positive(int? v) => v is > 0 ? v : null;
        int? rank = r.Int("Attributes") is > 0 and var a ? a & 0x0F : null;
        return new SmbiosMemoryDevice(r.Str("DeviceLocator"), r.Str("BankLabel"), r.Long("Capacity"), ff, smbiosType,
            Positive(r.Int("TotalWidth")), Positive(r.Int("DataWidth")), Positive(r.Int("Speed")), Positive(r.Int("ConfiguredClockSpeed")),
            r.Str("Manufacturer"), r.Str("PartNumber"), rank,
            Positive(r.Int("MinVoltage")), Positive(r.Int("MaxVoltage")), Positive(r.Int("ConfiguredVoltage")), null);
    }
}
