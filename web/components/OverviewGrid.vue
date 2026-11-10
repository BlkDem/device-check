<script setup lang="ts">
import { formatBytes, formatValue } from '~/lib/format'
import { unavailable } from '~/lib/field'
import type { Field, HardwareReport } from '~/lib/types'

const props = defineProps<{ report: HardwareReport }>()
const { t, has, locale } = useI18n()

interface Tile { key: string; icon: string; anchor: string; value: string | null; field: Field }

/** Joins text of several fields; confidence = weakest present one. */
function join(fields: Field[], sep = ' '): { value: string | null; field: Field } {
  const present = fields.filter((f) => f.confidence !== 'unavailable')
  if (!present.length) return { value: null, field: fields[0] ?? unavailable('—') }
  const order = ['exact', 'detected', 'approximate']
  const weakest = present.reduce((a, b) => (order.indexOf(b.confidence) > order.indexOf(a.confidence) ? b : a))
  return { value: present.map((f) => String(f.value)).join(sep), field: weakest }
}

const tiles = computed<Tile[]>(() => {
  const r = props.report
  const gpu = r.gpu[0]
  const disp = r.displays[0]
  const sysDisk = r.storage.find((s) => s.is_system.value) ?? r.storage[0]

  const cpu = r.cpu.model.confidence !== 'unavailable'
    ? { value: String(r.cpu.model.value), field: r.cpu.model }
    : r.cpu.threads_logical.value
      ? { value: t('overview.threads', { n: r.cpu.threads_logical.value }), field: r.cpu.threads_logical }
      : { value: null, field: r.cpu.model }
  const ram = r.memory.total_bytes
  const res = disp?.resolution

  return [
    { key: 'computer', icon: 'computer', anchor: 'motherboard', ...join([r.motherboard.system_manufacturer, r.motherboard.system_model]) },
    { key: 'cpu', icon: 'cpu', anchor: 'cpu', ...cpu },
    { key: 'board', icon: 'board', anchor: 'motherboard', ...join([r.motherboard.manufacturer, r.motherboard.model]) },
    { key: 'ram', icon: 'memory', anchor: 'memory', value: ram.value ? formatValue(ram, 'bytes', t, has, locale.value) : null, field: ram },
    { key: 'gpu', icon: 'gpu', anchor: 'gpu', ...(gpu ? join([gpu.model]) : { value: null, field: unavailable('WebGL', 'api_unsupported') }) },
    {
      key: 'disk', icon: 'storage', anchor: 'storage',
      ...(sysDisk ? { value: [sysDisk.model.value, sysDisk.size_bytes.value ? formatBytes(sysDisk.size_bytes.value, t) : null].filter(Boolean).join(' · ') || null, field: sysDisk.model } : { value: null, field: unavailable('—', 'agent_required') }),
    },
    { key: 'os', icon: 'system', anchor: 'system', ...join([r.system.os_name, r.system.architecture], ' · ') },
    {
      key: 'screen', icon: 'display', anchor: 'displays',
      value: res?.value ? `${formatValue(res, 'res', t, has, locale.value)}${disp?.refresh_hz.value ? ` · ${disp.refresh_hz.value} ${t('unit.hz')}` : ''}` : null,
      field: res ?? unavailable('screen', 'api_unsupported'),
    },
  ]
})
</script>

<template>
  <div class="grid">
    <a v-for="tile in tiles" :key="tile.key" :href="`#${tile.anchor}`" class="card tile" :class="{ off: !tile.value }">
      <div class="head">
        <HwIcon :name="tile.icon" :size="18" />
        <span>{{ t(`overview.${tile.key}`) }}</span>
      </div>
      <div class="value" :title="tile.value ?? ''">{{ tile.value ?? t('status.unavailable_value') }}</div>
      <ConfidenceBadge :c="tile.value ? tile.field.confidence : 'unavailable'" />
    </a>
  </div>
</template>

<style scoped>
.grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(250px, 1fr)); gap: 12px; }
.tile {
  display: grid; gap: 8px; align-content: start; justify-items: start; padding: 14px 16px; color: inherit;
  transition: border-color .15s, transform .15s;
}
.tile:hover { text-decoration: none; border-color: var(--border-strong); transform: translateY(-1px); }
.head { display: flex; align-items: center; gap: 8px; color: var(--accent); font-size: 12.5px; font-weight: 600; text-transform: uppercase; letter-spacing: .05em; }
.head span { color: var(--text-2); }
.value { font-size: 16px; font-weight: 600; line-height: 1.3; overflow-wrap: anywhere; }
.off .value { color: var(--text-3); font-weight: 500; font-style: italic; }
.off .head { color: var(--text-3); }
@media (max-width: 640px) {
  .grid { grid-template-columns: 1fr 1fr; gap: 8px; }
  .tile { padding: 12px; }
  .value { font-size: 14px; }
}
@media (max-width: 380px) {
  .grid { grid-template-columns: 1fr; }
}
</style>
