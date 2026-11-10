<script setup lang="ts">
import { SCAN_STEPS } from '~/lib/collectors'
import { completeness, reportToText } from '~/lib/report'

const { t, has, locale } = useI18n()
const { report, status, steps, scan } = useScan()
const { copy } = useCopy()
const { show } = useToast()
const { save } = useSavedReports()
const emit = defineEmits<{ openSaved: [] }>()

const comp = computed(() => (report.value ? completeness(report.value) : null))
const ring = computed(() => {
  const c = 2 * Math.PI * 26
  return { c, off: c * (1 - (comp.value?.percent ?? 0) / 100) }
})
const statusText = computed(() => t(`scan.${status.value}`))

function copyReport() {
  if (report.value) copy(reportToText(report.value, t, has, locale.value))
}
function saveLocal() {
  if (!report.value) return
  const ok = save(report.value)
  show(ok ? t('toast.saved') : t('toast.save_failed'), ok ? 'ok' : 'err')
}
</script>

<template>
  <div class="card bar">
    <div class="left">
      <button class="btn primary big" :disabled="status === 'scanning'" @click="scan">
        <HwIcon name="scan" :size="18" />
        {{ status === 'scanning' ? t('scan.scanning') : report ? t('scan.rescan') : t('scan.start') }}
      </button>
      <div class="state">
        <div class="st" :class="status"><i />{{ statusText }}</div>
        <div class="muted small">{{ t('scan.mode') }}</div>
      </div>
    </div>

    <ol class="steps" :aria-label="statusText">
      <li v-for="s in SCAN_STEPS" :key="s" :class="steps[s]">
        <span class="dot"><HwIcon v-if="steps[s] === 'done'" name="check" :size="11" /><HwIcon v-else-if="steps[s] === 'error'" name="x" :size="11" /></span>
        {{ t(`step.${s}`) }}
      </li>
    </ol>

    <div v-if="comp" class="comp" :title="t('completeness.desc', { n: comp.available, total: comp.total })">
      <svg viewBox="0 0 60 60" width="60" height="60" aria-hidden="true">
        <circle cx="30" cy="30" r="26" class="track" />
        <circle cx="30" cy="30" r="26" class="fill" :stroke-dasharray="ring.c" :stroke-dashoffset="ring.off" />
        <text x="30" y="35" text-anchor="middle">{{ comp.percent }}%</text>
      </svg>
      <div>
        <div class="small strong">{{ t('completeness.title') }}</div>
        <div class="muted small">{{ t('completeness.desc', { n: comp.available, total: comp.total }) }}</div>
        <div class="dist">
          <span v-for="k in (['exact', 'approximate', 'detected', 'unavailable'] as const)" :key="k" :class="k" :title="t(`status.${k}`)">{{ comp.by[k] }}</span>
        </div>
      </div>
    </div>

    <div class="actions">
      <button class="btn" :disabled="!report" @click="copyReport"><HwIcon name="copy" :size="16" />{{ t('action.copy_report') }}</button>
      <button class="btn" :disabled="!report" @click="report && downloadJson(report)"><HwIcon name="download" :size="16" />{{ t('action.download_json') }}</button>
      <button class="btn" :disabled="!report" @click="saveLocal"><HwIcon name="save" :size="16" />{{ t('action.save_local') }}</button>
      <button class="btn" @click="emit('openSaved')"><HwIcon name="folder" :size="16" />{{ t('action.saved_reports') }}</button>
      <button class="btn" disabled :title="t('action.public_link_soon')"><HwIcon name="link" :size="16" />{{ t('action.public_link') }}</button>
    </div>
  </div>
</template>

<style scoped>
.bar { display: grid; grid-template-columns: auto 1fr auto; gap: 16px 24px; align-items: center; padding: 18px 20px; }
.left { display: flex; align-items: center; gap: 16px; }
.big { padding: 12px 18px; font-size: 15px; }
.small { font-size: 12.5px; }
.strong { font-weight: 600; }
.st { display: flex; align-items: center; gap: 8px; font-weight: 600; font-size: 14px; }
.st i { width: 8px; height: 8px; border-radius: 50%; background: var(--c-unavailable); }
.st.scanning i { background: var(--accent); animation: pulse 1s infinite; }
.st.done i { background: var(--c-exact); }
.st.error i { background: var(--danger); }
@keyframes pulse { 50% { opacity: .3; } }

.steps { list-style: none; margin: 0; padding: 0; display: flex; flex-wrap: wrap; gap: 6px 14px; font-size: 13px; color: var(--text-3); }
.steps li { display: flex; align-items: center; gap: 6px; }
.dot { display: grid; place-items: center; width: 16px; height: 16px; border-radius: 50%; border: 1.5px solid var(--border-strong); }
.steps .running { color: var(--text); }
.steps .running .dot { border-color: var(--accent); border-top-color: transparent; animation: spin .8s linear infinite; }
.steps .done { color: var(--text-2); }
.steps .done .dot { background: var(--c-exact); border-color: var(--c-exact); color: #062016; }
.steps .error .dot { background: var(--danger); border-color: var(--danger); color: #200606; }
@keyframes spin { to { transform: rotate(360deg); } }

.comp { display: flex; align-items: center; gap: 12px; }
.track { fill: none; stroke: var(--surface-3); stroke-width: 6; }
.fill { fill: none; stroke: var(--accent); stroke-width: 6; stroke-linecap: round; transform: rotate(-90deg); transform-origin: 30px 30px; transition: stroke-dashoffset .6s; }
.comp text { fill: var(--text); font: 700 14px var(--font); }
.dist { display: flex; gap: 8px; margin-top: 4px; font: 600 12px var(--mono); }
.dist .exact { color: var(--c-exact); }
.dist .approximate { color: var(--c-approximate); }
.dist .detected { color: var(--c-detected); }
.dist .unavailable { color: var(--c-unavailable); }

.actions { grid-column: 1 / -1; display: flex; flex-wrap: wrap; gap: 8px; padding-top: 14px; border-top: 1px solid var(--border); }

@media (max-width: 900px) {
  .bar { grid-template-columns: minmax(0, 1fr); }
}
@media (max-width: 640px) {
  .bar { padding: 14px; }
  .left { flex-direction: column; align-items: stretch; }
  .big { justify-content: center; }
  .actions .btn { flex: 1 1 calc(50% - 8px); justify-content: center; }
}
</style>
