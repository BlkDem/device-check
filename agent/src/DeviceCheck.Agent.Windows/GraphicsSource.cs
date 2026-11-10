using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DeviceCheck.Agent.Core.Sources;

namespace DeviceCheck.Agent.Windows;

/// <summary>
/// Enumerates adapters through DXGI and asks D3D12 whether each one is UMA (integrated).
/// COM is called through raw vtable slots to avoid fragile interface declarations.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class GraphicsSource : IGraphicsSource
{
    private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid IID_ID3D12Device = new("189819f1-1db6-4b57-be54-1821339b85f7");
    private const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);
    private const int D3D_FEATURE_LEVEL_11_0 = 0xb000;

    // vtable slots
    private const int Release = 2;
    private const int Factory1_EnumAdapters1 = 12;   // IUnknown(3) + IDXGIObject(4) + IDXGIFactory(5)
    private const int Adapter1_GetDesc1 = 10;        // IUnknown(3) + IDXGIObject(4) + IDXGIAdapter(3)
    private const int Device_CheckFeatureSupport = 13; // IUnknown(3) + ID3D12Object(4) + GetNodeCount..CreateCommandList(6)

    [StructLayout(LayoutKind.Sequential)]
    private struct DXGI_ADAPTER_DESC1
    {
        public fixed char Description[128];
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public Native.LUID AdapterLuid;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D12_FEATURE_DATA_ARCHITECTURE { public uint NodeIndex; public int TileBasedRenderer; public int UMA; public int CacheCoherentUMA; }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D12_FEATURE_DATA_FEATURE_LEVELS { public uint NumFeatureLevels; public int* pFeatureLevelsRequested; public int MaxSupportedFeatureLevel; }

    private static void** Vtbl(IntPtr com) => *(void***)com;
    private static void ReleaseCom(IntPtr com)
    {
        if (com != IntPtr.Zero) ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Vtbl(com)[Release])(com);
    }

    public IReadOnlyList<GraphicsAdapterInfo> GetAdapters()
    {
        int hr = Native.CreateDXGIFactory1(IID_IDXGIFactory1, out var factory);
        if (hr < 0) throw new COMException("CreateDXGIFactory1 failed", hr);
        var list = new List<GraphicsAdapterInfo>();
        try
        {
            var enumAdapters = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Vtbl(factory)[Factory1_EnumAdapters1];
            for (uint i = 0; i < 16; i++)
            {
                IntPtr adapter;
                hr = enumAdapters(factory, i, &adapter);
                if (hr == DXGI_ERROR_NOT_FOUND) break;
                if (hr < 0) continue;
                try
                {
                    DXGI_ADAPTER_DESC1 d;
                    hr = ((delegate* unmanaged[Stdcall]<IntPtr, DXGI_ADAPTER_DESC1*, int>)Vtbl(adapter)[Adapter1_GetDesc1])(adapter, &d);
                    if (hr < 0) continue;
                    string desc = new string(d.Description).TrimEnd('\0').Trim();
                    bool software = (d.Flags & 2) != 0;
                    bool remote = (d.Flags & 1) != 0;
                    var (uma, fl) = software ? (null, null) : QueryD3D12(adapter);
                    list.Add(new GraphicsAdapterInfo(desc, d.VendorId, d.DeviceId, d.SubSysId, d.Revision,
                        d.DedicatedVideoMemory, d.DedicatedSystemMemory, d.SharedSystemMemory, software, remote, uma, fl));
                }
                finally { ReleaseCom(adapter); }
            }
        }
        finally { ReleaseCom(factory); }
        return list;
    }

    private static (bool? Uma, string? FeatureLevel) QueryD3D12(IntPtr adapter)
    {
        IntPtr device;
        try
        {
            if (Native.D3D12CreateDevice(adapter, D3D_FEATURE_LEVEL_11_0, IID_ID3D12Device, out device) < 0) return (null, null);
        }
        catch (DllNotFoundException) { return (null, null); }
        catch (EntryPointNotFoundException) { return (null, null); }

        try
        {
            var check = (delegate* unmanaged[Stdcall]<IntPtr, int, void*, uint, int>)Vtbl(device)[Device_CheckFeatureSupport];
            bool? uma = null;
            var arch = new D3D12_FEATURE_DATA_ARCHITECTURE { NodeIndex = 0 };
            if (check(device, 1 /* ARCHITECTURE */, &arch, (uint)sizeof(D3D12_FEATURE_DATA_ARCHITECTURE)) >= 0) uma = arch.UMA != 0;

            string? fl = null;
            int* levels = stackalloc int[] { 0xb000, 0xb100, 0xc000, 0xc100, 0xc200 };
            var fls = new D3D12_FEATURE_DATA_FEATURE_LEVELS { NumFeatureLevels = 5, pFeatureLevelsRequested = levels };
            if (check(device, 2 /* FEATURE_LEVELS */, &fls, (uint)sizeof(D3D12_FEATURE_DATA_FEATURE_LEVELS)) >= 0)
                fl = $"{(fls.MaxSupportedFeatureLevel >> 12) & 0xF}_{(fls.MaxSupportedFeatureLevel >> 8) & 0xF}";
            return (uma, fl);
        }
        finally { ReleaseCom(device); }
    }
}
