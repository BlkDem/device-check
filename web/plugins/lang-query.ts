// Allows sharing localized links: /?lang=en or /about?lang=ru
export default defineNuxtPlugin(() => {
  const q = useRequestURL().searchParams.get('lang')
  if (q === 'ru' || q === 'en') useI18n().locale.value = q
})
