<script setup lang="ts">
const { t, locale } = useI18n()
const toggle = () => (locale.value = locale.value === 'ru' ? 'en' : 'ru')
const links = [
  { to: '/', key: 'nav.dashboard' },
  { to: '/about', key: 'nav.about' },
  { to: '/limitations', key: 'nav.limitations' },
  { to: '/privacy', key: 'nav.privacy' },
]
</script>

<template>
  <header class="hdr">
    <div class="container inner">
      <NuxtLink to="/" class="logo">
        <svg width="26" height="26" viewBox="0 0 32 32" aria-hidden="true">
          <rect x="3" y="3" width="26" height="26" rx="7" fill="none" stroke="var(--accent)" stroke-width="2" />
          <path d="M10 16.5l4 4 8-9" fill="none" stroke="var(--accent)" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" />
        </svg>
        <span>Device<b>Check</b></span>
      </NuxtLink>
      <nav>
        <NuxtLink v-for="l in links" :key="l.to" :to="l.to" class="nl">{{ t(l.key) }}</NuxtLink>
      </nav>
      <button class="btn lang" @click="toggle"><HwIcon name="globe" :size="16" />{{ t('lang.switch') }}</button>
    </div>
  </header>
</template>

<style scoped>
.hdr { position: sticky; top: 0; z-index: 30; background: rgba(11, 14, 19, .85); backdrop-filter: blur(10px); border-bottom: 1px solid var(--border); }
.inner { display: flex; align-items: center; gap: 20px; height: 60px; }
.logo { display: flex; align-items: center; gap: 10px; color: var(--text); font-size: 17px; letter-spacing: -.01em; }
.logo:hover { text-decoration: none; }
.logo b { color: var(--accent); font-weight: 700; }
nav { display: flex; gap: 4px; overflow-x: auto; scrollbar-width: none; }
.nl { padding: 6px 10px; border-radius: 6px; color: var(--text-2); font-size: 14px; white-space: nowrap; }
.nl:hover { color: var(--text); background: var(--surface-2); text-decoration: none; }
.nl.router-link-exact-active { color: var(--text); background: var(--surface-2); }
.lang { margin-left: auto; padding: 6px 10px; font-size: 13px; }
@media (max-width: 760px) {
  .inner { flex-wrap: wrap; height: auto; padding-top: 10px; padding-bottom: 6px; gap: 6px 12px; }
  nav { order: 3; width: 100%; margin: 0 -6px; }
}
</style>
