import { localeFromLanguage } from '~/composables/useI18n'

// Static pages are prerendered in one language. Unless the visitor chose one (cookie) or asked via ?lang=,
// switch to the browser language after hydration. The choice is not stored: only an explicit toggle sets the cookie.
export default defineNuxtPlugin(() => {
  const cookie = useCookie('dc_locale')
  if (cookie.value || new URLSearchParams(location.search).has('lang')) return
  const state = useState<'ru' | 'en'>('locale')
  onNuxtReady(() => {
    const wanted = localeFromLanguage(navigator.language)
    if (state.value !== wanted) state.value = wanted
  })
})
