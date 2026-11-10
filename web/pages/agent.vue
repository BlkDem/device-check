<script setup lang="ts">
import { formatBytes } from '~/lib/format'

interface ReleaseFile { kind: 'installer' | 'portable' | 'framework' | 'cli'; name: string; size_bytes: number; sha256: string }
interface Manifest { version: string; released_at: string; release_url: string; files: ReleaseFile[] }

const { t, locale } = useI18n()
const { copy } = useCopy()
const cfg = useRuntimeConfig().public
const { agent } = useScan()

useSeoMeta({
  title: () => t('seo.agent.title'),
  description: () => t('seo.agent.desc'),
  ogTitle: () => `${t('seo.agent.title')} — Device Check`,
  ogDescription: () => t('seo.agent.desc'),
})

// Fetched in the browser: the manifest changes with every release, the page itself is static.
const manifest = ref<Manifest | null>(null)
const failed = ref(false)
// Opened from the agent's own dashboard (http://127.0.0.1:port): releases live on the public site.
const local = ref(false)
onMounted(async () => {
  local.value = location.hostname === '127.0.0.1' // the tray always opens the embedded dashboard on 127.0.0.1
  if (local.value) return
  try {
    const r = await fetch(cfg.agentManifest, { cache: 'no-store' })
    if (!r.ok) throw new Error(String(r.status))
    manifest.value = await r.json()
  } catch {
    failed.value = true
  }
})

const order: ReleaseFile['kind'][] = ['installer', 'portable', 'cli', 'framework']
const files = computed(() => (manifest.value?.files ?? []).slice().sort((a, b) => order.indexOf(a.kind) - order.indexOf(b.kind)))
const href = (f: ReleaseFile) => `${cfg.agentManifest.replace(/[^/]+$/, '')}${f.name}`
const date = computed(() => (manifest.value ? new Date(manifest.value.released_at).toLocaleDateString(locale.value) : ''))
const rows = ['cpu', 'memory', 'board', 'gpu', 'storage', 'health', 'display'] as const
</script>

<template>
  <article class="agent">
    <section class="hero">
      <span class="kicker">{{ t('nav.agent') }}</span>
      <h1>{{ t('agent.h1') }}</h1>
      <p class="dim">{{ t('agent.lead') }}</p>
      <p v-if="agent" class="notice ok"><HwIcon name="check" :size="16" />{{ t('agent.connected', { v: agent.version }) }}</p>
    </section>

    <section class="card dl">
      <div class="dl-head">
        <h2>{{ t('agent.download') }}</h2>
        <span v-if="manifest" class="mono ver">v{{ manifest.version }} · {{ date }}</span>
      </div>
      <p v-if="local" class="notice"><HwIcon name="info" :size="16" /><span>{{ t('agent.local_updates') }} <a :href="`${cfg.siteUrl}/agent`" target="_blank" rel="noopener">{{ cfg.siteUrl.replace('https://', '') }}/agent</a></span></p>
      <p v-else-if="!manifest && !failed" class="muted">{{ t('agent.loading') }}</p>
      <p v-if="failed" class="notice warn">
        <HwIcon name="alert" :size="16" />{{ t('agent.manifest_failed') }}
        <a :href="cfg.releasesUrl" target="_blank" rel="noopener">GitHub Releases</a>
      </p>
      <div v-if="cfg.storeUrl && !local" class="file store">
        <div class="fi">
          <div class="fk">Microsoft Store<span class="rec mono">{{ t('agent.recommended') }}</span></div>
          <div class="fd">{{ t('agent.store_desc') }}</div>
        </div>
        <a class="btn primary" :href="cfg.storeUrl" target="_blank" rel="noopener"><HwIcon name="download" :size="16" />{{ t('agent.store_get') }}</a>
      </div>
      <div v-if="manifest" class="files">
        <div v-for="f in files" :key="f.name" class="file" :class="f.kind">
          <div class="fi">
            <div class="fk">{{ t(`agent.kind.${f.kind}`) }}<span v-if="f.kind === 'installer' && !cfg.storeUrl" class="rec mono">{{ t('agent.recommended') }}</span></div>
            <div class="fd">{{ t(`agent.kind_desc.${f.kind}`) }}</div>
            <div class="fm mono">
              {{ f.name }} · {{ formatBytes(f.size_bytes, t) }}
              <button class="sha" :title="t('agent.copy_sha')" @click="copy(f.sha256)">SHA-256 {{ f.sha256.slice(0, 12) }}…</button>
            </div>
          </div>
          <a class="btn" :class="{ primary: f.kind === 'installer' && !cfg.storeUrl }" :href="href(f)" download>
            <HwIcon name="download" :size="16" />{{ t('agent.get') }}
          </a>
        </div>
      </div>
      <p class="muted small req">{{ t('agent.requirements') }} · <a :href="manifest?.release_url ?? cfg.releasesUrl" target="_blank" rel="noopener">{{ t('agent.release_notes') }}</a></p>
    </section>

    <section class="steps-sec">
      <h2>{{ t('agent.how') }}</h2>
      <ol class="how">
        <li v-for="i in 4" :key="i"><span class="n mono">0{{ i }}</span><div><strong>{{ t(`agent.s${i}.t`) }}</strong><p>{{ t(`agent.s${i}.d`) }}</p></div></li>
      </ol>
    </section>

    <section>
      <h2>{{ t('agent.compare') }}</h2>
      <div class="tbl-wrap card">
        <table class="cmp">
          <thead><tr><th /><th>{{ t('agent.col_browser') }}</th><th>{{ t('agent.col_agent') }}</th></tr></thead>
          <tbody>
            <tr v-for="r in rows" :key="r">
              <td>{{ t(`agent.row.${r}`) }}</td>
              <td class="dim">{{ t(`agent.row.${r}.browser`) }}</td>
              <td>{{ t(`agent.row.${r}.agent`) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <section class="cols">
      <div class="card col">
        <h2>{{ t('agent.safety') }}</h2>
        <ul><li v-for="i in 5" :key="i">{{ t(`agent.safe${i}`) }}</li></ul>
      </div>
      <div class="card col">
        <h2>{{ t('agent.faq') }}</h2>
        <dl>
          <template v-for="i in 4" :key="i"><dt>{{ t(`agent.q${i}`) }}</dt><dd>{{ t(`agent.a${i}`) }}</dd></template>
        </dl>
      </div>
    </section>
  </article>
</template>

<style scoped>
.agent { display: grid; gap: 28px; padding-top: 28px; padding-bottom: 12px; max-width: 1080px; margin: 0 auto; padding-left: 16px; padding-right: 16px; }
.hero h1 { margin: 6px 0 8px; }
.dim { color: var(--text-2); }
.dl { padding: 18px; display: grid; gap: 14px; }
.dl-head { display: flex; justify-content: space-between; align-items: baseline; gap: 10px; flex-wrap: wrap; }
.dl-head h2 { margin: 0; }
.ver { color: var(--yellow); font-size: 12px; letter-spacing: .08em; }
.files { display: grid; gap: 10px; }
.file { display: flex; justify-content: space-between; align-items: center; gap: 14px; padding: 12px 14px; background: rgba(8, 12, 20, .6); box-shadow: inset 0 0 0 1px var(--border); }
.file.installer, .file.store { box-shadow: inset 2px 0 0 var(--accent), inset 0 0 0 1px var(--accent-line); }
.fi { min-width: 0; }
.fk { font: 600 15px var(--display); letter-spacing: .04em; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
.rec { font-size: 10px; color: #001218; background: var(--accent); padding: 1px 6px; letter-spacing: .1em; text-transform: uppercase; }
.fd { color: var(--text-2); font-size: 13px; margin-top: 3px; }
.fm { color: var(--text-3); font-size: 11.5px; margin-top: 5px; word-break: break-all; }
.sha { background: none; border: 0; color: var(--text-3); font: inherit; cursor: pointer; padding: 0; text-decoration: underline dotted; }
.sha:hover { color: var(--accent); }
.file .btn { flex: none; }
.req { margin: 0; }
.how { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 10px; }
.how li { display: flex; gap: 12px; padding: 14px; background: rgba(8, 12, 20, .6); box-shadow: inset 0 0 0 1px var(--border); }
.how .n { color: var(--magenta); font-size: 13px; }
.how p { margin: 4px 0 0; color: var(--text-2); font-size: 13.5px; }
.tbl-wrap { overflow-x: auto; padding: 4px 0; }
.cmp { width: 100%; border-collapse: collapse; font-size: 13.5px; }
.cmp th, .cmp td { text-align: left; padding: 9px 14px; border-top: 1px dashed var(--border); vertical-align: top; }
.cmp thead th { border-top: 0; font: 600 11px var(--mono); text-transform: uppercase; letter-spacing: .12em; color: var(--text-3); }
.cmp td:first-child { font-weight: 600; white-space: nowrap; }
.cols { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 14px; }
.col { padding: 16px; }
.col h2 { margin-top: 0; }
.col ul { margin: 0; padding-left: 18px; display: grid; gap: 6px; color: var(--text-2); font-size: 14px; }
dl { margin: 0; display: grid; gap: 4px; }
dt { font-weight: 600; margin-top: 8px; }
dd { margin: 0; color: var(--text-2); font-size: 14px; }
@media (max-width: 640px) {
  .file { flex-direction: column; align-items: stretch; }
}
</style>
