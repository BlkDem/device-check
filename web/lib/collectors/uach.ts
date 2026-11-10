import type { UAHints } from '../parse'

/** Reads User-Agent Client Hints (Chromium only). Never throws. */
export async function getUAHints(): Promise<UAHints & { available: boolean }> {
  const d = (navigator as any).userAgentData
  if (!d) return { available: false }
  const base = { available: true, platform: d.platform as string, mobile: d.mobile as boolean, brands: d.brands }
  try {
    const h = await d.getHighEntropyValues(['architecture', 'bitness', 'model', 'platformVersion', 'fullVersionList'])
    return { ...base, ...h }
  } catch {
    return base
  }
}
