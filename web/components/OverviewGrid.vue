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
    <a v-for="tile in tiles" :key="tile.key" :href="`#${tile.anchor}`" class="card tile"
      :class="[{ off: !tile.value }, tile.value ? tile.field.confidence : 'unavailable']">
      <div class="head">
        <HwIcon :name="tile.icon" :size="18" />
        <span>{{ t(`overview.${tile.key}`) }}</span>
        <span class="ix" aria-hidden="true" />
      </div>
      <div class="value" :title="tile.value ?? ''">{{ tile.value ?? t('status.unavailable_value') }}</div>
      <div class="foot">
        <ConfidenceBadge :c="tile.value ? tile.field.confidence : 'unavailable'" />
        <span class="meter" aria-hidden="true"><i v-for="n in 8" :key="n" /></span>
      </div>
    </a>
  </div>
</template>

<style scoped>
.grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(250px, 100%), 1fr)); gap: 12px; counter-reset: tile; }
.tile {
  --tc: var(--c-unavailable);
  counter-increment: tile;
  display: grid; gap: 10px; align-content: start; padding: 14px 16px 14px; color: inherit; min-width: 0;
  transition: transform .15s;
}
.tile.exact { --tc: var(--c-exact); }
.tile.approximate { --tc: var(--c-approximate); }
.tile.detected { --tc: var(--c-detected); }
.tile:hover, .tile:focus-visible {
  text-decoration: none; transform: translateY(-2px);
  --frame: linear-gradient(135deg, var(--accent), var(--accent-line) 30%, var(--magenta-line) 70%, var(--magenta));
  --bracket: var(--magenta);
  --fill: linear-gradient(180deg, rgba(20, 36, 54, .96), rgba(9, 14, 24, .97));
}
.tile:focus-visible { outline: none; }
.tile:hover .value, .tile:focus-visible .value { animation: hud-glitch .26s steps(3) 1; }
.head {
  display: flex; align-items: center; gap: 8px; color: var(--accent);
  font: 600 11px/1 var(--mono); text-transform: uppercase; letter-spacing: .16em;
}
.head span { color: var(--text-2); }
.head .ix { margin-left: auto; color: var(--text-3); letter-spacing: .04em; }
.head .ix::before { content: '[' counter(tile, decimal-leading-zero) ']'; }
.value {
  font: 600 17px/1.25 var(--display); letter-spacing: .02em; color: var(--text);
  overflow-wrap: anywhere; min-height: 1.25em;
}
.off .value { color: var(--text-3); font-weight: 500; font-style: italic; font-family: var(--mono); font-size: 13.5px; }
.off .head { color: var(--text-3); }
.foot { display: flex; align-items: center; gap: 10px; justify-content: space-between; min-width: 0; }
.meter { display: flex; gap: 2px; flex: 0 1 64px; min-width: 0; }
.meter i { flex: 1; height: 6px; background: var(--tc); opacity: .85; transform: skewX(-20deg); }
.approximate .meter i:nth-child(n + 6), .detected .meter i:nth-child(n + 7) { opacity: .15; }
.off .meter i { opacity: .15; }
@media (max-width: 640px) {
  .grid { grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 8px; }
  .tile { padding: 12px 12px; gap: 8px; }
  .value { font-size: 14.5px; }
  .meter { display: none; }
}
@media (max-width: 380px) {
  .grid { grid-template-columns: minmax(0, 1fr); }
}
</style>
