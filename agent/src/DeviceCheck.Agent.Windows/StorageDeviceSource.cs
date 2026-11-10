using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DeviceCheck.Agent.Monitoring.Core;
using Microsoft.Win32.SafeHandles;

namespace DeviceCheck.Agent.Windows;

/// <summary>
/// Read-only queries on \\.\PhysicalDriveN via IOCTL_STORAGE_QUERY_PROPERTY. The handle is opened with zero access
/// rights (query only: no reads or writes of disk data are possible through it), which Windows allows for standard
/// users on most drivers. Only "query" property types are used; nothing is ever sent that changes the device.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StorageDeviceSource : IStorageDeviceSource
{
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

    // STORAGE_PROPERTY_ID
    private const int StorageDeviceProtocolSpecificProperty = 50;
    private const int StorageAdapterTemperatureProperty = 51;
    private const int StorageDeviceTemperatureProperty = 52;

    // STORAGE_PROTOCOL_TYPE / STORAGE_PROTOCOL_NVME_DATA_TYPE
    private const int ProtocolTypeNvme = 3;
    private const int NVMeDataTypeIdentify = 1;
    private const int NVMeDataTypeLogPage = 2;
    private const int NvmeLogPageHealthInfo = 0x02;
    private const int NvmeIdentifyCnsController = 0x01;

    private const int QueryHeader = 8;            // STORAGE_PROPERTY_QUERY: PropertyId, QueryType
    private const int ProtocolSpecificData = 40;  // STORAGE_PROTOCOL_SPECIFIC_DATA

    public byte[] NvmeHealthLog(int diskNumber) =>
        NvmeQuery(diskNumber, NVMeDataTypeLogPage, NvmeLogPageHealthInfo, 512);

    public byte[] NvmeIdentifyController(int diskNumber) =>
        NvmeQuery(diskNumber, NVMeDataTypeIdentify, NvmeIdentifyCnsController, 4096);

    public DeviceTemperatureData? DeviceTemperature(int diskNumber)
    {
        try
        {
            return Temperature(diskNumber, StorageDeviceTemperatureProperty);
        }
        catch (StorageAccessException e) when (e.Reason == DeviceCheck.Agent.Core.Model.F.Reasons.Unsupported)
        {
            // Some miniports (e.g. stornvme on a few drives) answer only the adapter-level property.
            return Temperature(diskNumber, StorageAdapterTemperatureProperty);
        }
    }

    private static DeviceTemperatureData? Temperature(int diskNumber, int propertyId)
    {
        var query = new byte[12]; // sizeof(STORAGE_PROPERTY_QUERY): PropertyId, QueryType, AdditionalParameters[1] + padding
        BinaryPrimitives.WriteInt32LittleEndian(query, propertyId);
        var output = new byte[1024];
        int returned = Query(diskNumber, query, output);

        // STORAGE_TEMPERATURE_DATA_DESCRIPTOR: Version, Size, CriticalTemperature (SHORT), WarningTemperature (SHORT),
        // InfoCount (USHORT), Reserved[2], Reserved[2] (ULONG) → STORAGE_TEMPERATURE_INFO[] at 24, 16 bytes each.
        if (returned < 24) return null;
        short critical = BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(8));
        short warning = BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(10));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(12));
        int? current = null;
        for (int i = 0; i < count && 24 + i * 16 + 4 <= returned; i++)
        {
            int index = BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(24 + i * 16));
            short t = BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(24 + i * 16 + 2));
            if (index == 0 && Plausible(t)) current = t; // index 0 = the device (composite) temperature
        }
        return new DeviceTemperatureData(current, Plausible(warning) ? warning : null, Plausible(critical) ? critical : null);
    }

    private static bool Plausible(short celsius) => celsius is > -40 and < 150;

    private static byte[] NvmeQuery(int diskNumber, int dataType, int requestValue, int length)
    {
        var buf = new byte[QueryHeader + ProtocolSpecificData + length];
        var s = buf.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(s, StorageDeviceProtocolSpecificProperty);
        BinaryPrimitives.WriteInt32LittleEndian(s[4..], 0);                     // PropertyStandardQuery
        BinaryPrimitives.WriteInt32LittleEndian(s[8..], ProtocolTypeNvme);
        BinaryPrimitives.WriteInt32LittleEndian(s[12..], dataType);
        BinaryPrimitives.WriteInt32LittleEndian(s[16..], requestValue);          // log page id / CNS
        BinaryPrimitives.WriteInt32LittleEndian(s[20..], 0);                     // namespace / sub value
        BinaryPrimitives.WriteInt32LittleEndian(s[24..], ProtocolSpecificData);  // data offset (from the protocol data struct)
        BinaryPrimitives.WriteInt32LittleEndian(s[28..], length);

        int returned = Query(diskNumber, buf, buf);

        // Output: STORAGE_PROTOCOL_DATA_DESCRIPTOR = Version, Size, STORAGE_PROTOCOL_SPECIFIC_DATA (same layout as above).
        int offset = BinaryPrimitives.ReadInt32LittleEndian(s[(8 + 16)..]);
        int dataLength = BinaryPrimitives.ReadInt32LittleEndian(s[(8 + 20)..]);
        int start = 8 + offset;
        if (offset <= 0 || dataLength < length || start + length > returned)
            throw new StorageAccessException(DeviceCheck.Agent.Core.Model.F.Reasons.NotReported, $"NVMe query returned {returned} bytes (offset {offset}, length {dataLength})");
        return buf.AsSpan(start, length).ToArray();
    }

    private static int Query(int diskNumber, byte[] input, byte[] output)
    {
        using var handle = Open(diskNumber);
        if (!DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY, input, input.Length, output, output.Length, out int returned, 0))
        {
            int err = Marshal.GetLastPInvokeError();
            throw new StorageAccessException(StorageAccessException.ReasonFor(err), $"IOCTL_STORAGE_QUERY_PROPERTY on disk {diskNumber} failed: Win32 error {err}", err);
        }
        return returned;
    }

    private static SafeFileHandle Open(int diskNumber)
    {
        const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
        var h = CreateFileW($@"\\.\PhysicalDrive{diskNumber}", 0, FILE_SHARE_READ | FILE_SHARE_WRITE, 0, OPEN_EXISTING, 0, 0);
        if (h.IsInvalid)
        {
            int err = Marshal.GetLastPInvokeError();
            h.Dispose();
            throw new StorageAccessException(StorageAccessException.ReasonFor(err), $"cannot open \\\\.\\PhysicalDrive{diskNumber}: Win32 error {err}", err);
        }
        return h;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] inBuffer, int inSize, byte[] outBuffer, int outSize, out int returned, nint overlapped);
}
