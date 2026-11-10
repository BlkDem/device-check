import type { Field, Resolution } from './types'

export type Fmt =
  | 'text' | 'enum' | 'int' | 'bool' | 'list' | 'bytes' | 'kb' | 'mhz' | 'mts' | 'hz'
  | 'res' | 'ratio' | 'percent' | 'mbps' | 'ms' | 'w' | 'nm' | 'mv' | 'inch' | 'date' | 'duration'

export type Translate = (key: string, params?: Record<string, string | number>) => string
export type Has = (key: string) => boolean

export function formatBytes(n: number, t: Translate): string {
  const units = ['unit.b', 'unit.kb', 'unit.mb', 'unit.gb', 'unit.tb']
  let i = 0
  let v = n
  while (v >= 1024 && i < units.length - 1) {
    v /= 1024
    i++
  }
  const num = v >= 100 || Number.isInteger(v) ? Math.round(v).toString() : v.toFixed(1)
  return `${num} ${t(units[i]!)}`
}

export function formatValue(f: Field, fmt: Fmt, t: Translate, has: Has, locale: string): string {
  const v = f.value as any
  if (v === null || v === undefined) return '—'
  const num = (x: number) => x.toLocaleString(locale)
  switch (fmt) {
    case 'enum':
      return has(`enum.${v}`) ? t(`enum.${v}`) : String(v)
    case 'int':
      return num(v)
    case 'bool':
      return v ? t('yes') : t('no')
    case 'list':
      return (v as unknown[]).join(', ')
    case 'bytes':
      return (f.note === 'device_memory_capped' ? '≥ ' : '') + formatBytes(v, t)
    case 'kb':
      return formatBytes(v * 1024, t)
    case 'mhz':
      return `${num(v)} ${t('unit.mhz')}`
    case 'mts':
      return `${num(v)} ${t('unit.mts')}`
    case 'hz':
      return `${num(v)} ${t('unit.hz')}`
    case 'res': {
      const r = v as Resolution
      return `${r.width} × ${r.height}`
    }
    case 'ratio':
      return `${v}×`
    case 'percent':
      return `${Math.round(v * 100)}%`
    case 'mbps':
      return `${num(v)} ${t('unit.mbps')}`
    case 'ms':
      return `${num(v)} ${t('unit.ms')}`
    case 'w':
      return `${num(v)} ${t('unit.w')}`
    case 'nm':
      return `${num(v)} ${t('unit.nm')}`
    case 'mv':
      return `${(v / 1000).toFixed(2)} ${t('unit.v')}`
    case 'inch':
      return `${num(v)}″`
    case 'date': {
      const d = new Date(v)
      return isNaN(d.getTime()) ? String(v) : d.toLocaleDateString(locale)
    }
    case 'duration': {
      const s = Number(v)
      const d = Math.floor(s / 86400)
      const h = Math.floor((s % 86400) / 3600)
      const m = Math.floor((s % 3600) / 60)
      return t('unit.duration', { d, h, m })
    }
    default:
      return String(v)
  }
}
