using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using DeviceCheck.Agent.Core.Sources;
using Microsoft.Win32;

namespace DeviceCheck.Agent.Windows;

/// <summary>Raw SMBIOS table via GetSystemFirmwareTable('RSMB') — no admin rights needed.</summary>
[SupportedOSPlatform("windows")]
public sealed class SmbiosSource : ISmbiosSource
{
    private const uint RSMB = 0x52534D42;
    private SmbiosTable? _cache;
    private bool _read;

    public SmbiosTable? Read()
    {
        lock (this)
        {
            if (_read) return _cache;
            _read = true;
            uint size = Native.GetSystemFirmwareTable(RSMB, 0, null, 0);
            if (size == 0) return null;
            var buf = new byte[size];
            if (Native.GetSystemFirmwareTable(RSMB, 0, buf, size) != size || size < 8) return null;
            // RawSMBIOSData: Used20CallingMethod, Major, Minor, DmiRevision, DWORD Length, then table
            uint len = BitConverter.ToUInt32(buf, 4);
            len = Math.Min(len, size - 8);
            _cache = new SmbiosTable(buf[1], buf[2], buf.AsSpan(8, (int)len).ToArray());
            return _cache;
        }
    }
}

[SupportedOSPlatform("windows")]
public sealed class PlatformSource : IPlatformSource
{
    public string? FirmwareType() =>
        Native.GetFirmwareType(out var t) ? t switch { 1 => "bios", 2 => "uefi", _ => null } : null;

    public long UptimeMilliseconds() => Environment.TickCount64;

    public string OsArchitecture() => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "x86-64",
        Architecture.X86 => "x86",
        Architecture.Arm64 => "ARM64",
        Architecture.Arm => "ARM",
        var a => a.ToString(),
    };

    public object? CurrentVersionValue(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        return key?.GetValue(name);
    }

    public MemoryStatus GetMemoryStatus()
    {
        long? installed = Native.GetPhysicallyInstalledSystemMemory(out var kb) ? (long)kb * 1024 : null;
        var ms = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
        long? usable = Native.GlobalMemoryStatusEx(ref ms) ? (long)ms.ullTotalPhys : null;
        return new MemoryStatus(installed, usable);
    }

    public IReadOnlyList<VolumeData> GetVolumes()
    {
        var list = new List<VolumeData>();
        foreach (var d in DriveInfo.GetDrives())
        {
            if (d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
            try
            {
                if (!d.IsReady) continue;
                list.Add(new VolumeData(d.Name, d.DriveFormat, d.DriveType.ToString().ToLowerInvariant(), d.TotalSize, d.TotalFreeSpace));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return list;
    }

    public bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
