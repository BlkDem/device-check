import type { Fmt } from './format'

/** Display definition of a single field: key in the report section + formatter. */
export interface FieldDef {
  key: string
  fmt: Fmt
}

const d = (key: string, fmt: Fmt = 'text'): FieldDef => ({ key, fmt })

export const CPU_FIELDS: FieldDef[] = [
  d('vendor'), d('model'), d('family'), d('architecture'), d('cores_physical', 'int'), d('threads_logical', 'int'),
  d('base_clock_mhz', 'mhz'), d('max_clock_mhz', 'mhz'), d('current_clock_mhz', 'mhz'),
  d('cache_l1_kb', 'kb'), d('cache_l2_kb', 'kb'), d('cache_l3_kb', 'kb'), d('instructions', 'list'), d('tdp_w', 'w'), d('process_nm', 'nm'),
]

export const BOARD_FIELDS: FieldDef[] = [
  d('manufacturer'), d('model'), d('version'), d('chipset'), d('firmware_type'), d('bios_vendor'), d('bios_version'),
  d('bios_date', 'date'), d('system_manufacturer'), d('system_model'),
]

export const MEMORY_FIELDS: FieldDef[] = [
  d('total_bytes', 'bytes'), d('memory_type'), d('speed_mts', 'mts'), d('max_speed_mts', 'mts'), d('slots_total', 'int'),
  d('slots_used', 'int'), d('slots_free', 'int'), d('channels', 'enum'), d('ecc', 'bool'),
]

export const MODULE_FIELDS: FieldDef[] = [
  d('slot'), d('manufacturer'), d('model'), d('size_bytes', 'bytes'), d('memory_type'), d('speed_mts', 'mts'),
  d('rated_speed_mts', 'mts'), d('rank', 'int'), d('part_number'), d('voltage_mv', 'mv'), d('serial'),
]

export const GPU_FIELDS: FieldDef[] = [
  d('vendor'), d('model'), d('architecture'), d('adapter_type', 'enum'), d('dedicated_vram_bytes', 'bytes'), d('shared_memory_bytes', 'bytes'),
  d('driver_version'), d('driver_date', 'date'), d('apis', 'list'), d('pci_device_id'), d('renderer_raw'),
]

export const STORAGE_FIELDS: FieldDef[] = [
  d('manufacturer'), d('model'), d('size_bytes', 'bytes'), d('media_type', 'enum'), d('interface', 'enum'), d('rpm', 'int'),
  d('form_factor'), d('smart_status', 'enum'),
]

export const DISPLAY_FIELDS: FieldDef[] = [
  d('manufacturer'), d('model'), d('resolution', 'res'), d('css_resolution', 'res'), d('pixel_ratio', 'ratio'), d('refresh_hz', 'hz'),
  d('diagonal_in', 'inch'), d('hdr', 'bool'), d('color_gamut'), d('color_depth', 'int'), d('connection', 'enum'), d('extended', 'bool'),
]

export const SYSTEM_FIELDS: FieldDef[] = [
  d('os_name'), d('os_version'), d('os_build'), d('architecture'), d('uptime_seconds', 'duration'), d('hostname'),
  d('device_type', 'enum'), d('browser_name'), d('browser_version'), d('language'), d('timezone'),
]

export const BROWSER_FIELDS: FieldDef[] = [
  d('webgl', 'bool'), d('webgl2', 'bool'), d('webgpu', 'bool'), d('cookies_enabled', 'bool'), d('storage_quota_bytes', 'bytes'),
  d('storage_usage_bytes', 'bytes'), d('network_type', 'enum'), d('effective_type'), d('downlink_mbps', 'mbps'), d('rtt_ms', 'ms'),
  d('battery_level', 'percent'), d('battery_charging', 'bool'), d('touch_points', 'int'), d('user_agent'),
]
