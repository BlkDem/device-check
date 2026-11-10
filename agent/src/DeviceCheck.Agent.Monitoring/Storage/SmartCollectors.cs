using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Core.Sources;
using DeviceCheck.Agent.Monitoring.Core;
using DeviceCheck.Agent.Monitoring.Smart;

namespace DeviceCheck.Agent.Monitoring.Storage;

/// <summary>Drive temperature reported by the storage driver (no SMART query; safe every minute).</summary>
public sealed class DeviceTemperatureCollector(IStorageDeviceSource source) : IStorageHealthCollector
{
    public string Id => "temperature";
    public bool FullOnly => false;
    private const string Src = "IOCTL_STORAGE_QUERY_PROPERTY StorageDeviceTemperatureProperty";

    public Task CollectAsync(StorageHealthContext ctx, CancellationToken ct)
    {
        foreach (var d in ctx.Disks)
        {
            ct.ThrowIfCancellationRequested();
            var t = d.Device.Temperature;
            try
            {
                var data = source.DeviceTemperature(d.Number);
                ctx.UsedSource(Src);
                t.CurrentCelsius = data?.CurrentCelsius is int c
                    ? F.Exact<int?>(c, Src)
                    : F.Unavailable<int?>(Src, F.Reasons.NotReported);
                t.WarningCelsius = F.Exact(data?.WarningCelsius, Src + " WarningTemperature", "manufacturer_threshold");
                t.CriticalCelsius = F.Exact(data?.CriticalCelsius, Src + " CriticalTemperature", "manufacturer_threshold");
            }
            catch (StorageAccessException e)
            {
                t.CurrentCelsius = F.Unavailable<int?>(Src, e.Reason);
                ctx.Warn($"disk{d.Number}_temperature_{e.Reason}" + (e.Win32Error != 0 ? $"_win32_{e.Win32Error}" : ""));
            }

            // NVMe without driver temperature: the composite temperature of the health log (a read-only query, safe every minute).
            if (!t.CurrentCelsius.HasValue && d.Bus == "nvme")
            {
                try
                {
                    var log = NvmeHealthLog.Parse(source.NvmeHealthLog(d.Number));
                    ctx.UsedSource(SmartMapping.NvmeLogSrc);
                    t.CurrentCelsius = F.Exact(NvmeHealthLog.Celsius(log.CompositeTemperatureKelvin), SmartMapping.NvmeLogSrc + " Composite Temperature", "kelvin_minus_273");
                }
                catch (Exception e) when (e is StorageAccessException or ArgumentException) { }
            }
        }
        return Task.CompletedTask;
    }
}

/// <summary>NVMe Health Information log + Identify Controller (temperature thresholds) for NVMe disks.</summary>
public sealed class NvmeHealthCollector(IStorageDeviceSource source) : IStorageHealthCollector
{
    public string Id => "nvme";
    public bool FullOnly => true;

    public Task CollectAsync(StorageHealthContext ctx, CancellationToken ct)
    {
        foreach (var d in ctx.Disks.Where(x => x.Bus == "nvme"))
        {
            ct.ThrowIfCancellationRequested();
            var dev = d.Device;
            try
            {
                var raw = source.NvmeHealthLog(d.Number);
                var log = NvmeHealthLog.Parse(raw);
                ctx.UsedSource(SmartMapping.NvmeLogSrc);
                var info = SmartMapping.Nvme(log);
                dev.Smart.Nvme = info;
                dev.Smart.Interface = F.Exact<string?>("nvme_health_log", SmartMapping.NvmeLogSrc);
                SmartMapping.ApplyNvmeSummary(dev.Smart, info, log);
                if (info.CompositeTemperatureCelsius.HasValue) dev.Temperature.CurrentCelsius = info.CompositeTemperatureCelsius;
                dev.Temperature.Sensors = SmartMapping.NvmeSensors(log).ToList();
            }
            catch (StorageAccessException e)
            {
                d.SmartReason = e.Reason;
                ctx.Warn($"disk{d.Number}_nvme_health_{e.Reason}");
                ctx.Limit("storage", e.Reason == F.Reasons.RequiresAdmin ? "smart_requires_admin" : "nvme_health_unavailable");
                continue;
            }
            catch (ArgumentException e)
            {
                d.SmartReason = "invalid_data";
                ctx.Error(SmartMapping.NvmeLogSrc, e);
                continue;
            }

            // Thresholds from the manufacturer (WCTEMP / CCTEMP); 0 = not reported.
            try
            {
                var id = NvmeIdentify.Parse(source.NvmeIdentifyController(d.Number));
                ctx.UsedSource(SmartMapping.NvmeIdentifySrc);
                if (NvmeHealthLog.Celsius(id.WarningTempKelvin) is int w)
                    dev.Temperature.WarningCelsius = F.Exact<int?>(w, SmartMapping.NvmeIdentifySrc + " WCTEMP", "manufacturer_threshold");
                if (NvmeHealthLog.Celsius(id.CriticalTempKelvin) is int c)
                    dev.Temperature.CriticalCelsius = F.Exact<int?>(c, SmartMapping.NvmeIdentifySrc + " CCTEMP", "manufacturer_threshold");
            }
            catch (Exception e) when (e is StorageAccessException or ArgumentException)
            {
                ctx.Warn($"disk{d.Number}_nvme_identify_unavailable");
            }
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// ATA SMART attributes for SATA/ATA disks through the storage driver's WMI classes (root\wmi). Windows exposes
/// them to administrators only; without elevation the limitation is reported, not guessed around.
/// </summary>
public sealed class AtaSmartCollector(IWmiClient wmi) : IStorageHealthCollector
{
    public string Id => "ata_smart";
    public bool FullOnly => true;
    private const string Ns = @"root\wmi";

    public async Task CollectAsync(StorageHealthContext ctx, CancellationToken ct)
    {
        var ata = ctx.Disks.Where(d => d.Bus is "sata" or "ata" or "atapi").ToList();
        if (ata.Count == 0) return;

        IReadOnlyList<WmiObject> data;
        try
        {
            data = await wmi.QueryAsync(Ns, "SELECT InstanceName, VendorSpecific FROM MSStorageDriver_ATAPISmartData", ct);
            ctx.UsedSource(SmartMapping.AtaSrc);
        }
        catch (WmiException e)
        {
            var reason = e.IsAccessDenied ? F.Reasons.RequiresAdmin : e.IsNotSupported ? F.Reasons.Unsupported : F.Reasons.Error;
            if (reason == F.Reasons.Error) ctx.Error(SmartMapping.AtaSrc, e);
            foreach (var d in ata)
            {
                d.SmartReason = reason;
                d.Device.Health.PredictFailure = F.Unavailable<bool?>("root\\wmi MSStorageDriver_FailurePredictStatus", reason);
            }
            ctx.Limit("storage", reason == F.Reasons.RequiresAdmin ? "smart_requires_admin" : "ata_smart_unavailable");
            return;
        }

        IReadOnlyList<WmiObject> thresholds = await ctx.TryAsync(SmartMapping.AtaThresholdSrc,
            () => wmi.QueryAsync(Ns, "SELECT InstanceName, VendorSpecific FROM MSStorageDriver_FailurePredictThresholds", ct), []);
        IReadOnlyList<WmiObject> status = await ctx.TryAsync("root\\wmi MSStorageDriver_FailurePredictStatus",
            () => wmi.QueryAsync(Ns, "SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus", ct), []);

        foreach (var d in ata)
        {
            var row = Match(data, d.PnpDeviceId);
            if (row?.Bytes("VendorSpecific") is not { Length: >= AtaSmartParser.Size } bytes)
            {
                d.SmartReason = "not_exposed_by_driver";
                continue;
            }
            try
            {
                var thr = Match(thresholds, d.PnpDeviceId)?.Bytes("VendorSpecific");
                var info = SmartMapping.Ata(bytes, thr is { Length: >= AtaSmartParser.Size } ? thr : null);
                d.Device.Smart.Ata = info;
                d.Device.Smart.Interface = F.Exact<string?>("ata_smart", SmartMapping.AtaSrc);
                SmartMapping.ApplyAtaSummary(d.Device.Smart, info);
                if (info.Attributes.FirstOrDefault(a => a.Id == 194)?.Value is { HasValue: true } temp && !d.Device.Temperature.CurrentCelsius.HasValue)
                    d.Device.Temperature.CurrentCelsius = F.Detected<int?>((int)temp.Value!.Value, temp.Source, "smart_attribute_194");
            }
            catch (ArgumentException e)
            {
                d.SmartReason = "invalid_data";
                ctx.Error(SmartMapping.AtaSrc, e);
            }
            d.Device.Health.PredictFailure = Match(status, d.PnpDeviceId)?.Bool("PredictFailure") is bool pf
                ? F.Exact<bool?>(pf, "root\\wmi MSStorageDriver_FailurePredictStatus.PredictFailure")
                : F.Unavailable<bool?>("root\\wmi MSStorageDriver_FailurePredictStatus", F.Reasons.NotReported);
        }
    }

    /// <summary>InstanceName = Win32_DiskDrive.PNPDeviceID + "_N" (case differs between the two providers).</summary>
    internal static WmiObject? Match(IReadOnlyList<WmiObject> rows, string? pnp)
    {
        if (pnp is null) return null;
        return rows.FirstOrDefault(r => r.Str("InstanceName") is { } n
            && System.Text.RegularExpressions.Regex.Replace(n, @"_\d+$", "").Equals(pnp, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Windows reliability counters (driver-normalized SMART / NVMe values). Administrator only.</summary>
public sealed class ReliabilityCounterCollector(IWmiClient wmi) : IStorageHealthCollector
{
    public string Id => "reliability";
    public bool FullOnly => true;

    public async Task CollectAsync(StorageHealthContext ctx, CancellationToken ct)
    {
        IReadOnlyList<WmiObject> rows;
        try
        {
            rows = await wmi.QueryAsync(DiskInventory.StorageNs,
                "SELECT DeviceId, Temperature, TemperatureMax, Wear, PowerOnHours, StartStopCycleCount, LoadUnloadCycleCount, ReadErrorsTotal, ReadErrorsCorrected, ReadErrorsUncorrected, WriteErrorsTotal, WriteErrorsCorrected, WriteErrorsUncorrected, ReadLatencyMax, WriteLatencyMax, FlushLatencyMax FROM MSFT_StorageReliabilityCounter", ct);
            ctx.UsedSource(SmartMapping.ReliabilitySrc);
        }
        catch (WmiException e)
        {
            var reason = e.IsAccessDenied ? F.Reasons.RequiresAdmin : e.IsNotSupported ? F.Reasons.Unsupported : F.Reasons.Error;
            if (e.IsAccessDenied) ctx.Limit("storage", "reliability_counters_require_admin");
            else if (!e.IsNotSupported) ctx.Error(SmartMapping.ReliabilitySrc, e);
            foreach (var d in ctx.Disks) d.Device.Temperature.MaxRecordedCelsius = F.Unavailable<int?>(SmartMapping.ReliabilitySrc, reason);
            return;
        }

        foreach (var d in ctx.Disks)
        {
            var row = rows.FirstOrDefault(r => r.Str("DeviceId") == d.StorageDeviceId);
            if (row is null) continue;
            var rc = SmartMapping.Reliability(row);
            var dev = d.Device;
            dev.Smart.Reliability = rc;
            dev.Temperature.MaxRecordedCelsius = rc.TemperatureMaxCelsius;
            if (!dev.Temperature.CurrentCelsius.HasValue && rc.TemperatureCelsius.HasValue) dev.Temperature.CurrentCelsius = rc.TemperatureCelsius;

            // Only when no direct SMART interface answered: driver counters become the summary.
            if (dev.Smart.Nvme is null && dev.Smart.Ata is null)
            {
                dev.Smart.Interface = F.Exact<string?>("storage_reliability_counter", SmartMapping.ReliabilitySrc);
                dev.Smart.PowerOnHours = rc.PowerOnHours;
                dev.Smart.PercentageUsed = rc.WearPercent.HasValue
                    ? F.Exact(rc.WearPercent.Value, rc.WearPercent.Source, "windows_reported_wear")
                    : rc.WearPercent;
            }
        }
    }
}
