<script setup lang="ts">
import { freeSlots } from '~/lib/report'
import * as S from '~/lib/sections'

const { t, locale } = useI18n()
const { report, origin, agent, storage, storageStatus } = useScan()
const hide = useState('hideUnavailable', () => false)
const savedOpen = ref(false)

useSeoMeta({
  title: () => t('seo.home.title'),
  description: () => t('seo.home.desc'),
  ogTitle: () => `${t('seo.home.title')} — Device Check`,
  ogDescription: () => t('seo.home.desc'),
})

const memoryView = computed(() => (report.value ? { ...report.value.memory, slots_free: freeSlots(report.value.memory) } : null))
const nav = ['cpu', 'motherboard', 'memory', 'gpu', 'storage', 'health', 'displays', 'system', 'browser'] as const
const shown = (n: string) => (n === 'browser' ? !!report.value?.browser : n === 'health' ? !!agent.value : true)
const confidences = ['exact', 'approximate', 'detected', 'unavailable'] as const
</script>

<template>
  <div class="dash">
    <section class="hero">
      <span class="kicker">{{ t('hud.kicker') }}</span>
      <h1>{{ t('dash.title') }}</h1>
      <p class="dim">{{ agent ? t('dash.subtitle_agent') : t('dash.subtitle') }}</p>
    </section>

    <ScanBar @open-saved="savedOpen = true" />

    <NuxtLink v-if="!agent" to="/agent" class="cta card">
      <HwIcon name="download" :size="20" />
      <span><strong>{{ t('cta.title') }}</strong><span class="dim">{{ t('cta.text') }}</span></span>
      <span class="mono go">{{ t('cta.go') }} →</span>
    </NuxtLink>

    <div class="legend">
      <span class="lg-title">{{ t('hud.legend') }}</span>
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
          <a v-for="n in nav.filter(shown)" :key="n" :href="`#${n}`">{{ t(`section.${n}`) }}</a>
        </div>
        <label class="toggle"><input v-model="hide" type="checkbox" /> {{ t('action.hide_unavailable') }}</label>
      </nav>

      <div class="sections">
        <SectionCard id="cpu" icon="cpu" :title="t('section.cpu')">
          <FieldList :defs="S.CPU_FIELDS" :data="report.cpu" />
        </SectionCard>

        <SectionCard id="motherboard" icon="board" :title="t('section.motherboard')">
          <p v-if="report.scan.source === 'browser'" class="notice"><HwIcon name="info" :size="16" /><span>{{ t('notice.agent') }} <NuxtLink to="/agent">{{ t('cta.go') }}</NuxtLink></span></p>
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
          <p v-if="!report.storage.length" class="notice"><HwIcon name="info" :size="16" /><span>{{ t('notice.storage') }} {{ t('notice.agent') }} <NuxtLink to="/agent">{{ t('cta.go') }}</NuxtLink></span></p>
          <div v-for="(d, i) in report.storage" :key="i" class="sub card">
            <FieldList :defs="S.STORAGE_FIELDS" :data="d" />
          </div>
          <h3>{{ t('section.volumes') }}</h3>
          <p v-if="!report.volumes.length" class="notice"><HwIcon name="info" :size="16" />{{ t('notice.volumes') }}</p>
          <VolumeTable v-else :volumes="report.volumes" />
        </SectionCard>

        <SectionCard v-if="agent" id="health" icon="storage" :title="t('section.health')" :subtitle="t('health.subtitle')">
          <p v-if="storageStatus === 'loading'" class="muted">{{ t('health.loading') }}</p>
          <p v-else-if="storageStatus === 'error'" class="notice warn"><HwIcon name="alert" :size="16" />{{ t('health.error') }}</p>
          <p v-else-if="!agent.features.includes('storage_health')" class="notice"><HwIcon name="info" :size="16" /><span>{{ t('health.update_agent') }} <NuxtLink to="/agent">{{ t('cta.go') }}</NuxtLink></span></p>
          <StorageHealth v-else-if="storage" :report="storage" />
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
            <li v-for="l in report.limitations" :key="l.code"><span class="mono tag">{{ l.section }}</span><span>{{ t(`lim.${l.code}`) }}</span></li>
          </ul>
          <NuxtLink to="/limitations" class="more">{{ t('nav.limitations') }} →</NuxtLink>
        </SectionCard>
      </div>
    </template>

    <section v-else class="card empty">
      <div class="reticle" aria-hidden="true">
        <span class="ring" />
        <span class="art">
          <HwIcon name="cpu" :size="30" /><HwIcon name="memory" :size="30" /><HwIcon name="gpu" :size="30" /><HwIcon name="display" :size="30" />
        </span>
      </div>
      <span class="kicker">{{ t('hud.awaiting') }}</span>
      <h2>{{ t('empty.title') }}</h2>
      <p class="dim">{{ t('empty.text') }}</p>
      <ul>
        <li v-for="i in 4" :key="i"><span class="ok mono" aria-hidden="true">[+]</span>{{ t(`empty.p${i}`) }}</li>
      </ul>
    </section>

    <SavedPanel v-if="savedOpen" @close="savedOpen = false" />
  </div>
</template>

<style scoped>
.dash { display: grid; grid-template-columns: minmax(0, 1fr); gap: 16px; padding: 30px 0 0; }
.hero { display: grid; gap: 8px; padding-bottom: 4px; }
.hero .dim { max-width: 720px; }

.legend {
  display: flex; flex-wrap: wrap; align-items: center; gap: 8px 18px; font-size: 12px;
  padding: 10px 14px; background: rgba(8, 12, 20, .6); box-shadow: inset 0 0 0 1px var(--border);
  clip-path: var(--chamfer-br);
}
.lg-title { font: 600 10.5px/1 var(--mono); text-transform: uppercase; letter-spacing: .2em; color: var(--yellow); }
.lg { display: inline-flex; align-items: center; gap: 8px; min-width: 0; }

.sec-title { margin-top: 10px; }

.cta { display: flex; align-items: center; gap: 14px; padding: 14px 16px; color: var(--text); text-decoration: none; box-shadow: inset 2px 0 0 var(--accent), inset 0 0 0 1px var(--accent-line); }
.cta:hover { text-decoration: none; background: rgba(25, 230, 255, .05); }
.cta > span:nth-child(2) { display: grid; gap: 2px; flex: 1; }
.cta .go { color: var(--accent); white-space: nowrap; font-size: 12px; letter-spacing: .1em; text-transform: uppercase; }

.subnav {
  position: sticky; top: 60px; z-index: 20; display: flex; align-items: center; gap: 12px; justify-content: space-between;
  padding: 8px 0; background: linear-gradient(180deg, rgba(5, 7, 12, .97), rgba(5, 7, 12, .92));
  backdrop-filter: blur(8px);
  border-bottom: 1px solid var(--border);
}
.links { display: flex; gap: 2px; overflow-x: auto; scrollbar-width: none; counter-reset: lk; min-width: 0; }
.links::-webkit-scrollbar { display: none; }
.links a {
  counter-increment: lk;
  display: inline-flex; align-items: baseline; gap: 6px;
  padding: 6px 10px; color: var(--text-2); white-space: nowrap;
  font: 600 11.5px/1.2 var(--mono); text-transform: uppercase; letter-spacing: .1em;
  box-shadow: inset 0 -2px 0 transparent; transition: color .15s, box-shadow .15s, background-color .15s;
}
.links a::before { content: counter(lk, decimal-leading-zero); font-size: 9.5px; color: var(--magenta); letter-spacing: 0; }
.links a:hover, .links a:focus-visible {
  color: var(--accent); text-decoration: none; background: var(--accent-soft); box-shadow: inset 0 -2px 0 var(--accent); outline: none;
}

.toggle {
  display: flex; align-items: center; gap: 8px; cursor: pointer; white-space: nowrap; flex: none;
  font: 600 11px/1.2 var(--mono); text-transform: uppercase; letter-spacing: .1em; color: var(--text-2);
}
.toggle input {
  appearance: none; -webkit-appearance: none; margin: 0; flex: none; cursor: pointer;
  position: relative; width: 34px; height: 16px;
  background: var(--surface-3); box-shadow: inset 0 0 0 1px var(--border-strong);
  clip-path: polygon(4px 0, 100% 0, 100% calc(100% - 4px), calc(100% - 4px) 100%, 0 100%, 0 4px);
  transition: background-color .15s;
}
.toggle input::before {
  content: ''; position: absolute; top: 3px; left: 3px; width: 12px; height: 10px; background: var(--text-3);
  transition: transform .15s, background-color .15s;
}
.toggle input:checked { background: rgba(25, 230, 255, .2); box-shadow: inset 0 0 0 1px var(--accent); }
.toggle input:checked::before { transform: translateX(16px); background: var(--accent); box-shadow: 0 0 8px var(--accent); }
.toggle input:focus-visible { outline: none; box-shadow: inset 0 0 0 2px var(--yellow); }
.toggle:hover { color: var(--text); }

.sections { display: grid; grid-template-columns: minmax(0, 1fr); gap: 18px; counter-reset: sec; }
.sub-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(320px, 100%), 1fr)); gap: 12px; }
.sub {
  padding: 12px 14px; display: grid; gap: 6px;
  --cut: 10px;
  --frame: linear-gradient(135deg, var(--accent-line), var(--border) 30%);
  --fill: rgba(5, 8, 14, .85);
  --bracket: var(--border-strong);
}

.lims { margin: 0; padding: 0; list-style: none; display: grid; gap: 8px; color: var(--text-2); font-size: 14px; }
.lims li { display: flex; gap: 10px; align-items: baseline; min-width: 0; }
.tag {
  flex: none; font-size: 10.5px; font-weight: 700; padding: 2px 7px; text-transform: uppercase; letter-spacing: .08em;
  color: var(--yellow); background: var(--yellow-soft); box-shadow: inset 0 0 0 1px rgba(246, 233, 74, .3);
}
.more { font: 600 12px var(--mono); text-transform: uppercase; letter-spacing: .1em; justify-self: start; }

/* idle state — "awaiting scan" */
.empty { padding: 40px 24px 36px; display: grid; gap: 12px; justify-items: center; text-align: center; }
.reticle { position: relative; display: grid; place-items: center; width: 240px; max-width: 100%; height: 96px; margin-bottom: 6px; }
.reticle .ring {
  position: absolute; inset: 0;
  background:
    linear-gradient(var(--accent), var(--accent)) left top / 18px 2px no-repeat,
    linear-gradient(var(--accent), var(--accent)) left top / 2px 18px no-repeat,
    linear-gradient(var(--accent), var(--accent)) right top / 18px 2px no-repeat,
    linear-gradient(var(--accent), var(--accent)) right top / 2px 18px no-repeat,
    linear-gradient(var(--magenta), var(--magenta)) left bottom / 18px 2px no-repeat,
    linear-gradient(var(--magenta), var(--magenta)) left bottom / 2px 18px no-repeat,
    linear-gradient(var(--magenta), var(--magenta)) right bottom / 18px 2px no-repeat,
    linear-gradient(var(--magenta), var(--magenta)) right bottom / 2px 18px no-repeat,
    linear-gradient(90deg, transparent, rgba(25, 230, 255, .35), transparent) center / 100% 1px no-repeat;
  animation: reticle 2.8s ease-in-out infinite;
}
@keyframes reticle { 50% { inset: 8px 14px; opacity: .7; } }
.art { display: flex; gap: 18px; color: var(--accent); filter: drop-shadow(0 0 6px rgba(25, 230, 255, .55)); }
.empty h2 { font-size: clamp(18px, 3vw, 24px); }
.empty .dim { max-width: 620px; }
.empty ul { list-style: none; padding: 0; margin: 10px 0 0; display: grid; gap: 8px; text-align: left; }
.empty li { display: flex; gap: 10px; align-items: baseline; color: var(--text-2); }
.empty .ok { color: var(--c-exact); font-weight: 700; font-size: 12px; flex: none; }

@media (max-width: 760px) {
  .subnav { top: 77px; flex-wrap: wrap; }
  .sub-grid { grid-template-columns: minmax(0, 1fr); }
}
@media (max-width: 640px) {
  .legend { padding: 10px 12px; gap: 8px 12px; }
  .lg-title { width: 100%; }
  .empty { padding: 32px 16px 28px; }
  .art { gap: 12px; }
  .art :deep(svg) { width: 26px; height: 26px; }
}
</style>
