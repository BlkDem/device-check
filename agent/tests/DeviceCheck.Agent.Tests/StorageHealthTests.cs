using System.Text.Json.Nodes;
using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Core.Sources;
using DeviceCheck.Agent.Export;
using DeviceCheck.Agent.Monitoring.Core;
using DeviceCheck.Agent.Monitoring.Filesystems;
using DeviceCheck.Agent.Monitoring.Smart;
using DeviceCheck.Agent.Monitoring.Storage;
using DeviceCheck.Agent.Tests.Fixtures;
using static DeviceCheck.Agent.Tests.Fixtures.StorageMachines;

namespace DeviceCheck.Agent.Tests;

public class NvmeParserTests
{
    [Fact]
    public void Health_log_fields_and_offsets()
    {
        var log = NvmeHealthLog.Parse(NvmeLogBuilder.Health(tempC: 42, spare: 97, spareThr: 10, used: 15, criticalWarning: 0x05, poh: 1200, cycles: 50,
            unsafeShutdowns: 3, mediaErrors: 4, errorLog: 2, sensorsC: [42, 50, 0, 0, 0, 0, 0, 39]));

        Assert.Equal(42, NvmeHealthLog.Celsius(log.CompositeTemperatureKelvin));
        Assert.Equal((97, 10, 15), (log.AvailableSpare, log.AvailableSpareThreshold, log.PercentageUsed));
        Assert.Equal(1200, log.PowerOnHours.AsLong);
        Assert.Equal(50, log.PowerCycles.AsLong);
        Assert.Equal(3, log.UnsafeShutdowns.AsLong);
        Assert.Equal(4, log.MediaErrors.AsLong);
        Assert.Equal(2, log.ErrorLogEntries.AsLong);
        Assert.Equal(9_000_000, log.HostReadCommands.AsLong);
        Assert.Equal(321, log.ControllerBusyTime.AsLong);
        Assert.Equal((5u, 1u), (log.WarningTemperatureTime, log.CriticalTemperatureTime));
        Assert.Equal(new[] { "available_spare_below_threshold", "reliability_degraded" }, NvmeHealthLog.WarningFlags(log.CriticalWarning));
        Assert.Equal(3, log.SensorKelvin.Count(k => k != 0));
    }

    [Fact]
    public void Data_units_are_thousands_of_512_byte_units()
    {
        var log = NvmeHealthLog.Parse(NvmeLogBuilder.Health(unitsWritten: 123_456));
        Assert.Equal(123_456L * 512_000, NvmeHealthLog.ToBytes(log.DataUnitsWritten));
        Assert.Equal(512_000, NvmeHealthLog.ToBytes(new NvmeCounter(1, 0)));
    }

    [Fact]
    public void Huge_counters_are_not_truncated()
    {
        var log = NvmeHealthLog.Parse(NvmeLogBuilder.Health(highWrittenBits: 1));
        Assert.Null(log.DataUnitsWritten.AsLong);
        Assert.Null(NvmeHealthLog.ToBytes(log.DataUnitsWritten));
        Assert.Null(NvmeHealthLog.ToBytes(new NvmeCounter(long.MaxValue / 1000, 0))); // fits as units, overflows as bytes
        var info = SmartMapping.Nvme(log);
        Assert.Equal("value_out_of_range", info.DataUnitsWritten.Reason);
    }

    [Fact]
    public void Percentage_used_is_used_not_remaining_and_can_exceed_100()
    {
        var info = SmartMapping.Nvme(NvmeHealthLog.Parse(NvmeLogBuilder.Health(used: 255)));
        Assert.Equal(255, info.PercentageUsed.Value);
        Assert.Equal("endurance_used_not_remaining", info.PercentageUsed.Note);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(273, 0)]
    [InlineData(358, 85)]
    public void Kelvin_to_celsius(int k, int? c) => Assert.Equal(c, NvmeHealthLog.Celsius(k));

    [Fact]
    public void Identify_thresholds_without_reading_the_serial()
    {
        var id = NvmeIdentify.Parse(NvmeLogBuilder.Identify(85, 95));
        Assert.Equal((85, 95), (NvmeHealthLog.Celsius(id.WarningTempKelvin), NvmeHealthLog.Celsius(id.CriticalTempKelvin)));
        Assert.Equal("Samsung SSD 990 PRO 2TB", id.Model);
        Assert.DoesNotContain("S6B0NL0W", id.ToString());
        Assert.Null(NvmeHealthLog.Celsius(NvmeIdentify.Parse(NvmeLogBuilder.Identify(0, 0)).WarningTempKelvin));
    }

    [Fact]
    public void Short_buffers_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => NvmeHealthLog.Parse(new byte[100]));
        Assert.Throws<ArgumentException>(() => AtaSmartParser.Parse(new byte[100], []));
    }
}

public class AtaSmartTests
{
    [Fact]
    public void Attributes_with_thresholds_and_48_bit_raw()
    {
        var (data, thr) = AtaSmartBuilder.Build(new(5, 100, 100, 0x0000_0102_0304, 36), new(9, 98, 97, 8760));
        var attrs = AtaSmartParser.Parse(data, thr);
        Assert.Equal(2, attrs.Count);
        Assert.Equal((5, 100, 100, 0x0000_0102_0304L, 36), (attrs[0].Id, attrs[0].Normalized, attrs[0].Worst, attrs[0].Raw, attrs[0].Threshold!.Value));
        Assert.Equal(16, AtaSmartParser.Revision(data));
    }

    [Fact]
    public void Empty_and_duplicate_slots_are_skipped()
    {
        var (data, _) = AtaSmartBuilder.Build(new(5, 100, 100, 1), new(5, 50, 50, 999), new(0, 0, 0, 0), new(12, 99, 99, 7));
        var attrs = AtaSmartParser.Parse(data, []);
        Assert.Equal(new[] { 5, 12 }, attrs.Select(a => a.Id));
        Assert.Equal(1, attrs[0].Raw); // the first copy of a duplicate wins
        Assert.Null(attrs[0].Threshold);
    }

    [Fact]
    public void Only_standard_attributes_are_interpreted()
    {
        var info = SmartMapping.Ata(AtaSmartBuilder.SataSsd().Data, AtaSmartBuilder.SataSsd().Thresholds);
        var a = info.Attributes.ToDictionary(x => x.Id);

        Assert.Equal(8760, a[9].Value.Value);
        Assert.Equal(Confidence.Detected, a[9].Value.Confidence); // never "exact": vendors differ
        Assert.Equal(35, a[194].Value.Value);                     // low byte, upper bytes hold min/max
        Assert.Equal("vendor_specific", a[177].Semantics);
        Assert.Equal("vendor_specific", a[177].Value.Reason);
        Assert.Equal(95, a[177].Normalized.Value);
        Assert.Equal("vendor_specific", a[241].Value.Reason);     // host writes: unit differs per vendor
        Assert.Equal(41_234_567_890, a[241].Raw.Value);
        Assert.Equal(10, a[5].Threshold.Value);
        Assert.True(a[5].PreFailure);
    }

    [Fact]
    public void Unknown_attribute_keeps_raw_values_without_meaning()
    {
        var info = SmartMapping.Ata(AtaSmartBuilder.Hdd().Data, null);
        var x = info.Attributes.Single(a => a.Id == 250);
        Assert.Null(x.Name);
        Assert.Equal("unknown", x.Semantics);
        Assert.Equal(77, x.Raw.Value);
        Assert.Equal("unknown_attribute", x.Value.Reason);
        Assert.Equal(F.Reasons.NotReported, x.Threshold.Reason);
    }

    [Fact]
    public void Implausible_temperature_byte_is_not_reported()
    {
        var (data, _) = AtaSmartBuilder.Build(new AtaSmartBuilder.Attr(194, 100, 100, 0, 0, 0x22, [200, 0, 0, 0, 0, 0]));
        Assert.Equal("raw_format_unrecognized", SmartMapping.Ata(data, null).Attributes[0].Value.Reason);
    }
}

public class StorageMonitorTests
{
    private static async Task<StorageHealthReport> Full(StorageHealthMonitor? m = null) => await (m ?? Monitor()).SampleAsync(SampleKind.Full);
    private static StorageHealthDevice Disk(StorageHealthReport r, int n) => r.StorageHealth.Devices.Single(d => d.DiskNumber.Value == n);

    [Fact]
    public async Task Inventory_of_several_drive_types()
    {
        var r = await Full();
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, r.StorageHealth.Devices.Select(d => d.DiskNumber.Value ?? -1));
        Assert.Equal(new[] { "nvme", "sata_ssd", "sata_hdd", "usb", "virtual" }, r.StorageHealth.Devices.Select(d => d.Type.Value));
        Assert.All(r.StorageHealth.Devices, d => Assert.Equal(F.Reasons.Privacy, d.Serial.Reason));
        Assert.All(r.StorageHealth.Devices, d => Assert.Matches("^disk-[0-9a-f]{12}$", d.Id));
        Assert.Equal(5, r.StorageHealth.Devices.Select(d => d.Id).Distinct().Count());
        Assert.Equal("Samsung", Disk(r, 1).Manufacturer.Value);
        Assert.Equal("warning", Disk(r, 2).Health.OsStatus.Value);
        Assert.All(r.Diagnostics.Collectors, c => Assert.Equal("ok", c.Status));
    }

    [Fact]
    public async Task Nvme_health_log()
    {
        var d = Disk(await Full(), 0);
        var s = d.Smart;
        Assert.Equal("nvme_health_log", s.Interface.Value);
        Assert.Equal(15, s.PercentageUsed.Value);
        Assert.Equal((100, 10), (s.AvailableSparePercent.Value, s.AvailableSpareThresholdPercent.Value));
        Assert.Equal(1200, s.PowerOnHours.Value);
        Assert.Equal(0, s.MediaErrors.Value);
        Assert.Equal(123_456L * 512_000, s.DataWrittenBytes.Value);
        Assert.Equal(234_567L * 512_000, s.DataReadBytes.Value);
        Assert.Equal(F.Reasons.NotApplicable, s.ReallocatedSectors.Reason);
        Assert.Empty(s.Nvme!.CriticalWarningFlags.Value!);
        Assert.Equal(Confidence.Exact, s.Nvme.CriticalWarningFlags.Confidence); // "no warnings" is a value, not missing data

        // Temperature: composite from the log, thresholds from Identify (manufacturer) override the driver.
        Assert.Equal(42, d.Temperature.CurrentCelsius.Value);
        Assert.Equal((85, 95), (d.Temperature.WarningCelsius.Value, d.Temperature.CriticalCelsius.Value));
        Assert.Contains("WCTEMP", d.Temperature.WarningCelsius.Source);
        Assert.Equal(new[] { "sensor_1", "sensor_2" }, d.Temperature.Sensors.Select(x => x.Name));
        Assert.Equal("ata_only", d.Health.PredictFailure.Reason);
    }

    [Fact]
    public async Task Sata_ssd_and_hdd_smart()
    {
        var r = await Full();
        var ssd = Disk(r, 1);
        Assert.Equal("ata_smart", ssd.Smart.Interface.Value);
        Assert.Equal(8, ssd.Smart.Ata!.Attributes.Count);
        Assert.Equal(8760, ssd.Smart.PowerOnHours.Value);
        Assert.Equal(0, ssd.Smart.ReallocatedSectors.Value);
        Assert.Equal("vendor_specific", ssd.Smart.PercentageUsed.Reason); // SATA wear is vendor-specific: not normalized
        Assert.Equal(38, ssd.Temperature.CurrentCelsius.Value);            // driver value wins over SMART 194
        Assert.False(ssd.Health.PredictFailure.Value);

        var hdd = Disk(r, 2);
        Assert.Equal((8L, 2L, 1L, 0L), (hdd.Smart.ReallocatedSectors.Value!.Value, hdd.Smart.PendingSectors.Value!.Value,
            hdd.Smart.UncorrectableSectors.Value!.Value, hdd.Smart.CrcErrors.Value!.Value));
        Assert.Equal(41, hdd.Temperature.CurrentCelsius.Value);           // no driver temperature → SMART 194, detected
        Assert.Equal(Confidence.Detected, hdd.Temperature.CurrentCelsius.Confidence);
        Assert.Equal(55, hdd.Temperature.MaxRecordedCelsius.Value);        // reliability counter
        Assert.NotNull(hdd.Smart.Reliability);
        Assert.Equal("ata_smart", hdd.Smart.Interface.Value);              // counters never replace a direct SMART source
    }

    [Fact]
    public async Task Usb_and_virtual_disks()
    {
        var r = await Full();
        var usb = Disk(r, 3);
        Assert.Equal("storage_reliability_counter", usb.Smart.Interface.Value); // elevated: Windows counters only
        Assert.Equal(7, usb.Smart.PercentageUsed.Value);
        Assert.Equal("windows_reported_wear", usb.Smart.PercentageUsed.Note);
        Assert.False(usb.Temperature.CurrentCelsius.HasValue);                // counter says 0 °C → unknown, not 0
        Assert.Equal(F.Reasons.Unsupported, usb.Temperature.CurrentCelsius.Reason);

        var vhd = Disk(r, 4);
        Assert.Equal(F.Reasons.NotApplicable, vhd.Smart.Interface.Reason);
    }

    [Fact]
    public async Task Without_admin_smart_is_unavailable_not_critical()
    {
        var wmi = Wmi()
            .Throw("MSStorageDriver_ATAPISmartData", new WmiException("q", "denied", null, "AccessDenied"))
            .Throw("MSFT_StorageReliabilityCounter", new WmiException("q", "denied", null, "AccessDenied"));
        var r = await Full(Monitor(wmi, platform: Platform(elevated: false)));

        foreach (var n in new[] { 1, 2 })
        {
            Assert.Equal(F.Reasons.RequiresAdmin, Disk(r, n).Smart.Interface.Reason);
            Assert.Equal(F.Reasons.RequiresAdmin, Disk(r, n).Health.PredictFailure.Reason);
        }
        Assert.Equal("usb_bridge_no_passthrough", Disk(r, 3).Smart.Interface.Reason);
        Assert.Equal(F.Reasons.RequiresAdmin, Disk(r, 2).Temperature.MaxRecordedCelsius.Reason);
        Assert.Contains(r.Limitations, l => l.Code == "smart_requires_admin");
        Assert.Contains(r.Limitations, l => l.Code == "reliability_counters_require_admin");
        Assert.Contains(r.Limitations, l => l.Code == "usb_smart_unavailable");
        // Missing SMART is never turned into a bad health status.
        Assert.All(r.StorageHealth.Devices, d => Assert.NotEqual("critical", d.Health.Status.Value));
        Assert.All(r.StorageHealth.Devices, d => Assert.Equal("not_evaluated", d.Health.Status.Reason));
        Assert.All(r.Diagnostics.Collectors, c => Assert.Equal("ok", c.Status)); // denial is a limitation, not an error
        Assert.Equal("nvme_health_log", Disk(r, 0).Smart.Interface.Value);
    }

    [Fact]
    public async Task Nvme_access_denied()
    {
        var dev = Devices();
        dev.Health[0] = () => throw new StorageAccessException(F.Reasons.RequiresAdmin, "denied", 5);
        var d = Disk(await Full(Monitor(devices: dev)), 0);
        Assert.Equal(F.Reasons.RequiresAdmin, d.Smart.Interface.Reason);
        Assert.Null(d.Smart.Nvme);
        Assert.Equal(41, d.Temperature.CurrentCelsius.Value); // driver temperature still works
    }

    [Fact]
    public async Task Corrupt_smart_data_is_isolated()
    {
        var dev = Devices();
        dev.Health[0] = () => new byte[64];
        var wmi = Wmi().On("MSStorageDriver_ATAPISmartData",
            FakeWmi.Row(("InstanceName", @"SCSI\Disk&Ven_&Prod_Samsung_SSD_870\4&2a&0&010000_0"), ("VendorSpecific", new byte[12])),
            FakeWmi.Row(("InstanceName", @"SCSI\Disk&Ven_WDC&Prod_WD40EZRZ\4&2a&0&020000_0"), ("VendorSpecific", AtaSmartBuilder.Hdd().Data)));
        var r = await Full(Monitor(wmi, dev));

        Assert.Equal("invalid_data", Disk(r, 0).Smart.Interface.Reason);
        Assert.Equal("not_exposed_by_driver", Disk(r, 1).Smart.Interface.Reason);
        Assert.Equal("partial", r.Diagnostics.Collectors.Single(c => c.Id == "nvme").Status);
        Assert.Equal("ata_smart", Disk(r, 2).Smart.Interface.Value); // the other disk is unaffected
        Assert.Empty(ReportValidator.ValidateStorageHealth(ReportJson.Serialize(r)));
    }

    [Fact]
    public async Task Wmi_unavailable()
    {
        var wmi = new FakeWmi();
        foreach (var c in new[] { "MSFT_PhysicalDisk", "MSFT_Disk", "Win32_DiskDrive", "MSFT_Volume", "MSFT_Partition", "MSStorageDriver_ATAPISmartData", "MSFT_StorageReliabilityCounter" })
            wmi.Throw(c, new TimeoutException("WMI query timed out"));
        var r = await Full(Monitor(wmi));

        Assert.Empty(r.StorageHealth.Devices);
        Assert.Equal(new[] { "C:", "Z:" }, r.StorageHealth.OtherVolumes.Select(v => v.Letter.Value)); // DriveInfo still works
        Assert.Equal("partial", r.Diagnostics.Collectors.Single(c => c.Id == "inventory").Status);
        Assert.Empty(ReportValidator.ValidateStorageHealth(ReportJson.Serialize(r)));
    }

    [Fact]
    public async Task Hanging_collector_times_out_and_others_finish()
    {
        var hang = new Hang();
        var r = await Monitor(collectors: [hang, new VolumeCollector(Wmi(), Platform())], timeout: TimeSpan.FromMilliseconds(200)).SampleAsync(SampleKind.Full);
        Assert.Equal("timeout", r.Diagnostics.Collectors.Single(c => c.Id == "hang").Status);
        Assert.Equal("ok", r.Diagnostics.Collectors.Single(c => c.Id == "volumes").Status);
        Assert.NotEmpty(Disk(r, 0).Volumes);
    }

    private sealed class Hang : IStorageHealthCollector
    {
        public string Id => "hang";
        public bool FullOnly => false;
        public Task CollectAsync(StorageHealthContext ctx, CancellationToken ct) { Thread.Sleep(2000); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Quick_sample_skips_smart()
    {
        var dev = Devices();
        dev.Temperature.Remove(0); // NVMe without driver temperature: quick mode reads the composite temperature only
        var r = await Monitor(devices: dev).SampleAsync(SampleKind.Quick);
        Assert.DoesNotContain(r.Diagnostics.Collectors, c => c.Id is "nvme" or "ata_smart" or "reliability");
        Assert.All(r.StorageHealth.Devices, d => Assert.Equal("not_sampled_quick", d.Smart.Interface.Reason));
        Assert.Equal(42, Disk(r, 0).Temperature.CurrentCelsius.Value);
        Assert.Equal(1, dev.HealthQueries);
        Assert.NotEmpty(Disk(r, 1).Volumes);
    }

    [Fact]
    public async Task Volumes_partitions_and_free_space()
    {
        var r = await Full();
        Assert.Equal(new[] { "C:", null }, Disk(r, 0).Volumes.Select(v => v.Letter.Value));    // C: + recovery, EFI has no volume
        Assert.Equal(new[] { "D:", "E:" }, Disk(r, 1).Volumes.Select(v => v.Letter.Value));    // two partitions on one disk
        Assert.Equal(new[] { "S:", "Z:" }, r.StorageHealth.OtherVolumes.Select(v => v.Letter.Value)); // spanned + subst

        var s = r.StorageHealth.OtherVolumes[0];
        Assert.Equal(new[] { 1, 2 }, s.DiskNumbers.Value);
        Assert.Equal(F.Reasons.NotCollected, r.StorageHealth.OtherVolumes[1].Label.Reason);

        var e = Disk(r, 1).Volumes[1];
        Assert.Equal(385 * GB, e.UsedBytes.Value);
        Assert.Equal(3.8, e.FreePercent.Value);
        Assert.Equal(96.2, e.UsedPercent.Value);
        Assert.Equal("Games", e.Label.Value);
        Assert.Equal("warning", Disk(r, 2).Volumes[0].HealthStatus.Value);
        Assert.Equal("removable", Disk(r, 3).Volumes[0].DriveType.Value);
        Assert.Single(Disk(r, 0).Volumes, v => v.Letter.Value == "C:"); // not duplicated by DriveInfo
    }

    [Theory]
    [InlineData(1000L, 250L, 25.0, 75.0)]
    [InlineData(3L, 1L, 33.3, 66.7)]
    [InlineData(100L, 0L, 0.0, 100.0)]
    public void Free_space_math(long total, long free, double freePct, double usedPct)
    {
        var v = new VolumeHealth();
        VolumeCollector.Space(v, total, free, "test");
        Assert.Equal((total - free, freePct, usedPct), (v.UsedBytes.Value!.Value, v.FreePercent.Value!.Value, v.UsedPercent.Value!.Value));
    }

    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(100L, 200L)]
    [InlineData(null, 10L)]
    public void Inconsistent_space_is_not_reported(long? total, long? free)
    {
        var v = new VolumeHealth();
        VolumeCollector.Space(v, total, free, "test");
        Assert.False(v.UsedBytes.HasValue);
        Assert.False(v.FreePercent.HasValue);
    }

    [Fact]
    public async Task Json_is_valid_private_and_stable()
    {
        var r = await Full();
        var json = ReportJson.Serialize(r);
        Assert.Empty(ReportValidator.ValidateStorageHealth(json));
        Assert.DoesNotContain("SERIAL", json);
        Assert.DoesNotContain("UNIQ-", json);
        Assert.DoesNotContain("S6B0NL0W", json);

        var root = JsonNode.Parse(json)!;
        Assert.Equal("storage_health", (string?)root["kind"]);
        var nvme = root["storage_health"]!["devices"]![0]!;
        Assert.Equal(15, (int?)nvme["smart"]!["percentage_used"]!["value"]);
        Assert.Equal(42, (int?)nvme["temperature"]!["current_celsius"]!["value"]);
        Assert.Equal("exact", (string?)nvme["smart"]!["nvme"]!["media_errors"]!["confidence"]);
        Assert.IsType<JsonArray>(root["storage_health"]!["alerts"]);

        var again = await Full();
        Assert.Equal(r.StorageHealth.Devices.Select(d => d.Id), again.StorageHealth.Devices.Select(d => d.Id));
        Assert.Equal(json, ReportJson.Serialize(ReportJson.DeserializeStorageHealth(json)!)); // lossless round trip
    }

    [Fact]
    public void Validator_rejects_broken_storage_reports()
    {
        Assert.NotEmpty(ReportValidator.ValidateStorageHealth("{}"));
        var root = JsonNode.Parse(ReportJson.Serialize(new StorageHealthReport()))!;
        root["kind"] = "hardware";
        root["storage_health"]!["devices"] = new JsonObject();
        var errors = ReportValidator.ValidateStorageHealth(root.ToJsonString());
        Assert.Contains(errors, e => e.Contains("kind"));
        Assert.Contains(errors, e => e.Contains("devices must be an array"));
        Assert.Empty(ReportValidator.ValidateStorageHealth(ReportJson.Serialize(new StorageHealthReport())));
    }

    [Fact]
    public void Wmi_byte_arrays_and_error_codes()
    {
        var o = FakeWmi.Row(("A", new byte[] { 1, 2 }), ("B", new object[] { (byte)3, 4 }), ("C", "x"));
        Assert.Equal(new byte[] { 1, 2 }, o.Bytes("A"));
        Assert.Equal(new byte[] { 3, 4 }, o.Bytes("B"));
        Assert.Null(o.Bytes("C"));
        Assert.True(new WmiException("q", "m", null, "AccessDenied").IsAccessDenied);
        Assert.True(new WmiException("q", "m", null, "InvalidClass").IsNotSupported);
        Assert.False(new WmiException("q", "m").IsAccessDenied);
    }

    [Theory]
    [InlineData(5, "requires_admin")]
    [InlineData(1117, "api_unsupported")]
    [InlineData(2, "device_not_accessible")]
    [InlineData(1450, "error")]
    public void Win32_errors_map_to_reasons(int err, string reason) => Assert.Equal(reason, StorageAccessException.ReasonFor(err));
}
