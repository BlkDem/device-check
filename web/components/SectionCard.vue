<script setup lang="ts">
defineProps<{ id: string; icon: string; title: string; subtitle?: string }>()
</script>

<template>
  <section :id="id" class="card section">
    <header>
      <span class="ico"><HwIcon :name="icon" :size="20" /></span>
      <div>
        <h2>{{ title }}</h2>
        <p v-if="subtitle" class="sub">{{ subtitle }}</p>
      </div>
      <div class="extra"><slot name="extra" /></div>
    </header>
    <div class="body"><slot /></div>
  </section>
</template>

<style scoped>
.section { padding: 18px 20px; scroll-margin-top: 120px; }
header { display: flex; align-items: center; gap: 12px; margin-bottom: 8px; }
.ico {
  display: grid; place-items: center; width: 38px; height: 38px; border-radius: 10px; flex: none;
  background: var(--accent-soft); color: var(--accent);
}
.sub { font-size: 13px; color: var(--text-3); }
.extra { margin-left: auto; }
.body { display: grid; grid-template-columns: minmax(0, 1fr); gap: 14px; }
@media (max-width: 640px) {
  .section { padding: 14px; }
}
</style>
