import en from '~/i18n/en'
import ru from '~/i18n/ru'

export type Locale = 'ru' | 'en'
const messages: Record<Locale, Record<string, string>> = { ru, en }
export const LOCALES: Locale[] = ['ru', 'en']

export const localeFromLanguage = (lang: string): Locale => (/^(ru|uk|be|kk)/i.test(lang) ? 'ru' : 'en')

function detectLocale(): Locale {
  if (import.meta.server) {
    // Prerendering (static build) has no request headers: Russian is the default, the browser corrects it on load.
    const header = useRequestHeaders(['accept-language'])['accept-language']
    return header ? localeFromLanguage(header) : 'ru'
  }
  return localeFromLanguage(navigator.language)
}

export function useI18n() {
  const cookie = useCookie<Locale | undefined>('dc_locale', { sameSite: 'lax', maxAge: 60 * 60 * 24 * 365 })
  // Shared state is the source of truth; the cookie only persists the choice.
  const state = useState<Locale>('locale', () => (cookie.value && LOCALES.includes(cookie.value) ? cookie.value : detectLocale()))
  const locale = computed<Locale>({
    get: () => state.value,
    set: (v) => {
      state.value = v
      cookie.value = v
    },
  })

  const has = (key: string) => key in messages[locale.value] || key in messages.en

  function t(key: string, params?: Record<string, string | number>): string {
    let s = messages[locale.value][key] ?? messages.en[key] ?? key
    if (params) for (const [k, v] of Object.entries(params)) s = s.replaceAll(`{${k}}`, String(v))
    return s
  }

  return { locale, t, has }
}
