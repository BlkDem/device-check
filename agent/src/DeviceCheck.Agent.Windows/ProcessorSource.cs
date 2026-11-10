using System.Numerics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Versioning;
using System.Text;
using DeviceCheck.Agent.Core.Sources;

namespace DeviceCheck.Agent.Windows;

[SupportedOSPlatform("windows")]
public sealed class ProcessorSource : IProcessorSource
{
    private const int RelationProcessorCore = 0;
    private const int RelationCache = 2;
    private const int RelationProcessorPackage = 3;
    private const int RelationAll = 0xFFFF;

    /// <summary>Cores, threads, packages, hybrid efficiency classes and caches from GetLogicalProcessorInformationEx.</summary>
    public ProcessorTopology? GetTopology()
    {
        uint len = 0;
        Native.GetLogicalProcessorInformationEx(RelationAll, null, ref len);
        if (len == 0) return null;
        var buf = new byte[len];
        if (!Native.GetLogicalProcessorInformationEx(RelationAll, buf, ref len)) return null;

        int packages = 0, cores = 0, logical = 0;
        var classes = new List<int>();
        var caches = new List<CacheInfo>();
        int off = 0;
        while (off + 8 <= len)
        {
            int rel = BitConverter.ToInt32(buf, off);
            int size = BitConverter.ToInt32(buf, off + 4);
            if (size <= 0) break;
            int body = off + 8;
            switch (rel)
            {
                case RelationProcessorCore:
                {
                    cores++;
                    classes.Add(buf[body + 1]); // EfficiencyClass
                    // PROCESSOR_RELATIONSHIP: Flags(1) EfficiencyClass(1) Reserved(20) GroupCount(2) GroupMask[GroupCount](16 each)
                    int groupCount = BitConverter.ToUInt16(buf, body + 22);
                    for (int g = 0; g < groupCount; g++)
                        logical += BitOperations.PopCount(BitConverter.ToUInt64(buf, body + 24 + g * 16));
                    break;
                }
                case RelationProcessorPackage:
                    packages++;
                    break;
                case RelationCache:
                {
                    // CACHE_RELATIONSHIP: Level(1) Associativity(1) LineSize(2) CacheSize(4) Type(4)
                    int level = buf[body];
                    uint cacheSize = BitConverter.ToUInt32(buf, body + 4);
                    int type = BitConverter.ToInt32(buf, body + 8);
                    string t = type switch { 0 => "unified", 1 => "instruction", 2 => "data", 3 => "trace", _ => "unknown" };
                    caches.Add(new CacheInfo(level, t, cacheSize));
                    break;
                }
            }
            off += size;
        }
        return cores == 0 ? null : new ProcessorTopology(Math.Max(packages, 1), cores, logical, classes, caches);
    }

    /// <summary>Direct CPUID (x86/x64). Hardware capability flags, not OS-enabled state.</summary>
    public CpuidInfo? GetCpuid()
    {
        if (!X86Base.IsSupported) return null;

        var (maxLeaf, b0, c0, d0) = X86Base.CpuId(0, 0);
        var vendorBytes = new byte[12];
        BitConverter.GetBytes(b0).CopyTo(vendorBytes, 0);
        BitConverter.GetBytes(d0).CopyTo(vendorBytes, 4);
        BitConverter.GetBytes(c0).CopyTo(vendorBytes, 8);
        string vendor = Encoding.ASCII.GetString(vendorBytes);

        var (eax1, _, ecx1, edx1) = X86Base.CpuId(1, 0);
        int baseFamily = (eax1 >> 8) & 0xF, baseModel = (eax1 >> 4) & 0xF;
        int family = baseFamily == 0xF ? baseFamily + ((eax1 >> 20) & 0xFF) : baseFamily;
        int model = baseFamily is 0x6 or 0xF ? baseModel | (((eax1 >> 16) & 0xF) << 4) : baseModel;
        int stepping = eax1 & 0xF;

        uint maxExt = (uint)X86Base.CpuId(unchecked((int)0x80000000), 0).Eax;
        string? brand = null;
        if (maxExt >= 0x80000004)
        {
            var sb = new StringBuilder();
            for (uint leaf = 0x80000002; leaf <= 0x80000004; leaf++)
            {
                var (a, b, c, d) = X86Base.CpuId(unchecked((int)leaf), 0);
                foreach (var r in new[] { a, b, c, d }) sb.Append(Encoding.ASCII.GetString(BitConverter.GetBytes(r)));
            }
            brand = sb.ToString().TrimEnd('\0').Trim();
        }

        var f = new List<string>();
        void Add(bool on, string name) { if (on) f.Add(name); }
        static bool Bit(int reg, int bit) => (reg & (1 << bit)) != 0;

        Add(Bit(edx1, 25), "SSE"); Add(Bit(edx1, 26), "SSE2"); Add(Bit(ecx1, 0), "SSE3"); Add(Bit(ecx1, 9), "SSSE3");
        Add(Bit(ecx1, 19), "SSE4.1"); Add(Bit(ecx1, 20), "SSE4.2"); Add(Bit(ecx1, 23), "POPCNT"); Add(Bit(ecx1, 25), "AES-NI");
        Add(Bit(ecx1, 1), "PCLMULQDQ"); Add(Bit(ecx1, 28), "AVX"); Add(Bit(ecx1, 12), "FMA3"); Add(Bit(ecx1, 29), "F16C");
        Add(Bit(ecx1, 22), "MOVBE"); Add(Bit(ecx1, 30), "RDRAND"); Add(Bit(ecx1, 5), "VT-x");

        bool hybrid = false;
        if (maxLeaf >= 7)
        {
            var (_, b7, c7, d7) = X86Base.CpuId(7, 0);
            Add(Bit(b7, 3), "BMI1"); Add(Bit(b7, 5), "AVX2"); Add(Bit(b7, 8), "BMI2"); Add(Bit(b7, 18), "RDSEED"); Add(Bit(b7, 19), "ADX");
            Add(Bit(b7, 29), "SHA"); Add(Bit(b7, 16), "AVX-512F"); Add(Bit(b7, 17), "AVX-512DQ"); Add(Bit(b7, 28), "AVX-512CD");
            Add(Bit(b7, 30), "AVX-512BW"); Add(Bit(b7, 31), "AVX-512VL"); Add(Bit(c7, 1), "AVX-512VBMI"); Add(Bit(c7, 11), "AVX-512VNNI");
            Add(Bit(c7, 8), "GFNI"); Add(Bit(c7, 9), "VAES"); Add(Bit(c7, 10), "VPCLMULQDQ");
            hybrid = Bit(d7, 15);
            var (a71, _, _, _) = X86Base.CpuId(7, 1);
            Add(Bit(a71, 4), "AVX-VNNI");
        }
        if (maxExt >= 0x80000001)
        {
            var (_, _, ce, de) = X86Base.CpuId(unchecked((int)0x80000001), 0);
            Add(Bit(de, 29), "x86-64"); Add(Bit(de, 20), "NX"); Add(Bit(ce, 5), "LZCNT"); Add(Bit(ce, 6), "SSE4a"); Add(Bit(ce, 2), "AMD-V");
        }

        bool hypervisor = Bit(ecx1, 31);
        return new CpuidInfo(vendor, brand, family, model, stepping, f, hypervisor, hybrid);
    }
}
