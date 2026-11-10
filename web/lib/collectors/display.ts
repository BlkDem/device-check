import { agentOnly, field, unavailable } from '../field'
import { refreshFromIntervals } from '../parse'
import type { DisplayInfo, Field } from '../types'

function measureRefresh(durationMs = 700): Promise<Field<number>> {
  const src = 'requestAnimationFrame timing'
  if (typeof requestAnimationFrame !== 'function') return Promise.resolve(unavailable(src, 'api_unsupported'))
  if (document.hidden) return Promise.resolve(unavailable(src, 'tab_hidden'))
  return new Promise((resolve) => {
    const intervals: number[] = []
    let last = 0
    let start = 0
    let done = false
    const finish = () => {
      if (done) return
      done = true
      const hz = refreshFromIntervals(intervals)
      resolve(hz ? field(hz, src, 'approximate', { note: 'measured' }) : unavailable(src, 'measure_failed'))
    }
    const tick = (ts: number) => {
      if (done) return
      if (!start) start = ts
      if (last) intervals.push(ts - last)
      last = ts
      if (ts - start < durationMs) requestAnimationFrame(tick)
      else finish()
    }
    requestAnimationFrame(tick)
    setTimeout(finish, durationMs + 1500)
  })
}

function mq(q: string): boolean {
  try {
    return window.matchMedia(q).matches
  } catch {
    return false
  }
}

export async function collectDisplays(): Promise<DisplayInfo[]> {
  const s = window.screen
  const dpr = window.devicePixelRatio || 1
  const css = { width: s.width, height: s.height }

  const supportsRange = mq('(dynamic-range: standard)') || mq('(dynamic-range: high)')
  const gamut = mq('(color-gamut: rec2020)') ? 'Rec. 2020' : mq('(color-gamut: p3)') ? 'Display P3' : mq('(color-gamut: srgb)') ? 'sRGB' : null

  const display: DisplayInfo = {
    manufacturer: agentOnly(),
    model: agentOnly(),
    resolution: field({ width: Math.round(css.width * dpr), height: Math.round(css.height * dpr) }, 'screen.width × devicePixelRatio', 'detected', { note: 'physical_from_dpr' }),
    css_resolution: field(css, 'screen.width / screen.height', 'exact'),
    pixel_ratio: field(Math.round(dpr * 100) / 100, 'window.devicePixelRatio', 'exact'),
    refresh_hz: await measureRefresh(),
    diagonal_in: agentOnly(),
    hdr: supportsRange ? field(mq('(dynamic-range: high)'), "matchMedia('(dynamic-range: high)')", 'detected') : unavailable('matchMedia(dynamic-range)', 'api_unsupported'),
    color_gamut: field(gamut, "matchMedia('(color-gamut)')", 'detected'),
    color_depth: field(s.colorDepth, 'screen.colorDepth', 'exact'),
    connection: agentOnly(),
    extended: 'isExtended' in s ? field(Boolean((s as any).isExtended), 'screen.isExtended', 'exact') : unavailable('screen.isExtended', 'api_unsupported'),
  }
  return [display]
}
