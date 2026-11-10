<script setup lang="ts">
import { formatBytes } from '~/lib/format'
import type { Volume } from '~/lib/types'

defineProps<{ volumes: Volume[] }>()
const { t } = useI18n()
const b = (n: number | null) => (n === null ? '—' : formatBytes(n, t))
const usedPct = (v: Volume) =>
  v.size_bytes.value && v.free_bytes.value !== null ? Math.round(((v.size_bytes.value - v.free_bytes.value) / v.size_bytes.value) * 100) : null
</script>

<template>
  <div class="tbl-wrap">
    <table>
      <thead>
        <tr>
          <th>{{ t('f.letter') }}</th>
          <th>{{ t('f.filesystem') }}</th>
          <th>{{ t('f.size_bytes') }}</th>
          <th>{{ t('f.free_bytes') }}</th>
          <th>{{ t('f.used') }}</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="(v, i) in volumes" :key="i">
          <td class="mono letter">{{ v.letter.value ?? '—' }}</td>
          <td class="mono">{{ v.filesystem.value ?? '—' }}</td>
          <td class="mono">{{ b(v.size_bytes.value) }}</td>
          <td class="mono">{{ b(v.free_bytes.value) }}</td>
          <td class="bar-cell">
            <div v-if="usedPct(v) !== null" class="bar" :class="{ hot: (usedPct(v) ?? 0) >= 90 }">
              <span class="track"><i :style="{ width: usedPct(v) + '%' }" /></span>
              <span class="mono pc">{{ usedPct(v) }}%</span>
            </div>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.tbl-wrap { overflow-x: auto; }
table { width: 100%; border-collapse: collapse; font-size: 13.5px; }
th, td { text-align: left; padding: 9px 10px; border-bottom: 1px dashed rgba(43, 66, 96, .6); }
th {
  color: var(--text-3); font: 600 10.5px/1.2 var(--mono); text-transform: uppercase; letter-spacing: .14em;
  border-bottom: 1px solid var(--border-strong);
}
tbody tr:hover { background: rgba(25, 230, 255, .035); }
.letter { color: var(--accent); font-weight: 700; }
.bar-cell { min-width: 150px; }
.bar { display: flex; align-items: center; gap: 8px; }
.track {
  flex: 1; height: 8px; position: relative; background: var(--surface-3);
  -webkit-mask: repeating-linear-gradient(90deg, #000 0 5px, transparent 5px 7px);
  mask: repeating-linear-gradient(90deg, #000 0 5px, transparent 5px 7px);
}
.track i { display: block; height: 100%; background: linear-gradient(90deg, var(--accent-2), var(--accent)); }
.hot .track i { background: linear-gradient(90deg, var(--c-approximate), var(--danger)); }
.pc { font-size: 11px; color: var(--text-2); min-width: 3.2em; text-align: right; }
</style>
