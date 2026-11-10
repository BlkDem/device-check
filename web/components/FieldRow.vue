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
  <div class="row" :class="[{ off: !available }, field.confidence]">
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
        <span class="src">{{ t('meta.source') }}: <code>{{ field.source }}</code></span>
        <span v-if="field.reason" class="rsn"> · {{ t(`reason.${field.reason}`) }}</span>
        <span v-if="field.note" class="nte"> · {{ t(`note.${field.note}`) }}</span>
      </div>
    </dd>
  </div>
</template>

<style scoped>
.row {
  position: relative;
  display: grid; grid-template-columns: minmax(150px, 34%) minmax(0, 1fr); gap: 4px 18px;
  padding: 10px 10px 10px 14px;
  border-top: 1px dashed rgba(43, 66, 96, .55);
  transition: background-color .15s;
}
.row:first-child { border-top: 0; }
.row::before {
  content: ''; position: absolute; left: 0; top: 10px; bottom: 10px; width: 2px;
  background: var(--border-strong); transition: background-color .15s, box-shadow .15s;
}
.row.exact::before { background: color-mix(in srgb, var(--c-exact) 55%, transparent); }
.row.approximate::before { background: color-mix(in srgb, var(--c-approximate) 55%, transparent); }
.row.detected::before { background: color-mix(in srgb, var(--c-detected) 55%, transparent); }
.row:hover { background-color: rgba(25, 230, 255, .035); }
.row:hover::before { background: var(--accent); box-shadow: 0 0 8px var(--accent); }
dt {
  display: flex; align-items: flex-start; gap: 6px; padding-top: 5px;
  color: var(--text-2); font: 600 11.5px/1.35 var(--mono); text-transform: uppercase; letter-spacing: .08em;
}
dd { margin: 0; min-width: 0; }
.val { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; min-height: 28px; }
.v { font-size: 14.5px; font-weight: 500; color: var(--text); word-break: break-word; min-width: 0; }
.v.long { font-size: 12.5px; color: var(--text-2); }
.none { color: var(--text-3); font-style: italic; font-family: var(--mono); font-size: 13px; }
.meta { font-size: 11.5px; color: var(--text-3); margin-top: 3px; word-break: break-word; }
.meta code { color: color-mix(in srgb, var(--accent) 60%, var(--text-3)); font-size: 11px; }
.rsn { color: #c99aa9; }
.off dt { color: var(--text-3); }
.off::before { background: repeating-linear-gradient(180deg, var(--c-unavailable) 0 3px, transparent 3px 6px); opacity: .6; }
@media (max-width: 640px) {
  .row { grid-template-columns: minmax(0, 1fr); padding: 10px 4px 10px 12px; }
  dt { padding-top: 0; }
}
</style>
