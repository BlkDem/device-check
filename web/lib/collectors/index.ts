import { field, unavailable } from '../field'
import type { BrowserInfo, CpuInfo, DisplayInfo, GpuInfo, HardwareReport, Limitation, SystemInfo } from '../types'
import { collectBrowser } from './browser'
import { collectCpu, collectMemory, collectSystem, emptyMotherboard } from './basic'
import { collectDisplays } from './display'
import { collectGpu, type GpuProbe } from './gpu'
import { getUAHints } from './uach'

export const COLLECTOR_VERSION = '0.1.0'
export const SCAN_STEPS = ['system', 'cpu', 'memory', 'gpu', 'display', 'browser'] as const
export type ScanStepId = (typeof SCAN_STEPS)[number]
export type StepStatus = 'pending' | 'running' | 'done' | 'error'

function uuid(): string {
  try {
    if (crypto.randomUUID) return crypto.randomUUID()
  } catch {}
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0
    return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16)
  })
}

/**
 * Runs all browser collectors. A failure of one collector never breaks the scan:
 * its step is marked as "error" and a fallback (all-unavailable) section is used.
 */
export async function runBrowserScan(onStep: (id: ScanStepId, s: StepStatus) => void): Promise<HardwareReport> {
  async function step<T>(id: ScanStepId, fn: () => T | Promise<T>, fallback: () => T): Promise<T> {
    onStep(id, 'running')
    try {
      const r = await fn()
      onStep(id, 'done')
      return r
    } catch (e) {
      console.warn(`[device-check] collector "${id}" failed`, e)
      onStep(id, 'error')
      return fallback()
    }
  }

  const hints = await getUAHints()
  const { system, platform } = await step('system', () => collectSystem(hints), () => ({ system: errorSection<SystemInfo>(SYSTEM_KEYS), platform: null }))
  const cpu = await step('cpu', () => collectCpu(hints), () => errorSection<CpuInfo>(CPU_KEYS))
  const memory = await step('memory', collectMemory, collectMemory)
  const gpuProbe = await step<GpuProbe | null>('gpu', collectGpu, () => null)
  const displays = await step<DisplayInfo[]>('display', collectDisplays, () => [])
  const browser = await step<BrowserInfo | null>(
    'browser',
    () => collectBrowser({ webgl: gpuProbe?.webgl ?? false, webgl2: gpuProbe?.webgl2 ?? false, webgpu: gpuProbe?.webgpu ?? false }),
    () => null,
  )

  const gpu: GpuInfo[] = gpuProbe ? [gpuProbe.gpu] : []

  // Apple Silicon: the GPU renderer names the SoC, which is also the CPU.
  const soc = gpu[0]?.model.value?.match(/^Apple (M\d+(?: (?:Pro|Max|Ultra))?)$/)
  if (soc && cpu.model.confidence === 'unavailable') {
    cpu.vendor = field('Apple', 'GPU renderer (SoC)', 'detected', { note: 'from_gpu_soc' })
    cpu.model = field(`Apple ${soc[1]}`, 'GPU renderer (SoC)', 'detected', { note: 'from_gpu_soc' })
  }

  const limitations: Limitation[] = [
    { section: 'cpu', code: 'cpu_hidden' },
    { section: 'motherboard', code: 'board_hidden' },
    { section: 'memory', code: 'ram_hidden' },
    { section: 'gpu', code: 'gpu_single' },
    { section: 'storage', code: 'storage_hidden' },
    { section: 'displays', code: 'display_hidden' },
  ]
  if (!hints.available) limitations.push({ section: 'system', code: 'no_uach' })
  if (memory.total_bytes.confidence === 'unavailable') limitations.push({ section: 'memory', code: 'no_device_memory' })

  return {
    schema_version: '1.0',
    scan: {
      id: uuid(),
      created_at: new Date().toISOString(),
      source: 'browser',
      platform,
      collector: { name: 'device-check-browser', version: COLLECTOR_VERSION },
    },
    system,
    cpu,
    motherboard: emptyMotherboard(),
    memory,
    gpu,
    storage: [],
    volumes: [],
    displays,
    browser,
    limitations,
  }
}

const SYSTEM_KEYS = ['os_name', 'os_version', 'os_build', 'architecture', 'uptime_seconds', 'hostname', 'device_type', 'browser_name', 'browser_version', 'language', 'timezone']
const CPU_KEYS = ['vendor', 'model', 'family', 'architecture', 'cores_physical', 'threads_logical', 'base_clock_mhz', 'max_clock_mhz', 'current_clock_mhz', 'cache_l1_kb', 'cache_l2_kb', 'cache_l3_kb', 'instructions', 'tdp_w', 'process_nm']

function errorSection<T>(keys: string[]): T {
  return Object.fromEntries(keys.map((k) => [k, unavailable('—', 'error')])) as T
}
