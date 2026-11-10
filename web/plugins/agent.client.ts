// Opened from the Windows Agent tray menu (/#agent=<port>&token=…): connect and show the agent's report.
export default defineNuxtPlugin(() => {
  onNuxtReady(() => useScan().connectAgent())
})
