<script setup lang="ts">
import type { FieldDef } from '~/lib/sections'
import type { Field } from '~/lib/types'

// `data` is any report section (CpuInfo, GpuInfo, ...). Typed as object to accept interfaces.
const props = defineProps<{ defs: FieldDef[]; data: object }>()
const hide = useState('hideUnavailable', () => false)

const rows = computed(() =>
  props.defs
    .map((def) => ({ def, field: (props.data as Record<string, Field | undefined>)[def.key] }))
    .filter((r): r is { def: FieldDef; field: Field } => !!r.field && !(hide.value && r.field.confidence === 'unavailable')),
)
</script>

<template>
  <dl class="fields">
    <FieldRow v-for="r in rows" :key="r.def.key" :def="r.def" :field="r.field" />
  </dl>
</template>

<style scoped>
.fields { margin: 0; }
</style>
