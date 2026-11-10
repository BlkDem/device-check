<script setup lang="ts">
defineProps<{ id: string; icon: string; title: string; subtitle?: string }>()
</script>

<template>
  <section :id="id" class="card section">
    <header>
      <span class="ico"><HwIcon :name="icon" :size="20" /></span>
      <div class="ttl">
        <h2>{{ title }}</h2>
        <p v-if="subtitle" class="sub">{{ subtitle }}</p>
      </div>
      <div class="extra"><slot name="extra" /></div>
      <span class="sig" aria-hidden="true"><i v-for="n in 6" :key="n" /></span>
    </header>
    <div class="body"><slot /></div>
  </section>
</template>

<style scoped>
.section { counter-increment: sec; padding: 18px 20px 20px; scroll-margin-top: 120px; }
header {
  position: relative; display: flex; align-items: center; gap: 14px;
  margin: 0 0 10px; padding-bottom: 14px; border-bottom: 1px solid var(--border);
}
header::after {
  content: ''; position: absolute; left: 0; bottom: -1px; width: 88px; height: 1px;
  background: var(--accent); box-shadow: 0 0 8px var(--accent);
}
.ico {
  --b: var(--accent);
  display: grid; place-items: center; width: 40px; height: 40px; flex: none;
  color: var(--accent);
  background:
    linear-gradient(var(--b), var(--b)) left top / 8px 1.5px no-repeat,
    linear-gradient(var(--b), var(--b)) left top / 1.5px 8px no-repeat,
    linear-gradient(var(--b), var(--b)) right top / 8px 1.5px no-repeat,
    linear-gradient(var(--b), var(--b)) right top / 1.5px 8px no-repeat,
    linear-gradient(var(--b), var(--b)) left bottom / 8px 1.5px no-repeat,
    linear-gradient(var(--b), var(--b)) left bottom / 1.5px 8px no-repeat,
    linear-gradient(var(--b), var(--b)) right bottom / 8px 1.5px no-repeat,
    linear-gradient(var(--b), var(--b)) right bottom / 1.5px 8px no-repeat,
    radial-gradient(circle at 50% 50%, rgba(25, 230, 255, .2), rgba(25, 230, 255, .03) 70%);
}
.ico :deep(svg) { filter: drop-shadow(0 0 4px rgba(25, 230, 255, .6)); }
.ttl { min-width: 0; }
h2 { display: flex; align-items: baseline; gap: 10px; flex-wrap: wrap; font-size: 19px; letter-spacing: .1em; }
h2::before {
  content: counter(sec, decimal-leading-zero) ' //';
  font: 700 12px/1 var(--mono); letter-spacing: .08em; color: var(--magenta);
  text-shadow: 0 0 8px rgba(255, 46, 196, .5);
}
.sub { margin-top: 4px; font-size: 12.5px; color: var(--text-3); }
.extra { margin-left: auto; }
.extra:empty { display: none; }
.sig { display: flex; align-items: flex-end; gap: 3px; height: 16px; margin-left: auto; flex: none; }
.extra:not(:empty) + .sig { margin-left: 10px; }
.sig i { width: 3px; background: var(--accent); opacity: .7; }
.sig i:nth-child(1) { height: 30%; }
.sig i:nth-child(2) { height: 45%; }
.sig i:nth-child(3) { height: 60%; }
.sig i:nth-child(4) { height: 75%; }
.sig i:nth-child(5) { height: 90%; opacity: .35; }
.sig i:nth-child(6) { height: 100%; opacity: .2; }
.body { display: grid; grid-template-columns: minmax(0, 1fr); gap: 14px; }
@media (max-width: 640px) {
  .section { padding: 14px 12px 16px; scroll-margin-top: 170px; }
  .ico { width: 34px; height: 34px; }
  h2 { font-size: 16px; }
  .sig { display: none; }
}
</style>
