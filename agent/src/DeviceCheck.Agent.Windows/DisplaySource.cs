using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DeviceCheck.Agent.Core.Sources;
using Microsoft.Win32;

namespace DeviceCheck.Agent.Windows;

/// <summary>Active displays via QueryDisplayConfig + DisplayConfigGetDeviceInfo; EDID from the device registry key (readable by users).</summary>
[SupportedOSPlatform("windows")]
public sealed class DisplaySource : IDisplaySource
{
    public IReadOnlyList<DisplayPathInfo> GetActiveDisplays()
    {
        int err = Native.GetDisplayConfigBufferSizes(Native.QDC_ONLY_ACTIVE_PATHS, out var nPaths, out var nModes);
        if (err != 0) throw new InvalidOperationException($"GetDisplayConfigBufferSizes failed: {err}");
        var paths = new Native.DISPLAYCONFIG_PATH_INFO[nPaths];
        var modes = new Native.DISPLAYCONFIG_MODE_INFO[nModes];
        err = Native.QueryDisplayConfig(Native.QDC_ONLY_ACTIVE_PATHS, ref nPaths, paths, ref nModes, modes, IntPtr.Zero);
        if (err != 0) throw new InvalidOperationException($"QueryDisplayConfig failed: {err}");

        var list = new List<DisplayPathInfo>();
        for (int i = 0; i < nPaths; i++)
        {
            var p = paths[i];
            int w = 0, h = 0;
            bool primary = false;
            uint si = p.sourceInfo.modeInfoIdx;
            if (si < nModes && modes[si].infoType == 1)
            {
                w = (int)modes[si].sourceWidth;
                h = (int)modes[si].sourceHeight;
                primary = modes[si].sourcePositionX == 0 && modes[si].sourcePositionY == 0;
            }

            var name = new Native.DISPLAYCONFIG_TARGET_DEVICE_NAME
            {
                header = new Native.DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = Native.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                    size = (uint)Marshal.SizeOf<Native.DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                    adapterId = p.targetInfo.adapterId,
                    id = p.targetInfo.id,
                },
                monitorFriendlyDeviceName = "",
                monitorDevicePath = "",
            };
            bool haveName = Native.DisplayConfigGetDeviceInfo(ref name) == 0;

            var color = new Native.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
            {
                header = new Native.DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = Native.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                    size = (uint)Marshal.SizeOf<Native.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(),
                    adapterId = p.targetInfo.adapterId,
                    id = p.targetInfo.id,
                },
            };
            bool haveColor = Native.DisplayConfigGetDeviceInfo(ref color) == 0;

            list.Add(new DisplayPathInfo(
                w, h,
                p.targetInfo.refreshRate.Numerator, p.targetInfo.refreshRate.Denominator,
                haveName ? name.outputTechnology : p.targetInfo.outputTechnology,
                haveName ? name.monitorFriendlyDeviceName : null,
                haveName ? name.edidManufactureId : (ushort)0,
                haveName ? name.edidProductCodeId : (ushort)0,
                haveColor ? (color.value & 1) != 0 : null,
                haveColor ? (color.value & 2) != 0 : null,
                primary,
                haveName ? ReadEdid(name.monitorDevicePath) : null));
        }
        return list;
    }

    /// <summary>"\\?\DISPLAY#BOE0A1C#4&amp;2f5d&amp;0&amp;UID8388688#{guid}" → HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\BOE0A1C\4&amp;...\Device Parameters\EDID</summary>
    internal static byte[]? ReadEdid(string? devicePath)
    {
        if (string.IsNullOrEmpty(devicePath)) return null;
        var parts = devicePath.Split('#');
        if (parts.Length < 3) return null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{parts[2]}\Device Parameters");
            return key?.GetValue("EDID") as byte[];
        }
        catch (System.Security.SecurityException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
