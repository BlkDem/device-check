<script setup lang="ts">
const { t } = useI18n()
useSeoMeta({ title: () => t('seo.about.title'), description: () => t('seo.about.desc'), ogTitle: () => t('seo.about.title'), ogDescription: () => t('seo.about.desc') })
const statuses = ['exact', 'approximate', 'detected', 'unavailable'] as const
</script>

<template>
  <article class="prose">
    <span class="kicker">{{ t('nav.about') }}</span>
    <h1>{{ t('about.h1') }}</h1>
    <p>{{ t('about.p1') }}</p>
    <p>{{ t('about.p2') }}</p>
    <h2>{{ t('about.h2_statuses') }}</h2>
    <ul class="st">
      <li v-for="s in statuses" :key="s"><ConfidenceBadge :c="s" /> {{ t(`status.desc.${s}`) }}</li>
    </ul>
    <h2>{{ t('about.h2_roadmap') }}</h2>
    <ul>
      <li v-for="i in 4" :key="i">{{ t(`about.r${i}`) }}</li>
    </ul>
  </article>
</template>

<style scoped>
.st { list-style: none; padding: 0 !important; gap: 10px !important; }
.st li {
  display: flex; gap: 12px; align-items: center; padding: 10px 14px !important;
  background: rgba(8, 12, 20, .7); box-shadow: inset 0 0 0 1px var(--border);
  clip-path: var(--chamfer-br);
}
.st li::before { display: none; }
@media (max-width: 480px) { .st li { flex-direction: column; align-items: flex-start; gap: 6px; } }
</style>
