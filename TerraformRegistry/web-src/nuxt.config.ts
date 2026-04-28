// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  compatibilityDate: "2025-05-15",
  devtools: { enabled: true },
  modules: ["@nuxt/icon", "@nuxt/ui", "@nuxt/fonts", "@vueuse/nuxt"],
  css: ["~/assets/css/main.css"],
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
