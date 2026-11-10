/**
 * Pure parsing helpers (no DOM access) — covered by unit tests.
 */
import type { Confidence } from './types'

export interface Detected<T = string> {
  value: T | null
  source: string
  confidence: Exclude<Confidence, 'unavailable'>
  note?: string
}

export interface UAHints {
  platform?: string
  platformVersion?: string
  architecture?: string
  bitness?: string
  mobile?: boolean
  fullVersionList?: { brand: string; version: string }[]
  brands?: { brand: string; version: string }[]
}

/* ------------------------------------------------------------------ OS */

export interface OsResult {
  name: string | null
  version: string | null
  family: string | null
  source: string
  confidence: Exclude<Confidence, 'unavailable'>
  note?: string
}

export function detectOs(h: UAHints, ua: string): OsResult {
  if (h.platform && h.platformVersion !== undefined) {
    const src = 'navigator.userAgentData (platform, platformVersion)'
    const major = parseInt(h.platformVersion, 10)
    switch (h.platform) {
      case 'Windows':
        if (major >= 13) return { name: 'Windows 11', version: h.platformVersion, family: 'Windows', source: src, confidence: 'exact', note: 'uach_platform_version' }
        if (major >= 1) return { name: 'Windows 10', version: h.platformVersion, family: 'Windows', source: src, confidence: 'exact', note: 'uach_platform_version' }
        return { name: 'Windows 7 / 8 / 8.1', version: null, family: 'Windows', source: src, confidence: 'detected' }
      case 'macOS':
        return { name: 'macOS', version: h.platformVersion, family: 'macOS', source: src, confidence: 'exact' }
      case 'Android':
        return { name: 'Android', version: h.platformVersion, family: 'Android', source: src, confidence: 'exact' }
      case 'Chrome OS':
      case 'Chromium OS':
        return { name: 'ChromeOS', version: h.platformVersion || null, family: 'ChromeOS', source: src, confidence: 'exact' }
      case 'Linux':
        return { name: 'Linux', version: null, family: 'Linux', source: src, confidence: 'exact' }
      default:
        if (h.platform) return { name: h.platform, version: h.platformVersion || null, family: h.platform, source: src, confidence: 'exact' }
    }
  }

  const src = 'navigator.userAgent'
  if (/Windows NT 10\.0/.test(ua)) return { name: 'Windows 10 / 11', version: null, family: 'Windows', source: src, confidence: 'approximate', note: 'ua_frozen' }
  const nt = /Windows NT (6\.[123])/.exec(ua)
  if (nt) return { name: ({ '6.1': 'Windows 7', '6.2': 'Windows 8', '6.3': 'Windows 8.1' } as Record<string, string>)[nt[1]!]!, version: nt[1]!, family: 'Windows', source: src, confidence: 'detected' }
  const ios = /(iPhone|iPad|iPod).*? OS (\d+)[_.](\d+)/.exec(ua)
  if (ios) return { name: ios[1] === 'iPad' ? 'iPadOS' : 'iOS', version: `${ios[2]}.${ios[3]}`, family: 'iOS', source: src, confidence: 'detected' }
  const android = /Android (\d+(?:\.\d+)*)/.exec(ua)
  if (android) return { name: 'Android', version: android[1]!, family: 'Android', source: src, confidence: 'detected' }
  if (/CrOS/.test(ua)) return { name: 'ChromeOS', version: null, family: 'ChromeOS', source: src, confidence: 'detected' }
  const mac = /Mac OS X (\d+)[._](\d+)(?:[._](\d+))?/.exec(ua)
  if (mac) return { name: 'macOS', version: [mac[1], mac[2], mac[3]].filter(Boolean).join('.'), family: 'macOS', source: src, confidence: 'approximate', note: 'ua_frozen' }
  if (/Linux/.test(ua)) return { name: 'Linux', version: null, family: 'Linux', source: src, confidence: 'detected' }
  return { name: null, version: null, family: null, source: src, confidence: 'detected' }
}

/* ------------------------------------------------------------ Browser */

const BRAND_PRIORITY = ['Microsoft Edge', 'Opera', 'YaBrowser', 'Yandex', 'Vivaldi', 'Brave', 'Google Chrome', 'Chromium']

export function detectBrowser(h: UAHints, ua: string): { name: Detected; version: Detected } {
  const list = h.fullVersionList ?? h.brands
  if (list?.length) {
    const src = h.fullVersionList ? 'navigator.userAgentData.fullVersionList' : 'navigator.userAgentData.brands'
    const real = list.filter((b) => !/not.?a.?brand/i.test(b.brand))
    const pick = BRAND_PRIORITY.map((p) => real.find((b) => b.brand === p)).find(Boolean) ?? real[0]
    if (pick) {
      return {
        name: { value: pick.brand, source: src, confidence: 'exact' },
        version: { value: pick.version, source: src, confidence: h.fullVersionList ? 'exact' : 'approximate' },
      }
    }
  }
  const src = 'navigator.userAgent'
  const tests: [RegExp, string, boolean][] = [
    [/Firefox\/([\d.]+)/, 'Firefox', true],
    [/Edg\/([\d.]+)/, 'Microsoft Edge', false],
    [/OPR\/([\d.]+)/, 'Opera', false],
    [/YaBrowser\/([\d.]+)/, 'Yandex Browser', true],
    [/Chrome\/([\d.]+)/, 'Google Chrome', false],
    [/Version\/([\d.]+).*Safari/, 'Safari', true],
  ]
  for (const [re, name, precise] of tests) {
    const m = re.exec(ua)
    if (m) {
      return {
        name: { value: name, source: src, confidence: 'detected' },
        version: { value: m[1]!, source: src, confidence: precise ? 'exact' : 'approximate', note: precise ? undefined : 'ua_reduced' },
      }
    }
  }
  return { name: { value: null, source: src, confidence: 'detected' }, version: { value: null, source: src, confidence: 'detected' } }
}

/* -------------------------------------------------------- Architecture */

export function detectArch(h: UAHints, ua: string): Detected {
  if (h.architecture) {
    const src = 'navigator.userAgentData (architecture, bitness)'
    const b64 = h.bitness === '64'
    if (h.architecture === 'x86') return { value: b64 ? 'x86-64' : 'x86 (32-bit)', source: src, confidence: 'exact' }
    if (h.architecture === 'arm') return { value: b64 ? 'ARM64' : 'ARM (32-bit)', source: src, confidence: 'exact' }
    return { value: h.architecture, source: src, confidence: 'exact' }
  }
  const src = 'navigator.userAgent'
  if (/Win64|x86_64|x64|amd64|WOW64/i.test(ua)) return { value: 'x86-64', source: src, confidence: 'detected' }
  if (/aarch64|arm64/i.test(ua)) return { value: 'ARM64', source: src, confidence: 'detected' }
  if (/iPhone|iPad/.test(ua)) return { value: 'ARM64', source: src, confidence: 'detected' }
  if (/armv\d/i.test(ua)) return { value: 'ARM', source: src, confidence: 'detected' }
  return { value: null, source: src, confidence: 'detected' }
}

/* --------------------------------------------------------- Device type */

export function detectDeviceType(h: UAHints, ua: string, touchPoints: number): string {
  if (/iPad|Tablet/i.test(ua) || (/Macintosh/.test(ua) && touchPoints > 1) || (/Android/.test(ua) && !/Mobile/.test(ua))) return 'tablet'
  if (h.mobile || /Mobi|iPhone|Android/i.test(ua)) return 'mobile'
  return 'desktop'
}

/* ---------------------------------------------------------------- GPU */

export interface RendererInfo {
  vendor: string | null
  model: string | null
  pciDeviceId: string | null
  software: boolean
  approximate: boolean
}

export function normalizeVendor(s: string | null | undefined): string | null {
  if (!s) return null
  const v = s.toLowerCase()
  if (/nvidia|geforce|quadro|tesla|rtx|gtx/.test(v)) return 'NVIDIA'
  if (/\bamd\b|radeon|\bati\b|advanced micro/.test(v)) return 'AMD'
  if (/intel/.test(v)) return 'Intel'
  if (/apple/.test(v)) return 'Apple'
  if (/qualcomm|adreno/.test(v)) return 'Qualcomm'
  if (/\bmali\b|\barm\b/.test(v)) return 'ARM'
  if (/powervr|imagination/.test(v)) return 'Imagination'
  if (/microsoft/.test(v)) return 'Microsoft'
  if (/google|swiftshader/.test(v)) return 'Google'
  if (/mesa|llvmpipe/.test(v)) return 'Mesa'
  return null
}

export function parseRenderer(raw: string | null | undefined): RendererInfo {
  const empty: RendererInfo = { vendor: null, model: null, pciDeviceId: null, software: false, approximate: false }
  if (!raw) return empty
  let s = raw.trim()
  const approximate = /,?\s*or similar$/i.test(s)
  s = s.replace(/,?\s*or similar$/i, '')

  let vendorPart: string | null = null
  let model = s
  const angle = /^ANGLE \((.*)\)$/.exec(s)
  if (angle) {
    const parts = angle[1]!.split(/,\s+/)
    if (parts.length >= 2) {
      vendorPart = parts[0]!
      model = parts[1]!
    } else model = parts[0]!
  }

  const pci = /\(0x([0-9a-f]{4,8})\)/i.exec(model)
  const pciDeviceId = pci ? '0x' + pci[1]!.replace(/^0+(?=[0-9a-f]{4})/i, '').toUpperCase() : null

  model = model
    .replace(/\(0x[0-9a-f]+\)/gi, '')
    .replace(/\s+Direct3D.*$/i, '')
    .replace(/\s+vs_\d.*$/i, '')
    .replace(/\/PCIe\/SSE2$/i, '')
    .replace(/^ANGLE Metal Renderer:\s*/i, '')
    .replace(/\((TM|R)\)/gi, '')
    .replace(/,\s*Unspecified Version$/i, '')
    .replace(/\s{2,}/g, ' ')
    .trim()

  const software = /swiftshader|llvmpipe|softpipe|basic render|microsoft basic/i.test(raw)
  const vendor = normalizeVendor(model) ?? normalizeVendor(vendorPart)
  return { vendor, model: model || null, pciDeviceId, software, approximate }
}

/** Heuristic: integrated vs discrete. Result is always "detected", never "exact". */
export function guessAdapterType(vendor: string | null, model: string | null, software: boolean): string | null {
  if (software) return 'software'
  if (!model) return null
  const m = model.toLowerCase()
  if (vendor === 'Intel') return /\barc\b/.test(m) && !/arc graphics$/.test(m) ? 'discrete' : 'integrated'
  if (vendor === 'NVIDIA') return /tegra/.test(m) ? 'integrated' : 'discrete'
  if (vendor === 'AMD') {
    if (/\b(rx|pro|w\d{4}|r9|r7|hd \d{4}|vii|fire)/.test(m)) return 'discrete'
    if (/radeon graphics|vega \d+ graphics|radeon \d{3}m/.test(m)) return 'integrated'
    return null
  }
  if (vendor === 'Apple' || vendor === 'Qualcomm' || vendor === 'ARM' || vendor === 'Imagination') return 'integrated'
  return null
}

/* ------------------------------------------------------------- Display */

const COMMON_RATES = [24, 30, 48, 50, 60, 72, 75, 85, 90, 100, 120, 144, 160, 165, 170, 175, 180, 200, 240, 280, 300, 360, 480, 500, 540]

/** Converts measured frame intervals (ms) to the nearest common refresh rate. */
export function refreshFromIntervals(intervals: number[]): number | null {
  const clean = intervals.filter((d) => d > 1 && d < 100).sort((a, b) => a - b)
  if (clean.length < 10) return null
  const median = clean[Math.floor(clean.length / 2)]!
  const hz = 1000 / median
  const nearest = COMMON_RATES.reduce((a, b) => (Math.abs(b - hz) < Math.abs(a - hz) ? b : a))
  return Math.abs(nearest - hz) / nearest < 0.04 ? nearest : Math.round(hz)
}
