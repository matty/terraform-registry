// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  compatibilityDate: "2025-05-15",
  devtools: { enabled: true },
  modules: ["@nuxt/icon", "@nuxt/ui", "@nuxt/fonts"],
  css: ["~/assets/css/main.css"],
  nitro: {
    prerender: {
      routes: ["/"],
    },
  },
  ssr: false,
});
