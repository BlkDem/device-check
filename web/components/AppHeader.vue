<script setup lang="ts">
const { t, locale } = useI18n()
const toggle = () => (locale.value = locale.value === 'ru' ? 'en' : 'ru')
const links = [
  { to: '/', key: 'nav.dashboard' },
  { to: '/agent', key: 'nav.agent' },
  { to: '/about', key: 'nav.about' },
  { to: '/limitations', key: 'nav.limitations' },
  { to: '/privacy', key: 'nav.privacy' },
]
</script>

<template>
  <header class="hdr">
    <div class="container inner">
      <NuxtLink to="/" class="logo">
        <svg width="30" height="30" viewBox="0 0 32 32" aria-hidden="true" class="mark">
          <path d="M9 2h14l7 7v14l-7 7H9l-7-7V9z" fill="rgba(25,230,255,.08)" stroke="var(--accent)" stroke-width="1.6" />
          <path d="M16 6v4M16 22v4M6 16h4M22 16h4" stroke="var(--magenta)" stroke-width="1.6" />
          <path d="M11.5 16.2l3.2 3.2 6-6.6" fill="none" stroke="var(--accent)" stroke-width="2.4" stroke-linecap="square" />
        </svg>
        <span class="word">Device<b>Check</b></span>
      </NuxtLink>
      <nav>
        <NuxtLink v-for="(l, i) in links" :key="l.to" :to="l.to" class="nl"><span class="ix" aria-hidden="true">0{{ i + 1 }}</span>{{ t(l.key) }}</NuxtLink>
      </nav>
      <button class="btn lang" @click="toggle"><HwIcon name="globe" :size="15" />{{ t('lang.switch') }}</button>
    </div>
  </header>
</template>

<style scoped>
.hdr {
  position: sticky; top: 0; z-index: 30;
  background: linear-gradient(180deg, rgba(5, 7, 12, .96), rgba(6, 9, 15, .88));
  backdrop-filter: blur(10px);
  border-bottom: 1px solid var(--border);
}
.hdr::after {
  content: ''; position: absolute; left: 0; right: 0; bottom: -1px; height: 1px; pointer-events: none;
  background: linear-gradient(90deg, var(--accent) 0, var(--accent-line) 18%, transparent 45%, transparent 70%, var(--magenta-line) 100%);
}
.inner { display: flex; align-items: center; gap: 22px; height: 60px; }
.logo { display: flex; align-items: center; gap: 10px; color: var(--text); flex: none; }
.logo:hover { text-decoration: none; }
.mark { filter: drop-shadow(0 0 6px rgba(25, 230, 255, .45)); }
.word { font: 600 18px/1 var(--display); text-transform: uppercase; letter-spacing: .1em; }
.word b { color: var(--accent); font-weight: 700; text-shadow: 0 0 12px rgba(25, 230, 255, .55); }
.logo:hover .word { animation: hud-glitch .28s steps(3) 1; }
nav { display: flex; gap: 2px; overflow-x: auto; scrollbar-width: none; min-width: 0; }
nav::-webkit-scrollbar { display: none; }
.nl {
  position: relative; display: inline-flex; align-items: baseline; gap: 6px;
  padding: 8px 10px; color: var(--text-2); white-space: nowrap;
  font: 600 12px/1 var(--mono); text-transform: uppercase; letter-spacing: .12em;
}
.ix { font-size: 10px; color: var(--text-3); letter-spacing: 0; }
.nl::after {
  content: ''; position: absolute; left: 10px; right: 10px; bottom: 2px; height: 2px;
  background: var(--accent); box-shadow: 0 0 8px var(--accent); transform: scaleX(0); transform-origin: left;
  transition: transform .18s;
}
.nl:hover { color: var(--text); text-decoration: none; }
.nl:hover::after { transform: scaleX(.4); }
.nl:hover .ix { color: var(--magenta); }
.nl.router-link-exact-active { color: var(--accent); text-shadow: 0 0 10px rgba(25, 230, 255, .4); }
.nl.router-link-exact-active .ix { color: var(--yellow); }
.nl.router-link-exact-active::after { transform: scaleX(1); }
.nl:focus-visible { outline: 2px solid var(--yellow); outline-offset: -2px; }
.lang { margin-left: auto; padding: 7px 12px; font-size: 11.5px; flex: none; }
@media (max-width: 760px) {
  .inner { flex-wrap: wrap; height: auto; padding-top: 10px; padding-bottom: 4px; gap: 4px 12px; }
  nav { order: 3; width: 100%; margin: 0 -10px; }
  .nl { padding: 8px 10px 9px; }
}
</style>
