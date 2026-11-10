<script setup lang="ts">
import { formatBytes } from '~/lib/format'
import type { MemoryInfo, MemoryModule } from '~/lib/types'

const props = defineProps<{ memory: MemoryInfo }>()
const { t } = useI18n()

/** Slot count: firmware value if known, else the number of modules seen. Never guessed. */
const total = computed(() => props.memory.smbios_slots?.length || props.memory.slots_total.value || props.memory.modules.length || null)
/** Agent reports carry the SMBIOS device list (empty slots included); populated entries map to modules in order. */
const slots = computed<{ m: MemoryModule | null; label: string | null }[]>(() => {
  const mods = props.memory.modules.filter((m) => (m.size_bytes.value ?? 0) > 0)
  const fw = props.memory.smbios_slots
  if (fw?.length) {
    let k = 0
    return fw.map((s) => ({ m: s.populated ? (mods[k++] ?? null) : null, label: s.locator }))
  }
  const n = total.value ?? 0
  return Array.from({ length: Math.max(n, mods.length) }, (_, i) => ({ m: mods[i] ?? null, label: null }))
})
const spec = (m: MemoryModule) =>
  [m.manufacturer.value, m.memory_type.value, m.speed_mts.value ? `${m.speed_mts.value} ${t('unit.mts')}` : null].filter(Boolean).join(' · ')
const fromSmbios = computed(() => /smbios|Win32_PhysicalMemoryArray/i.test(props.memory.slots_total.source))
const chips = [0, 1, 2, 3, 4, 5, 6, 7]
</script>

<template>
  <div class="slots-wrap">
    <div v-if="!total" class="unknown">
      <svg viewBox="0 0 220 64" class="ghost" aria-hidden="true">
        <path d="M10 8h200l8 8v28l-8 8H10l-8-8V16z" />
        <rect v-for="i in chips" :key="i" :x="14 + i * 25" y="18" width="16" height="22" class="chip" />
        <path d="M100 52v6M120 52v6" />
        <text x="110" y="37" text-anchor="middle">?</text>
      </svg>
      <div>
        <strong>{{ t('slots.unknown') }}</strong>
        <p class="muted">{{ t('slots.unknown_desc') }}</p>
      </div>
    </div>

    <template v-else>
      <div class="slots" role="list">
        <div v-for="({ m, label }, i) in slots" :key="i" class="slot" :class="{ used: !!m, soldered: m?.soldered.value }" role="listitem">
          <svg viewBox="0 0 40 160" aria-hidden="true">
            <path class="socket" d="M10 2h20l6 6v144l-6 6H10l-6-6V8z" />
            <template v-if="m">
              <path class="stick" d="M12 8h16l4 4v136l-4 4H12l-4-4V12z" />
              <rect v-for="c in 6" :key="c" class="chip" x="12" :y="14 + (c - 1) * 22" width="16" height="15" />
              <path class="pins" d="M14 148v4M18 148v4M22 148v4M26 148v4" />
            </template>
          </svg>
          <div class="cap">
            <span class="mono id">{{ m?.slot.value ?? label ?? `#${i + 1}` }}</span>
            <span v-if="m" class="size">{{ m.size_bytes.value ? formatBytes(m.size_bytes.value, t) : '' }}</span>
            <span v-if="m && spec(m)" class="spec mono">{{ spec(m) }}</span>
            <span class="state">{{ m ? (m.soldered.value ? t('slots.onboard') : t('slots.used')) : t('slots.empty') }}</span>
          </div>
        </div>
      </div>
      <p v-if="fromSmbios" class="notice warn"><HwIcon name="alert" :size="16" />{{ t('slots.smbios_warning') }}</p>
    </template>
  </div>
</template>

<style scoped>
.unknown {
  display: flex; align-items: center; gap: 18px; padding: 16px;
  background:
    repeating-linear-gradient(-45deg, rgba(108, 122, 140, .06) 0 8px, transparent 8px 16px),
    var(--bg-2);
  box-shadow: inset 0 0 0 1px var(--border);
  outline: 1px dashed var(--border-strong); outline-offset: -5px;
  clip-path: var(--chamfer-sm);
}
.unknown strong { font: 600 13px/1.3 var(--mono); text-transform: uppercase; letter-spacing: .08em; color: var(--text-2); }
.unknown p { font-size: 13px; margin-top: 4px; }
.ghost { width: 180px; flex: none; fill: none; stroke: var(--text-3); stroke-width: 1.5; stroke-dasharray: 4 3; }
.ghost .chip { stroke-dasharray: none; stroke: var(--border-strong); fill: none; }
.ghost text { fill: var(--magenta); stroke: none; font: 700 22px var(--mono); }
.slots { display: flex; gap: 14px; flex-wrap: wrap; }
.slot { display: grid; justify-items: center; gap: 6px; width: 88px; }
.slot svg { width: 40px; height: 160px; overflow: visible; }
.socket { fill: var(--bg-2); stroke: var(--border-strong); stroke-dasharray: 4 3; }
.used .socket { stroke-dasharray: none; stroke: var(--accent-line); }
.stick { fill: #06303a; stroke: var(--accent); filter: drop-shadow(0 0 4px rgba(25, 230, 255, .5)); }
.chip { fill: #0b5563; }
.pins { stroke: var(--yellow); stroke-width: 1.2; }
.soldered .socket { stroke: rgba(255, 185, 56, .5); }
.soldered .stick { fill: #2f2408; stroke: var(--c-approximate); filter: drop-shadow(0 0 4px rgba(255, 185, 56, .45)); }
.soldered .chip { fill: #5a4512; }
.cap { display: grid; justify-items: center; font-size: 12px; text-align: center; gap: 1px; }
.id { color: var(--text-3); font-size: 11px; }
.size { font: 600 13px var(--display); letter-spacing: .04em; }
.spec { color: var(--text-2); font-size: 10.5px; line-height: 1.3; max-width: 100%; }
.state { font: 600 10.5px var(--mono); text-transform: uppercase; letter-spacing: .1em; color: var(--text-3); }
.used .state { color: var(--accent); }
.soldered .state { color: var(--c-approximate); }
.slots-wrap { display: grid; gap: 12px; }
@media (max-width: 480px) {
  .unknown { flex-direction: column; align-items: flex-start; }
  .ghost { width: 150px; }
}
</style>
