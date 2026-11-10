<script setup lang="ts">
import type { Confidence } from '~/lib/types'
defineProps<{ c: Confidence }>()
const { t } = useI18n()
</script>

<template>
  <span class="badge" :class="c" :title="t(`status.desc.${c}`)">
    <i aria-hidden="true" />{{ t(`status.${c}`) }}
  </span>
</template>

<style scoped>
/* Status is encoded three ways: colour, glyph shape and the text label. */
.badge {
  --c: var(--c-unavailable);
  display: inline-flex; align-items: center; gap: 7px; flex: none;
  padding: 3px 9px 3px 7px;
  font: 700 10.5px/1.4 var(--mono); letter-spacing: .12em; text-transform: uppercase;
  color: var(--c); white-space: nowrap;
  background: color-mix(in srgb, var(--c) 10%, transparent);
  box-shadow: inset 0 0 0 1px color-mix(in srgb, var(--c) 55%, transparent);
  clip-path: polygon(0 0, calc(100% - 6px) 0, 100% 6px, 100% 100%, 0 100%);
}
.badge i { width: 7px; height: 7px; flex: none; background: var(--c); }
.exact { --c: var(--c-exact); }
.exact i { box-shadow: 0 0 6px var(--c); }
.approximate { --c: var(--c-approximate); }
.approximate i { transform: rotate(45deg) scale(.9); }
.detected { --c: var(--c-detected); }
.detected i { background: transparent; box-shadow: inset 0 0 0 1.5px var(--c); }
.unavailable { --c: var(--c-unavailable); background: transparent; box-shadow: none; outline: 1px dashed color-mix(in srgb, var(--c) 70%, transparent); outline-offset: -1px; }
.unavailable i { height: 2px; width: 8px; }
</style>
