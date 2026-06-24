<script setup lang="ts">
import { extractErrorMessage } from '~/composables/useErrorMessage'
import { useMirrorAdmin, type MirrorConfigResponse, type MirrorEntry, type MirrorRuntimeOptions } from '~/composables/useMirrorAdmin'

definePageMeta({
  middleware: 'auth',
})

const { isSidebarOpen } = useDashboard()
const { hasPermission } = usePermissions()
const { getSummary, getConfig, updateConfig, listEntries, retryEntry, deleteEntry } = useMirrorAdmin()

const canRead = computed(() => hasPermission('mirror.read'))
const canManage = computed(() => hasPermission('mirror.manage'))
const canConfigure = computed(() => hasPermission('mirror.configure'))

const activeTab = ref<'summary' | 'entries' | 'config'>('summary')
const summary = ref<Awaited<ReturnType<typeof getSummary>> | null>(null)
const config = ref<MirrorConfigResponse | null>(null)
const draftConfig = ref<MirrorRuntimeOptions | null>(null)
const entries = ref<MirrorEntry[]>([])
const isLoading = ref(false)
const isEntriesLoading = ref(false)
const isSavingConfig = ref(false)
const actionKey = ref<string | null>(null)
const errorMessage = ref<string | null>(null)
const successMessage = ref<string | null>(null)

const entryKind = ref('')
const entryState = ref('')
const entrySearch = ref('')
const limit = 50
const offset = ref(0)

const providerLists = reactive({
  allowedHostnames: '',
  allowedArtifactHosts: '',
  allowlist: '',
  denylist: '',
  platforms: '',
})
const moduleLists = reactive({
  allowedNamespaces: '',
  allowedArchiveHosts: '',
  allowlist: '',
  denylist: '',
})

const tabs = [
  { key: 'summary', label: 'Summary', icon: 'i-lucide-gauge' },
  { key: 'entries', label: 'Cache', icon: 'i-lucide-database' },
  { key: 'config', label: 'Config', icon: 'i-lucide-settings-2' },
] as const

const stateOptions = [
  { value: '', label: 'All states' },
  { value: 'ready', label: 'Ready' },
  { value: 'pending', label: 'Pending' },
  { value: 'failed', label: 'Failed' },
]

const kindOptions = [
  { value: '', label: 'All kinds' },
  { value: 'provider', label: 'Providers' },
  { value: 'module', label: 'Modules' },
]

const stats = computed(() => {
  const current = summary.value
  return [
    { label: 'Total', value: current?.total.total ?? 0, icon: 'i-lucide-database', tone: 'text-sky-300' },
    { label: 'Ready', value: current?.total.ready ?? 0, icon: 'i-lucide-check-circle-2', tone: 'text-green-300' },
    { label: 'Pending', value: current?.total.pending ?? 0, icon: 'i-lucide-clock-3', tone: 'text-amber-300' },
    { label: 'Failed', value: current?.total.failed ?? 0, icon: 'i-lucide-alert-triangle', tone: 'text-red-300' },
  ]
})

function splitList(value: string): string[] {
  return value
    .split(',')
    .map(item => item.trim())
    .filter(Boolean)
}

function joinList(values: string[] | null | undefined): string {
  return values?.join(', ') ?? ''
}

function syncDraftLists() {
  const current = draftConfig.value
  if (!current) return
  providerLists.allowedHostnames = joinList(current.providers.allowedHostnames)
  providerLists.allowedArtifactHosts = joinList(current.providers.allowedArtifactHosts)
  providerLists.allowlist = joinList(current.providers.allowlist)
  providerLists.denylist = joinList(current.providers.denylist)
  providerLists.platforms = joinList(current.providers.platforms)
  moduleLists.allowedNamespaces = joinList(current.modules.allowedNamespaces)
  moduleLists.allowedArchiveHosts = joinList(current.modules.allowedArchiveHosts)
  moduleLists.allowlist = joinList(current.modules.allowlist)
  moduleLists.denylist = joinList(current.modules.denylist)
}

function applyDraftLists() {
  const current = draftConfig.value
  if (!current) return
  current.providers.allowedHostnames = splitList(providerLists.allowedHostnames)
  current.providers.allowedArtifactHosts = splitList(providerLists.allowedArtifactHosts)
  current.providers.allowlist = splitList(providerLists.allowlist)
  current.providers.denylist = splitList(providerLists.denylist)
  current.providers.platforms = splitList(providerLists.platforms)
  current.modules.allowedNamespaces = splitList(moduleLists.allowedNamespaces)
  current.modules.allowedArchiveHosts = splitList(moduleLists.allowedArchiveHosts)
  current.modules.allowlist = splitList(moduleLists.allowlist)
  current.modules.denylist = splitList(moduleLists.denylist)
}

function cloneConfig(value: MirrorRuntimeOptions): MirrorRuntimeOptions {
  return JSON.parse(JSON.stringify(value))
}

function entryKey(entry: MirrorEntry): string {
  return entry.kind === 'provider'
    ? `provider:${entry.hostname}/${entry.namespace}/${entry.type}/${entry.version}`
    : `module:${entry.hostname}/${entry.namespace}/${entry.name}/${entry.provider}/${entry.version}`
}

function entryTitle(entry: MirrorEntry): string {
  return entry.kind === 'provider'
    ? `${entry.namespace}/${entry.type}`
    : `${entry.namespace}/${entry.name}/${entry.provider}`
}

function entrySubtitle(entry: MirrorEntry): string {
  return entry.kind === 'provider'
    ? `${entry.hostname} / ${entry.os}_${entry.arch}`
    : entry.hostname
}

function stateClass(state: string): string {
  const map: Record<string, string> = {
    ready: 'bg-green-500/15 text-green-300 ring-green-500/25',
    pending: 'bg-amber-500/15 text-amber-300 ring-amber-500/25',
    failed: 'bg-red-500/15 text-red-300 ring-red-500/25',
  }
  return map[state] ?? 'bg-neutral-500/15 text-neutral-300 ring-neutral-500/25'
}

function formatDate(value: string | null): string {
  if (!value) return 'Never'
  return new Date(value).toLocaleString()
}

function formatBytes(value: number | null): string {
  if (!value) return 'Unknown'
  if (value < 1024) return `${value} B`
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KiB`
  return `${(value / 1024 / 1024).toFixed(1)} MiB`
}

async function fetchSummaryAndConfig() {
  const [summaryResult, configResult] = await Promise.all([
    getSummary(),
    canConfigure.value ? getConfig() : Promise.resolve(null),
  ])
  summary.value = summaryResult
  if (configResult) {
    config.value = configResult
    draftConfig.value = cloneConfig(configResult.effective)
    syncDraftLists()
  }
}

async function fetchEntries() {
  isEntriesLoading.value = true
  try {
    const result = await listEntries({
      kind: entryKind.value || undefined,
      state: entryState.value || undefined,
      q: entrySearch.value || undefined,
      limit,
      offset: offset.value,
    })
    entries.value = [
      ...result.providers.map(entry => ({ ...entry, kind: 'provider' as const })),
      ...result.modules.map(entry => ({ ...entry, kind: 'module' as const })),
    ]
  }
  finally {
    isEntriesLoading.value = false
  }
}

async function refresh() {
  if (!canRead.value) return
  isLoading.value = true
  errorMessage.value = null
  try {
    await Promise.all([fetchSummaryAndConfig(), fetchEntries()])
  }
  catch (error) {
    console.error('Failed to load mirror admin state', error)
    errorMessage.value = extractErrorMessage(error, 'Failed to load mirror admin state')
  }
  finally {
    isLoading.value = false
  }
}

async function applyFilters() {
  offset.value = 0
  await fetchEntries()
}

async function saveConfig() {
  if (!draftConfig.value || !canConfigure.value) return
  isSavingConfig.value = true
  errorMessage.value = null
  successMessage.value = null
  try {
    applyDraftLists()
    config.value = await updateConfig(draftConfig.value)
    draftConfig.value = cloneConfig(config.value.effective)
    syncDraftLists()
    successMessage.value = 'Mirror configuration updated'
  }
  catch (error) {
    console.error('Failed to update mirror config', error)
    errorMessage.value = extractErrorMessage(error, 'Failed to update mirror configuration')
  }
  finally {
    isSavingConfig.value = false
  }
}

async function retry(entry: MirrorEntry) {
  if (!canManage.value) return
  actionKey.value = `retry:${entryKey(entry)}`
  errorMessage.value = null
  successMessage.value = null
  try {
    await retryEntry(entry)
    successMessage.value = 'Retry queued'
    await Promise.all([fetchSummaryAndConfig(), fetchEntries()])
  }
  catch (error) {
    console.error('Failed to retry mirror cache entry', error)
    errorMessage.value = extractErrorMessage(error, 'Failed to retry mirror cache entry')
  }
  finally {
    actionKey.value = null
  }
}

async function remove(entry: MirrorEntry) {
  if (!canManage.value) return
  actionKey.value = `delete:${entryKey(entry)}`
  errorMessage.value = null
  successMessage.value = null
  try {
    await deleteEntry(entry)
    successMessage.value = 'Mirror cache entry deleted'
    await Promise.all([fetchSummaryAndConfig(), fetchEntries()])
  }
  catch (error) {
    console.error('Failed to delete mirror cache entry', error)
    errorMessage.value = extractErrorMessage(error, 'Failed to delete mirror cache entry')
  }
  finally {
    actionKey.value = null
  }
}

onMounted(refresh)
</script>

<template>
  <div class="flex flex-col h-full">
    <div class="lg:hidden px-4 pt-4">
      <UButton icon="i-lucide-menu" variant="ghost" color="neutral" @click="isSidebarOpen = true" />
    </div>

    <div class="page-header">
      <div class="flex items-center justify-between gap-4">
        <div>
          <h1 class="page-header-title">Registry Mirroring</h1>
          <p class="page-header-subtitle">Provider and module read-through cache operations</p>
        </div>
        <UButton icon="i-lucide-refresh-cw" color="neutral" variant="ghost" :loading="isLoading" @click="refresh" />
      </div>
      <div class="page-header-actions">
        <div class="inline-flex rounded-lg border border-neutral-800 bg-neutral-900 p-1">
          <button
            v-for="tab in tabs"
            :key="tab.key"
            type="button"
            :class="[
              'flex h-8 items-center gap-2 rounded-md px-3 text-sm transition',
              activeTab === tab.key ? 'bg-neutral-700 text-white' : 'text-neutral-400 hover:text-white'
            ]"
            @click="activeTab = tab.key"
          >
            <UIcon :name="tab.icon" />
            <span>{{ tab.label }}</span>
          </button>
        </div>
      </div>
    </div>
    <div class="page-divider" />

    <div class="flex-1 overflow-y-auto p-6">
      <UAlert v-if="errorMessage" color="error" variant="soft" :title="errorMessage" icon="i-lucide-alert-circle" class="mb-4" />
      <UAlert v-if="successMessage" color="success" variant="soft" :title="successMessage" icon="i-lucide-check-circle-2" class="mb-4" />

      <div v-if="!canRead" class="rounded-lg border border-neutral-800 bg-neutral-900/60 p-6 text-neutral-300">
        Mirror administration requires mirror read permission.
      </div>

      <div v-else-if="activeTab === 'summary'" class="space-y-6">
        <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <div v-for="item in stats" :key="item.label" class="rounded-lg border border-neutral-800 bg-neutral-900/60 p-4">
            <div class="flex items-center justify-between">
              <span class="text-sm text-neutral-400">{{ item.label }}</span>
              <UIcon :name="item.icon" :class="['text-lg', item.tone]" />
            </div>
            <div class="mt-3 text-2xl font-semibold text-white">{{ item.value }}</div>
          </div>
        </div>

        <div class="grid gap-4 lg:grid-cols-2">
          <div class="rounded-lg border border-neutral-800 bg-neutral-900/60 p-4">
            <h2 class="text-sm font-semibold text-neutral-200">Providers</h2>
            <dl class="mt-4 grid grid-cols-2 gap-3 text-sm">
              <div><dt class="text-neutral-500">Total</dt><dd class="text-neutral-100">{{ summary?.providers.total ?? 0 }}</dd></div>
              <div><dt class="text-neutral-500">Failed</dt><dd class="text-red-300">{{ summary?.providers.failed ?? 0 }}</dd></div>
              <div><dt class="text-neutral-500">Pending</dt><dd class="text-amber-300">{{ summary?.providers.pending ?? 0 }}</dd></div>
              <div><dt class="text-neutral-500">Last sync</dt><dd class="text-neutral-100">{{ formatDate(summary?.providers.lastSyncAt ?? null) }}</dd></div>
            </dl>
          </div>
          <div class="rounded-lg border border-neutral-800 bg-neutral-900/60 p-4">
            <h2 class="text-sm font-semibold text-neutral-200">Modules</h2>
            <dl class="mt-4 grid grid-cols-2 gap-3 text-sm">
              <div><dt class="text-neutral-500">Total</dt><dd class="text-neutral-100">{{ summary?.modules.total ?? 0 }}</dd></div>
              <div><dt class="text-neutral-500">Failed</dt><dd class="text-red-300">{{ summary?.modules.failed ?? 0 }}</dd></div>
              <div><dt class="text-neutral-500">Pending</dt><dd class="text-amber-300">{{ summary?.modules.pending ?? 0 }}</dd></div>
              <div><dt class="text-neutral-500">Last sync</dt><dd class="text-neutral-100">{{ formatDate(summary?.modules.lastSyncAt ?? null) }}</dd></div>
            </dl>
          </div>
        </div>
      </div>

      <div v-else-if="activeTab === 'entries'" class="space-y-4">
        <div class="flex flex-wrap items-center gap-3">
          <UInput v-model="entrySearch" icon="i-lucide-search" placeholder="Filter hostname, namespace, name, version" class="w-80" />
          <select v-model="entryKind" class="h-9 rounded-md border border-neutral-700 bg-neutral-900 px-3 text-sm text-neutral-100">
            <option v-for="option in kindOptions" :key="option.value" :value="option.value">{{ option.label }}</option>
          </select>
          <select v-model="entryState" class="h-9 rounded-md border border-neutral-700 bg-neutral-900 px-3 text-sm text-neutral-100">
            <option v-for="option in stateOptions" :key="option.value" :value="option.value">{{ option.label }}</option>
          </select>
          <UButton icon="i-lucide-filter" color="neutral" variant="soft" :loading="isEntriesLoading" @click="applyFilters" />
        </div>

        <div class="overflow-hidden rounded-lg border border-neutral-800">
          <table class="min-w-full divide-y divide-neutral-800 text-sm">
            <thead class="bg-neutral-900">
              <tr class="text-left text-neutral-400">
                <th class="px-4 py-3 font-medium">Entry</th>
                <th class="px-4 py-3 font-medium">Version</th>
                <th class="px-4 py-3 font-medium">State</th>
                <th class="px-4 py-3 font-medium">Size</th>
                <th class="px-4 py-3 font-medium">Last sync</th>
                <th class="px-4 py-3 font-medium text-right">Actions</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-neutral-800 bg-neutral-950/40">
              <tr v-for="entry in entries" :key="entryKey(entry)" class="text-neutral-200">
                <td class="px-4 py-3">
                  <div class="font-medium">{{ entryTitle(entry) }}</div>
                  <div class="mt-1 text-xs text-neutral-500">{{ entrySubtitle(entry) }}</div>
                  <div v-if="entry.lastError" class="mt-1 max-w-md truncate text-xs text-red-300">{{ entry.lastError }}</div>
                </td>
                <td class="px-4 py-3">{{ entry.version }}</td>
                <td class="px-4 py-3">
                  <span :class="['inline-flex rounded-full px-2 py-1 text-xs ring-1', stateClass(entry.state)]">{{ entry.state }}</span>
                </td>
                <td class="px-4 py-3 text-neutral-400">{{ formatBytes(entry.sizeBytes) }}</td>
                <td class="px-4 py-3 text-neutral-400">{{ formatDate(entry.lastSyncAt) }}</td>
                <td class="px-4 py-3">
                  <div class="flex justify-end gap-2">
                    <UButton
                      icon="i-lucide-rotate-cw"
                      size="xs"
                      color="neutral"
                      variant="ghost"
                      :disabled="!canManage"
                      :loading="actionKey === `retry:${entryKey(entry)}`"
                      @click="retry(entry)"
                    />
                    <UButton
                      icon="i-lucide-trash-2"
                      size="xs"
                      color="error"
                      variant="ghost"
                      :disabled="!canManage"
                      :loading="actionKey === `delete:${entryKey(entry)}`"
                      @click="remove(entry)"
                    />
                  </div>
                </td>
              </tr>
              <tr v-if="!entries.length">
                <td colspan="6" class="px-4 py-12 text-center text-neutral-500">No mirror cache entries match the current filters.</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <div v-else-if="activeTab === 'config'" class="space-y-4">
        <div v-if="!canConfigure" class="rounded-lg border border-neutral-800 bg-neutral-900/60 p-4 text-neutral-300">
          Mirror configuration requires mirror configure permission.
        </div>

        <div v-else-if="draftConfig" class="grid gap-4 xl:grid-cols-2">
          <section class="rounded-lg border border-neutral-800 bg-neutral-900/60 p-4 space-y-4">
            <div class="flex items-center justify-between">
              <h2 class="text-sm font-semibold text-neutral-100">General</h2>
              <label class="flex items-center gap-2 text-sm text-neutral-300">
                <input v-model="draftConfig.enabled" type="checkbox" class="h-4 w-4 rounded border-neutral-700 bg-neutral-950">
                Enabled
              </label>
            </div>
            <UInput v-model="draftConfig.upstreamRegistryBaseUrl" disabled label="Upstream" />
            <div class="grid gap-3 sm:grid-cols-2">
              <label class="text-xs text-neutral-400">Max concurrent downloads
                <input v-model.number="draftConfig.limits.maxConcurrentDownloads" type="number" min="1" class="mt-1 h-9 w-full rounded-md border border-neutral-700 bg-neutral-950 px-3 text-sm text-neutral-100">
              </label>
              <label class="text-xs text-neutral-400">Per-coordinate downloads
                <input v-model.number="draftConfig.limits.maxConcurrentDownloadsPerCoordinate" type="number" min="1" class="mt-1 h-9 w-full rounded-md border border-neutral-700 bg-neutral-950 px-3 text-sm text-neutral-100">
              </label>
              <label class="text-xs text-neutral-400">Max cache bytes
                <input v-model.number="draftConfig.limits.maxTotalCachedBytes" type="number" min="0" class="mt-1 h-9 w-full rounded-md border border-neutral-700 bg-neutral-950 px-3 text-sm text-neutral-100">
              </label>
              <label class="text-xs text-neutral-400">Negative cache TTL seconds
                <input v-model.number="draftConfig.limits.negativeCacheTtlSeconds" type="number" min="0" class="mt-1 h-9 w-full rounded-md border border-neutral-700 bg-neutral-950 px-3 text-sm text-neutral-100">
              </label>
            </div>
          </section>

          <section class="rounded-lg border border-neutral-800 bg-neutral-900/60 p-4 space-y-4">
            <div class="flex items-center justify-between">
              <h2 class="text-sm font-semibold text-neutral-100">Providers</h2>
              <label class="flex items-center gap-2 text-sm text-neutral-300">
                <input v-model="draftConfig.providers.enabled" type="checkbox" class="h-4 w-4 rounded border-neutral-700 bg-neutral-950">
                Enabled
              </label>
            </div>
            <label class="flex items-center gap-2 text-sm text-neutral-300">
              <input v-model="draftConfig.providers.requireAuthentication" type="checkbox" class="h-4 w-4 rounded border-neutral-700 bg-neutral-950">
              Require authentication
            </label>
            <div class="grid gap-3 sm:grid-cols-2">
              <label class="text-xs text-neutral-400">Allowed hostnames
                <input v-model="providerLists.allowedHostnames" class="mt-1 h-9 w-full rounded-md border border-neutral-700 bg-neutral-950 px-3 text-sm text-neutral-100">
              </label>
              <label class="text-xs text-neutral-400">Allowed artifact hosts
                <input v-model="providerLists.allowedArtifactHosts" class="mt-1 h-9 w-full rounded-md border border-neutral-700 bg-neutral-950 px-3 text-sm text-neutral-100">
              </label>
              <label class="text-xs text-neutral-400">Allow patterns
                <input v-model="providerLists.allowlist" class="mt-1 h-9 w-full rounded-md border border-neutral-700 bg-neutral-950 px-3 text-sm text-neutral-100">
              </label>
              <label class="text-xs text-neutral-400">Deny patterns
                <input v-model="providerLists.denylist" class="mt-1 h-9 w-full rounded-md border border-neutral-700 bg-neutral-950 px-3 text-sm text-neutral-100">
              </label>
              <label class="text-xs text-neutral-400">Platforms
                <input v-model="providerLists.platforms" class="mt-1 h-9 w-full rounded-md border border-neutral-700 bg-neutral-950 px-3 text-sm text-neutral-100">
              </label>
              <label class="text-xs text-neutral-400">Max package bytes
                <input v-model.number="draftConfig.providers.maxPackageBytes" type="number" min="0" class="mt-1 h-9 w-full rounded-md border border-neutral-700 bg-neutral-950 px-3 text-sm text-neutral-100">
              </label>
              <label class="text-xs text-neutral-400">Max redirects
                <input v-model.number="draftConfig.providers.maxRedirects" type="number" min="0" class="mt-1 h-9 w-full rounded-md border border-neutral-700 bg-neutral-950 px-3 text-sm text-neutral-100">
              </label>
              <label class="text-xs text-neutral-400">Metadata TTL minutes
                <input v-model.number="draftConfig.providers.metadataTtlMinutes" type="number" min="1" class="mt-1 h-9 w-full rounded-md border border-neutral-700 bg-neutral-950 px-3 text-sm text-neutral-100">
              </label>
            </div>
          </section>

          <section class="rounded-lg border border-neutral-800 bg-neutral-900/60 p-4 space-y-4 xl:col-span-2">
            <div class="flex items-center justify-between">
              <h2 class="text-sm font-semibold text-neutral-100">Modules</h2>
              <label class="flex items-center gap-2 text-sm text-neutral-300">
                <input v-model="draftConfig.modules.enabled" type="checkbox" class="h-4 w-4 rounded border-neutral-700 bg-neutral-950">
                Enabled
              </label>
            </div>
            <label class="flex items-center gap-2 text-sm text-neutral-300">
              <input v-model="draftConfig.modules.requireAuthentication" type="checkbox" class="h-4 w-4 rounded border-neutral-700 bg-neutral-950">
              Require authentication
            </label>
            <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
              <label class="text-xs text-neutral-400">Allowed namespaces
                <input v-model="moduleLists.allowedNamespaces" class="mt-1 h-9 w-full rounded-md border border-neutral-700 bg-neutral-950 px-3 text-sm text-neutral-100">
              </label>
              <label class="text-xs text-neutral-400">Allowed archive hosts
                <input v-model="moduleLists.allowedArchiveHosts" class="mt-1 h-9 w-full rounded-md border border-neutral-700 bg-neutral-950 px-3 text-sm text-neutral-100">
              </label>
              <label class="text-xs text-neutral-400">Allow patterns
                <input v-model="moduleLists.allowlist" class="mt-1 h-9 w-full rounded-md border border-neutral-700 bg-neutral-950 px-3 text-sm text-neutral-100">
              </label>
              <label class="text-xs text-neutral-400">Deny patterns
                <input v-model="moduleLists.denylist" class="mt-1 h-9 w-full rounded-md border border-neutral-700 bg-neutral-950 px-3 text-sm text-neutral-100">
              </label>
              <label class="text-xs text-neutral-400">Max package bytes
                <input v-model.number="draftConfig.modules.maxPackageBytes" type="number" min="0" class="mt-1 h-9 w-full rounded-md border border-neutral-700 bg-neutral-950 px-3 text-sm text-neutral-100">
              </label>
              <label class="text-xs text-neutral-400">Max redirects
                <input v-model.number="draftConfig.modules.maxRedirects" type="number" min="0" class="mt-1 h-9 w-full rounded-md border border-neutral-700 bg-neutral-950 px-3 text-sm text-neutral-100">
              </label>
              <label class="text-xs text-neutral-400">Metadata TTL minutes
                <input v-model.number="draftConfig.modules.metadataTtlMinutes" type="number" min="1" class="mt-1 h-9 w-full rounded-md border border-neutral-700 bg-neutral-950 px-3 text-sm text-neutral-100">
              </label>
              <label class="text-xs text-neutral-400">Download timeout seconds
                <input v-model.number="draftConfig.modules.downloadTimeoutSeconds" type="number" min="1" class="mt-1 h-9 w-full rounded-md border border-neutral-700 bg-neutral-950 px-3 text-sm text-neutral-100">
              </label>
            </div>
          </section>

          <div class="xl:col-span-2 flex justify-end">
            <UButton icon="i-lucide-save" color="primary" :loading="isSavingConfig" @click="saveConfig">
              Save
            </UButton>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
