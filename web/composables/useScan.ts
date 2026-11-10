import { AGENT_STEPS, agentReport, agentStatus, agentStorage, AgentError, forgetAgentLink, takeAgentLink, type AgentLink } from '~/lib/agent'
import { runBrowserScan, SCAN_STEPS, type StepStatus } from '~/lib/collectors'
import type { StorageHealthReport } from '~/lib/storage'
import type { HardwareReport } from '~/lib/types'

export type ScanState = 'idle' | 'scanning' | 'done' | 'error'

export interface AgentConnection {
  link: AgentLink
  version: string
  features: string[]
}

const pending = (ids: readonly string[]) => Object.fromEntries(ids.map((s) => [s, 'pending' as StepStatus]))

/**
 * Shared scan state. "origin" tells whether the shown report is a fresh scan or a saved one.
 * When the page was opened from the Windows Agent tray menu, scans go through the agent instead of the browser.
 */
export function useScan() {
  const report = useState<HardwareReport | null>('report', () => null)
  const origin = useState<'scan' | 'saved' | null>('reportOrigin', () => null)
  const status = useState<ScanState>('scanStatus', () => 'idle')
  const agent = useState<AgentConnection | null>('agent', () => null)
  const agentLost = useState<boolean>('agentLost', () => false)
  const stepIds = computed<readonly string[]>(() => (agent.value ? AGENT_STEPS : SCAN_STEPS))
  const steps = useState<Record<string, StepStatus>>('scanSteps', () => pending(SCAN_STEPS))
  const storage = useState<StorageHealthReport | null>('storageHealth', () => null)
  const storageStatus = useState<'idle' | 'loading' | 'done' | 'error'>('storageStatus', () => 'idle')

  /** Storage health is fetched separately: it is optional and must never delay the main report. */
  async function loadStorage(conn: AgentConnection) {
    if (!conn.features.includes('storage_health')) return
    storageStatus.value = 'loading'
    try {
      storage.value = await agentStorage(conn.link)
      storageStatus.value = 'done'
    } catch (e) {
      console.warn('[device-check] storage health unavailable', e)
      storageStatus.value = 'error'
    }
  }

  async function scanWithAgent(conn: AgentConnection, fresh: boolean) {
    steps.value = Object.fromEntries(AGENT_STEPS.map((s) => [s, 'running' as StepStatus]))
    try {
      const r = await agentReport(conn.link, fresh)
      const byId = new Map(r.diagnostics?.collectors.map((c) => [c.id, c.status]))
      // partial = some sources failed but the category was collected
      const ok = (s: string) => ['ok', 'partial', undefined].includes(byId.get(s))
      steps.value = Object.fromEntries(AGENT_STEPS.map((s) => [s, ok(s) ? 'done' : 'error']))
      report.value = r
      origin.value = 'scan'
      status.value = 'done'
      void loadStorage(conn)
    } catch (e) {
      console.error('[device-check] agent scan failed', e)
      if (e instanceof AgentError && (e.code === 'unreachable' || e.status === 401 || e.status === 403)) {
        // Agent was closed or restarted (new token): fall back to the browser scan next time.
        agent.value = null
        agentLost.value = true
        forgetAgentLink()
        steps.value = pending(SCAN_STEPS)
      }
      status.value = 'error'
    }
  }

  async function scan() {
    if (status.value === 'scanning') return
    status.value = 'scanning'
    if (agent.value) return scanWithAgent(agent.value, true)
    steps.value = pending(SCAN_STEPS)
    try {
      report.value = await runBrowserScan((id, s) => (steps.value = { ...steps.value, [id]: s }))
      origin.value = 'scan'
      status.value = 'done'
    } catch (e) {
      console.error('[device-check] scan failed', e)
      status.value = 'error'
    }
  }

  /** Connects to the agent from the tray link (client only) and shows its latest report. */
  async function connectAgent() {
    const link = takeAgentLink()
    if (!link || agent.value) return
    try {
      const s = await agentStatus(link)
      agent.value = { link, version: s.agent_version, features: s.features ?? ['hardware'] }
      agentLost.value = false
    } catch {
      forgetAgentLink()
      agentLost.value = true
      return
    }
    if (status.value === 'scanning') return
    status.value = 'scanning'
    await scanWithAgent(agent.value, false)
  }

  function show(r: HardwareReport) {
    report.value = r
    origin.value = 'saved'
    storage.value = null // a saved hardware report has no live storage data
  }

  return { report, origin, status, steps, stepIds, agent, agentLost, storage, storageStatus, scan, show, connectAgent }
}

export function downloadJson(r: HardwareReport) {
  const blob = new Blob([JSON.stringify(r, null, 2)], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `device-check-${r.scan.created_at.slice(0, 19).replace(/[:T]/g, '-')}.json`
  document.body.appendChild(a)
  a.click()
  a.remove()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
