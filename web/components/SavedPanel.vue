<script setup lang="ts">
const emit = defineEmits<{ close: [] }>()
const { t, locale } = useI18n()
const { saved, refresh, remove } = useSavedReports()
const { show: showReport } = useScan()
const { show } = useToast()

onMounted(refresh)

function open(id: string) {
  const s = saved.value.find((x) => x.id === id)
  if (!s) return
  showReport(s.report)
  emit('close')
}
function del(id: string) {
  remove(id)
  show(t('toast.deleted'))
}
const fmt = (iso: string) => new Date(iso).toLocaleString(locale.value)
</script>

<template>
  <div class="overlay" @click.self="emit('close')" @keydown.esc="emit('close')">
    <div class="card panel" role="dialog" aria-modal="true" :aria-label="t('saved.title')">
      <header>
        <h2><HwIcon name="folder" :size="18" />{{ t('saved.title') }}</h2>
        <button class="icon-btn" :aria-label="t('action.close')" @click="emit('close')"><HwIcon name="x" :size="18" /></button>
      </header>
      <p v-if="!saved.length" class="muted empty">{{ t('saved.empty') }}</p>
      <ul v-else>
        <li v-for="(s, i) in saved" :key="s.id">
          <span class="ix mono" aria-hidden="true">{{ String(i + 1).padStart(2, '0') }}</span>
          <div class="info">
            <div class="strong">{{ fmt(s.created_at) }}</div>
            <div class="muted small mono">
              {{ s.report.scan.source }} · {{ s.report.gpu[0]?.model.value ?? '—' }}
            </div>
          </div>
          <div class="btns">
            <button class="btn" @click="open(s.id)">{{ t('action.load') }}</button>
            <button class="icon-btn" :aria-label="t('action.delete')" :title="t('action.delete')" @click="del(s.id)"><HwIcon name="trash" :size="16" /></button>
          </div>
        </li>
      </ul>
    </div>
  </div>
</template>

<style scoped>
.overlay {
  position: fixed; inset: 0; z-index: 50; display: grid; place-items: center; padding: 16px;
  background:
    repeating-linear-gradient(180deg, rgba(25, 230, 255, .025) 0 1px, transparent 1px 4px),
    rgba(2, 4, 8, .78);
  backdrop-filter: blur(4px);
}
.panel {
  width: min(580px, 100%); max-height: 80vh; padding: 20px 22px; display: grid; grid-template-rows: auto minmax(0, 1fr); gap: 14px;
  --frame: linear-gradient(135deg, var(--accent), var(--accent-line) 25%, var(--border-strong) 50%, var(--magenta-line) 80%, var(--magenta));
  --fill: linear-gradient(180deg, rgba(14, 24, 38, .99), rgba(7, 11, 19, .99));
  animation: panel-in .2s ease-out;
}
@keyframes panel-in { from { opacity: 0; transform: scale(.98) translateY(6px); } }
header { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding-bottom: 12px; border-bottom: 1px solid var(--border); }
h2 { display: flex; align-items: center; gap: 10px; color: var(--text); }
h2 :deep(svg) { color: var(--accent); }
.empty { font-size: 13.5px; }
ul { list-style: none; margin: 0; padding: 0 4px 0 0; display: grid; align-content: start; gap: 8px; overflow: auto; min-height: 0; scrollbar-width: thin; scrollbar-color: var(--border-strong) transparent; }
li {
  display: flex; align-items: center; gap: 12px; padding: 10px 12px;
  background:
    linear-gradient(var(--accent-line), var(--accent-line)) left top / 2px 100% no-repeat,
    var(--bg-2);
  box-shadow: inset 0 0 0 1px var(--border);
  clip-path: var(--chamfer-br);
}
li:hover { box-shadow: inset 0 0 0 1px var(--accent-line); }
.ix { color: var(--magenta); font-size: 11px; font-weight: 700; flex: none; }
.info { flex: 1; min-width: 0; overflow-wrap: anywhere; }
.btns { display: flex; align-items: center; gap: 6px; flex: none; }
.strong { font-weight: 600; font-family: var(--mono); font-size: 13.5px; }
.small { font-size: 11.5px; margin-top: 2px; }
@media (max-width: 480px) {
  .panel { padding: 16px 14px; }
  li { flex-wrap: wrap; }
  .btns { margin-left: auto; }
}
</style>
