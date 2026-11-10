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
        <h2>{{ t('saved.title') }}</h2>
        <button class="icon-btn" :aria-label="t('action.close')" @click="emit('close')"><HwIcon name="x" :size="18" /></button>
      </header>
      <p v-if="!saved.length" class="muted">{{ t('saved.empty') }}</p>
      <ul v-else>
        <li v-for="s in saved" :key="s.id">
          <div>
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
.overlay { position: fixed; inset: 0; z-index: 50; background: rgba(4, 6, 10, .7); display: grid; place-items: center; padding: 16px; }
.panel { width: min(560px, 100%); max-height: 80vh; overflow: auto; padding: 18px 20px; display: grid; gap: 12px; }
header { display: flex; align-items: center; justify-content: space-between; }
ul { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; }
li { display: flex; justify-content: space-between; align-items: center; gap: 12px; padding: 10px 12px; border: 1px solid var(--border); border-radius: var(--radius-sm); background: var(--bg-2); }
.btns { display: flex; align-items: center; gap: 4px; }
.strong { font-weight: 600; }
.small { font-size: 12px; }
</style>
