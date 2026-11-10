import { field, unavailable } from '../field'
import type { BrowserInfo } from '../types'

export async function collectBrowser(gl: { webgl: boolean; webgl2: boolean; webgpu: boolean }): Promise<BrowserInfo> {
  let quota: number | undefined
  let usage: number | undefined
  try {
    const est = await navigator.storage?.estimate?.()
    quota = est?.quota
    usage = est?.usage
  } catch {}

  const conn = (navigator as any).connection
  let battery: any = null
  try {
    battery = await (navigator as any).getBattery?.()
  } catch {}

  const nc = conn ? 'navigator.connection' : 'navigator.connection'
  const noConn = (k: string) => unavailable(`${nc}.${k}`, 'api_unsupported')

  return {
    user_agent: field(navigator.userAgent, 'navigator.userAgent', 'exact'),
    webgl: field(gl.webgl, "canvas.getContext('webgl')", 'exact'),
    webgl2: field(gl.webgl2, "canvas.getContext('webgl2')", 'exact'),
    webgpu: field(gl.webgpu, 'navigator.gpu.requestAdapter()', 'exact'),
    cookies_enabled: field(navigator.cookieEnabled, 'navigator.cookieEnabled', 'exact'),
    storage_quota_bytes: quota !== undefined ? field(quota, 'navigator.storage.estimate()', 'approximate', { note: 'quota_not_disk' }) : unavailable('navigator.storage.estimate()', 'api_unsupported'),
    storage_usage_bytes: usage !== undefined ? field(usage, 'navigator.storage.estimate()', 'exact') : unavailable('navigator.storage.estimate()', 'api_unsupported'),
    network_type: conn?.type ? field(conn.type as string, `${nc}.type`, 'exact') : noConn('type'),
    effective_type: conn?.effectiveType ? field(conn.effectiveType as string, `${nc}.effectiveType`, 'approximate') : noConn('effectiveType'),
    downlink_mbps: typeof conn?.downlink === 'number' ? field(conn.downlink as number, `${nc}.downlink`, 'approximate', { note: 'network_estimate' }) : noConn('downlink'),
    rtt_ms: typeof conn?.rtt === 'number' ? field(conn.rtt as number, `${nc}.rtt`, 'approximate', { note: 'network_estimate' }) : noConn('rtt'),
    battery_level: battery
      ? field(battery.level as number, 'navigator.getBattery()', 'exact', {
          note: battery.level === 1 && battery.charging && battery.chargingTime === 0 ? 'battery_ambiguous' : undefined,
        })
      : unavailable('navigator.getBattery()', 'api_unsupported'),
    battery_charging: battery ? field(Boolean(battery.charging), 'navigator.getBattery()', 'exact') : unavailable('navigator.getBattery()', 'api_unsupported'),
    touch_points: field(navigator.maxTouchPoints ?? 0, 'navigator.maxTouchPoints', 'exact'),
  }
}
