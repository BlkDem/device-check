import type { HardwareReport } from './types'

/**
 * Windows Agent link. The tray menu opens the site as `/#agent=<port>&token=<token>`:
 * the fragment never reaches any server, and the token is valid only while that agent process runs.
 */
export interface AgentLink {
  port: number
  token: string
}

export interface AgentStatus {
  status: 'idle' | 'scanning' | 'ready'
  agent_version: string
  schema_version: string
  last_scan: { id: string; created_at: string } | null
}

/** Agent collector ids, in the order the agent reports them (report.diagnostics.collectors). */
export const AGENT_STEPS = ['system', 'cpu', 'motherboard', 'memory', 'gpu', 'storage', 'display'] as const

const STORAGE_KEY = 'dc:agent'

export function parseAgentHash(hash: string): AgentLink | null {
  const p = new URLSearchParams(hash.replace(/^#/, ''))
  const port = Number(p.get('agent'))
  const token = p.get('token') ?? ''
  if (!Number.isInteger(port) || port < 1 || port > 65535 || !/^[A-Za-z0-9_-]{20,}$/.test(token)) return null
  return { port, token }
}

/** Takes the link from the URL (and removes it from the address bar), else from this tab's session. */
export function takeAgentLink(): AgentLink | null {
  const fromHash = parseAgentHash(location.hash)
  if (fromHash) {
    history.replaceState(history.state, '', location.pathname + location.search)
    try { sessionStorage.setItem(STORAGE_KEY, JSON.stringify(fromHash)) } catch {}
    return fromHash
  }
  try {
    const saved = JSON.parse(sessionStorage.getItem(STORAGE_KEY) ?? 'null')
    return saved ? parseAgentHash(new URLSearchParams({ agent: String(saved.port), token: String(saved.token) }).toString()) : null
  } catch {
    return null
  }
}

export function forgetAgentLink() {
  try { sessionStorage.removeItem(STORAGE_KEY) } catch {}
}

export class AgentError extends Error {
  constructor(public code: string, public status = 0) {
    super(code)
  }
}

async function call<T>(link: AgentLink, path: string, method: 'GET' | 'POST' = 'GET', timeoutMs = 60_000): Promise<T> {
  const ctrl = new AbortController()
  const timer = setTimeout(() => ctrl.abort(), timeoutMs)
  try {
    const r = await fetch(`http://127.0.0.1:${link.port}${path}`, {
      method,
      headers: { Authorization: `Bearer ${link.token}` },
      cache: 'no-store',
      signal: ctrl.signal,
    })
    if (!r.ok) {
      const body = await r.json().catch(() => ({}))
      throw new AgentError(body?.error ?? `http_${r.status}`, r.status)
    }
    return (await r.json()) as T
  } catch (e) {
    if (e instanceof AgentError) throw e
    throw new AgentError('unreachable') // agent closed, blocked by the browser, or wrong port
  } finally {
    clearTimeout(timer)
  }
}

export const agentStatus = (link: AgentLink) => call<AgentStatus>(link, '/api/status', 'GET', 5_000)

/** Latest agent report; with `fresh` (or when the agent has none yet) runs a new scan first. */
export async function agentReport(link: AgentLink, fresh: boolean): Promise<HardwareReport> {
  if (!fresh) {
    const s = await agentStatus(link)
    if (s.status === 'ready' && s.last_scan) return call<HardwareReport>(link, `/api/report/${s.last_scan.id}`)
  }
  // POST /api/scan joins a scan that is already running.
  const { id } = await call<{ id: string }>(link, '/api/scan', 'POST')
  return call<HardwareReport>(link, `/api/report/${id}`)
}
