<script setup lang="ts">
import { completeness, reportToText } from '~/lib/report'

const { t, has, locale } = useI18n()
const { report, status, steps, stepIds, agent, agentLost, scan } = useScan()
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
  <div class="card bar" :class="status">
    <div class="fx" aria-hidden="true"><i class="sweep" /></div>

    <div class="left">
      <button class="btn primary big" :disabled="status === 'scanning'" @click="scan">
        <HwIcon name="scan" :size="18" />
        {{ status === 'scanning' ? t('scan.scanning') : report ? t('scan.rescan') : t('scan.start') }}
      </button>
      <div class="state">
        <div class="st" :class="status"><i />{{ statusText }}</div>
        <div v-if="agent" class="muted small mode agent"><i aria-hidden="true" />{{ t('scan.mode_agent', { v: agent.version }) }}</div>
        <div v-else class="muted small mode">{{ t(agentLost ? 'scan.agent_lost' : 'scan.mode') }}</div>
      </div>
    </div>

    <div class="boot">
      <div class="boot-head">
        <span>{{ t('hud.sequence') }}</span>
        <span class="cnt">{{ String(stepIds.filter((s) => steps[s] === 'done' || steps[s] === 'error').length).padStart(2, '0') }}/{{ String(stepIds.length).padStart(2, '0') }}</span>
      </div>
      <ol class="steps" :aria-label="statusText" :style="{ '--n': stepIds.length }">
        <li v-for="s in stepIds" :key="s" :class="steps[s] ?? 'pending'">
          <span class="dot"><HwIcon v-if="steps[s] === 'done'" name="check" :size="11" /><HwIcon v-else-if="steps[s] === 'error'" name="x" :size="11" /></span>
          <span class="nm">{{ t(`step.${s}`) }}</span>
        </li>
      </ol>
    </div>

    <div v-if="comp" class="comp" :title="t('completeness.desc', { n: comp.available, total: comp.total })">
      <div class="pct"><b>{{ comp.percent }}</b><span>%</span></div>
      <div class="cinfo">
        <div class="small strong ttl">{{ t('completeness.title') }}</div>
        <div class="segs" aria-hidden="true"><i v-for="n in 20" :key="n" :class="{ on: n <= Math.round(comp.percent / 5) }" /></div>
        <div class="muted small">{{ t('completeness.desc', { n: comp.available, total: comp.total }) }}</div>
        <div class="dist">
          <span v-for="k in (['exact', 'approximate', 'detected', 'unavailable'] as const)" :key="k" :class="k" :title="t(`status.${k}`)"><i aria-hidden="true" />{{ comp.by[k] }}</span>
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
.bar {
  display: grid; grid-template-columns: auto minmax(0, 1fr) auto; gap: 18px 28px; align-items: center;
  padding: 20px 22px;
  --frame: linear-gradient(135deg, var(--accent), var(--accent-line) 18%, var(--border-strong) 40%, var(--border-strong) 75%, var(--magenta));
}
.bar.scanning { --bracket: var(--yellow); }
.bar.error { --frame: linear-gradient(135deg, var(--danger), var(--border-strong) 30%, var(--border-strong) 75%, var(--danger)); }

/* scanning sweep — decorative, clipped to the panel */
.fx { position: absolute; inset: 1px; overflow: hidden; pointer-events: none; clip-path: var(--chamfer); z-index: 0; }
.sweep {
  position: absolute; top: 0; bottom: 0; left: -30%; width: 30%; opacity: 0;
  background: linear-gradient(90deg, transparent, rgba(25, 230, 255, .10) 70%, rgba(25, 230, 255, .55) 99%, transparent);
}
.scanning .sweep { animation: sweep 1.5s cubic-bezier(.45, 0, .55, 1) infinite; }
@keyframes sweep { 0% { left: -30%; opacity: 1; } 90% { opacity: 1; } 100% { left: 100%; opacity: 0; } }
.bar > :not(.fx) { position: relative; z-index: 1; }

.left { display: flex; align-items: center; gap: 18px; }
.big { padding: 14px 22px 14px 18px; font-size: 13.5px; }
.small { font-size: 12.5px; }
.strong { font-weight: 600; }
.st {
  display: flex; align-items: center; gap: 8px;
  font: 700 12.5px/1.3 var(--mono); text-transform: uppercase; letter-spacing: .12em;
}
.st i { width: 9px; height: 9px; flex: none; background: var(--c-unavailable); }
.st.idle i { animation: hud-blink 1.6s steps(1) infinite; background: var(--text-2); }
.st.scanning { color: var(--accent); }
.st.scanning i { background: var(--accent); box-shadow: 0 0 10px var(--accent); animation: hud-blink .6s steps(1) infinite; }
.st.done { color: var(--c-exact); }
.st.done i { background: var(--c-exact); box-shadow: 0 0 10px var(--c-exact); }
.st.error { color: var(--danger); }
.st.error i { background: var(--danger); box-shadow: 0 0 10px var(--danger); }
.mode { margin-top: 3px; font-family: var(--mono); font-size: 11.5px; letter-spacing: .04em; }
.mode.agent { display: inline-flex; align-items: center; gap: 7px; color: var(--c-exact); }
.mode.agent i { width: 7px; height: 7px; background: var(--c-exact); box-shadow: 0 0 8px var(--c-exact); }

/* boot sequence */
.boot { grid-column: 1 / -1; grid-row: 2; display: grid; gap: 8px; min-width: 0; }
.comp { grid-column: 3; grid-row: 1; justify-self: end; }
.boot-head {
  display: flex; justify-content: space-between; gap: 10px;
  font: 600 10.5px/1 var(--mono); text-transform: uppercase; letter-spacing: .2em; color: var(--text-3);
}
.boot-head .cnt { color: var(--yellow); letter-spacing: .1em; }
.steps {
  list-style: none; margin: 0; padding: 0; counter-reset: step;
  display: grid; grid-template-columns: repeat(var(--n, 6), minmax(0, 1fr)); gap: 6px;
  font: 600 11.5px/1.2 var(--mono); text-transform: uppercase; letter-spacing: .06em; color: var(--text-3);
}
.steps li {
  counter-increment: step;
  display: flex; align-items: center; gap: 8px; min-width: 0;
  padding: 8px 9px 13px; position: relative;
  background: rgba(8, 12, 20, .7); box-shadow: inset 0 0 0 1px var(--border);
  clip-path: polygon(0 0, calc(100% - 6px) 0, 100% 6px, 100% 100%, 0 100%);
  transition: color .2s, box-shadow .2s;
}
.steps li::after {
  content: ''; position: absolute; left: 9px; right: 9px; bottom: 5px; height: 3px;
  background: var(--surface-3);
}
.nm { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; min-width: 0; }
.dot {
  display: grid; place-items: center; flex: none; min-width: 18px; height: 16px; padding: 0 2px;
  font-size: 9.5px; color: var(--text-3); box-shadow: inset 0 0 0 1px var(--border-strong);
}
.dot:empty::before { content: counter(step, decimal-leading-zero); }
.steps .running { color: var(--accent); box-shadow: inset 0 0 0 1px var(--accent-line); }
.steps .running .dot { color: #001218; background: var(--accent); box-shadow: 0 0 8px var(--accent); animation: hud-blink .5s steps(1) infinite; }
.steps .running::after {
  background: repeating-linear-gradient(90deg, var(--accent) 0 6px, transparent 6px 9px);
  background-size: 18px 100%; animation: march .4s linear infinite;
}
@keyframes march { to { background-position: 18px 0; } }
.steps .done { color: var(--text-2); }
.steps .done .dot { background: var(--c-exact); color: #022313; box-shadow: none; }
.steps .done::after { background: var(--c-exact); box-shadow: 0 0 6px rgba(54, 245, 154, .6); }
.steps .error { color: var(--danger); box-shadow: inset 0 0 0 1px rgba(255, 77, 109, .45); }
.steps .error .dot { background: var(--danger); color: #22030a; box-shadow: none; }
.steps .error::after { background: repeating-linear-gradient(90deg, var(--danger) 0 4px, transparent 4px 7px); }

/* coverage */
.comp { display: flex; align-items: center; gap: 14px; min-width: 0; }
.pct { display: flex; align-items: flex-start; font-family: var(--display); color: var(--accent); line-height: .9; text-shadow: 0 0 16px rgba(25, 230, 255, .45); }
.pct b { font-size: 46px; font-weight: 700; letter-spacing: -.01em; }
.pct span { font-size: 18px; margin-top: 4px; margin-left: 2px; color: var(--text-2); text-shadow: none; }
.cinfo { display: grid; gap: 5px; min-width: 0; }
.ttl { font-family: var(--mono); font-size: 11px; text-transform: uppercase; letter-spacing: .16em; color: var(--text-2); }
.segs { display: flex; gap: 2px; width: 200px; max-width: 100%; }
.segs i { flex: 1; height: 8px; background: var(--surface-3); transform: skewX(-20deg); }
.segs i.on { background: var(--accent); box-shadow: 0 0 6px rgba(25, 230, 255, .5); }
.dist { display: flex; gap: 10px; flex-wrap: wrap; font: 700 12px var(--mono); }
.dist span { display: inline-flex; align-items: center; gap: 5px; }
.dist i { width: 7px; height: 7px; background: currentColor; }
.dist .exact { color: var(--c-exact); }
.dist .approximate { color: var(--c-approximate); }
.dist .approximate i { transform: rotate(45deg) scale(.9); }
.dist .detected { color: var(--c-detected); }
.dist .detected i { background: transparent; box-shadow: inset 0 0 0 1.5px currentColor; }
.dist .unavailable { color: var(--c-unavailable); }
.dist .unavailable i { height: 2px; width: 8px; }

.actions {
  grid-column: 1 / -1; display: flex; flex-wrap: wrap; gap: 8px; padding-top: 16px;
  border-top: 1px dashed var(--border-strong);
}

@media (max-width: 960px) {
  .bar { grid-template-columns: minmax(0, 1fr); }
  .boot, .comp { grid-column: auto; grid-row: auto; justify-self: stretch; }
}
@media (max-width: 760px) {
  .steps { grid-template-columns: repeat(3, minmax(0, 1fr)); }
}
@media (max-width: 640px) {
  .bar { grid-template-columns: minmax(0, 1fr); padding: 16px 14px; gap: 16px; }
  .left { flex-direction: column; align-items: stretch; gap: 12px; }
  .big { justify-content: center; }
  .pct b { font-size: 38px; }
  .actions { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .actions .btn { justify-content: center; white-space: normal; text-align: center; padding: 10px 8px; font-size: 11px; letter-spacing: .05em; }
  .actions .btn:last-child { grid-column: 1 / -1; }
}
@media (max-width: 420px) {
  .steps { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
</style>
