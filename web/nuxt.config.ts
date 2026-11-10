export default defineNuxtConfig({
  compatibilityDate: '2024-09-01',
  devtools: { enabled: false },
  ssr: true,
  css: ['~/assets/main.css'],
  app: {
    head: {
      charset: 'utf-8',
      viewport: 'width=device-width, initial-scale=1',
      meta: [{ name: 'theme-color', content: '#0b0e13' }],
      link: [{ rel: 'icon', type: 'image/svg+xml', href: '/favicon.svg' }],
    },
  },
  typescript: { strict: true },
  // Production is a static site (nuxt generate → nginx). Every page is prerendered; scans run in the browser.
  nitro: {
    prerender: { crawlLinks: true, routes: ['/', '/agent', '/about', '/limitations', '/privacy'], failOnError: true },
  },
  // Payload stays inlined in each page: separate _payload.json files are not needed for a site this small, and
  // payload caching of "/" fails with unstorage ≥ 1.16 ("Invalid key") under Nuxt 3.13.
  experimental: { payloadExtraction: false },
  runtimeConfig: {
    public: {
      siteUrl: 'https://device-check.umolab.ru',
      /** Manifest of the latest agent release, published next to the site by CI. */
      agentManifest: '/downloads/agent/latest.json',
      releasesUrl: 'https://github.com/BlkDem/device-check/releases',
    },
  },
})
