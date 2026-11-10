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
          <td class="mono">{{ v.letter.value ?? '—' }}</td>
          <td>{{ v.filesystem.value ?? '—' }}</td>
          <td>{{ b(v.size_bytes.value) }}</td>
          <td>{{ b(v.free_bytes.value) }}</td>
          <td class="bar-cell">
            <div v-if="usedPct(v) !== null" class="bar"><i :style="{ width: usedPct(v) + '%' }" /></div>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.tbl-wrap { overflow-x: auto; }
table { width: 100%; border-collapse: collapse; font-size: 14px; }
th, td { text-align: left; padding: 8px 10px; border-bottom: 1px solid var(--border); }
th { color: var(--text-3); font-weight: 500; font-size: 12.5px; }
.bar-cell { min-width: 120px; }
.bar { height: 6px; border-radius: 3px; background: var(--surface-3); overflow: hidden; }
.bar i { display: block; height: 100%; background: var(--accent); }
</style>
