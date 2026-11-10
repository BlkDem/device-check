/**
 * HardwareReport — unified data format for every collector (browser, Windows Agent, future Linux/macOS).
 * Every hardware parameter is wrapped in Field<T> so that the UI always knows
 * where a value came from and how much it can be trusted.
 */

export type Confidence = 'exact' | 'approximate' | 'detected' | 'unavailable'

export interface Field<T = unknown> {
  value: T | null
  /** API / table the value was read from, e.g. "navigator.hardwareConcurrency", "Win32_BIOS" */
  source: string
  confidence: Confidence
  collected_at: string
  /** i18n code: why the value is missing (only when confidence = unavailable) */
  reason?: string
  /** i18n code: caveat for a present value */
  note?: string
}

export interface Resolution {
  width: number
  height: number
}

export type ReportSource = 'browser' | 'agent'

export interface ScanInfo {
  id: string
  created_at: string
  source: ReportSource
  /** OS family: Windows | macOS | Linux | Android | iOS | ChromeOS | null */
  platform: string | null
  collector: { name: string; version: string }
}

export interface SystemInfo {
  os_name: Field<string>
  os_version: Field<string>
  os_build: Field<string>
  architecture: Field<string>
  uptime_seconds: Field<number>
  hostname: Field<string>
  device_type: Field<string>
  browser_name: Field<string>
  browser_version: Field<string>
  language: Field<string>
  timezone: Field<string>
}

export interface CpuInfo {
  vendor: Field<string>
  model: Field<string>
  family: Field<string>
  architecture: Field<string>
  cores_physical: Field<number>
  threads_logical: Field<number>
  base_clock_mhz: Field<number>
  max_clock_mhz: Field<number>
  current_clock_mhz: Field<number>
  cache_l1_kb: Field<number>
  cache_l2_kb: Field<number>
  cache_l3_kb: Field<number>
  instructions: Field<string[]>
  tdp_w: Field<number>
  process_nm: Field<number>
}

export interface MotherboardInfo {
  manufacturer: Field<string>
  model: Field<string>
  version: Field<string>
  chipset: Field<string>
  firmware_type: Field<string>
  bios_vendor: Field<string>
  bios_version: Field<string>
  bios_date: Field<string>
  system_manufacturer: Field<string>
  system_model: Field<string>
}

export interface MemoryModule {
  slot: Field<string>
  soldered: Field<boolean>
  manufacturer: Field<string>
  model: Field<string>
  /** Never collected without separate consent */
  serial: Field<string>
  size_bytes: Field<number>
  memory_type: Field<string>
  speed_mts: Field<number>
  rated_speed_mts: Field<number>
  rank: Field<number>
  part_number: Field<string>
  voltage_mv: Field<number>
}

export interface MemoryInfo {
  total_bytes: Field<number>
  memory_type: Field<string>
  speed_mts: Field<number>
  max_speed_mts: Field<number>
  /** As reported by firmware (SMBIOS) — may differ from physical slots */
  slots_total: Field<number>
  slots_used: Field<number>
  channels: Field<string>
  ecc: Field<boolean>
  modules: MemoryModule[]
}

export interface GpuInfo {
  vendor: Field<string>
  model: Field<string>
  architecture: Field<string>
  adapter_type: Field<string>
  dedicated_vram_bytes: Field<number>
  shared_memory_bytes: Field<number>
  driver_version: Field<string>
  driver_date: Field<string>
  apis: Field<string[]>
  pci_device_id: Field<string>
  renderer_raw: Field<string>
}

export interface StorageDevice {
  manufacturer: Field<string>
  model: Field<string>
  size_bytes: Field<number>
  media_type: Field<string>
  interface: Field<string>
  rpm: Field<number>
  form_factor: Field<string>
  smart_status: Field<string>
  is_system: Field<boolean>
}

export interface Volume {
  letter: Field<string>
  filesystem: Field<string>
  size_bytes: Field<number>
  free_bytes: Field<number>
}

export interface DisplayInfo {
  manufacturer: Field<string>
  model: Field<string>
  resolution: Field<Resolution>
  css_resolution: Field<Resolution>
  pixel_ratio: Field<number>
  refresh_hz: Field<number>
  diagonal_in: Field<number>
  hdr: Field<boolean>
  color_gamut: Field<string>
  color_depth: Field<number>
  connection: Field<string>
  extended: Field<boolean>
}

export interface BrowserInfo {
  user_agent: Field<string>
  webgl: Field<boolean>
  webgl2: Field<boolean>
  webgpu: Field<boolean>
  cookies_enabled: Field<boolean>
  storage_quota_bytes: Field<number>
  storage_usage_bytes: Field<number>
  network_type: Field<string>
  effective_type: Field<string>
  downlink_mbps: Field<number>
  rtt_ms: Field<number>
  battery_level: Field<number>
  battery_charging: Field<boolean>
  touch_points: Field<number>
}

export interface Limitation {
  section: string
  /** i18n code */
  code: string
}

export interface HardwareReport {
  schema_version: '1.0'
  scan: ScanInfo
  system: SystemInfo
  cpu: CpuInfo
  motherboard: MotherboardInfo
  memory: MemoryInfo
  gpu: GpuInfo[]
  storage: StorageDevice[]
  volumes: Volume[]
  displays: DisplayInfo[]
  browser: BrowserInfo | null
  limitations: Limitation[]
}
