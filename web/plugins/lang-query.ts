// Allows sharing localized links: /?lang=en or /about?lang=ru
export default defineNuxtPlugin(() => {
  const q = useRequestURL().searchParams.get('lang')
  if (q !== 'ru' && q !== 'en') return
  const { locale } = useI18n()
  // On the client switch after hydration: static pages are prerendered in one language.
  if (import.meta.server) locale.value = q
  else onNuxtReady(() => (locale.value = q))
})
