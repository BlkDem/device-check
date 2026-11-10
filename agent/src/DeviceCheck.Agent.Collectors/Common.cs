using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Core.Normalization;

namespace DeviceCheck.Agent.Collectors;

internal static class Ns
{
    public const string Cimv2 = @"root\cimv2";
    public const string Storage = @"root\Microsoft\Windows\Storage";
}

internal static class Fw
{
    /// <summary>
    /// Firmware string field: prefers raw SMBIOS, falls back to WMI. OEM placeholders
    /// ("To be filled by O.E.M.", "Default string") become unavailable with reason firmware_placeholder.
    /// </summary>
    public static Field<string?> Str(string? primary, string primarySource, string? fallback = null, string? fallbackSource = null)
    {
        var p = FirmwareText.Clean(primary);
        if (p is not null) return F.Exact<string?>(p, primarySource);
        var f = FirmwareText.Clean(fallback);
        if (f is not null && fallbackSource is not null) return F.Exact<string?>(f, fallbackSource);
        bool placeholder = FirmwareText.IsPlaceholder(primary) || FirmwareText.IsPlaceholder(fallback);
        return F.Unavailable<string?>(primary is not null ? primarySource : fallbackSource ?? primarySource,
            placeholder ? F.Reasons.FirmwarePlaceholder : F.Reasons.NotReported);
    }

    public static string Hex(uint v, int digits) => "0x" + v.ToString($"X{digits}");
}
