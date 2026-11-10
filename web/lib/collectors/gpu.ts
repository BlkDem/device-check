import { agentOnly, field, unavailable } from '../field'
import { guessAdapterType, normalizeVendor, parseRenderer } from '../parse'
import type { GpuInfo } from '../types'

export interface GpuProbe {
  gpu: GpuInfo
  webgl: boolean
  webgl2: boolean
  webgpu: boolean
}

interface GlProbe {
  renderer: string | null
  vendor: string | null
  source: string
}

function probeGl(type: 'webgl2' | 'webgl'): { ok: boolean; info: GlProbe | null } {
  try {
    const canvas = document.createElement('canvas')
    const gl = canvas.getContext(type) as WebGLRenderingContext | null
    if (!gl) return { ok: false, info: null }
    const ext = gl.getExtension('WEBGL_debug_renderer_info')
    const info: GlProbe = ext
      ? {
          renderer: String(gl.getParameter(ext.UNMASKED_RENDERER_WEBGL) ?? ''),
          vendor: String(gl.getParameter(ext.UNMASKED_VENDOR_WEBGL) ?? ''),
          source: 'WEBGL_debug_renderer_info',
        }
      : { renderer: String(gl.getParameter(gl.RENDERER) ?? ''), vendor: String(gl.getParameter(gl.VENDOR) ?? ''), source: `${type}.getParameter(RENDERER)` }
    gl.getExtension('WEBGL_lose_context')?.loseContext()
    return { ok: true, info }
  } catch {
    return { ok: false, info: null }
  }
}

interface WebGpuInfo {
  vendor?: string
  architecture?: string
  device?: string
  description?: string
}

async function probeWebGpu(): Promise<{ ok: boolean; info: WebGpuInfo | null }> {
  const gpu = (navigator as any).gpu
  if (!gpu) return { ok: false, info: null }
  try {
    const adapter = await Promise.race([
      gpu.requestAdapter(),
      new Promise((r) => setTimeout(() => r(null), 3000)),
    ]) as any
    if (!adapter) return { ok: false, info: null }
    const info: WebGpuInfo = adapter.info ?? (await adapter.requestAdapterInfo?.()) ?? null
    return { ok: true, info }
  } catch {
    return { ok: false, info: null }
  }
}

export async function collectGpu(): Promise<GpuProbe> {
  const gl2 = probeGl('webgl2')
  const gl1 = gl2.ok ? gl2 : probeGl('webgl')
  const webgl = gl2.ok || probeGl('webgl').ok
  const wgpu = await probeWebGpu()

  const glInfo = gl2.info ?? gl1.info
  const parsed = parseRenderer(glInfo?.renderer)
  const wVendor = wgpu.info?.vendor ? normalizeVendor(wgpu.info.vendor) ?? wgpu.info.vendor : null

  const apis = [webgl && 'WebGL 1.0', gl2.ok && 'WebGL 2.0', wgpu.ok && 'WebGPU'].filter(Boolean) as string[]
  const adapterType = guessAdapterType(parsed.vendor ?? wVendor, parsed.model, parsed.software)

  const gpu: GpuInfo = {
    vendor: wVendor
      ? field(wVendor, 'navigator.gpu adapter.info.vendor', 'exact')
      : field(parsed.vendor, glInfo?.source ?? 'WebGL', 'detected'),
    model: field(parsed.model, glInfo?.source ?? 'WebGL', parsed.approximate ? 'approximate' : 'detected', {
      note: parsed.approximate ? 'or_similar' : parsed.software ? 'software_renderer' : undefined,
      reason: glInfo ? 'not_exposed' : 'api_unsupported',
    }),
    architecture: field(wgpu.info?.architecture, 'navigator.gpu adapter.info.architecture', 'exact', {
      reason: wgpu.ok ? 'not_exposed' : 'api_unsupported',
    }),
    adapter_type: field(adapterType, 'heuristic (renderer string)', 'detected', { note: 'heuristic' }),
    dedicated_vram_bytes: agentOnly(),
    shared_memory_bytes: agentOnly(),
    driver_version: agentOnly(),
    driver_date: agentOnly(),
    apis: field(apis, 'canvas.getContext / navigator.gpu', 'exact', { note: 'browser_apis' }),
    pci_device_id: parsed.pciDeviceId ? field(parsed.pciDeviceId, `${glInfo?.source} (ANGLE)`, 'detected') : agentOnly(),
    renderer_raw: glInfo?.renderer ? field(glInfo.renderer, glInfo.source, 'exact') : unavailable('WebGL', 'api_unsupported'),
  }
  return { gpu, webgl, webgl2: gl2.ok, webgpu: wgpu.ok }
}
