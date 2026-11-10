import type { Field, Limitation } from './types'

/** StorageHealthReport v1.0 — produced by the Windows Agent's storage monitor (agent/…/Monitoring). */
export interface StorageHealthReport {
  schema_version: '1.0'
  kind: 'storage_health'
  collected_at: string
  storage_health: {
    devices: StorageDevice[]
    other_volumes: StorageVolume[]
    alerts: unknown[]
  }
  limitations: Limitation[]
}

export interface StorageDevice {
  id: string
  disk_number: Field<number>
  model: Field<string>
  manufacturer: Field<string>
  firmware_version: Field<string>
  /** nvme | sata_ssd | sata_hdd | sata | usb | sas | scsi | raid | storage_spaces | virtual | sd | unknown */
  type: Field<string>
  bus_type: Field<string>
  media_type: Field<string>
  capacity_bytes: Field<number>
  is_system: Field<boolean>
  health: {
    status: Field<string>
    os_status: Field<string>
    operational_status: Field<string[]>
    predict_failure: Field<boolean>
  }
  temperature: {
    current_celsius: Field<number>
    warning_celsius: Field<number>
    critical_celsius: Field<number>
    max_recorded_celsius: Field<number>
    sensors: { name: string; celsius: Field<number> }[]
  }
  smart: {
    interface: Field<string>
    percentage_used: Field<number>
    available_spare_percent: Field<number>
    available_spare_threshold_percent: Field<number>
    power_on_hours: Field<number>
    power_cycles: Field<number>
    unsafe_shutdowns: Field<number>
    media_errors: Field<number>
    data_read_bytes: Field<number>
    data_written_bytes: Field<number>
    reallocated_sectors: Field<number>
    pending_sectors: Field<number>
    uncorrectable_sectors: Field<number>
    crc_errors: Field<number>
    nvme: { critical_warning: Field<number>; critical_warning_flags: Field<string[]>; error_log_entries: Field<number> } | null
    ata: { attributes: SmartAttribute[] } | null
  }
  volumes: StorageVolume[]
}

export interface SmartAttribute {
  id: number
  name: string | null
  semantics: 'standard' | 'vendor_specific' | 'unknown'
  pre_failure: boolean
  normalized: Field<number>
  worst: Field<number>
  threshold: Field<number>
  raw: Field<number>
  value: Field<number>
}

export interface StorageVolume {
  id: string
  letter: Field<string>
  label: Field<string>
  filesystem: Field<string>
  drive_type: Field<string>
  total_bytes: Field<number>
  free_bytes: Field<number>
  used_bytes: Field<number>
  free_percent: Field<number>
  used_percent: Field<number>
  disk_numbers: Field<number[]>
  health_status: Field<string>
}

/** Free-space level for the fill bar. Default thresholds of the monitor spec (configurable later in the Health Engine). */
export type SpaceLevel = 'normal' | 'warning' | 'critical' | 'emergency'

export function spaceLevel(freePercent: number): SpaceLevel {
  if (freePercent < 5) return 'emergency'
  if (freePercent < 10) return 'critical'
  if (freePercent <= 20) return 'warning'
  return 'normal'
}
