using System.Globalization;

namespace DeviceCheck.Agent.Core.Parsing;

/// <summary>Parses CIM_DATETIME strings ("yyyyMMddHHmmss.ffffff+UUU", UUU = UTC offset in minutes).</summary>
public static class CimDate
{
    public static DateTimeOffset? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length < 14) return null;
        if (!DateTime.TryParseExact(s[..14], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return null;
        var offset = TimeSpan.Zero;
        if (s.Length >= 25 && int.TryParse(s.AsSpan(21, 4), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var min))
            offset = TimeSpan.FromMinutes(min);
        try { return new DateTimeOffset(dt, offset); } catch { return null; }
    }

    /// <summary>SMBIOS BIOS date "MM/dd/yyyy" (or "MM/dd/yy") → ISO yyyy-MM-dd.</summary>
    public static string? BiosDateToIso(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        string[] formats = ["MM/dd/yyyy", "M/d/yyyy", "MM/dd/yy"];
        return DateTime.TryParseExact(s.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }
}
