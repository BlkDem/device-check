using System.Text.RegularExpressions;

namespace DeviceCheck.Agent.Core.Normalization;

/// <summary>Microarchitecture reference entry. Only well-documented CPUID family/model pairs are listed.</summary>
public sealed record CpuArch(string Microarchitecture, string? Codename, string? ProcessNode, int? ProcessNm);

public static partial class CpuIdentification
{
    public static string? NormalizeVendor(string? cpuidVendor) => cpuidVendor switch
    {
        "GenuineIntel" => "Intel",
        "AuthenticAMD" => "AMD",
        "HygonGenuine" => "Hygon",
        "CentaurHauls" or "  Shanghai  " => "Zhaoxin",
        null => null,
        _ => cpuidVendor.Trim(),
    };

    public static string? NormalizeBrand(string? brand)
    {
        if (string.IsNullOrWhiteSpace(brand)) return null;
        var s = Regex.Replace(brand, @"\((R|TM|tm|r)\)", "");
        s = Regex.Replace(s, @"\s+(CPU|Processor)\b", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s+\d+-Core\s*$", "", RegexOptions.IgnoreCase); // "AMD Ryzen 7 7840HS w/ Radeon 780M Graphics" keeps graphics
        s = Regex.Replace(s, @"\s{2,}", " ").Trim();
        return s.Length == 0 ? null : s;
    }

    /// <summary>Microarchitecture by CPUID display family / model. Returns null when not in the table — never guesses.</summary>
    public static CpuArch? Lookup(string vendor, int family, int model)
    {
        if (vendor == "Intel" && family == 6)
        {
            return model switch
            {
                0x2A or 0x2D => new("Sandy Bridge", "Sandy Bridge", "Intel 32 nm", 32),
                0x3A or 0x3E => new("Ivy Bridge", "Ivy Bridge", "Intel 22 nm", 22),
                0x3C or 0x3F or 0x45 or 0x46 => new("Haswell", "Haswell", "Intel 22 nm", 22),
                0x3D or 0x47 or 0x4F or 0x56 => new("Broadwell", "Broadwell", "Intel 14 nm", 14),
                0x4E or 0x5E => new("Skylake", "Skylake", "Intel 14 nm", 14),
                0x55 => new("Skylake / Cascade Lake (server)", null, "Intel 14 nm", 14),
                0x8E or 0x9E => new("Skylake (Kaby / Coffee / Whiskey Lake)", null, "Intel 14 nm", 14),
                0xA5 or 0xA6 => new("Skylake (Comet Lake)", "Comet Lake", "Intel 14 nm", 14),
                0x66 => new("Palm Cove", "Cannon Lake", "Intel 10 nm", 10),
                0x7D or 0x7E => new("Sunny Cove", "Ice Lake", "Intel 10 nm", 10),
                0x6A or 0x6C => new("Sunny Cove", "Ice Lake-SP", "Intel 10 nm", 10),
                0xA7 => new("Cypress Cove", "Rocket Lake", "Intel 14 nm", 14),
                0x8C or 0x8D => new("Willow Cove", "Tiger Lake", "Intel 10 nm SuperFin", 10),
                0x97 or 0x9A or 0xBE => new("Golden Cove + Gracemont", "Alder Lake", "Intel 7", null),
                0xB7 or 0xBA or 0xBF => new("Raptor Cove + Gracemont", "Raptor Lake", "Intel 7", null),
                0x8F => new("Golden Cove", "Sapphire Rapids", "Intel 7", null),
                0xCF => new("Raptor Cove", "Emerald Rapids", "Intel 7", null),
                0xAA or 0xAC => new("Redwood Cove + Crestmont", "Meteor Lake", "Intel 4 (compute tile)", null),
                0xBD => new("Lion Cove + Skymont", "Lunar Lake", "TSMC N3B (compute tile)", 3),
                0xC5 or 0xC6 => new("Lion Cove + Skymont", "Arrow Lake", "TSMC N3B (compute tile)", 3),
                0x5C or 0x5F => new("Goldmont", "Apollo Lake / Denverton", "Intel 14 nm", 14),
                0x7A => new("Goldmont Plus", "Gemini Lake", "Intel 14 nm", 14),
                0x9C or 0x96 => new("Tremont", "Jasper / Elkhart Lake", "Intel 10 nm", 10),
                _ => null,
            };
        }

        if (vendor is "AMD" or "Hygon")
        {
            return (family, model) switch
            {
                (0x17, 0x01) or (0x17, 0x11) => new("Zen", model == 0x11 ? "Raven Ridge" : "Summit Ridge", "GlobalFoundries 14 nm", 14),
                (0x17, 0x08) or (0x17, 0x18) => new("Zen+", model == 0x18 ? "Picasso" : "Pinnacle Ridge", "GlobalFoundries 12 nm", 12),
                (0x17, 0x31) => new("Zen 2", "Rome / Castle Peak", "TSMC N7 (CCD)", 7),
                (0x17, 0x60) => new("Zen 2", "Renoir", "TSMC N7", 7),
                (0x17, 0x68) => new("Zen 2", "Lucienne", "TSMC N7", 7),
                (0x17, 0x71) => new("Zen 2", "Matisse", "TSMC N7 (CCD)", 7),
                (0x17, 0x90) => new("Zen 2", "Van Gogh", "TSMC N7", 7),
                (0x17, 0xA0) => new("Zen 2", "Mendocino", "TSMC N6", 6),
                (0x19, 0x01) => new("Zen 3", "Milan", "TSMC N7 (CCD)", 7),
                (0x19, 0x21) => new("Zen 3", "Vermeer", "TSMC N7 (CCD)", 7),
                (0x19, 0x44) => new("Zen 3+", "Rembrandt", "TSMC N6", 6),
                (0x19, 0x50) => new("Zen 3", "Cezanne / Barcelo", "TSMC N7", 7),
                (0x19, 0x11) => new("Zen 4", "Genoa", "TSMC N5 (CCD)", 5),
                (0x19, 0x61) => new("Zen 4", "Raphael", "TSMC N5 (CCD)", 5),
                (0x19, 0x74) or (0x19, 0x75) => new("Zen 4", "Phoenix", "TSMC N4", 4),
                (0x19, 0x78) => new("Zen 4 / Zen 4c", "Phoenix 2", "TSMC N4", 4),
                (0x19, 0x7C) => new("Zen 4", "Hawk Point", "TSMC N4", 4),
                (0x1A, 0x24) => new("Zen 5 / Zen 5c", "Strix Point", "TSMC N4P", 4),
                (0x1A, 0x44) => new("Zen 5", "Granite Ridge", "TSMC N4P (CCD)", 4),
                (0x1A, 0x60) => new("Zen 5 / Zen 5c", "Krackan Point", "TSMC N4P", 4),
                (0x1A, 0x70) => new("Zen 5", "Strix Halo", "TSMC N4P (CCD)", 4),
                _ => null,
            };
        }
        return null;
    }

    /// <summary>Marketing generation parsed from the brand string, e.g. "12th Gen", "Core Ultra Series 2", "Ryzen 7000 Series".</summary>
    public static string? Generation(string? brand)
    {
        if (string.IsNullOrWhiteSpace(brand)) return null;

        var ultra = CoreUltra().Match(brand);
        if (ultra.Success) return $"Core Ultra Series {ultra.Groups[1].Value[0]}";

        var core = IntelCore().Match(brand);
        if (core.Success)
        {
            var num = core.Groups[1].Value;
            // 4-digit = gen 2..9 (first digit) or mobile 10..13 starting with "1" (i7-1165G7); 5-digit = 10..14 (first two digits)
            var gen = num.Length == 5 || num[0] == '1' ? int.Parse(num[..2]) : int.Parse(num[..1]);
            return gen is >= 2 and <= 14 ? $"{gen}th Gen Intel Core" : null;
        }

        var ryzen = Ryzen().Match(brand);
        if (ryzen.Success)
        {
            var model = ryzen.Groups[1].Value;
            return model.Length == 4 ? $"Ryzen {model[0]}000 Series" : null;
        }
        var ryzenAi = RyzenAi().Match(brand);
        if (ryzenAi.Success) return $"Ryzen AI {ryzenAi.Groups[1].Value}00 Series";
        return null;
    }

    [GeneratedRegex(@"Core\s*(?:\(TM\)\s*)?Ultra\s+\d\s+(\d)\d{2}", RegexOptions.IgnoreCase)]
    private static partial Regex CoreUltra();

    [GeneratedRegex(@"\bi[3579]-(\d{4,5})", RegexOptions.IgnoreCase)]
    private static partial Regex IntelCore();

    [GeneratedRegex(@"Ryzen\s+(?:Threadripper\s+)?(?:PRO\s+)?\d\s+(?:PRO\s+)?(\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex Ryzen();

    [GeneratedRegex(@"Ryzen\s+AI\s+(?:MAX\+?\s+|\d\s+)?(?:PRO\s+)?(?:HX\s+)?(\d)\d{2}", RegexOptions.IgnoreCase)]
    private static partial Regex RyzenAi();
}
