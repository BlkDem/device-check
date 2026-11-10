namespace DeviceCheck.Agent.Core.Normalization;

/// <summary>Windows Storage Management API enums (MSFT_PhysicalDisk / MSFT_Volume).</summary>
public static class StorageEnums
{
    public static string? MediaType(int? v) => v switch { 3 => "hdd", 4 => "ssd", 5 => "scm", _ => null };

    public static string? BusType(int? v) => v switch
    {
        1 => "scsi", 2 => "atapi", 3 => "ata", 4 => "ieee1394", 5 => "ssa", 6 => "fibre_channel", 7 => "usb", 8 => "raid",
        9 => "iscsi", 10 => "sas", 11 => "sata", 12 => "sd", 13 => "mmc", 14 => "virtual", 15 => "file_backed_virtual",
        16 => "storage_spaces", 17 => "nvme", 18 => "scm", 19 => "ufs",
        _ => null,
    };

    /// <summary>HealthStatus of MSFT_PhysicalDisk (0/1/2/5) and MSFT_Volume (0/1/2).</summary>
    public static string? Health(int? v) => v switch { 0 => "healthy", 1 => "warning", 2 => "unhealthy", 5 => "unknown", _ => null };

    /// <summary>MSFT_PhysicalDisk.OperationalStatus values.</summary>
    public static string OperationalStatus(long v) => v switch
    {
        0 => "unknown", 1 => "other", 2 => "ok", 3 => "degraded", 4 => "stressed", 5 => "predictive_failure", 6 => "error",
        7 => "non_recoverable_error", 8 => "starting", 9 => "stopping", 10 => "stopped", 11 => "in_service", 12 => "no_contact",
        13 => "lost_communication", 14 => "aborted", 15 => "dormant", 16 => "supporting_entity_in_error", 17 => "completed",
        0xD010 => "in_maintenance", 0xD011 => "transient_error", 0xD012 => "failed_media", 0xD013 => "no_redundancy",
        _ => $"code_{v}",
    };

    /// <summary>MSFT_Volume.DriveType.</summary>
    public static string? DriveType(int? v) => v switch { 1 => "no_root", 2 => "removable", 3 => "fixed", 4 => "remote", 5 => "cdrom", 6 => "ramdisk", _ => null };
}
