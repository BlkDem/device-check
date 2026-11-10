<script setup lang="ts">
const { t, locale } = useI18n()
const { toast } = useToast()
useHead(() => ({
  htmlAttrs: { lang: locale.value },
  titleTemplate: (s?: string) => (s ? `${s} — Device Check` : 'Device Check'),
}))
useSeoMeta({ ogSiteName: 'Device Check', ogType: 'website', twitterCard: 'summary' })
</script>

<template>
  <div class="app">
    <AppHeader />
    <main class="container">
      <NuxtPage />
    </main>
    <footer class="ftr">
      <div class="container inner">
        <span>{{ t('footer.text') }}</span>
        <span class="mono">{{ t('footer.stage') }}</span>
      </div>
    </footer>
    <Transition name="fade">
      <div v-if="toast" class="toast" :class="toast.kind" role="status">{{ toast.text }}</div>
    </Transition>
  </div>
</template>

<style>
.app { min-height: 100vh; display: flex; flex-direction: column; }
.app > main { flex: 1; }
.ftr { border-top: 1px solid var(--border); margin-top: 40px; color: var(--text-3); font-size: 13px; }
.ftr .inner { display: flex; justify-content: space-between; gap: 12px; flex-wrap: wrap; padding-top: 18px; padding-bottom: 24px; }
.toast {
  position: fixed; left: 50%; bottom: 24px; transform: translateX(-50%); z-index: 60;
  padding: 10px 16px; border-radius: 10px; background: #1f3a2e; border: 1px solid var(--c-exact); color: var(--text);
  font-size: 14px; box-shadow: 0 8px 24px rgba(0, 0, 0, .4); max-width: calc(100vw - 32px);
}
.toast.err { background: #3a1f22; border-color: var(--danger); }
.fade-enter-active, .fade-leave-active { transition: opacity .2s; }
.fade-enter-from, .fade-leave-to { opacity: 0; }
</style>
