import { describe, expect, it } from 'vitest'
import { agentOnly, field, unavailable } from '../lib/field'
import { formatBytes, formatValue } from '../lib/format'
import { freeSlots } from '../lib/report'
import type { MemoryInfo } from '../lib/types'

const t = (k: string, p?: Record<string, string | number>) => (p ? `${k}:${JSON.stringify(p)}` : k.replace('unit.', ''))
const has = () => false

describe('field', () => {
  it('empty values become unavailable', () => {
    expect(field(null, 'x', 'exact').confidence).toBe('unavailable')
    expect(field('', 'x', 'exact').confidence).toBe('unavailable')
    expect(field([], 'x', 'exact').confidence).toBe('unavailable')
    expect(field(NaN, 'x', 'exact').confidence).toBe('unavailable')
    expect(field(0, 'x', 'exact').confidence).toBe('exact')
    expect(field(false, 'x', 'exact').value).toBe(false)
  })
  it('agentOnly has a reason', () => {
    expect(agentOnly()).toMatchObject({ value: null, reason: 'agent_required' })
  })
})

describe('format', () => {
  it('bytes', () => {
    expect(formatBytes(8 * 1024 ** 3, t)).toBe('8 gb')
    expect(formatBytes(1536, t)).toBe('1.5 kb')
  })
  it('capped device memory gets ≥', () => {
    const f = field(8 * 1024 ** 3, 'navigator.deviceMemory', 'approximate', { note: 'device_memory_capped' })
    expect(formatValue(f, 'bytes', t, has, 'en')).toBe('≥ 8 gb')
  })
})

describe('freeSlots', () => {
  const mem = (total: number | null, used: number | null) =>
    ({
      slots_total: total === null ? unavailable('Win32_PhysicalMemoryArray', 'agent_required') : field(total, 'SMBIOS', 'detected'),
      slots_used: used === null ? unavailable('x') : field(used, 'Win32_PhysicalMemory', 'exact'),
    }) as MemoryInfo
  it('derives only when both known', () => {
    expect(freeSlots(mem(4, 2))).toMatchObject({ value: 2, confidence: 'detected' })
    expect(freeSlots(mem(null, 2)).confidence).toBe('unavailable')
  })
})
