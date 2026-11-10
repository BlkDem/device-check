let timer: ReturnType<typeof setTimeout> | undefined

export function useToast() {
  const toast = useState<{ text: string; kind: 'ok' | 'err' } | null>('toast', () => null)
  function show(text: string, kind: 'ok' | 'err' = 'ok') {
    toast.value = { text, kind }
    clearTimeout(timer)
    timer = setTimeout(() => (toast.value = null), 2200)
  }
  return { toast, show }
}

export function useCopy() {
  const { show } = useToast()
  const { t } = useI18n()
  async function copy(text: string) {
    try {
      if (navigator.clipboard?.writeText) await navigator.clipboard.writeText(text)
      else {
        const ta = document.createElement('textarea')
        ta.value = text
        ta.style.position = 'fixed'
        ta.style.opacity = '0'
        document.body.appendChild(ta)
        ta.select()
        const ok = document.execCommand('copy')
        ta.remove()
        if (!ok) throw new Error('execCommand failed')
      }
      show(t('toast.copied'))
    } catch {
      show(t('toast.copy_failed'), 'err')
    }
  }
  return { copy }
}
