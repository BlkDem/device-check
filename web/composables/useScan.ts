import { runBrowserScan, SCAN_STEPS, type ScanStepId, type StepStatus } from '~/lib/collectors'
import type { HardwareReport } from '~/lib/types'

export type ScanState = 'idle' | 'scanning' | 'done' | 'error'

/** Shared scan state. "origin" tells whether the shown report is a fresh scan or a saved one. */
export function useScan() {
  const report = useState<HardwareReport | null>('report', () => null)
  const origin = useState<'scan' | 'saved' | null>('reportOrigin', () => null)
  const status = useState<ScanState>('scanStatus', () => 'idle')
  const steps = useState<Record<ScanStepId, StepStatus>>('scanSteps', () =>
    Object.fromEntries(SCAN_STEPS.map((s) => [s, 'pending'])) as Record<ScanStepId, StepStatus>,
  )

  async function scan() {
    if (status.value === 'scanning') return
    status.value = 'scanning'
    steps.value = Object.fromEntries(SCAN_STEPS.map((s) => [s, 'pending'])) as Record<ScanStepId, StepStatus>
    try {
      report.value = await runBrowserScan((id, s) => (steps.value = { ...steps.value, [id]: s }))
      origin.value = 'scan'
      status.value = 'done'
    } catch (e) {
      console.error('[device-check] scan failed', e)
      status.value = 'error'
    }
  }

  function show(r: HardwareReport) {
    report.value = r
    origin.value = 'saved'
  }

  return { report, origin, status, steps, scan, show }
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
