import { describe, expect, it } from 'vitest'
import { detectArch, detectBrowser, detectDeviceType, detectOs, guessAdapterType, parseRenderer, refreshFromIntervals } from '../lib/parse'

const UA_CHROME_WIN = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36'
const UA_FF_WIN = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:132.0) Gecko/20100101 Firefox/132.0'
const UA_SAFARI_MAC = 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15'
const UA_IPHONE = 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_4 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Mobile/15E148 Safari/604.1'
const UA_ANDROID = 'Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36'

describe('detectOs', () => {
  it('Windows 11 via client hints', () => {
    const r = detectOs({ platform: 'Windows', platformVersion: '15.0.0' }, UA_CHROME_WIN)
    expect(r.name).toBe('Windows 11')
    expect(r.confidence).toBe('exact')
  })
  it('Windows 10 via client hints', () => {
    expect(detectOs({ platform: 'Windows', platformVersion: '10.0.0' }, UA_CHROME_WIN).name).toBe('Windows 10')
  })
  it('Windows from frozen UA is approximate', () => {
    const r = detectOs({}, UA_FF_WIN)
    expect(r.name).toBe('Windows 10 / 11')
    expect(r.confidence).toBe('approximate')
  })
  it('iOS / Android / macOS from UA', () => {
    expect(detectOs({}, UA_IPHONE)).toMatchObject({ name: 'iOS', version: '17.4' })
    expect(detectOs({}, UA_ANDROID)).toMatchObject({ name: 'Android', version: '14' })
    expect(detectOs({}, UA_SAFARI_MAC)).toMatchObject({ name: 'macOS', confidence: 'approximate' })
  })
})

describe('detectBrowser', () => {
  it('prefers Edge over Chromium brands', () => {
    const r = detectBrowser(
      { fullVersionList: [{ brand: 'Chromium', version: '130.0.6723.70' }, { brand: 'Microsoft Edge', version: '130.0.2849.56' }, { brand: 'Not?A_Brand', version: '99.0.0.0' }] },
      UA_CHROME_WIN,
    )
    expect(r.name.value).toBe('Microsoft Edge')
    expect(r.version.value).toBe('130.0.2849.56')
  })
  it('Firefox from UA', () => {
    const r = detectBrowser({}, UA_FF_WIN)
    expect(r.name.value).toBe('Firefox')
    expect(r.version).toMatchObject({ value: '132.0', confidence: 'exact' })
  })
})

describe('detectArch / device type', () => {
  it('maps client hints', () => {
    expect(detectArch({ architecture: 'x86', bitness: '64' }, '').value).toBe('x86-64')
    expect(detectArch({ architecture: 'arm', bitness: '64' }, '').value).toBe('ARM64')
  })
  it('falls back to UA', () => {
    expect(detectArch({}, UA_FF_WIN)).toMatchObject({ value: 'x86-64', confidence: 'detected' })
  })
  it('device types', () => {
    expect(detectDeviceType({}, UA_CHROME_WIN, 0)).toBe('desktop')
    expect(detectDeviceType({}, UA_IPHONE, 5)).toBe('mobile')
    expect(detectDeviceType({}, UA_SAFARI_MAC, 5)).toBe('tablet') // iPadOS desktop-mode UA
  })
})

describe('parseRenderer', () => {
  it('ANGLE / Direct3D NVIDIA', () => {
    const r = parseRenderer('ANGLE (NVIDIA, NVIDIA GeForce RTX 3070 (0x00002484) Direct3D11 vs_5_0 ps_5_0, D3D11)')
    expect(r).toMatchObject({ vendor: 'NVIDIA', model: 'NVIDIA GeForce RTX 3070', pciDeviceId: '0x2484', software: false })
  })
  it('ANGLE Intel with (R)', () => {
    const r = parseRenderer('ANGLE (Intel, Intel(R) UHD Graphics 630 (0x00003E92) Direct3D11 vs_5_0 ps_5_0, D3D11)')
    expect(r.model).toBe('Intel UHD Graphics 630')
    expect(guessAdapterType(r.vendor, r.model, r.software)).toBe('integrated')
  })
  it('Firefox "or similar"', () => {
    const r = parseRenderer('ANGLE (NVIDIA, NVIDIA GeForce GTX 980 Direct3D11 vs_5_0 ps_5_0), or similar')
    expect(r).toMatchObject({ model: 'NVIDIA GeForce GTX 980', approximate: true })
  })
  it('Apple Metal', () => {
    const r = parseRenderer('ANGLE (Apple, ANGLE Metal Renderer: Apple M2 Pro, Unspecified Version)')
    expect(r).toMatchObject({ vendor: 'Apple', model: 'Apple M2 Pro' })
  })
  it('Linux Mesa / software', () => {
    expect(parseRenderer('NVIDIA GeForce GTX 1060 6GB/PCIe/SSE2').model).toBe('NVIDIA GeForce GTX 1060 6GB')
    expect(parseRenderer('ANGLE (Google, Vulkan 1.3.0 (SwiftShader Device (Subzero) (0x0000C0DE)), SwiftShader driver)').software).toBe(true)
  })
  it('AMD types', () => {
    expect(guessAdapterType('AMD', 'AMD Radeon RX 7800 XT', false)).toBe('discrete')
    expect(guessAdapterType('AMD', 'AMD Radeon Graphics', false)).toBe('integrated')
    expect(guessAdapterType('Intel', 'Intel Arc A770', false)).toBe('discrete')
    expect(guessAdapterType(null, null, false)).toBeNull()
  })
})

describe('refreshFromIntervals', () => {
  it('snaps to common rates', () => {
    expect(refreshFromIntervals(Array(40).fill(16.67))).toBe(60)
    expect(refreshFromIntervals(Array(40).fill(6.94))).toBe(144)
    expect(refreshFromIntervals([16, 17])).toBeNull()
  })
})
