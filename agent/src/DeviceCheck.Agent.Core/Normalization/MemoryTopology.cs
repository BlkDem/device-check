using System.Text.RegularExpressions;

namespace DeviceCheck.Agent.Core.Normalization;

public sealed record ChannelResult(string Mode, int? BusWidthBits, int ChannelCount, string Basis);

/// <summary>
/// Memory channel detection. Never infers the mode from module count alone:
/// it requires explicit channel labels in SMBIOS locators. Otherwise returns "unknown".
/// </summary>
public static partial class MemoryTopology
{
    /// <summary>Extracts a channel identifier ("A", "B", "0", ...) from SMBIOS device/bank locators.</summary>
    public static string? Channel(string? deviceLocator, string? bankLocator)
    {
        foreach (var s in new[] { bankLocator, deviceLocator })
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            var m = ChannelRx().Match(s);
            if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
            // Common desktop locator: "DIMM_A1", "DIMM_B2", "ChannelA-DIMM0", "A1_DIMM0"
            m = DimmLetterRx().Match(s);
            if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
        }
        return null;
    }

    /// <param name="populated">Populated devices: (channel label, data width in bits).</param>
    public static ChannelResult Detect(IReadOnlyList<(string? Channel, int? DataWidth)> populated)
    {
        if (populated.Count == 0) return new("unknown", null, 0, "no_modules");
        if (populated.Any(p => p.Channel is null)) return new("unknown", null, 0, "no_channel_labels");

        var channels = populated.GroupBy(p => p.Channel!).ToList();
        int count = channels.Count;

        int? bus = null;
        if (populated.All(p => p.DataWidth is > 0))
            bus = channels.Sum(g => g.Max(p => p.DataWidth!.Value));

        // Wide bus by narrow LPDDR channels (e.g. 8 × 16-bit) is classified by total width.
        string mode;
        if (bus is not null && populated.All(p => p.DataWidth < 64))
            mode = bus switch { <= 64 => "single", <= 128 => "dual", <= 256 => "quad", _ => "unknown" };
        else
            mode = count switch { 1 => "single", 2 => "dual", 4 => "quad", _ => "unknown" };

        return new(mode, bus, count, "smbios_locators");
    }

    [GeneratedRegex(@"CHANNEL\s*([A-H0-9])\b", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelRx();

    [GeneratedRegex(@"(?:DIMM|CH)[\s_\-]*([A-H])\d", RegexOptions.IgnoreCase)]
    private static partial Regex DimmLetterRx();
}
