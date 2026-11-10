<script setup lang="ts">
const { t } = useI18n()
useSeoMeta({ title: () => t('seo.limitations.title'), description: () => t('seo.limitations.desc'), ogTitle: () => t('seo.limitations.title'), ogDescription: () => t('seo.limitations.desc') })
</script>

<template>
  <article class="prose">
    <h1>{{ t('limits.h1') }}</h1>
    <p>{{ t('limits.p1') }}</p>
    <div class="cols">
      <div class="card col">
        <h2 class="ok">{{ t('limits.h_avail') }}</h2>
        <ul><li v-for="i in 5" :key="i">{{ t(`limits.a${i}`) }}</li></ul>
      </div>
      <div class="card col">
        <h2 class="no">{{ t('limits.h_unavail') }}</h2>
        <ul><li v-for="i in 5" :key="i">{{ t(`limits.u${i}`) }}</li></ul>
      </div>
    </div>
    <h2>{{ t('limits.h_browsers') }}</h2>
    <ul><li v-for="i in 3" :key="i">{{ t(`limits.b${i}`) }}</li></ul>
    <p>{{ t('limits.p2') }}</p>
  </article>
</template>

<style scoped>
.cols { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
.col { padding: 16px; display: grid; gap: 10px; align-content: start; }
.col h2 { margin: 0; font-size: 16px; }
.ok { color: var(--c-exact); }
.no { color: var(--text-3); }
@media (max-width: 640px) { .cols { grid-template-columns: 1fr; } }
</style>
