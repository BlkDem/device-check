using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Core.Sources;
using DeviceCheck.Agent.Monitoring.Core;

namespace DeviceCheck.Agent.Monitoring.Smart;

/// <summary>Turns parsed SMART structures into report fields (source + confidence on every value).</summary>
public static class SmartMapping
{
    public const string NvmeLogSrc = "NVMe Health Information log (02h)";
    public const string NvmeIdentifySrc = "NVMe Identify Controller";
    public const string AtaSrc = "ATA SMART (root\\wmi MSStorageDriver_ATAPISmartData)";
    public const string AtaThresholdSrc = "ATA SMART thresholds (root\\wmi MSStorageDriver_FailurePredictThresholds)";
    public const string ReliabilitySrc = "Storage API MSFT_StorageReliabilityCounter";

    private static Field<long?> Counter(NvmeCounter c, string name) => c.AsLong is long v
        ? F.Exact<long?>(v, $"{NvmeLogSrc} {name}")
        : F.Unavailable<long?>($"{NvmeLogSrc} {name}", "value_out_of_range");

    public static NvmeHealthInfo Nvme(NvmeHealthLog log) => new()
    {
        CriticalWarning = F.Exact<int?>(log.CriticalWarning, NvmeLogSrc + " Critical Warning"),
        CriticalWarningFlags = log.CriticalWarning == 0
            ? new Field<List<string>?> { Value = [], Source = NvmeLogSrc + " Critical Warning", Confidence = Confidence.Exact }
            : F.Exact<List<string>?>(NvmeHealthLog.WarningFlags(log.CriticalWarning).ToList(), NvmeLogSrc + " Critical Warning"),
        CompositeTemperatureCelsius = F.Exact(NvmeHealthLog.Celsius(log.CompositeTemperatureKelvin), NvmeLogSrc + " Composite Temperature", "kelvin_minus_273"),
        AvailableSparePercent = F.Exact<int?>(log.AvailableSpare, NvmeLogSrc + " Available Spare"),
        AvailableSpareThresholdPercent = F.Exact<int?>(log.AvailableSpareThreshold, NvmeLogSrc + " Available Spare Threshold"),
        PercentageUsed = F.Exact<int?>(log.PercentageUsed, NvmeLogSrc + " Percentage Used", "endurance_used_not_remaining"),
        DataUnitsRead = Counter(log.DataUnitsRead, "Data Units Read"),
        DataUnitsWritten = Counter(log.DataUnitsWritten, "Data Units Written"),
        HostReadCommands = Counter(log.HostReadCommands, "Host Read Commands"),
        HostWriteCommands = Counter(log.HostWriteCommands, "Host Write Commands"),
        ControllerBusyTimeMinutes = Counter(log.ControllerBusyTime, "Controller Busy Time"),
        PowerCycles = Counter(log.PowerCycles, "Power Cycles"),
        PowerOnHours = Counter(log.PowerOnHours, "Power On Hours"),
        UnsafeShutdowns = Counter(log.UnsafeShutdowns, "Unsafe Shutdowns"),
        MediaErrors = Counter(log.MediaErrors, "Media and Data Integrity Errors"),
        ErrorLogEntries = Counter(log.ErrorLogEntries, "Number of Error Information Log Entries"),
        WarningTemperatureTimeMinutes = F.Exact<long?>(log.WarningTemperatureTime, NvmeLogSrc + " Warning Composite Temperature Time"),
        CriticalTemperatureTimeMinutes = F.Exact<long?>(log.CriticalTemperatureTime, NvmeLogSrc + " Critical Composite Temperature Time"),
    };

    /// <summary>Fills the normalized SMART summary from the NVMe log (the most direct source for NVMe).</summary>
    public static void ApplyNvmeSummary(SmartInfo s, NvmeHealthInfo n, NvmeHealthLog log)
    {
        s.PercentageUsed = n.PercentageUsed;
        s.AvailableSparePercent = n.AvailableSparePercent;
        s.AvailableSpareThresholdPercent = n.AvailableSpareThresholdPercent;
        s.PowerOnHours = n.PowerOnHours;
        s.PowerCycles = n.PowerCycles;
        s.UnsafeShutdowns = n.UnsafeShutdowns;
        s.MediaErrors = n.MediaErrors;
        s.DataReadBytes = Bytes(log.DataUnitsRead, "Data Units Read");
        s.DataWrittenBytes = Bytes(log.DataUnitsWritten, "Data Units Written");
        foreach (var f in new Action<Field<long?>>[] { v => s.ReallocatedSectors = v, v => s.PendingSectors = v, v => s.UncorrectableSectors = v, v => s.CrcErrors = v })
            f(F.Unavailable<long?>(NvmeLogSrc, F.Reasons.NotApplicable));
    }

    private static Field<long?> Bytes(NvmeCounter units, string name) => NvmeHealthLog.ToBytes(units) is long b
        ? F.Exact<long?>(b, $"{NvmeLogSrc} {name} × 512000", "nvme_data_unit_512000_bytes")
        : F.Unavailable<long?>($"{NvmeLogSrc} {name}", "value_out_of_range");

    public static IReadOnlyList<TemperatureSensor> NvmeSensors(NvmeHealthLog log) =>
        log.SensorKelvin.Select((k, i) => (k, i)).Where(x => x.k != 0)
            .Select(x => new TemperatureSensor($"sensor_{x.i + 1}", F.Exact(NvmeHealthLog.Celsius(x.k), $"{NvmeLogSrc} Temperature Sensor {x.i + 1}")))
            .ToList();

    public static AtaSmartInfo Ata(byte[] data, byte[]? thresholds)
    {
        var info = new AtaSmartInfo { Revision = F.Exact<int?>(AtaSmartParser.Revision(data), AtaSrc) };
        foreach (var e in AtaSmartParser.Parse(data, thresholds ?? []))
        {
            var known = AtaAttributeCatalog.Lookup(e.Id);
            var src = $"{AtaSrc} #{e.Id}";
            Field<long?> value = known switch
            {
                { Semantics: AttributeSemantics.Standard, Interpret: { } fn } when fn(e.Raw, e.RawBytes) is long v =>
                    F.Detected<long?>(v, src + " raw", e.Id == 9 ? "some_drives_count_minutes" : "standard_attribute"),
                { Semantics: AttributeSemantics.Standard } => F.Unavailable<long?>(src, "raw_format_unrecognized"),
                { Semantics: AttributeSemantics.VendorSpecific } => F.Unavailable<long?>(src, "vendor_specific"),
                _ => F.Unavailable<long?>(src, "unknown_attribute"),
            };
            info.Attributes.Add(new SmartAttribute
            {
                Id = e.Id,
                Name = known?.Name,
                Semantics = known?.Semantics switch { AttributeSemantics.Standard => "standard", AttributeSemantics.VendorSpecific => "vendor_specific", _ => "unknown" },
                Flags = e.Flags,
                PreFailure = (e.Flags & 0x01) != 0,
                Normalized = F.Exact<int?>(e.Normalized, src + " current"),
                Worst = F.Exact<int?>(e.Worst, src + " worst"),
                Threshold = e.Threshold is int t
                    ? F.Exact<int?>(t, $"{AtaThresholdSrc} #{e.Id}")
                    : F.Unavailable<int?>(AtaThresholdSrc, thresholds is null ? F.Reasons.NotReported : "no_threshold_entry"),
                Raw = F.Exact<long?>(e.Raw, src + " raw"),
                Value = value,
            });
        }
        return info;
    }

    /// <summary>Normalized summary from ATA attributes with standard semantics only.</summary>
    public static void ApplyAtaSummary(SmartInfo s, AtaSmartInfo a)
    {
        Field<long?> V(int id) => a.Attributes.FirstOrDefault(x => x.Id == id)?.Value
                                   ?? F.Unavailable<long?>($"{AtaSrc} #{id}", "attribute_not_reported");
        s.PowerOnHours = V(9);
        s.PowerCycles = V(12);
        s.ReallocatedSectors = V(5);
        s.PendingSectors = V(197);
        s.UncorrectableSectors = V(198);
        s.CrcErrors = V(199);
        // Wear, spare, host reads/writes are vendor-specific in ATA SMART: not normalized (see attributes).
        foreach (var f in new Action<Field<int?>>[] { v => s.PercentageUsed = v, v => s.AvailableSparePercent = v, v => s.AvailableSpareThresholdPercent = v })
            f(F.Unavailable<int?>(AtaSrc, "vendor_specific"));
        foreach (var f in new Action<Field<long?>>[] { v => s.DataReadBytes = v, v => s.DataWrittenBytes = v, v => s.MediaErrors = v, v => s.UnsafeShutdowns = v })
            f(F.Unavailable<long?>(AtaSrc, "vendor_specific"));
    }

    public static ReliabilityCounters Reliability(WmiObject r)
    {
        Field<long?> L(string p) => F.Exact(r.Long(p), $"{ReliabilitySrc}.{p}");
        Field<int?> I(string p) => F.Exact(r.Int(p), $"{ReliabilitySrc}.{p}");
        return new ReliabilityCounters
        {
            // 0 °C is what drivers report when they do not know the temperature.
            TemperatureCelsius = r.Int("Temperature") is > 0 ? I("Temperature") : F.Unavailable<int?>($"{ReliabilitySrc}.Temperature", F.Reasons.NotReported),
            TemperatureMaxCelsius = r.Int("TemperatureMax") is > 0 ? I("TemperatureMax") : F.Unavailable<int?>($"{ReliabilitySrc}.TemperatureMax", F.Reasons.NotReported),
            WearPercent = I("Wear"),
            PowerOnHours = L("PowerOnHours"),
            StartStopCycles = L("StartStopCycleCount"),
            LoadUnloadCycles = L("LoadUnloadCycleCount"),
            ReadErrorsTotal = L("ReadErrorsTotal"),
            ReadErrorsCorrected = L("ReadErrorsCorrected"),
            ReadErrorsUncorrected = L("ReadErrorsUncorrected"),
            WriteErrorsTotal = L("WriteErrorsTotal"),
            WriteErrorsCorrected = L("WriteErrorsCorrected"),
            WriteErrorsUncorrected = L("WriteErrorsUncorrected"),
            ReadLatencyMaxMs = L("ReadLatencyMax"),
            WriteLatencyMaxMs = L("WriteLatencyMax"),
            FlushLatencyMaxMs = L("FlushLatencyMax"),
        };
    }
}
