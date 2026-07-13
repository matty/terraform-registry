<script setup lang="ts">
import { extractErrorMessage } from '~/composables/useErrorMessage'
import { useMirrorAdmin } from '~/composables/useMirrorAdmin'
import type { MirrorConfigResponse, MirrorLease, MirrorModuleCacheEntry, MirrorProviderCacheEntry } from '~/composables/useMirrorAdmin'

definePageMeta({ middleware: 'auth' })

const { hasPermission } = usePermissions()
const { getConfig, updateConfig, listProviders, listModules, listLeases, purgeProvider, purgeModule } = useMirrorAdmin()
const canRead = computed(() => hasPermission('mirror.read'))
const canManage = computed(() => hasPermission('mirror.manage'))
const canConfigure = computed(() => hasPermission('mirror.configure'))
const config = ref<MirrorConfigResponse | null>(null)
const providers = ref<MirrorProviderCacheEntry[]>([])
const modules = ref<MirrorModuleCacheEntry[]>([])
const leases = ref<MirrorLease[]>([])
const loading = ref(false)
const purging = ref<string | null>(null)
const errorMessage = ref<string | null>(null)
const successMessage = ref<string | null>(null)
const query = ref('')
const state = ref('')
const updatingConfig = ref(false)

const bytes = (value: number | null) => value === null ? 'Unknown' : new Intl.NumberFormat().format(value)
const date = (value: string | null) => value ? new Date(value).toLocaleString() : 'Never'
const providerKey = (item: MirrorProviderCacheEntry) => `${item.hostname}/${item.namespace}/${item.type}/${item.version}/${item.os}/${item.arch}`
const moduleKey = (item: MirrorModuleCacheEntry) => `${item.hostname}/${item.namespace}/${item.name}/${item.provider}/${item.version}`

async function refresh() {
  if (!canRead.value) return
  loading.value = true
  errorMessage.value = null
  try {
    const params = { q: query.value || undefined, state: state.value || undefined, limit: 100 }
    const results = await Promise.all([getConfig(), listProviders(params), listModules(params), listLeases({ limit: 100 })])
    config.value = results[0]
    providers.value = results[1]
    modules.value = results[2]
    leases.value = results[3]
  } catch (error) {
    errorMessage.value = extractErrorMessage(error, 'Failed to load mirror administration data')
  } finally {
    loading.value = false
  }
}

async function removeProvider(entry: MirrorProviderCacheEntry) {
  const key = providerKey(entry)
  if (!canManage.value || !confirm(`Purge cached provider ${key}?`)) return
  purging.value = key
  try {
    await purgeProvider(entry)
    successMessage.value = `Purged ${key}`
    await refresh()
  } catch (error) { errorMessage.value = extractErrorMessage(error, 'Failed to purge provider cache entry') }
  finally { purging.value = null }
}

async function setMirrorEnabled(enabled: boolean) {
  if (!canConfigure.value || !config.value || updatingConfig.value) return
  updatingConfig.value = true
  errorMessage.value = null
  try {
    const effective = { ...config.value.effective, enabled }
    config.value = await updateConfig(effective)
    successMessage.value = enabled ? 'Mirror enabled' : 'Mirror disabled'
  } catch (error) { errorMessage.value = extractErrorMessage(error, 'Failed to update mirror configuration') }
  finally { updatingConfig.value = false }
}

async function removeModule(entry: MirrorModuleCacheEntry) {
  const key = moduleKey(entry)
  if (!canManage.value || !confirm(`Purge cached module ${key}?`)) return
  purging.value = key
  try {
    await purgeModule(entry)
    successMessage.value = `Purged ${key}`
    await refresh()
  } catch (error) { errorMessage.value = extractErrorMessage(error, 'Failed to purge module cache entry') }
  finally { purging.value = null }
}

watch([query, state], refresh)
onMounted(refresh)
</script>

<template>
  <main class="mx-auto max-w-7xl space-y-6 p-6 text-neutral-100">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <div><h1 class="text-2xl font-semibold">Mirror operations</h1><p class="text-sm text-neutral-400">Inspect effective configuration, cache state, and active work.</p></div>
      <button class="rounded bg-neutral-700 px-4 py-2 text-sm hover:bg-neutral-600" :disabled="loading" @click="refresh">Refresh</button>
    </div>
    <div v-if="!canRead" class="rounded border border-amber-500/40 bg-amber-500/10 p-4 text-amber-200">Mirror read permission is required.</div>
    <div v-if="errorMessage" class="rounded border border-red-500/40 bg-red-500/10 p-4 text-red-200">{{ errorMessage }}</div>
    <div v-if="successMessage" class="rounded border border-green-500/40 bg-green-500/10 p-4 text-green-200">{{ successMessage }}</div>
    <section v-if="config" class="rounded border border-neutral-800 bg-neutral-900 p-4"><div class="flex items-center justify-between gap-3"><h2 class="font-medium">Effective configuration</h2><button v-if="canConfigure" class="rounded bg-neutral-700 px-3 py-2 text-sm hover:bg-neutral-600" :disabled="updatingConfig" @click="setMirrorEnabled(!config.effective.enabled)">{{ config.effective.enabled ? 'Disable mirror' : 'Enable mirror' }}</button></div><dl class="mt-3 grid gap-3 sm:grid-cols-4 text-sm"><div><dt class="text-neutral-500">Mirror</dt><dd>{{ config.effective.enabled ? 'Enabled' : 'Disabled' }}</dd></div><div><dt class="text-neutral-500">Provider mirror</dt><dd>{{ config.effective.providers.enabled ? 'Enabled' : 'Disabled' }}</dd></div><div><dt class="text-neutral-500">Module mirror</dt><dd>{{ config.effective.modules.enabled ? 'Enabled' : 'Disabled' }}</dd></div><div><dt class="text-neutral-500">Concurrent downloads</dt><dd>{{ config.effective.limits.maxConcurrentDownloads }}</dd></div></dl><p v-if="canConfigure" class="mt-3 text-xs text-neutral-500">Runtime override: {{ config.hasRuntimeOverride ? 'active' : 'not set' }}.</p></section>
    <section class="rounded border border-neutral-800 bg-neutral-900 p-4"><div class="flex flex-wrap gap-3"><input v-model="query" class="rounded bg-neutral-800 px-3 py-2 text-sm" placeholder="Search cache" /><select v-model="state" class="rounded bg-neutral-800 px-3 py-2 text-sm"><option value="">All states</option><option value="ready">Ready</option><option value="failed">Failed</option><option value="evicted">Evicted</option></select></div></section>
    <section class="rounded border border-neutral-800 bg-neutral-900 p-4"><h2 class="font-medium">Provider cache</h2><div class="mt-3 overflow-x-auto"><table class="w-full text-left text-sm"><thead class="text-neutral-500"><tr><th>Coordinate</th><th>State</th><th>Bytes</th><th>Synced</th><th></th></tr></thead><tbody><tr v-for="item in providers" :key="providerKey(item)" class="border-t border-neutral-800"><td class="py-2">{{ providerKey(item) }}</td><td>{{ item.state }}</td><td>{{ bytes(item.cacheSizeBytes) }}</td><td>{{ date(item.lastSyncAt) }}</td><td><button v-if="canManage && item.packageStoragePath" class="text-red-300 hover:text-red-200" :disabled="purging === providerKey(item)" @click="removeProvider(item)">Purge</button></td></tr></tbody></table></div></section>
    <section class="rounded border border-neutral-800 bg-neutral-900 p-4"><h2 class="font-medium">Module cache</h2><div class="mt-3 overflow-x-auto"><table class="w-full text-left text-sm"><thead class="text-neutral-500"><tr><th>Coordinate</th><th>State</th><th>Bytes</th><th>Synced</th><th></th></tr></thead><tbody><tr v-for="item in modules" :key="moduleKey(item)" class="border-t border-neutral-800"><td class="py-2">{{ moduleKey(item) }}</td><td>{{ item.state }}</td><td>{{ bytes(item.cacheSizeBytes) }}</td><td>{{ date(item.lastSyncAt) }}</td><td><button v-if="canManage && item.packageStoragePath" class="text-red-300 hover:text-red-200" :disabled="purging === moduleKey(item)" @click="removeModule(item)">Purge</button></td></tr></tbody></table></div></section>
    <section class="rounded border border-neutral-800 bg-neutral-900 p-4"><h2 class="font-medium">Active leases</h2><ul class="mt-3 space-y-2 text-sm"><li v-for="lease in leases" :key="lease.leaseKey" class="rounded bg-neutral-800 p-2"><span class="font-mono text-xs">{{ lease.leaseKey }}</span><span class="ml-3 text-neutral-400">{{ lease.operationType }} · {{ lease.ownerInstanceId }} · expires {{ date(lease.expiresAt) }}</span></li><li v-if="leases.length === 0" class="text-neutral-500">No active leases.</li></ul></section>
  </main>
</template>
