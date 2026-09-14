// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  srcDir: '.',
  dir: {
    app: 'app',
  },
  compatibilityDate: "2025-05-15",
  devtools: { enabled: true },
  modules: ["@nuxt/icon", "@nuxt/ui", "@nuxt/fonts", "@vueuse/nuxt"],
  css: ["~/assets/css/main.css"],
  icon: {
    // The portal ships as a static SPA served by the .NET host, so there is no
    // Nitro server behind /api/_nuxt_icon, and the app's CSP sets
    // connect-src 'self'. Anything not bundled at build time would be fetched
    // from api.iconify.design at runtime and blocked. Bundle it all instead.
    provider: "none",
    fallbackToApi: false,
    serverBundle: false,
    clientBundle: {
      // Icon names also live in plain .ts (see useProviderIdentity), which the
      // scanner skips by default.
      scan: {
        globInclude: ["**/*.{vue,jsx,tsx,md,mdc,mdx,ts}"],
      },
      includeCustomCollections: true,
      sizeLimitKb: 512,
      // Nuxt UI resolves these from its own defaults inside node_modules, which
      // the scanner does not read.
      icons: [
        "lucide:arrow-left",
        "lucide:arrow-right",
        "lucide:arrow-up-right",
        "lucide:bug",
        "lucide:check",
        "lucide:chevron-down",
        "lucide:chevron-left",
        "lucide:chevron-right",
        "lucide:chevron-up",
        "lucide:chevrons-left",
        "lucide:chevrons-right",
        "lucide:chevrons-up-down",
        "lucide:ellipsis",
        "lucide:file",
        "lucide:folder",
        "lucide:folder-open",
        "lucide:loader-2",
        "lucide:loader-circle",
        "lucide:minus",
        "lucide:plus",
        "lucide:search",
        "lucide:upload",
        "lucide:x",
      ],
    },
  },
  nitro: {
    prerender: {
      routes: ["/"],
    },
  },
  runtimeConfig: {
    public: {
      featureCreateModule: false,
    },
  },
  ssr: false,
  colorMode: {
    preference: "dark", // default to dark, ready for light theme toggle
  },
  vite: {
    server: {
      proxy: {
        // Forward to .NET backend in dev
        "/api": {
          target: "http://localhost:5131",
          changeOrigin: true,
        },
        "/v1": {
          target: "http://localhost:5131",
          changeOrigin: true,
        },
        "/.well-known": {
          target: "http://localhost:5131",
          changeOrigin: true,
        },
      },
    },
  },
});
