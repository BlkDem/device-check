<script setup lang="ts">
const { t } = useI18n()
useSeoMeta({ title: () => t('seo.privacy.title'), description: () => t('seo.privacy.desc'), ogTitle: () => t('seo.privacy.title'), ogDescription: () => t('seo.privacy.desc') })
</script>

<template>
  <article class="prose">
    <h1>{{ t('privacy.h1') }}</h1>
    <p>{{ t('privacy.p1') }}</p>
    <ul>
      <li v-for="i in 5" :key="i">{{ t(`privacy.l${i}`) }}</li>
    </ul>
    <p>{{ t('privacy.p2') }}</p>
  </article>
</template>
