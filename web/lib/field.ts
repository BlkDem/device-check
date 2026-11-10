import type { Confidence, Field } from './types'

export const nowIso = () => new Date().toISOString()

function isEmpty(v: unknown): boolean {
  return (
    v === null ||
    v === undefined ||
    v === '' ||
    (typeof v === 'number' && !Number.isFinite(v)) ||
    (Array.isArray(v) && v.length === 0)
  )
}

export function unavailable<T = never>(source: string, reason = 'not_exposed'): Field<T> {
  return { value: null, source, confidence: 'unavailable', collected_at: nowIso(), reason }
}

/** Creates a field; an empty value automatically becomes "unavailable". */
export function field<T>(
  value: T | null | undefined,
  source: string,
  confidence: Exclude<Confidence, 'unavailable'>,
  opts: { note?: string; reason?: string } = {},
): Field<T> {
  if (isEmpty(value)) return unavailable<T>(source, opts.reason ?? 'not_exposed')
  const f: Field<T> = { value: value as T, source, confidence, collected_at: nowIso() }
  if (opts.note) f.note = opts.note
  return f
}

/** Field that the browser can never provide — needs the Windows Agent. */
export const agentOnly = <T = never>(): Field<T> => unavailable<T>('—', 'agent_required')

export function isField(v: unknown): v is Field {
  return !!v && typeof v === 'object' && 'confidence' in v && 'source' in v && 'value' in v
}
