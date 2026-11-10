import type { HardwareReport } from '~/lib/types'

const KEY = 'dc:reports:v1'
const MAX = 10

export interface SavedReport {
  id: string
  created_at: string
  saved_at: string
  report: HardwareReport
}

function read(): SavedReport[] {
  try {
    const raw = localStorage.getItem(KEY)
    const list = raw ? JSON.parse(raw) : []
    return Array.isArray(list) ? list.filter((x) => x?.report?.schema_version) : []
  } catch {
    return []
  }
}

function write(list: SavedReport[]): boolean {
  try {
    localStorage.setItem(KEY, JSON.stringify(list))
    return true
  } catch {
    return false
  }
}

/** Reports saved in this browser only (localStorage). Never leaves the device. */
export function useSavedReports() {
  const saved = useState<SavedReport[]>('savedReports', () => [])

  function refresh() {
    saved.value = read()
  }

  function save(r: HardwareReport): boolean {
    const list = read().filter((x) => x.id !== r.scan.id)
    list.unshift({ id: r.scan.id, created_at: r.scan.created_at, saved_at: new Date().toISOString(), report: r })
    const ok = write(list.slice(0, MAX))
    if (ok) refresh()
    return ok
  }

  function remove(id: string) {
    write(read().filter((x) => x.id !== id))
    refresh()
  }

  return { saved, refresh, save, remove }
}
