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
.tip { position: relative; display: inline-grid; place-items: center; color: var(--text-3); cursor: help; outline: none; flex: none; }
.tip:hover, .tip:focus { color: var(--accent); }
.tip:hover > svg, .tip:focus > svg { filter: drop-shadow(0 0 4px var(--accent)); }
.tip:focus-visible { outline: 1px solid var(--yellow); outline-offset: 2px; }
.bubble {
  position: absolute; z-index: 20; left: -10px; bottom: calc(100% + 9px);
  width: max-content; max-width: min(320px, 80vw);
  padding: 9px 12px 9px 13px;
  background:
    linear-gradient(var(--accent), var(--accent)) left top / 2px 100% no-repeat,
    linear-gradient(180deg, #13243a, #0c1626);
  box-shadow: inset 0 0 0 1px var(--accent-line);
  clip-path: var(--chamfer-br);
  color: var(--text); font: 400 12.5px/1.45 var(--font); text-transform: none; letter-spacing: 0;
  display: none; pointer-events: none;
}
.tip:hover .bubble, .tip:focus .bubble { display: block; }
/* On phones the bubble spans the nearest positioned ancestor (the field row)
   instead of the icon, so it never pushes past the viewport edge. */
@media (max-width: 640px) {
  .tip { position: static; }
  .bubble { left: 4px; right: 4px; bottom: calc(100% - 6px); width: auto; max-width: none; }
}
</style>
