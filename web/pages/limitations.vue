<script setup lang="ts">
const { t } = useI18n()
useSeoMeta({ title: () => t('seo.limitations.title'), description: () => t('seo.limitations.desc'), ogTitle: () => t('seo.limitations.title'), ogDescription: () => t('seo.limitations.desc') })
</script>

<template>
  <article class="prose">
    <span class="kicker">{{ t('nav.limitations') }}</span>
    <h1>{{ t('limits.h1') }}</h1>
    <p>{{ t('limits.p1') }}</p>
    <div class="cols">
      <div class="card col">
        <h2 class="ok"><span class="mk" aria-hidden="true">[+]</span>{{ t('limits.h_avail') }}</h2>
        <ul><li v-for="i in 5" :key="i">{{ t(`limits.a${i}`) }}</li></ul>
      </div>
      <div class="card col">
        <h2 class="no"><span class="mk" aria-hidden="true">[-]</span>{{ t('limits.h_unavail') }}</h2>
        <ul><li v-for="i in 5" :key="i">{{ t(`limits.u${i}`) }}</li></ul>
      </div>
    </div>
    <h2>{{ t('limits.h_browsers') }}</h2>
    <ul><li v-for="i in 3" :key="i">{{ t(`limits.b${i}`) }}</li></ul>
    <p>{{ t('limits.p2') }}</p>
  </article>
</template>

<style scoped>
.cols { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; }
.col { padding: 18px; display: grid; gap: 12px; align-content: start; }
.col h2 { margin: 0; font-size: 15px; display: flex; align-items: baseline; gap: 8px; padding-bottom: 10px; border-bottom: 1px solid var(--border); }
.mk { font: 700 12px var(--mono); }
.ok { color: var(--c-exact); }
.col:first-child { --frame: linear-gradient(135deg, var(--c-exact), var(--border) 30%, var(--border) 80%, var(--accent-line)); --bracket: var(--c-exact); }
.col:last-child { --frame: linear-gradient(135deg, var(--c-unavailable), var(--border) 30%, var(--border) 80%, var(--magenta-line)); --bracket: var(--c-unavailable); }
.col:last-child :deep(li)::before { background: var(--c-unavailable); box-shadow: none; clip-path: none; height: 2px; width: 8px; top: .7em; }
.no { color: var(--text-2); }
@media (max-width: 640px) { .cols { grid-template-columns: minmax(0, 1fr); } }
</style>
