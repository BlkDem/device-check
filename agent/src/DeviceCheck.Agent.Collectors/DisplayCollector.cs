using DeviceCheck.Agent.Core.Collection;
using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Core.Normalization;
using DeviceCheck.Agent.Core.Parsing;
using DeviceCheck.Agent.Core.Sources;

namespace DeviceCheck.Agent.Collectors;

/// <summary>
/// Monitors via QueryDisplayConfig (current mode, real refresh rate, connector, HDR) + EDID (identity, native mode, size).
/// The refresh rate is the active mode's rate — never derived from monitor capabilities.
/// </summary>
public sealed class DisplayCollector(IDisplaySource displays) : IHardwareCollector
{
    public string Id => "display";
    private const string QdcSrc = "QueryDisplayConfig";
    private const string NameSrc = "DisplayConfigGetDeviceInfo (target name)";

    public Task CollectAsync(CollectorContext ctx, CancellationToken ct)
    {
        var paths = ctx.Try(QdcSrc, displays.GetActiveDisplays, (IReadOnlyList<DisplayPathInfo>)[]);
        ctx.Report.Displays = paths.Select(p => Build(p, ctx)).ToList();
        return Task.CompletedTask;
    }

    internal static DisplayDevice Build(DisplayPathInfo p, CollectorContext? ctx = null)
    {
        var edid = EdidParser.Parse(p.Edid);
        if (p.Edid is null) ctx?.Warn("edid_not_found");
        else ctx?.UsedSource("EDID (registry)");

        string? code = edid?.ManufacturerCode ?? (p.EdidManufacturerId != 0 ? EdidParser.DecodePnpId(p.EdidManufacturerId) : null);
        var makerName = Vendors.MonitorManufacturer(code);
        string? model = FirmwareText.Clean(p.FriendlyName) ?? edid?.MonitorName;
        ushort product = edid?.ProductCode ?? p.EdidProductCode;

        double? hz = p.RefreshDenominator > 0 ? Math.Round((double)p.RefreshNumerator / p.RefreshDenominator, 3) : null;
        double? diag = edid?.ImageWidthMm is > 0 && edid.ImageHeightMm is > 0
            ? Math.Round(Math.Sqrt(Math.Pow(edid.ImageWidthMm.Value, 2) + Math.Pow(edid.ImageHeightMm.Value, 2)) / 25.4, 1)
            : null;

        return new DisplayDevice
        {
            Manufacturer = makerName is not null
                ? F.Detected<string?>(makerName, "PNP manufacturer id", "pnp_id_lookup")
                : F.Exact(code, "EDID manufacturer id"),
            ManufacturerCode = F.Exact(code, edid is not null ? "EDID" : NameSrc),
            Model = F.Exact(model, FirmwareText.Clean(p.FriendlyName) is not null ? NameSrc : "EDID monitor name descriptor"),
            ProductCode = product != 0 ? F.Exact<string?>(Fw.Hex(product, 4), edid is not null ? "EDID" : NameSrc) : F.Unavailable<string?>("EDID", F.Reasons.NotReported),
            Resolution = p.Width > 0 && p.Height > 0 ? F.Exact<Resolution?>(new Resolution(p.Width, p.Height), QdcSrc + " source mode") : F.Unavailable<Resolution?>(QdcSrc, F.Reasons.NotReported),
            NativeResolution = edid?.NativeWidth is > 0 && edid.NativeHeight is > 0
                ? F.Exact<Resolution?>(new Resolution(edid.NativeWidth.Value, edid.NativeHeight.Value), "EDID preferred timing")
                : F.Unavailable<Resolution?>("EDID", F.Reasons.NotReported),
            RefreshHz = F.Exact(hz, QdcSrc + " target refresh rate"),
            PhysicalWidthMm = F.Exact(edid?.ImageWidthMm, "EDID"),
            PhysicalHeightMm = F.Exact(edid?.ImageHeightMm, "EDID"),
            DiagonalIn = F.Detected(diag, "EDID image size", "computed_from_edid_size"),
            Hdr = F.Exact(p.AdvancedColorSupported, "DisplayConfig advanced color info"),
            HdrEnabled = F.Exact(p.AdvancedColorEnabled, "DisplayConfig advanced color info"),
            Connection = F.Exact(Connection(p.OutputTechnology), NameSrc + " output technology"),
            IsPrimary = F.Exact<bool?>(p.IsPrimary, QdcSrc + " source position"),
            ManufactureYear = F.Exact(edid?.ManufactureYear, "EDID"),
            Serial = F.Private<string?>(),
        };
    }

    internal static string? Connection(int tech) => tech switch
    {
        0 => "vga",
        1 => "s-video",
        2 => "composite",
        3 => "component",
        4 => "dvi",
        5 => "hdmi",
        6 => "lvds",
        8 => "d-jpn",
        9 => "sdi",
        10 => "displayport",
        11 => "edp",
        12 => "udi",
        13 => "udi-embedded",
        14 => "sdtv-dongle",
        15 => "miracast",
        16 => "indirect-wired",
        17 => "indirect-virtual",
        18 => "displayport-usb-tunnel",
        int.MinValue => "internal",
        _ => null,
    };
}
