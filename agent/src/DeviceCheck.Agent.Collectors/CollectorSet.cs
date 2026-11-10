using DeviceCheck.Agent.Core.Collection;
using DeviceCheck.Agent.Core.Sources;

namespace DeviceCheck.Agent.Collectors;

/// <summary>Builds the standard set of collectors from platform sources (composition helper).</summary>
public static class CollectorSet
{
    public static IReadOnlyList<IHardwareCollector> Create(
        IWmiClient wmi, ISmbiosSource smbios, IProcessorSource processor, IGraphicsSource graphics,
        IDisplaySource displays, IPlatformSource platform) =>
    [
        new SystemCollector(wmi, platform),
        new CpuCollector(wmi, processor),
        new MotherboardCollector(wmi, smbios, platform),
        new MemoryCollector(wmi, smbios, platform),
        new GpuCollector(wmi, graphics),
        new StorageCollector(wmi, platform),
        new DisplayCollector(displays),
    ];
}
