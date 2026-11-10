<script setup lang="ts">
import { formatBytes } from '~/lib/format'
import type { MemoryInfo, MemoryModule } from '~/lib/types'

const props = defineProps<{ memory: MemoryInfo }>()
const { t } = useI18n()

/** Slot count: firmware value if known, else the number of modules seen. Never guessed. */
const total = computed(() => props.memory.slots_total.value ?? (props.memory.modules.length || null))
const slots = computed<(MemoryModule | null)[]>(() => {
  const n = total.value ?? 0
  const mods = props.memory.modules.filter((m) => (m.size_bytes.value ?? 0) > 0)
  return Array.from({ length: Math.max(n, mods.length) }, (_, i) => mods[i] ?? null)
})
const fromSmbios = computed(() => /smbios|Win32_PhysicalMemoryArray/i.test(props.memory.slots_total.source))
const chips = [0, 1, 2, 3, 4, 5, 6, 7]
</script>

<template>
  <div class="slots-wrap">
    <div v-if="!total" class="unknown">
      <svg viewBox="0 0 220 64" class="ghost" aria-hidden="true">
        <rect x="2" y="8" width="216" height="44" rx="4" />
        <rect v-for="i in chips" :key="i" :x="14 + i * 25" y="18" width="16" height="22" rx="2" class="chip" />
        <path d="M100 52v6M120 52v6" />
        <text x="110" y="36" text-anchor="middle">?</text>
      </svg>
      <div>
        <strong>{{ t('slots.unknown') }}</strong>
        <p class="muted">{{ t('slots.unknown_desc') }}</p>
      </div>
    </div>

    <template v-else>
      <div class="slots" role="list">
        <div v-for="(m, i) in slots" :key="i" class="slot" :class="{ used: !!m, soldered: m?.soldered.value }" role="listitem">
          <svg viewBox="0 0 40 160" aria-hidden="true">
            <rect class="socket" x="4" y="2" width="32" height="156" rx="3" />
            <template v-if="m">
              <rect class="stick" x="8" y="8" width="24" height="144" rx="2" />
              <rect v-for="c in 6" :key="c" class="chip" x="12" :y="14 + (c - 1) * 22" width="16" height="15" rx="1.5" />
            </template>
          </svg>
          <div class="cap">
            <span class="mono">{{ m?.slot.value ?? `#${i + 1}` }}</span>
            <span v-if="m" class="size">{{ m.size_bytes.value ? formatBytes(m.size_bytes.value, t) : '' }}</span>
            <span class="state">{{ m ? (m.soldered.value ? t('slots.onboard') : t('slots.used')) : t('slots.empty') }}</span>
          </div>
        </div>
      </div>
      <p v-if="fromSmbios" class="notice warn"><HwIcon name="alert" :size="16" />{{ t('slots.smbios_warning') }}</p>
    </template>
  </div>
</template>

<style scoped>
.unknown { display: flex; align-items: center; gap: 16px; padding: 14px; border: 1px dashed var(--border-strong); border-radius: var(--radius-sm); background: var(--bg-2); }
.unknown p { font-size: 13px; }
.ghost { width: 180px; flex: none; fill: none; stroke: var(--text-3); stroke-width: 1.5; stroke-dasharray: 4 3; }
.ghost .chip { stroke-dasharray: none; stroke: var(--border-strong); }
.ghost text { fill: var(--text-2); stroke: none; font: 600 22px var(--font); }
.slots { display: flex; gap: 14px; flex-wrap: wrap; }
.slot { display: grid; justify-items: center; gap: 6px; width: 72px; }
.slot svg { width: 40px; height: 160px; }
.socket { fill: var(--bg-2); stroke: var(--border-strong); stroke-dasharray: 4 3; }
.used .socket { stroke-dasharray: none; stroke: var(--accent-2); }
.stick { fill: #10324a; stroke: var(--accent); }
.chip { fill: #1d4f73; }
.soldered .stick { fill: #2b2a14; stroke: var(--c-approximate); }
.soldered .chip { fill: #4a4520; }
.cap { display: grid; justify-items: center; font-size: 12px; text-align: center; }
.size { font-weight: 600; }
.state { color: var(--text-3); }
.used .state { color: var(--accent); }
.soldered .state { color: var(--c-approximate); }
@media (max-width: 480px) {
  .unknown { flex-direction: column; align-items: flex-start; }
}
</style>
