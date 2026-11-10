<script setup lang="ts">
import { formatBytes } from '~/lib/format'
import { spaceLevel, type StorageDevice, type StorageHealthReport, type StorageVolume } from '~/lib/storage'
import type { Field } from '~/lib/types'

const props = defineProps<{ report: StorageHealthReport }>()
const { t, locale } = useI18n()

const num = (n: number) => n.toLocaleString(locale.value)
const has = <T,>(f?: Field<T>): f is Field<T> & { value: T } => !!f && f.confidence !== 'unavailable' && f.value !== null
const why = (f?: Field) => (f?.reason ? t(`reason.${f.reason}`) : t('status.unavailable_value'))

interface Metric { key: string; value: string | null; field?: Field; sub?: string; bar?: number; level?: string }

function metrics(d: StorageDevice): Metric[] {
  const s = d.smart
  const tp = d.temperature
  const m: Metric[] = []
  const limits = [has(tp.warning_celsius) ? `${t('sh.warn')} ${tp.warning_celsius.value} °C` : '', has(tp.critical_celsius) ? `${t('sh.crit')} ${tp.critical_celsius.value} °C` : '']
    .filter(Boolean).join(' · ')
  const temp = tp.current_celsius
  m.push({
    key: 'temperature', field: temp,
    value: has(temp) ? `${temp.value} °C` : null,
    sub: limits ? `${limits} (${t('sh.manufacturer')})` : undefined,
    level: has(temp) && has(tp.critical_celsius) && temp.value >= tp.critical_celsius.value ? 'critical'
      : has(temp) && has(tp.warning_celsius) && temp.value >= tp.warning_celsius.value ? 'warning' : undefined,
  })
  m.push({ key: 'os_status', field: d.health.os_status, value: has(d.health.os_status) ? t(`enum.${d.health.os_status.value}`) : null,
    level: d.health.os_status.value === 'healthy' ? 'ok' : d.health.os_status.value === 'unhealthy' ? 'critical' : d.health.os_status.value === 'warning' ? 'warning' : undefined })
  if (has(s.percentage_used)) m.push({ key: 'percentage_used', field: s.percentage_used, value: `${s.percentage_used.value}%`, bar: Math.min(100, s.percentage_used.value), sub: t('sh.used_hint') })
  else m.push({ key: 'percentage_used', field: s.percentage_used, value: null })
  if (has(s.available_spare_percent)) m.push({ key: 'spare', field: s.available_spare_percent, value: `${s.available_spare_percent.value}%`,
    sub: has(s.available_spare_threshold_percent) ? `${t('sh.threshold')} ${s.available_spare_threshold_percent.value}%` : undefined })
  m.push({ key: 'power_on', field: s.power_on_hours, value: has(s.power_on_hours) ? `${num(s.power_on_hours.value)} ${t('sh.h')}` : null,
    sub: has(s.power_on_hours) ? `≈ ${num(Math.round(s.power_on_hours.value / 24))} ${t('sh.days')}` : undefined })
  m.push({ key: 'written', field: s.data_written_bytes, value: has(s.data_written_bytes) ? formatBytes(s.data_written_bytes.value, t) : null,
    sub: has(s.data_read_bytes) ? `${t('sh.read')} ${formatBytes(s.data_read_bytes.value, t)}` : undefined })
  m.push({ key: 'media_errors', field: s.media_errors, value: has(s.media_errors) ? num(s.media_errors.value) : null,
    level: has(s.media_errors) ? (s.media_errors.value > 0 ? 'warning' : 'ok') : undefined })
  for (const [key, f] of [['reallocated', s.reallocated_sectors], ['pending', s.pending_sectors], ['uncorrectable', s.uncorrectable_sectors], ['crc', s.crc_errors]] as const)
    if (has(f)) m.push({ key, field: f, value: num(f.value), level: f.value > 0 ? 'warning' : 'ok' })
  if (has(s.power_cycles)) m.push({ key: 'power_cycles', field: s.power_cycles, value: num(s.power_cycles.value),
    sub: has(s.unsafe_shutdowns) ? `${t('sh.unsafe')} ${num(s.unsafe_shutdowns.value)}` : undefined })
  return m
}

const volumes = (v: StorageVolume[]) => v.filter((x) => has(x.total_bytes))
const level = (v: StorageVolume) => (has(v.free_percent) ? spaceLevel(v.free_percent.value) : 'normal')
const devices = computed(() => props.report.storage_health.devices)
const warnings = (d: StorageDevice) => (has(d.smart.nvme?.critical_warning_flags) ? d.smart.nvme!.critical_warning_flags.value : [])
</script>

<template>
  <div class="sh">
    <div v-for="d in devices" :key="d.id" class="disk card">
      <div class="dh">
        <div>
          <div class="model">{{ d.model.value ?? t('status.unavailable_value') }}</div>
          <div class="chips mono">
            <span v-if="has(d.type)" class="chip">{{ t(`sh.type.${d.type.value}`) }}</span>
            <span v-if="has(d.capacity_bytes)" class="chip">{{ formatBytes(d.capacity_bytes.value, t) }}</span>
            <span v-if="d.is_system.value" class="chip sys">{{ t('sh.system_disk') }}</span>
            <span class="chip dim">{{ t('sh.disk') }} {{ d.disk_number.value }}</span>
          </div>
        </div>
        <div class="iface mono" :title="d.smart.interface.source">
          SMART: {{ has(d.smart.interface) ? t(`sh.iface.${d.smart.interface.value}`) : why(d.smart.interface) }}
        </div>
      </div>

      <p v-if="warnings(d).length" class="notice warn"><HwIcon name="alert" :size="16" />{{ t('sh.nvme_warning') }}: {{ warnings(d).map((w) => t(`sh.cw.${w}`)).join(', ') }}</p>

      <div class="metrics">
        <div v-for="m in metrics(d)" :key="m.key" class="metric" :class="[m.level, { off: m.value === null }]" :title="m.field?.source">
          <div class="mk">{{ t(`sh.m.${m.key}`) }}</div>
          <div class="mv mono">{{ m.value ?? why(m.field) }}</div>
          <div v-if="m.bar !== undefined" class="mbar"><i :style="{ width: m.bar + '%' }" /></div>
          <div v-if="m.sub" class="ms">{{ m.sub }}</div>
        </div>
      </div>

      <div v-if="volumes(d.volumes).length" class="vols">
        <div v-for="v in volumes(d.volumes)" :key="v.id" class="vol" :class="level(v)">
          <span class="vl mono">{{ v.letter.value ?? '—' }}</span>
          <span class="vn">{{ v.label.value ?? '' }} <span class="dim mono">{{ v.filesystem.value ?? '' }}</span></span>
          <span class="vt"><i :style="{ width: (v.used_percent.value ?? 0) + '%' }" /></span>
          <span class="vf mono">{{ t('sh.free') }} {{ formatBytes(v.free_bytes.value ?? 0, t) }} / {{ formatBytes(v.total_bytes.value ?? 0, t) }} · {{ v.free_percent.value }}%</span>
        </div>
      </div>

      <details v-if="d.smart.ata?.attributes.length" class="attrs">
        <summary>{{ t('sh.attributes') }} ({{ d.smart.ata.attributes.length }})</summary>
        <div class="tbl-wrap">
          <table>
            <thead><tr><th>ID</th><th>{{ t('sh.attr_name') }}</th><th>{{ t('sh.norm') }}</th><th>{{ t('sh.worst') }}</th><th>{{ t('sh.thr') }}</th><th>Raw</th><th>{{ t('sh.value') }}</th></tr></thead>
            <tbody>
              <tr v-for="a in d.smart.ata.attributes" :key="a.id" :class="{ fail: has(a.threshold) && has(a.normalized) && a.threshold.value > 0 && a.normalized.value <= a.threshold.value }">
                <td class="mono">{{ a.id }}</td>
                <td>{{ a.name ?? '?' }}<span v-if="a.semantics === 'vendor_specific'" class="dim"> · {{ t('sh.vendor') }}</span></td>
                <td class="mono">{{ a.normalized.value ?? '—' }}</td>
                <td class="mono">{{ a.worst.value ?? '—' }}</td>
                <td class="mono">{{ a.threshold.value ?? '—' }}</td>
                <td class="mono">{{ a.raw.value ?? '—' }}</td>
                <td class="mono">{{ has(a.value) ? num(a.value.value) : '—' }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </details>
    </div>

    <div v-if="volumes(report.storage_health.other_volumes).length" class="card disk">
      <div class="model">{{ t('sh.other_volumes') }}</div>
      <div class="vols">
        <div v-for="v in volumes(report.storage_health.other_volumes)" :key="v.id" class="vol" :class="level(v)">
          <span class="vl mono">{{ v.letter.value ?? '—' }}</span>
          <span class="vn">{{ v.label.value ?? '' }} <span class="dim mono">{{ v.filesystem.value ?? '' }}</span></span>
          <span class="vt"><i :style="{ width: (v.used_percent.value ?? 0) + '%' }" /></span>
          <span class="vf mono">{{ t('sh.free') }} {{ formatBytes(v.free_bytes.value ?? 0, t) }} / {{ formatBytes(v.total_bytes.value ?? 0, t) }} · {{ v.free_percent.value }}%</span>
        </div>
      </div>
    </div>

    <ul v-if="report.limitations.length" class="lims">
      <li v-for="l in report.limitations" :key="l.code"><span class="mono tag">{{ l.section }}</span><span>{{ t(`lim.${l.code}`) }}</span></li>
    </ul>
    <p class="muted small">{{ t('sh.engine_note') }}</p>
  </div>
</template>

<style scoped>
.sh { display: grid; gap: 14px; }
.disk { padding: 16px; display: grid; gap: 14px; }
.dh { display: flex; justify-content: space-between; gap: 12px; flex-wrap: wrap; align-items: flex-start; }
.model { font: 600 16px/1.3 var(--display); letter-spacing: .04em; color: var(--text); }
.chips { display: flex; gap: 6px; flex-wrap: wrap; margin-top: 6px; font-size: 11px; }
.chip { padding: 2px 7px; box-shadow: inset 0 0 0 1px var(--border-strong); color: var(--text-2); text-transform: uppercase; letter-spacing: .08em; }
.chip.sys { color: var(--accent); box-shadow: inset 0 0 0 1px var(--accent-line); }
.chip.dim, .dim { color: var(--text-3); }
.iface { font-size: 11.5px; color: var(--text-2); letter-spacing: .06em; }
.metrics { display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: 8px; }
.metric { padding: 10px 12px; background: rgba(8, 12, 20, .6); box-shadow: inset 0 0 0 1px var(--border); min-width: 0; }
.metric.ok { box-shadow: inset 2px 0 0 var(--c-exact), inset 0 0 0 1px var(--border); }
.metric.warning { box-shadow: inset 2px 0 0 var(--c-approximate), inset 0 0 0 1px var(--border); }
.metric.critical { box-shadow: inset 2px 0 0 var(--danger), inset 0 0 0 1px rgba(255, 77, 109, .45); }
.mk { font: 600 10.5px/1.3 var(--mono); text-transform: uppercase; letter-spacing: .1em; color: var(--text-3); }
.mv { margin-top: 4px; font-size: 16px; color: var(--text); word-break: break-word; }
.off .mv { font-size: 12px; color: var(--text-3); font-style: italic; }
.ms { margin-top: 3px; font-size: 11px; color: var(--text-3); }
.mbar { margin-top: 6px; height: 4px; background: var(--border); }
.mbar i { display: block; height: 100%; background: var(--accent); box-shadow: 0 0 6px var(--accent); }
.vols { display: grid; gap: 8px; }
.vol { display: grid; grid-template-columns: 44px minmax(0, 1fr) minmax(120px, 2fr) auto; gap: 10px; align-items: center; font-size: 13px; }
.vl { color: var(--accent); }
.vn { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.vt { height: 8px; background: var(--border); position: relative; }
.vt i { position: absolute; inset: 0 auto 0 0; background: var(--c-exact); }
.vol.warning .vt i { background: var(--c-approximate); }
.vol.critical .vt i, .vol.emergency .vt i { background: var(--danger); box-shadow: 0 0 6px var(--danger); }
.vol.emergency .vf { color: var(--danger); }
.vf { font-size: 11.5px; color: var(--text-2); white-space: nowrap; }
.attrs summary { cursor: pointer; font: 600 11.5px var(--mono); text-transform: uppercase; letter-spacing: .1em; color: var(--text-2); }
.attrs table { margin-top: 8px; width: 100%; font-size: 12.5px; border-collapse: collapse; }
.attrs th, .attrs td { text-align: left; padding: 4px 8px; border-top: 1px dashed var(--border); }
.attrs tr.fail td { color: var(--danger); }
.tbl-wrap { overflow-x: auto; }
.lims { margin: 0; padding: 0; list-style: none; display: grid; gap: 6px; font-size: 13px; }
.lims li { display: flex; gap: 10px; }
.tag { color: var(--yellow); font-size: 11px; text-transform: uppercase; }
@media (max-width: 640px) {
  .vol { grid-template-columns: 36px minmax(0, 1fr); }
  .vt, .vf { grid-column: 1 / -1; }
}
</style>
