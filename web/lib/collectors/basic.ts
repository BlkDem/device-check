import { agentOnly, field, unavailable } from '../field'
import { detectArch, detectBrowser, detectDeviceType, detectOs, type UAHints } from '../parse'
import type { CpuInfo, MemoryInfo, MotherboardInfo, SystemInfo } from '../types'

export function collectSystem(h: UAHints): { system: SystemInfo; platform: string | null } {
  const ua = navigator.userAgent
  const os = detectOs(h, ua)
  const br = detectBrowser(h, ua)
  const arch = detectArch(h, ua)
  let tz: string | null = null
  try {
    tz = Intl.DateTimeFormat().resolvedOptions().timeZone
  } catch {}
  return {
    platform: os.family,
    system: {
      os_name: field(os.name, os.source, os.confidence, { note: os.note }),
      os_version: field(os.version, os.source, os.confidence, { note: os.note }),
      os_build: agentOnly(),
      architecture: field(arch.value, arch.source, arch.confidence),
      uptime_seconds: agentOnly(),
      hostname: unavailable('—', 'privacy'),
      device_type: field(detectDeviceType(h, ua, navigator.maxTouchPoints ?? 0), 'userAgentData.mobile / userAgent / maxTouchPoints', 'detected'),
      browser_name: field(br.name.value, br.name.source, br.name.confidence, { note: br.name.note }),
      browser_version: field(br.version.value, br.version.source, br.version.confidence, { note: br.version.note }),
      language: field(navigator.language, 'navigator.language', 'exact'),
      timezone: field(tz, 'Intl.DateTimeFormat', 'exact'),
    },
  }
}

export function collectCpu(h: UAHints): CpuInfo {
  const arch = detectArch(h, navigator.userAgent)
  const threads = navigator.hardwareConcurrency
  return {
    vendor: agentOnly(),
    model: agentOnly(),
    family: agentOnly(),
    architecture: field(arch.value, arch.source, arch.confidence),
    cores_physical: agentOnly(),
    threads_logical: threads
      ? field(threads, 'navigator.hardwareConcurrency', 'exact', { note: 'threads_limit' })
      : unavailable('navigator.hardwareConcurrency', 'api_unsupported'),
    base_clock_mhz: agentOnly(),
    max_clock_mhz: agentOnly(),
    current_clock_mhz: agentOnly(),
    cache_l1_kb: agentOnly(),
    cache_l2_kb: agentOnly(),
    cache_l3_kb: agentOnly(),
    instructions: agentOnly(),
    tdp_w: agentOnly(),
    process_nm: agentOnly(),
  }
}

export function collectMemory(): MemoryInfo {
  const dm = (navigator as any).deviceMemory as number | undefined
  return {
    total_bytes:
      typeof dm === 'number' && dm > 0
        ? field(dm * 1024 ** 3, 'navigator.deviceMemory', 'approximate', { note: dm >= 8 ? 'device_memory_capped' : 'device_memory_rounded' })
        : unavailable('navigator.deviceMemory', 'api_unsupported'),
    memory_type: agentOnly(),
    speed_mts: agentOnly(),
    max_speed_mts: agentOnly(),
    slots_total: agentOnly(),
    slots_used: agentOnly(),
    channels: agentOnly(),
    ecc: agentOnly(),
    modules: [],
  }
}

export function emptyMotherboard(): MotherboardInfo {
  return {
    manufacturer: agentOnly(),
    model: agentOnly(),
    version: agentOnly(),
    chipset: agentOnly(),
    firmware_type: agentOnly(),
    bios_vendor: agentOnly(),
    bios_version: agentOnly(),
    bios_date: agentOnly(),
    system_manufacturer: agentOnly(),
    system_model: agentOnly(),
  }
}
