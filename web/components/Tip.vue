<script setup lang="ts">
defineProps<{ text: string }>()
</script>

<template>
  <span class="tip" tabindex="0" role="note" :aria-label="text">
    <HwIcon name="info" :size="14" />
    <span class="bubble" role="tooltip">{{ text }}</span>
  </span>
</template>

<style scoped>
.tip { position: relative; display: inline-grid; place-items: center; color: var(--text-3); cursor: help; outline: none; }
.tip:hover, .tip:focus { color: var(--accent); }
.bubble {
  position: absolute; z-index: 20; left: 50%; bottom: calc(100% + 8px); transform: translateX(-50%);
  width: max-content; max-width: min(300px, 80vw);
  padding: 8px 10px; border-radius: 8px; background: #263043; border: 1px solid var(--border-strong);
  color: var(--text); font-size: 12.5px; font-weight: 400; line-height: 1.45; text-transform: none; letter-spacing: 0;
  box-shadow: 0 8px 24px rgba(0, 0, 0, .4);
  display: none; pointer-events: none;
}
.tip:hover .bubble, .tip:focus .bubble { display: block; }
@media (max-width: 640px) {
  .bubble { left: 0; transform: none; max-width: calc(100vw - 64px); }
}
</style>
