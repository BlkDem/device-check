using System.Text;

namespace DeviceCheck.Agent.Core.Parsing;

public sealed record EdidInfo(
    string ManufacturerCode,
    ushort ProductCode,
    int? ManufactureYear,
    string? MonitorName,
    int? NativeWidth,
    int? NativeHeight,
    int? ImageWidthMm,
    int? ImageHeightMm);

/// <summary>EDID 1.x base block parser (128 bytes). Serial numbers are intentionally not extracted.</summary>
public static class EdidParser
{
    private static readonly byte[] Header = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

    public static EdidInfo? Parse(byte[]? e)
    {
        if (e is null || e.Length < 128 || !e.AsSpan(0, 8).SequenceEqual(Header)) return null;

        int m = (e[8] << 8) | e[9];
        string code = new([(char)('A' - 1 + ((m >> 10) & 0x1F)), (char)('A' - 1 + ((m >> 5) & 0x1F)), (char)('A' - 1 + (m & 0x1F))]);
        ushort product = (ushort)(e[10] | (e[11] << 8));
        int? year = e[17] is > 0 and < 0xFF ? 1990 + e[17] : null;

        string? name = null;
        int? nw = null, nh = null, wmm = null, hmm = null;
        for (int d = 54; d <= 108; d += 18)
        {
            if (e[d] == 0 && e[d + 1] == 0)
            {
                // Display descriptor; 0xFC = monitor name (0xFF = serial: skipped on purpose)
                if (e[d + 3] == 0xFC) name = Encoding.ASCII.GetString(e, d + 5, 13).Split('\n')[0].Trim();
            }
            else if (d == 54)
            {
                // First detailed timing descriptor = preferred (native) mode
                nw = e[d + 2] | ((e[d + 4] & 0xF0) << 4);
                nh = e[d + 5] | ((e[d + 7] & 0xF0) << 4);
                int w = e[d + 12] | ((e[d + 14] & 0xF0) << 4);
                int h = e[d + 13] | ((e[d + 14] & 0x0F) << 8);
                if (w > 0 && h > 0) { wmm = w; hmm = h; }
            }
        }
        // Fallback: basic display parameters in cm
        if (wmm is null && e[21] > 0 && e[22] > 0) { wmm = e[21] * 10; hmm = e[22] * 10; }

        return new EdidInfo(code, product, year, string.IsNullOrEmpty(name) ? null : name, nw, nh, wmm, hmm);
    }

    /// <summary>Decodes the 16-bit PNP manufacturer id as reported by DisplayConfig (big-endian bytes swapped).</summary>
    public static string DecodePnpId(ushort displayConfigId)
    {
        int m = ((displayConfigId & 0xFF) << 8) | (displayConfigId >> 8);
        return new string([(char)('A' - 1 + ((m >> 10) & 0x1F)), (char)('A' - 1 + ((m >> 5) & 0x1F)), (char)('A' - 1 + (m & 0x1F))]);
    }
}
