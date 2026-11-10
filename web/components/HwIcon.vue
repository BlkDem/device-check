<script setup lang="ts">
const props = withDefaults(defineProps<{ name: string; size?: number }>(), { size: 20 })

const paths: Record<string, string> = {
  cpu: 'M9 3v2M15 3v2M9 19v2M15 19v2M3 9h2M3 15h2M19 9h2M19 15h2M7 5h10a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2zM9.5 9.5h5v5h-5z',
  board: 'M4 4h16v16H4zM8 8h4v4H8zM15 8h2M15 11h2M8 15h9M4 9H2M4 14H2',
  memory: 'M3 7h18v9H3zM6 10v3M9.5 10v3M13 10v3M16.5 10v3M3 16v2M7 16v2M11 16v2M15 16v2M19 16v2',
  gpu: 'M2 6h17v10H2zM19 9h3M19 13h3M6 16v3M11 16v3M9 11m-3 0a3 3 0 1 0 6 0a3 3 0 1 0-6 0M15 9v4',
  storage: 'M4 5h16v14H4zM4 14h16M8 17h.01M12 17h4',
  display: 'M3 4h18v12H3zM8 20h8M12 16v4',
  system: 'M4 5h16v11H4zM2 19h20M9 9l-2 2 2 2M15 9l2 2-2 2',
  browser: 'M3 5h18v14H3zM3 9h18M6 7h.01M8.5 7h.01',
  computer: 'M5 3h10v18H5zM8 7h4M8 10h4M10 17h.01M17 8h3v13h-3',
  info: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20zM12 16v-4M12 8h.01',
  copy: 'M9 9h11v11H9zM5 15H4V4h11v1',
  download: 'M12 3v12M7 10l5 5 5-5M4 20h16',
  save: 'M5 3h11l3 3v15H5zM8 3v5h7V3M8 14h8v7H8z',
  scan: 'M3 8V5a2 2 0 0 1 2-2h3M16 3h3a2 2 0 0 1 2 2v3M21 16v3a2 2 0 0 1-2 2h-3M8 21H5a2 2 0 0 1-2-2v-3M3 12h18',
  check: 'M5 12l5 5L20 7',
  alert: 'M12 3l10 18H2zM12 10v5M12 18h.01',
  link: 'M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1',
  globe: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20zM2 12h20M12 2a15 15 0 0 1 0 20M12 2a15 15 0 0 0 0 20',
  trash: 'M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3',
  folder: 'M3 6h6l2 2h10v11H3z',
  x: 'M6 6l12 12M18 6L6 18',
}
const d = computed(() => paths[props.name] ?? paths.info)
</script>

<template>
  <svg :width="size" :height="size" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7"
    stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
    <path :d="d" />
  </svg>
</template>
