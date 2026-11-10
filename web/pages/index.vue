<script setup lang="ts">
import { freeSlots } from '~/lib/report'
import * as S from '~/lib/sections'

const { t, locale } = useI18n()
const { report, origin } = useScan()
const hide = useState('hideUnavailable', () => false)
const savedOpen = ref(false)

useSeoMeta({
  title: () => t('seo.home.title'),
  description: () => t('seo.home.desc'),
  ogTitle: () => `${t('seo.home.title')} — Device Check`,
  ogDescription: () => t('seo.home.desc'),
})

const memoryView = computed(() => (report.value ? { ...report.value.memory, slots_free: freeSlots(report.value.memory) } : null))
const nav = ['cpu', 'motherboard', 'memory', 'gpu', 'storage', 'displays', 'system', 'browser'] as const
const confidences = ['exact', 'approximate', 'detected', 'unavailable'] as const
</script>

<template>
  <div class="dash">
    <section class="hero">
      <h1>{{ t('dash.title') }}</h1>
      <p class="dim">{{ t('dash.subtitle') }}</p>
    </section>

    <ScanBar @open-saved="savedOpen = true" />

    <div class="legend">
      <span v-for="c in confidences" :key="c" class="lg"><ConfidenceBadge :c="c" /><span class="muted">{{ t(`status.desc.${c}`) }}</span></span>
    </div>

    <template v-if="report && memoryView">
      <div v-if="origin === 'saved'" class="notice warn">
        <HwIcon name="alert" :size="16" />
        {{ t('saved.banner', { date: new Date(report.scan.created_at).toLocaleString(locale) }) }}
      </div>

      <h3 class="sec-title">{{ t('section.overview') }}</h3>
      <OverviewGrid :report="report" />

      <nav class="subnav">
        <div class="links">
          <a v-for="n in nav" :key="n" :href="`#${n}`">{{ t(`section.${n}`) }}</a>
        </div>
        <label class="toggle"><input v-model="hide" type="checkbox" /> {{ t('action.hide_unavailable') }}</label>
      </nav>

      <div class="sections">
        <SectionCard id="cpu" icon="cpu" :title="t('section.cpu')">
          <FieldList :defs="S.CPU_FIELDS" :data="report.cpu" />
        </SectionCard>

        <SectionCard id="motherboard" icon="board" :title="t('section.motherboard')">
          <p v-if="report.scan.source === 'browser'" class="notice"><HwIcon name="info" :size="16" />{{ t('notice.agent') }}</p>
          <FieldList :defs="S.BOARD_FIELDS" :data="report.motherboard" />
        </SectionCard>

        <SectionCard id="memory" icon="memory" :title="t('section.memory')">
          <FieldList :defs="S.MEMORY_FIELDS" :data="memoryView" />
          <h3>{{ t('slots.title') }}</h3>
          <MemorySlots :memory="report.memory" />
          <h3>{{ t('section.modules') }}</h3>
          <p v-if="!report.memory.modules.length" class="notice"><HwIcon name="info" :size="16" />{{ t('notice.modules') }}</p>
          <div v-else class="sub-grid">
            <div v-for="(m, i) in report.memory.modules" :key="i" class="sub card">
              <h3>{{ t('section.module') }} {{ i + 1 }}</h3>
              <FieldList :defs="S.MODULE_FIELDS" :data="m" />
            </div>
          </div>
        </SectionCard>

        <SectionCard id="gpu" icon="gpu" :title="t('section.gpu')" :subtitle="report.scan.source === 'browser' ? t('gpu.single_note') : undefined">
          <p v-if="!report.gpu.length" class="notice"><HwIcon name="info" :size="16" />{{ t('reason.api_unsupported') }}</p>
          <div v-for="(g, i) in report.gpu" :key="i">
            <h3 v-if="report.gpu.length > 1">{{ t('gpu.adapter', { n: i + 1 }) }}</h3>
            <FieldList :defs="S.GPU_FIELDS" :data="g" />
          </div>
        </SectionCard>

        <SectionCard id="storage" icon="storage" :title="t('section.storage')">
          <p v-if="!report.storage.length" class="notice"><HwIcon name="info" :size="16" />{{ t('notice.storage') }} {{ t('notice.agent') }}</p>
          <div v-for="(d, i) in report.storage" :key="i" class="sub card">
            <FieldList :defs="S.STORAGE_FIELDS" :data="d" />
          </div>
          <h3>{{ t('section.volumes') }}</h3>
          <p v-if="!report.volumes.length" class="notice"><HwIcon name="info" :size="16" />{{ t('notice.volumes') }}</p>
          <VolumeTable v-else :volumes="report.volumes" />
        </SectionCard>

        <SectionCard id="displays" icon="display" :title="t('section.displays')" :subtitle="report.scan.source === 'browser' ? t('display.only_current') : undefined">
          <div v-for="(d, i) in report.displays" :key="i">
            <h3 v-if="report.displays.length > 1">{{ t('display.n', { n: i + 1 }) }}</h3>
            <FieldList :defs="S.DISPLAY_FIELDS" :data="d" />
          </div>
        </SectionCard>

        <SectionCard id="system" icon="system" :title="t('section.system')">
          <FieldList :defs="S.SYSTEM_FIELDS" :data="report.system" />
        </SectionCard>

        <SectionCard v-if="report.browser" id="browser" icon="browser" :title="t('section.browser')">
          <FieldList :defs="S.BROWSER_FIELDS" :data="report.browser" />
        </SectionCard>

        <SectionCard id="limitations" icon="alert" :title="t('section.limitations')">
          <ul class="lims">
            <li v-for="l in report.limitations" :key="l.code"><span class="mono tag">{{ l.section }}</span>{{ t(`lim.${l.code}`) }}</li>
          </ul>
          <NuxtLink to="/limitations">{{ t('nav.limitations') }} →</NuxtLink>
        </SectionCard>
      </div>
    </template>

    <section v-else class="card empty">
      <div class="art" aria-hidden="true">
        <HwIcon name="cpu" :size="34" /><HwIcon name="memory" :size="34" /><HwIcon name="gpu" :size="34" /><HwIcon name="display" :size="34" />
      </div>
      <h2>{{ t('empty.title') }}</h2>
      <p class="dim">{{ t('empty.text') }}</p>
      <ul>
        <li v-for="i in 4" :key="i"><HwIcon name="check" :size="16" />{{ t(`empty.p${i}`) }}</li>
      </ul>
    </section>

    <SavedPanel v-if="savedOpen" @close="savedOpen = false" />
  </div>
</template>

<style scoped>
.dash { display: grid; grid-template-columns: minmax(0, 1fr); gap: 16px; padding: 28px 0 0; }
.hero { display: grid; gap: 6px; }
.legend { display: flex; flex-wrap: wrap; gap: 8px 18px; font-size: 12.5px; }
.lg { display: inline-flex; align-items: center; gap: 8px; }
.sec-title { margin-top: 8px; }
.subnav {
  position: sticky; top: 60px; z-index: 20; display: flex; align-items: center; gap: 12px; justify-content: space-between;
  padding: 8px 0; background: var(--bg); border-bottom: 1px solid var(--border);
}
.links { display: flex; gap: 4px; overflow-x: auto; scrollbar-width: none; }
.links a { padding: 5px 10px; border-radius: 6px; color: var(--text-2); font-size: 13.5px; white-space: nowrap; }
.links a:hover { background: var(--surface-2); color: var(--text); text-decoration: none; }
.toggle { display: flex; align-items: center; gap: 6px; font-size: 13px; color: var(--text-2); white-space: nowrap; cursor: pointer; }
.toggle input { accent-color: var(--accent); }
.sections { display: grid; grid-template-columns: minmax(0, 1fr); gap: 16px; }
.sub-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 12px; }
.sub { padding: 12px 14px; background: var(--bg-2); box-shadow: none; }
.lims { margin: 0; padding: 0; list-style: none; display: grid; gap: 8px; color: var(--text-2); font-size: 14px; }
.lims li { display: flex; gap: 10px; align-items: baseline; }
.tag { flex: none; font-size: 11px; padding: 1px 6px; border-radius: 4px; background: var(--surface-3); color: var(--text-2); }
.empty { padding: 36px 24px; display: grid; gap: 12px; justify-items: center; text-align: center; }
.empty .art { display: flex; gap: 18px; color: var(--accent); opacity: .8; margin-bottom: 6px; }
.empty ul { list-style: none; padding: 0; margin: 8px 0 0; display: grid; gap: 8px; text-align: left; }
.empty li { display: flex; gap: 8px; align-items: center; color: var(--text-2); }
.empty li :deep(svg) { color: var(--c-exact); }
@media (max-width: 760px) {
  .subnav { top: 88px; flex-wrap: wrap; }
  .sub-grid { grid-template-columns: 1fr; }
}
</style>
