<script setup lang="ts">
import { formatValue } from '~/lib/format'
import type { FieldDef } from '~/lib/sections'
import type { Field } from '~/lib/types'

const props = defineProps<{ def: FieldDef; field: Field }>()
const { t, has, locale } = useI18n()
const { copy } = useCopy()

const label = computed(() => t(`f.${props.def.key}`))
const hint = computed(() => (has(`hint.${props.def.key}`) ? t(`hint.${props.def.key}`) : ''))
const available = computed(() => props.field.confidence !== 'unavailable')
const text = computed(() => formatValue(props.field, props.def.fmt, t, has, locale.value))
const long = computed(() => props.def.key === 'user_agent' || props.def.key === 'renderer_raw')
</script>

<template>
  <div class="row" :class="{ off: !available }">
    <dt>
      <span>{{ label }}</span>
      <Tip v-if="hint" :text="hint" />
    </dt>
    <dd>
      <div class="val">
        <span v-if="available" class="mono v" :class="{ long }">{{ text }}</span>
        <span v-else class="v none">{{ t('status.unavailable_value') }}</span>
        <ConfidenceBadge :c="field.confidence" />
        <button v-if="available" class="icon-btn" :aria-label="t('action.copy')" :title="t('action.copy')" @click="copy(`${label}: ${text}`)">
          <HwIcon name="copy" :size="14" />
        </button>
      </div>
      <div class="meta">
        <span>{{ t('meta.source') }}: <code>{{ field.source }}</code></span>
        <span v-if="field.reason"> · {{ t(`reason.${field.reason}`) }}</span>
        <span v-if="field.note"> · {{ t(`note.${field.note}`) }}</span>
      </div>
    </dd>
  </div>
</template>

<style scoped>
.row {
  display: grid; grid-template-columns: minmax(150px, 34%) 1fr; gap: 4px 16px;
  padding: 10px 0; border-top: 1px solid var(--border);
}
.row:first-child { border-top: 0; }
dt { display: flex; align-items: flex-start; gap: 6px; color: var(--text-2); font-size: 13.5px; padding-top: 2px; }
dd { margin: 0; min-width: 0; }
.val { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; min-height: 28px; }
.v { font-size: 14px; color: var(--text); word-break: break-word; }
.v.long { font-size: 12.5px; color: var(--text-2); }
.none { color: var(--text-3); font-style: italic; }
.meta { font-size: 11.5px; color: var(--text-3); margin-top: 2px; word-break: break-word; }
.meta code { color: var(--text-3); font-size: 11.5px; }
.off dt { color: var(--text-3); }
@media (max-width: 640px) {
  .row { grid-template-columns: minmax(0, 1fr); }
}
</style>
