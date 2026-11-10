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
        <span class="mono stage"><i aria-hidden="true" />{{ t('footer.stage') }}</span>
      </div>
    </footer>
    <Transition name="toast">
      <div v-if="toast" class="toast" :class="toast.kind" role="status">
        <span class="tk mono" aria-hidden="true">{{ toast.kind === 'err' ? '!!' : '>>' }}</span>{{ toast.text }}
      </div>
    </Transition>
  </div>
</template>

<style>
.app { min-height: 100vh; display: flex; flex-direction: column; }
.app > main { flex: 1; }
.ftr {
  position: relative; margin-top: 48px; color: var(--text-3); font-size: 12.5px;
  border-top: 1px solid var(--border); background: linear-gradient(180deg, rgba(8, 12, 20, .6), rgba(5, 7, 12, .9));
}
.ftr::before {
  content: ''; position: absolute; left: 0; right: 0; top: -1px; height: 1px;
  background: linear-gradient(90deg, transparent, var(--accent-line) 20%, var(--magenta-line) 80%, transparent);
}
.ftr .inner { display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap; padding-top: 18px; padding-bottom: 24px; }
.ftr .stage {
  display: inline-flex; align-items: center; gap: 8px; text-transform: uppercase; letter-spacing: .14em; font-size: 11px; color: var(--text-2);
}
.ftr .stage i { width: 7px; height: 7px; background: var(--c-exact); box-shadow: 0 0 8px var(--c-exact); animation: hud-blink 2.4s steps(1) infinite; }
.toast {
  position: fixed; left: 50%; bottom: 24px; transform: translateX(-50%); z-index: 60;
  display: flex; align-items: center; gap: 10px;
  padding: 11px 18px 11px 14px; max-width: calc(100vw - 32px);
  background:
    linear-gradient(var(--c-exact), var(--c-exact)) left top / 3px 100% no-repeat,
    linear-gradient(90deg, rgba(54, 245, 154, .16), rgba(8, 12, 20, .96) 60%);
  box-shadow: inset 0 0 0 1px rgba(54, 245, 154, .45);
  clip-path: var(--chamfer-br);
  color: var(--text); font: 600 13px/1.35 var(--mono); letter-spacing: .04em;
}
.toast .tk { color: var(--c-exact); font-weight: 700; }
.toast.err {
  background:
    linear-gradient(var(--danger), var(--danger)) left top / 3px 100% no-repeat,
    linear-gradient(90deg, rgba(255, 77, 109, .18), rgba(8, 12, 20, .96) 60%);
  box-shadow: inset 0 0 0 1px rgba(255, 77, 109, .5);
}
.toast.err .tk { color: var(--danger); }
.toast-enter-active, .toast-leave-active { transition: opacity .18s, transform .18s; }
.toast-enter-from, .toast-leave-to { opacity: 0; transform: translate(-50%, 8px); }
</style>
