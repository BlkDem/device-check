using System.Runtime.InteropServices;

namespace DeviceCheck.Agent.Windows;

internal static partial class Native
{
    // ---------------------------------------------------------------- kernel32

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint GetSystemFirmwareTable(uint provider, uint tableId, byte[]? buffer, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetFirmwareType(out int firmwareType);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetLogicalProcessorInformationEx(int relationship, byte[]? buffer, ref uint length);

    // ---------------------------------------------------------------- user32 (display config)

    internal const uint QDC_ONLY_ACTIVE_PATHS = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    internal struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public int outputTechnology;
        public int rotation;
        public int scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public int scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    /// <summary>DISPLAYCONFIG_MODE_INFO (64 bytes); the union is read through explicit offsets.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    internal struct DISPLAYCONFIG_MODE_INFO
    {
        [FieldOffset(0)] public int infoType; // 1 = source, 2 = target
        [FieldOffset(4)] public uint id;
        [FieldOffset(8)] public LUID adapterId;
        // source mode
        [FieldOffset(16)] public uint sourceWidth;
        [FieldOffset(20)] public uint sourceHeight;
        [FieldOffset(24)] public int sourcePixelFormat;
        [FieldOffset(28)] public int sourcePositionX;
        [FieldOffset(32)] public int sourcePositionY;
    }

    [LibraryImport("user32.dll")]
    internal static partial int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [LibraryImport("user32.dll")]
    internal static partial int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public int type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public int outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value; // bit0 supported, bit1 enabled
        public int colorEncoding;
        public uint bitsPerColorChannel;
    }

    internal const int DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;
    internal const int DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket);

    // ---------------------------------------------------------------- DXGI / D3D12

    [LibraryImport("dxgi.dll")]
    internal static partial int CreateDXGIFactory1(in Guid riid, out IntPtr factory);

    [LibraryImport("d3d12.dll")]
    internal static partial int D3D12CreateDevice(IntPtr adapter, int minimumFeatureLevel, in Guid riid, out IntPtr device);
}
