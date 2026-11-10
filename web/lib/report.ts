import { field, isField, unavailable } from './field'
import { formatValue, type Has, type Translate } from './format'
import * as S from './sections'
import type { Confidence, Field, HardwareReport, MemoryInfo } from './types'

export interface Completeness {
  total: number
  available: number
  percent: number
  by: Record<Confidence, number>
}

/** Counts every Field in the hardware part of the report (browser-environment info excluded). */
export function completeness(r: HardwareReport): Completeness {
  const by: Record<Confidence, number> = { exact: 0, approximate: 0, detected: 0, unavailable: 0 }
  const walk = (v: unknown) => {
    if (isField(v)) by[v.confidence]++
    else if (Array.isArray(v)) v.forEach(walk)
    else if (v && typeof v === 'object') Object.values(v).forEach(walk)
  }
  walk([r.system, r.cpu, r.motherboard, r.memory, r.gpu, r.displays])
  // Sections that are empty lists still count as missing data.
  if (!r.storage.length) by.unavailable += S.STORAGE_FIELDS.length
  if (!r.gpu.length) by.unavailable += S.GPU_FIELDS.length
  if (!r.displays.length) by.unavailable += S.DISPLAY_FIELDS.length
  const total = by.exact + by.approximate + by.detected + by.unavailable
  const available = total - by.unavailable
  return { total, available, percent: total ? Math.round((available / total) * 100) : 0, by }
}

/** Free slots — derived only if both inputs are known. */
export function freeSlots(m: MemoryInfo): Field<number> {
  if (m.slots_total.value === null || m.slots_used.value === null) return unavailable('slots_total − slots_used', m.slots_total.reason ?? 'not_exposed')
  // The derived value is only as trustworthy as its weakest input.
  const rank = { exact: 0, detected: 1, approximate: 2 } as const
  const a = m.slots_total.confidence as keyof typeof rank
  const b = m.slots_used.confidence as keyof typeof rank
  const c = rank[a] >= rank[b] ? a : b
  return field(Math.max(0, m.slots_total.value - m.slots_used.value), 'slots_total − slots_used', c)
}

/** Plain-text version of the report for clipboard. */
export function reportToText(r: HardwareReport, t: Translate, has: Has, locale: string): string {
  const lines: string[] = [`Device Check — ${new Date(r.scan.created_at).toLocaleString(locale)} (${r.scan.source})`, '']
  const block = (title: string, data: object, defs: S.FieldDef[]) => {
    lines.push(`== ${title} ==`)
    for (const def of defs) {
      const f = (data as Record<string, Field | undefined>)[def.key]
      if (!f) continue
      const val = f.confidence === 'unavailable' ? t('status.unavailable_value') : formatValue(f, def.fmt, t, has, locale)
      lines.push(`${t(`f.${def.key}`)}: ${val} [${t(`status.${f.confidence}`)}]`)
    }
    lines.push('')
  }
  block(t('section.system'), r.system, S.SYSTEM_FIELDS)
  block(t('section.cpu'), r.cpu, S.CPU_FIELDS)
  block(t('section.motherboard'), r.motherboard, S.BOARD_FIELDS)
  block(t('section.memory'), { ...r.memory, slots_free: freeSlots(r.memory) }, S.MEMORY_FIELDS)
  r.memory.modules.forEach((m, i) => block(`${t('section.module')} ${i + 1}`, m, S.MODULE_FIELDS))
  r.gpu.forEach((g, i) => block(`${t('section.gpu')} #${i + 1}`, g, S.GPU_FIELDS))
  r.storage.forEach((s, i) => block(`${t('section.storage')} #${i + 1}`, s, S.STORAGE_FIELDS))
  r.displays.forEach((d, i) => block(`${t('section.displays')} #${i + 1}`, d, S.DISPLAY_FIELDS))
  if (r.browser) block(t('section.browser'), r.browser, S.BROWSER_FIELDS)
  return lines.join('\n').trim() + '\n'
}
