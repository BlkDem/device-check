<script setup lang="ts">
import type { Confidence } from '~/lib/types'
defineProps<{ c: Confidence }>()
const { t } = useI18n()
</script>

<template>
  <span class="badge" :class="c" :title="t(`status.desc.${c}`)">
    <i />{{ t(`status.${c}`) }}
  </span>
</template>

<style scoped>
.badge {
  display: inline-flex; align-items: center; gap: 6px; flex: none;
  font-size: 11px; font-weight: 600; letter-spacing: .03em; text-transform: uppercase;
  padding: 2px 8px; border-radius: 99px; border: 1px solid currentColor;
  line-height: 1.5; white-space: nowrap;
}
.badge i { width: 6px; height: 6px; border-radius: 50%; background: currentColor; }
.exact { color: var(--c-exact); }
.approximate { color: var(--c-approximate); }
.detected { color: var(--c-detected); }
.unavailable { color: var(--c-unavailable); }
</style>
