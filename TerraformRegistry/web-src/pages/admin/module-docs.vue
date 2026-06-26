<script setup lang="ts">
import { useDashboard } from '~/composables/useDashboard'
import { extractErrorMessage } from '~/composables/useErrorMessage'
import { useModuleDocsAdmin } from '~/composables/useModuleDocsAdmin'
import type {
  ModuleExtractionAdminDetail,
  ModuleExtractionAdminListItem,
  ModuleExtractionDocument,
  ModuleExtractionAdminSummary,
  ModuleExtractionRuntimeConfig,
  ModuleInputDefinition,
  ModuleLlmContextDocument,
  ModuleOutputDefinition,
  ModuleResourceDefinition,
} from '~/composables/useModuleDocsAdmin'

definePageMeta({
  middleware: 'auth',
})

const { isSidebarOpen } = useDashboard()
const { hasPermission } = usePermissions()
const {
  getSummary,
  listModules,
  getModuleDetail,
  requeueModule,
  regenerateLlmContext,
  queueBackfill,
  updateConfig,
} = useModuleDocsAdmin()

const canRead = computed(() => hasPermission('module_docs.read'))
const canManage = computed(() => hasPermission('module_docs.manage'))
const canConfigure = computed(() => hasPermission('module_docs.configure'))

const summary = ref<ModuleExtractionAdminSummary | null>(null)
const config = ref<ModuleExtractionRuntimeConfig | null>(null)
const modules = ref<ModuleExtractionAdminListItem[]>([])
const total = ref(0)
const selectedDetail = ref<ModuleExtractionAdminDetail | null>(null)

const isLoading = ref(false)
const isDetailLoading = ref(false)
const isConfigUpdating = ref(false)
const isBackfilling = ref(false)
const requeueKey = ref<string | null>(null)
const regenerateLlmKey = ref<string | null>(null)
const errorMessage = ref<string | null>(null)
const successMessage = ref<string | null>(null)
const showRawJson = ref(false)
const showLlmJson = ref(false)

const statusFilter = ref('')
const searchText = ref('')
const limit = 25
const offset = ref(0)
const backfillLimit = ref(25)

const statusOptions = [
  { value: '', label: 'All statuses' },
  { value: 'succeeded', label: 'Succeeded' },
  { value: 'failed', label: 'Failed' },
  { value: 'pending', label: 'Pending' },
  { value: 'processing', label: 'Processing' },
  { value: 'never_extracted', label: 'Never extracted' },
]

const currentPage = computed(() => Math.floor(offset.value / limit) + 1)
const totalPages = computed(() => Math.max(1, Math.ceil(total.value / limit)))
const hasPrevious = computed(() => offset.value > 0)
const hasNext = computed(() => offset.value + limit < total.value)
const selectedKey = computed(() => selectedDetail.value ? moduleKey(selectedDetail.value) : null)
const extractionEnabled = computed(() => config.value?.enabled === true)
const activeFilterCount = computed(() => Number(Boolean(statusFilter.value)) + Number(Boolean(searchText.value)))

const stats = computed(() => {
  const current = summary.value
  return [
    { label: 'Succeeded', value: current?.succeeded ?? 0, icon: 'i-lucide-check-circle-2', color: 'text-green-400', bg: 'bg-green-500/10', ring: 'ring-green-500/20' },
    { label: 'Failed', value: current?.failed ?? 0, icon: 'i-lucide-alert-triangle', color: 'text-red-400', bg: 'bg-red-500/10', ring: 'ring-red-500/20' },
    { label: 'Pending', value: current?.pending ?? 0, icon: 'i-lucide-clock-3', color: 'text-amber-400', bg: 'bg-amber-500/10', ring: 'ring-amber-500/20' },
    { label: 'Never', value: current?.neverExtracted ?? 0, icon: 'i-lucide-circle-dashed', color: 'text-neutral-400', bg: 'bg-neutral-500/10', ring: 'ring-neutral-500/20' },
  ]
})

const llmStats = computed(() => {
  const current = summary.value
  return [
    { label: 'LLM Ready', value: current?.llmSucceeded ?? 0, icon: 'i-lucide-bot', color: 'text-cyan-400', bg: 'bg-cyan-500/10', ring: 'ring-cyan-500/20' },
    { label: 'LLM Failed', value: current?.llmFailed ?? 0, icon: 'i-lucide-badge-alert', color: 'text-rose-400', bg: 'bg-rose-500/10', ring: 'ring-rose-500/20' },
    { label: 'LLM Pending', value: current?.llmPending ?? 0, icon: 'i-lucide-hourglass', color: 'text-amber-400', bg: 'bg-amber-500/10', ring: 'ring-amber-500/20' },
    { label: 'LLM Missing', value: current?.llmNeverGenerated ?? 0, icon: 'i-lucide-file-x-2', color: 'text-neutral-400', bg: 'bg-neutral-500/10', ring: 'ring-neutral-500/20' },
  ]
})

function moduleKey(module: Pick<ModuleExtractionAdminListItem, 'namespace' | 'name' | 'provider' | 'version'>): string {
  return `${module.namespace}/${module.name}/${module.provider}/${module.version}`
}

function statusLabel(status: string): string {
  const option = statusOptions.find(item => item.value === status)
  return option?.label ?? status.replaceAll('_', ' ')
}

function statusClass(status: string): string {
  const map: Record<string, string> = {
    succeeded: 'bg-green-500/15 text-green-300 ring-green-500/25',
    failed: 'bg-red-500/15 text-red-300 ring-red-500/25',
    pending: 'bg-amber-500/15 text-amber-300 ring-amber-500/25',
    processing: 'bg-blue-500/15 text-blue-300 ring-blue-500/25',
    never_extracted: 'bg-neutral-500/15 text-neutral-300 ring-neutral-500/25',
  }
  return map[status] ?? 'bg-neutral-500/15 text-neutral-300 ring-neutral-500/25'
}

function formatDate(value: string | null): string {
  if (!value) return 'Not recorded'
  return new Date(value).toLocaleString()
}

function formatShortDate(value: string | null): string {
  if (!value) return 'Never'
  return new Date(value).toLocaleDateString()
}

function clampBackfillLimit() {
  backfillLimit.value = Math.min(100, Math.max(1, Number(backfillLimit.value) || 25))
}

function compactResource(resource: ModuleResourceDefinition): string {
  return `${resource.type}.${resource.name}`
}

function inputMeta(input: ModuleInputDefinition): string {
  const parts = []
  parts.push(input.required ? 'required' : 'optional')
  if (input.type) parts.push(input.type)
  if (input.sensitive) parts.push('sensitive')
  return parts.join(' / ')
}

function outputMeta(output: ModuleOutputDefinition): string {
  return output.sensitive ? 'sensitive' : 'output'
}

function formatDocumentJson(document: ModuleExtractionDocument): string {
  return JSON.stringify(document, null, 2)
}

function formatLlmJson(document: ModuleLlmContextDocument): string {
  return JSON.stringify(document, null, 2)
}

function splitLlmSummary(value: string | null): string[] {
  if (!value) return []

  return value
    .split(/\s+-\s+(?=[A-Za-z0-9_"'([])/)
    .map(part => part.trim())
    .filter(Boolean)
}

function llmSummaryLead(value: string | null): string {
  return splitLlmSummary(value)[0] || 'No curated summary'
}

function llmSummaryBullets(value: string | null): string[] {
  return splitLlmSummary(value).slice(1)
}

const fetchSummary = async () => {
  const result = await getSummary()
  summary.value = result.summary
  config.value = result.config
}

const fetchModules = async () => {
  const result = await listModules({
    status: statusFilter.value || undefined,
    q: searchText.value || undefined,
    limit,
    offset: offset.value,
  })
  modules.value = result.items
  total.value = result.total
}

const fetchDashboard = async () => {
  if (!canRead.value) return

  isLoading.value = true
  errorMessage.value = null
  try {
    await Promise.all([fetchSummary(), fetchModules()])
    if (!selectedDetail.value && modules.value.length > 0) {
      await selectModule(modules.value[0])
    }
  }
  catch (error) {
    console.error('Failed to load module docs admin state', error)
    errorMessage.value = extractErrorMessage(error, 'Failed to load module docs')
  }
  finally {
    isLoading.value = false
  }
}

const selectModule = async (module: ModuleExtractionAdminListItem) => {
  isDetailLoading.value = true
  errorMessage.value = null
  showRawJson.value = false
  showLlmJson.value = false
  try {
    selectedDetail.value = await getModuleDetail(module)
  }
  catch (error) {
    console.error('Failed to load module documentation detail', error)
    errorMessage.value = extractErrorMessage(error, 'Failed to load module documentation')
  }
  finally {
    isDetailLoading.value = false
  }
}

const refresh = async () => {
  await fetchDashboard()
  if (selectedDetail.value) {
    const selected = selectedDetail.value
    await selectModule(selected)
  }
}

const applyFilters = async () => {
  offset.value = 0
  selectedDetail.value = null
  await fetchDashboard()
}

const clearFilters = async () => {
  statusFilter.value = ''
  searchText.value = ''
  offset.value = 0
  selectedDetail.value = null
  await fetchDashboard()
}

const previousPage = async () => {
  if (!hasPrevious.value) return
  offset.value = Math.max(0, offset.value - limit)
  selectedDetail.value = null
  await fetchDashboard()
}

const nextPage = async () => {
  if (!hasNext.value) return
  offset.value += limit
  selectedDetail.value = null
  await fetchDashboard()
}

const setExtractionEnabled = async (enabled: boolean) => {
  if (!canConfigure.value || isConfigUpdating.value) return

  isConfigUpdating.value = true
  errorMessage.value = null
  successMessage.value = null
  try {
    config.value = await updateConfig(enabled)
    successMessage.value = enabled ? 'Module extraction enabled' : 'Module extraction disabled'
  }
  catch (error) {
    console.error('Failed to update module extraction config', error)
    errorMessage.value = extractErrorMessage(error, 'Failed to update module extraction setting')
  }
  finally {
    isConfigUpdating.value = false
  }
}

const handleBackfill = async () => {
  if (!canManage.value || !extractionEnabled.value) return

  clampBackfillLimit()
  isBackfilling.value = true
  errorMessage.value = null
  successMessage.value = null
  try {
    const result = await queueBackfill(backfillLimit.value)
    successMessage.value = `${result.queued} modules queued`
    await fetchDashboard()
  }
  catch (error) {
    console.error('Failed to queue module docs backfill', error)
    errorMessage.value = extractErrorMessage(error, 'Failed to queue backfill')
  }
  finally {
    isBackfilling.value = false
  }
}

const handleRequeue = async (module: ModuleExtractionAdminListItem) => {
  if (!canManage.value || !extractionEnabled.value) return

  requeueKey.value = moduleKey(module)
  errorMessage.value = null
  successMessage.value = null
  try {
    const result = await requeueModule(module)
    successMessage.value = result.queued ? 'Module queued' : 'Module was not queued'
    await fetchDashboard()
    await selectModule(module)
  }
  catch (error) {
    console.error('Failed to requeue module docs extraction', error)
    errorMessage.value = extractErrorMessage(error, 'Failed to requeue module')
  }
  finally {
    requeueKey.value = null
  }
}

const handleRegenerateLlm = async (module: ModuleExtractionAdminListItem) => {
  if (!canManage.value) return

  regenerateLlmKey.value = moduleKey(module)
  errorMessage.value = null
  successMessage.value = null
  try {
    const result = await regenerateLlmContext(module)
    successMessage.value = result.regenerated
      ? 'LLM context regenerated'
      : (result.queued ? 'Module queued for extraction before LLM generation' : 'LLM context was not regenerated')
    await fetchDashboard()
    await selectModule(module)
  }
  catch (error) {
    console.error('Failed to regenerate module LLM context', error)
    errorMessage.value = extractErrorMessage(error, 'Failed to regenerate LLM context')
  }
  finally {
    regenerateLlmKey.value = null
  }
}

onMounted(() => {
  fetchDashboard()
})
</script>

<template>
  <div class="flex flex-col h-full">
    <!-- Mobile menu button -->
    <div class="lg:hidden px-4 pt-4">
      <UButton
        icon="i-lucide-menu"
        variant="ghost"
        color="neutral"
        @click="isSidebarOpen = true"
      />
    </div>

    <!-- Page Header -->
    <div class="page-header">
      <div class="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
        <div>
          <h1 class="page-header-title">
            Documentation
          </h1>
          <p class="page-header-subtitle">
            Generated documentation operations, LLM context state, and documentation quality controls
          </p>
        </div>
        <div class="flex flex-wrap items-center gap-2">
          <span
            v-if="config"
            class="inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium ring-1"
            :class="extractionEnabled ? 'bg-green-500/10 text-green-300 ring-green-500/25' : 'bg-red-500/10 text-red-300 ring-red-500/25'"
          >
            <UIcon :name="extractionEnabled ? 'i-lucide-power' : 'i-lucide-power-off'" class="text-sm" />
            {{ extractionEnabled ? 'Enabled' : 'Disabled' }}
          </span>
          <UButton
            icon="i-lucide-refresh-cw"
            label="Refresh"
            color="neutral"
            variant="outline"
            size="sm"
            :loading="isLoading"
            :disabled="!canRead"
            @click="refresh"
          />
        </div>
      </div>
    </div>
    <div class="page-divider" />

    <!-- Body -->
    <div class="flex-1 overflow-y-auto px-6 py-8">
      <div v-if="!canRead" class="max-w-3xl rounded-lg border border-neutral-800 bg-neutral-900/40 p-8">
        <div class="flex items-start gap-4">
          <div class="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-red-500/10 text-red-400 ring-1 ring-red-500/25">
            <UIcon name="i-lucide-lock" class="text-xl" />
          </div>
          <div>
            <h2 class="text-base font-semibold text-neutral-100">
              Access denied
            </h2>
            <p class="mt-1 text-sm text-neutral-400">
              The module_docs.read permission is required.
            </p>
          </div>
        </div>
      </div>

      <div v-else class="max-w-7xl space-y-6">
        <!-- Error Message -->
        <div
          v-if="errorMessage"
          class="p-4 bg-red-900/20 border border-red-800/50 rounded-xl flex items-center gap-3 backdrop-blur-sm"
        >
          <UIcon name="i-lucide-alert-circle" class="text-red-500 text-xl shrink-0" />
          <p class="text-sm text-red-300">
            {{ errorMessage }}
          </p>
          <UButton
            icon="i-lucide-x"
            color="neutral"
            variant="ghost"
            size="sm"
            class="ml-auto"
            @click="errorMessage = null"
          />
        </div>

        <!-- Success Message -->
        <div
          v-if="successMessage"
          class="p-4 bg-green-900/20 border border-green-800/50 rounded-xl flex items-center gap-3 backdrop-blur-sm"
        >
          <UIcon name="i-lucide-check-circle-2" class="text-green-500 text-xl shrink-0" />
          <p class="text-sm text-green-300">
            {{ successMessage }}
          </p>
          <UButton
            icon="i-lucide-x"
            color="neutral"
            variant="ghost"
            size="sm"
            class="ml-auto"
            @click="successMessage = null"
          />
        </div>

        <div class="rounded-xl border border-neutral-800/60 bg-neutral-950/25 p-5">
          <div class="flex flex-col gap-1">
            <h2 class="text-lg font-semibold text-neutral-100">
              Modules
            </h2>
            <p class="text-sm text-neutral-500">
              Extracted documentation, queue health, and LLM-generated docs for published modules.
            </p>
          </div>
        </div>

        <!-- Stat Cards -->
        <div class="grid gap-4 grid-cols-2 lg:grid-cols-4">
          <div
            v-for="stat in stats"
            :key="stat.label"
            class="docs-card rounded-xl border border-neutral-800/60 p-4"
          >
            <div class="flex items-center justify-between gap-3">
              <div>
                <p class="text-xs font-medium uppercase text-neutral-500">
                  {{ stat.label }}
                </p>
                <p class="mt-1 text-2xl font-semibold tabular-nums text-neutral-100">
                  {{ stat.value.toLocaleString() }}
                </p>
              </div>
              <div
                class="flex h-10 w-10 items-center justify-center rounded-lg ring-1"
                :class="[stat.bg, stat.ring, stat.color]"
              >
                <UIcon :name="stat.icon" class="text-xl" />
              </div>
            </div>
          </div>
        </div>

        <div class="grid gap-4 grid-cols-2 lg:grid-cols-4">
          <div
            v-for="stat in llmStats"
            :key="stat.label"
            class="docs-card rounded-xl border border-neutral-800/60 p-4"
          >
            <div class="flex items-center justify-between gap-3">
              <div>
                <p class="text-xs font-medium uppercase text-neutral-500">
                  {{ stat.label }}
                </p>
                <p class="mt-1 text-2xl font-semibold tabular-nums text-neutral-100">
                  {{ stat.value.toLocaleString() }}
                </p>
              </div>
              <div
                class="flex h-10 w-10 items-center justify-center rounded-lg ring-1"
                :class="[stat.bg, stat.ring, stat.color]"
              >
                <UIcon :name="stat.icon" class="text-xl" />
              </div>
            </div>
          </div>
        </div>

        <!-- Runtime + Queue cards -->
        <div class="grid gap-4 md:grid-cols-2">
          <div class="docs-card rounded-xl border border-neutral-800/60 p-5">
            <div class="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
              <div>
                  <h3 class="text-sm font-semibold text-neutral-100">
                    Runtime
                  </h3>
                <div class="mt-2 flex flex-wrap gap-x-5 gap-y-2 text-xs text-neutral-500">
                  <span>Startup: <strong class="font-medium text-neutral-300">{{ config?.startupEnabled ? 'enabled' : 'disabled' }}</strong></span>
                  <span>Override: <strong class="font-medium text-neutral-300">{{ config?.hasRuntimeOverride ? 'yes' : 'no' }}</strong></span>
                  <span>Updated: <strong class="font-medium text-neutral-300">{{ formatShortDate(config?.updatedAt ?? null) }}</strong></span>
                </div>
              </div>
              <UButton
                v-if="canConfigure"
                :icon="extractionEnabled ? 'i-lucide-power-off' : 'i-lucide-power'"
                :label="extractionEnabled ? 'Disable' : 'Enable'"
                :color="extractionEnabled ? 'error' : 'success'"
                variant="soft"
                size="sm"
                :loading="isConfigUpdating"
                @click="setExtractionEnabled(!extractionEnabled)"
              />
            </div>
          </div>

          <div class="docs-card rounded-xl border border-neutral-800/60 p-5">
            <div class="flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
              <div>
                  <h3 class="text-sm font-semibold text-neutral-100">
                    Queue
                  </h3>
                <p class="mt-2 text-xs text-neutral-500">
                  {{ summary?.total?.toLocaleString() ?? 0 }} module versions tracked
                </p>
              </div>
              <div class="flex items-end gap-2">
                <label class="flex flex-col gap-1.5">
                  <span class="text-xs font-medium uppercase text-neutral-500">Limit</span>
                  <input
                    v-model.number="backfillLimit"
                    type="number"
                    min="1"
                    max="100"
                    class="docs-number-input w-24"
                    :disabled="!canManage || !extractionEnabled"
                    @blur="clampBackfillLimit"
                  >
                </label>
                <UButton
                  icon="i-lucide-list-plus"
                  label="Backfill"
                  color="primary"
                  variant="soft"
                  size="sm"
                  :loading="isBackfilling"
                  :disabled="!canManage || !extractionEnabled"
                  @click="handleBackfill"
                />
              </div>
            </div>
          </div>
        </div>

        <!-- Modules list + detail panel -->
        <div class="grid gap-6 xl:grid-cols-[minmax(320px,0.65fr)_minmax(0,1.35fr)]">
          <!-- Modules list card -->
          <div class="docs-card rounded-xl border border-neutral-800/60 overflow-hidden">
            <div class="border-b border-neutral-800/60 px-4 py-3">
              <div class="flex flex-col gap-3">
                <div>
                  <div class="flex items-baseline justify-between gap-3">
                    <h2 class="text-sm font-semibold text-neutral-100">
                      Modules
                    </h2>
                    <p class="shrink-0 text-xs text-neutral-500">
                      {{ total.toLocaleString() }} versions
                    </p>
                  </div>
                </div>
                <div class="grid gap-2 sm:grid-cols-[minmax(9rem,0.9fr)_minmax(12rem,1.4fr)_auto] sm:items-end">
                  <label class="flex flex-col gap-1">
                    <span class="text-xs font-medium uppercase tracking-wider text-neutral-400">Status</span>
                    <select v-model="statusFilter" class="docs-select w-full">
                      <option
                        v-for="option in statusOptions"
                        :key="option.value"
                        :value="option.value"
                      >
                        {{ option.label }}
                      </option>
                    </select>
                  </label>
                  <label class="flex flex-col gap-1">
                    <span class="text-xs font-medium uppercase tracking-wider text-neutral-400">Search</span>
                    <div class="relative">
                      <UIcon
                        name="i-lucide-search"
                        class="absolute left-2.5 top-1/2 -translate-y-1/2 text-sm text-neutral-500 pointer-events-none"
                      />
                      <input
                        v-model="searchText"
                        class="docs-input w-full pl-8"
                        placeholder="namespace, name, provider"
                        @keyup.enter="applyFilters"
                      >
                    </div>
                  </label>
                  <div class="flex gap-2 sm:pb-px">
                    <UButton
                      icon="i-lucide-search"
                      color="primary"
                      size="sm"
                      title="Apply filters"
                      @click="applyFilters"
                    />
                    <UButton
                      v-if="activeFilterCount > 0"
                      icon="i-lucide-x"
                      color="neutral"
                      variant="ghost"
                      size="sm"
                      @click="clearFilters"
                    />
                  </div>
                </div>
              </div>
            </div>

            <!-- Loading -->
            <div v-if="isLoading" class="py-12 text-center">
              <UIcon
                name="i-lucide-loader-2"
                class="animate-spin text-3xl text-primary-400"
              />
            </div>

            <!-- Empty -->
            <div
              v-else-if="modules.length === 0"
              class="flex flex-col items-center justify-center py-16 px-6 text-center"
            >
              <div class="w-16 h-16 rounded-full bg-neutral-800/60 flex items-center justify-center mb-5">
                <UIcon name="i-lucide-file-search" class="text-3xl text-neutral-600" />
              </div>
              <h3 class="text-base font-medium text-neutral-300 mb-1.5">
                No modules found
              </h3>
              <p class="text-sm text-neutral-500 max-w-sm">
                {{ activeFilterCount > 0 ? 'No modules match your current filters. Try adjusting or clearing them.' : 'Modules will appear here once they are tracked by the extraction queue.' }}
              </p>
              <UButton
                v-if="activeFilterCount > 0"
                label="Clear filters"
                variant="outline"
                color="neutral"
                size="sm"
                class="mt-4"
                @click="clearFilters"
              />
            </div>

            <!-- List -->
            <div v-else class="divide-y divide-neutral-800/60">
              <div
                v-for="module in modules"
                :key="moduleKey(module)"
                class="group cursor-pointer px-4 py-3 transition-colors hover:bg-neutral-800/30"
                :class="selectedKey === moduleKey(module) ? 'bg-neutral-800/50 ring-1 ring-inset ring-primary-500/20' : ''"
                @click="selectModule(module)"
              >
                <div class="flex items-start justify-between gap-3">
                  <div class="min-w-0">
                    <div class="flex min-w-0 flex-wrap items-center gap-1.5">
                      <span class="truncate font-mono text-sm font-semibold text-neutral-100">
                        {{ module.namespace }}/{{ module.name }}/{{ module.provider }}
                      </span>
                      <span class="shrink-0 rounded bg-neutral-800 px-1.5 py-0.5 font-mono text-[11px] text-neutral-400">
                        {{ module.version }}
                      </span>
                    </div>
                    <p class="mt-1 line-clamp-1 text-sm text-neutral-500">
                      {{ module.description || 'No description' }}
                    </p>
                    <div class="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-neutral-500">
                      <span>Attempt: {{ formatShortDate(module.lastAttemptedAt) }}</span>
                      <span>Success: {{ formatShortDate(module.lastSucceededAt) }}</span>
                      <span v-if="module.documentation">
                        {{ module.documentation.inputCount }} inputs / {{ module.documentation.outputCount }} outputs / {{ module.documentation.exampleCount }} examples
                      </span>
                    </div>
                  </div>
                  <div class="flex shrink-0 items-center gap-1.5">
                    <span
                      class="hidden items-center rounded-full px-2 py-0.5 text-xs font-medium capitalize ring-1 sm:inline-flex"
                      :class="statusClass(module.status)"
                    >
                      {{ statusLabel(module.status) }}
                    </span>
                    <span
                      class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium capitalize ring-1"
                      :class="statusClass(module.llmStatus)"
                    >
                      LLM<span class="hidden md:inline">&nbsp;{{ statusLabel(module.llmStatus) }}</span>
                    </span>
                    <UButton
                      v-if="canManage"
                      icon="i-lucide-rotate-cw"
                      color="neutral"
                      variant="ghost"
                      size="xs"
                      :loading="requeueKey === moduleKey(module)"
                      :disabled="!extractionEnabled"
                      title="Requeue"
                      @click.stop="handleRequeue(module)"
                    />
                  </div>
                </div>
                <p v-if="module.error" class="mt-3 rounded border border-red-900/50 bg-red-950/30 px-3 py-2 text-xs text-red-300">
                  {{ module.error }}
                </p>
                <p v-if="module.llmError" class="mt-3 rounded border border-cyan-900/50 bg-cyan-950/20 px-3 py-2 text-xs text-cyan-200">
                  LLM: {{ module.llmError }}
                </p>
              </div>
            </div>

            <!-- Pagination -->
            <div class="flex items-center justify-between border-t border-neutral-800/60 px-4 py-3">
              <p class="text-xs text-neutral-500">
                Page {{ currentPage }} of {{ totalPages }}
              </p>
              <div class="flex items-center gap-2">
                <UButton
                  icon="i-lucide-chevron-left"
                  label="Previous"
                  color="neutral"
                  variant="outline"
                  size="xs"
                  :disabled="!hasPrevious"
                  @click="previousPage"
                />
                <UButton
                  label="Next"
                  trailing-icon="i-lucide-chevron-right"
                  color="neutral"
                  variant="outline"
                  size="xs"
                  :disabled="!hasNext"
                  @click="nextPage"
                />
              </div>
            </div>
          </div>

          <!-- Detail panel -->
          <div class="docs-card rounded-xl border border-neutral-800/60 overflow-hidden xl:sticky xl:top-6 xl:max-h-[calc(100vh-3rem)]">
            <div class="border-b border-neutral-800/60 px-5 py-4">
              <div class="flex items-start justify-between gap-4">
                <div class="min-w-0">
                  <h2 class="text-sm font-semibold text-neutral-100">
                    Module Documentation
                  </h2>
                  <p v-if="selectedDetail" class="mt-1 truncate font-mono text-xs text-neutral-500">
                    {{ moduleKey(selectedDetail) }}
                  </p>
                  <p v-else class="mt-1 text-xs text-neutral-500">
                    No module selected
                  </p>
                </div>
                <UButton
                  v-if="selectedDetail && canManage"
                  icon="i-lucide-rotate-cw"
                  color="neutral"
                  variant="outline"
                  size="xs"
                  :loading="requeueKey === moduleKey(selectedDetail)"
                  :disabled="!extractionEnabled"
                  @click="handleRequeue(selectedDetail)"
                />
              </div>
              <div
                v-if="selectedDetail && canManage"
                class="mt-3 flex flex-wrap gap-2"
              >
                <UButton
                  icon="i-lucide-bot"
                  color="neutral"
                  variant="outline"
                  size="xs"
                  :loading="regenerateLlmKey === moduleKey(selectedDetail)"
                  @click="handleRegenerateLlm(selectedDetail)"
                >
                  Regenerate LLM
                </UButton>
              </div>
            </div>

            <div v-if="isDetailLoading" class="py-12 text-center">
              <UIcon
                name="i-lucide-loader-2"
                class="animate-spin text-3xl text-primary-400"
              />
            </div>

            <div
              v-else-if="!selectedDetail"
              class="flex flex-col items-center justify-center py-16 px-6 text-center"
            >
              <div class="w-16 h-16 rounded-full bg-neutral-800/60 flex items-center justify-center mb-5">
                <UIcon name="i-lucide-mouse-pointer-2" class="text-3xl text-neutral-600" />
              </div>
              <h3 class="text-base font-medium text-neutral-300 mb-1.5">
                Select a module
              </h3>
              <p class="text-sm text-neutral-500 max-w-sm">
                Choose a module from the list to view its extracted documentation.
              </p>
            </div>

            <div v-else class="overflow-y-auto p-5 xl:max-h-[calc(100vh-9.5rem)]">
              <div class="mb-5 grid gap-3 rounded-lg border border-neutral-800/70 bg-neutral-950/35 p-3 sm:grid-cols-[auto_auto_1fr] sm:items-center">
                <div class="flex flex-wrap items-center gap-2">
                  <span
                    class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium capitalize ring-1"
                    :class="statusClass(selectedDetail.status)"
                  >
                    {{ statusLabel(selectedDetail.status) }}
                  </span>
                  <span
                    class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium capitalize ring-1"
                    :class="statusClass(selectedDetail.llmStatus)"
                  >
                    LLM {{ statusLabel(selectedDetail.llmStatus) }}
                  </span>
                </div>
                <div class="hidden h-4 w-px bg-neutral-800 sm:block" />
                <div class="grid gap-1 text-xs text-neutral-500 sm:grid-cols-2">
                  <span>Generated: {{ formatDate(selectedDetail.document?.generatedAt ?? null) }}</span>
                  <span>LLM: {{ formatDate(selectedDetail.llmContext?.generatedAt ?? null) }}</span>
                </div>
              </div>

              <div v-if="selectedDetail.llmError" class="mb-5 rounded-lg border border-cyan-900/50 bg-cyan-950/20 p-3 text-sm text-cyan-200">
                {{ selectedDetail.llmError }}
              </div>

              <div v-if="!selectedDetail.document" class="rounded-lg border border-neutral-800 bg-neutral-950/40 p-5 text-sm text-neutral-400">
                No extraction document is stored for this version.
              </div>

              <div v-else class="space-y-6">
                <section class="space-y-3">
                  <div class="flex items-center justify-between">
                    <h3 class="text-sm font-semibold text-neutral-100">
                      LLM Context
                    </h3>
                    <span class="text-xs text-neutral-500">
                      {{ selectedDetail.llmContext ? selectedDetail.llmContext.schemaVersion : 'not generated' }}
                    </span>
                  </div>
                  <div v-if="!selectedDetail.llmContext" class="rounded-lg border border-neutral-800 bg-neutral-950/35 p-4 text-sm text-neutral-400">
                    No LLM context artifact is stored for this version.
                  </div>
                  <div v-else class="space-y-3">
                    <div class="rounded-lg border border-neutral-800 bg-neutral-950/35 p-4">
                      <p class="max-w-3xl text-base leading-7 text-neutral-100">
                        {{ llmSummaryLead(selectedDetail.llmContext.summary.oneLine) }}
                      </p>
                      <ul
                        v-if="llmSummaryBullets(selectedDetail.llmContext.summary.oneLine).length"
                        class="mt-4 grid gap-2 text-sm leading-6 text-neutral-300 md:grid-cols-2"
                      >
                        <li
                          v-for="item in llmSummaryBullets(selectedDetail.llmContext.summary.oneLine)"
                          :key="item"
                          class="flex gap-2"
                        >
                          <UIcon name="i-lucide-minus" class="mt-1.5 shrink-0 text-xs text-cyan-400" />
                          <span>{{ item }}</span>
                        </li>
                      </ul>
                      <div
                        v-if="selectedDetail.llmContext.summary.capabilities.length"
                        class="mt-4 flex flex-wrap gap-2"
                      >
                        <span
                          v-for="capability in selectedDetail.llmContext.summary.capabilities"
                          :key="capability"
                          class="rounded-full bg-cyan-500/10 px-2 py-1 text-xs text-cyan-200 ring-1 ring-cyan-500/20"
                        >
                          {{ capability }}
                        </span>
                      </div>
                    </div>
                    <div
                      v-if="selectedDetail.llmContext.summary.usageNotes.length"
                      class="rounded-lg border border-neutral-800 bg-neutral-950/35 p-4"
                    >
                      <h4 class="text-sm font-semibold text-neutral-100">
                        Usage Notes
                      </h4>
                      <ul class="mt-3 space-y-2 text-sm leading-6 text-neutral-300">
                        <li
                          v-for="note in selectedDetail.llmContext.summary.usageNotes"
                          :key="note"
                          class="flex gap-2"
                        >
                          <UIcon name="i-lucide-check" class="mt-1.5 shrink-0 text-xs text-cyan-400" />
                          <span>{{ note }}</span>
                        </li>
                      </ul>
                    </div>
                    <div class="grid gap-3 sm:grid-cols-2">
                      <div class="rounded-lg border border-neutral-800 bg-neutral-950/35 p-3">
                        <p class="text-xs uppercase text-neutral-500">
                          Inputs / Outputs
                        </p>
                        <p class="mt-2 text-sm text-neutral-100">
                          {{ selectedDetail.llmContext.inputs.length }} / {{ selectedDetail.llmContext.outputs.length }}
                        </p>
                      </div>
                      <div class="rounded-lg border border-neutral-800 bg-neutral-950/35 p-3">
                        <p class="text-xs uppercase text-neutral-500">
                          Resource Types
                        </p>
                        <p class="mt-2 text-sm text-neutral-100">
                          {{ selectedDetail.llmContext.resources.managed.length }} managed / {{ selectedDetail.llmContext.resources.data.length }} data
                        </p>
                      </div>
                    </div>
                    <div v-if="selectedDetail.llmContext.navigation.humanUrl || selectedDetail.llmContext.navigation.moduleVersionsUrl" class="flex flex-wrap gap-2">
                      <NuxtLink
                        v-if="selectedDetail.llmContext.navigation.humanUrl"
                        :to="selectedDetail.llmContext.navigation.humanUrl"
                        class="text-xs text-cyan-300 hover:text-cyan-200"
                      >
                        Human docs
                      </NuxtLink>
                      <a
                        v-if="selectedDetail.llmContext.navigation.moduleVersionsUrl"
                        :href="selectedDetail.llmContext.navigation.moduleVersionsUrl"
                        target="_blank"
                        rel="noreferrer"
                        class="text-xs text-cyan-300 hover:text-cyan-200"
                      >
                        LLM versions endpoint
                      </a>
                    </div>
                    <div class="space-y-3">
                      <div class="flex items-center justify-between">
                        <h4 class="text-sm font-semibold text-neutral-100">
                          LLM JSON
                        </h4>
                        <UButton
                          :label="showLlmJson ? 'Hide' : 'View raw JSON'"
                          :icon="showLlmJson ? 'i-lucide-chevron-up' : 'i-lucide-chevron-down'"
                          color="neutral"
                          variant="ghost"
                          size="xs"
                          @click="showLlmJson = !showLlmJson"
                        />
                      </div>
                      <pre
                        v-if="showLlmJson"
                        class="docs-json max-h-80 overflow-auto rounded-lg border border-neutral-800 bg-neutral-950/60 p-4 text-xs text-neutral-300"
                      >{{ formatLlmJson(selectedDetail.llmContext) }}</pre>
                    </div>
                  </div>
                </section>

                <section v-if="selectedDetail.document.readme" class="space-y-2">
                  <div class="flex items-center gap-2">
                    <UIcon name="i-lucide-book-open" class="text-neutral-500" />
                    <h3 class="text-sm font-semibold text-neutral-100">
                      {{ selectedDetail.document.readme.title || 'README' }}
                    </h3>
                  </div>
                  <p class="font-mono text-xs text-neutral-500">
                    {{ selectedDetail.document.readme.path }}
                  </p>
                  <pre class="docs-markdown max-h-56 overflow-auto rounded-lg border border-neutral-800 bg-neutral-950/60 p-4 text-xs text-neutral-300">{{ selectedDetail.document.readme.markdown }}</pre>
                </section>

                <section class="space-y-3">
                  <div class="flex items-center justify-between">
                    <h3 class="text-sm font-semibold text-neutral-100">
                      Inputs
                    </h3>
                    <span class="text-xs tabular-nums text-neutral-500">{{ selectedDetail.document.inputs.length }}</span>
                  </div>
                  <div v-if="selectedDetail.document.inputs.length === 0" class="text-sm text-neutral-500">
                    No inputs.
                  </div>
                  <div v-else class="space-y-2">
                    <div
                      v-for="input in selectedDetail.document.inputs"
                      :key="input.name"
                      class="rounded-lg border border-neutral-800 bg-neutral-950/35 p-3"
                    >
                      <div class="flex flex-wrap items-center gap-2">
                        <code class="text-sm text-neutral-100">{{ input.name }}</code>
                        <span class="text-xs text-neutral-500">{{ inputMeta(input) }}</span>
                      </div>
                      <p v-if="input.description" class="mt-2 text-sm text-neutral-400">
                        {{ input.description }}
                      </p>
                      <p v-if="input.defaultJson" class="mt-2 font-mono text-xs text-neutral-500">
                        default = {{ input.defaultJson }}
                      </p>
                    </div>
                  </div>
                </section>

                <section class="space-y-3">
                  <div class="flex items-center justify-between">
                    <h3 class="text-sm font-semibold text-neutral-100">
                      Outputs
                    </h3>
                    <span class="text-xs tabular-nums text-neutral-500">{{ selectedDetail.document.outputs.length }}</span>
                  </div>
                  <div v-if="selectedDetail.document.outputs.length === 0" class="text-sm text-neutral-500">
                    No outputs.
                  </div>
                  <div v-else class="space-y-2">
                    <div
                      v-for="output in selectedDetail.document.outputs"
                      :key="output.name"
                      class="rounded-lg border border-neutral-800 bg-neutral-950/35 p-3"
                    >
                      <div class="flex flex-wrap items-center gap-2">
                        <code class="text-sm text-neutral-100">{{ output.name }}</code>
                        <span class="text-xs text-neutral-500">{{ outputMeta(output) }}</span>
                      </div>
                      <p v-if="output.description" class="mt-2 text-sm text-neutral-400">
                        {{ output.description }}
                      </p>
                    </div>
                  </div>
                </section>

                <section v-if="selectedDetail.document.providerRequirements.length > 0" class="space-y-3">
                  <h3 class="text-sm font-semibold text-neutral-100">
                    Providers
                  </h3>
                  <div class="space-y-2">
                    <div
                      v-for="provider in selectedDetail.document.providerRequirements"
                      :key="`${provider.name}:${provider.source}`"
                      class="flex items-center justify-between gap-3 rounded-lg border border-neutral-800 bg-neutral-950/35 p-3"
                    >
                      <div class="min-w-0">
                        <p class="truncate font-mono text-sm text-neutral-100">
                          {{ provider.source || provider.name }}
                        </p>
                        <p class="text-xs text-neutral-500">
                          {{ provider.namespace || 'hashicorp' }}
                        </p>
                      </div>
                      <span class="shrink-0 font-mono text-xs text-neutral-400">
                        {{ provider.versionConstraint || 'any' }}
                      </span>
                    </div>
                  </div>
                </section>

                <section v-if="selectedDetail.document.managedResources.length + selectedDetail.document.dataResources.length > 0" class="space-y-3">
                  <h3 class="text-sm font-semibold text-neutral-100">
                    Resources
                  </h3>
                  <div class="grid gap-2 sm:grid-cols-2">
                    <div
                      v-for="resource in selectedDetail.document.managedResources"
                      :key="`managed:${compactResource(resource)}`"
                      class="rounded-lg border border-neutral-800 bg-neutral-950/35 p-3"
                    >
                      <p class="break-all font-mono text-xs text-neutral-100">
                        {{ compactResource(resource) }}
                      </p>
                      <p class="mt-1 text-xs text-neutral-500">
                        managed
                      </p>
                    </div>
                    <div
                      v-for="resource in selectedDetail.document.dataResources"
                      :key="`data:${compactResource(resource)}`"
                      class="rounded-lg border border-neutral-800 bg-neutral-950/35 p-3"
                    >
                      <p class="break-all font-mono text-xs text-neutral-100">
                        {{ compactResource(resource) }}
                      </p>
                      <p class="mt-1 text-xs text-neutral-500">
                        data
                      </p>
                    </div>
                  </div>
                </section>

                <section v-if="selectedDetail.document.examples.length > 0" class="space-y-3">
                  <h3 class="text-sm font-semibold text-neutral-100">
                    Examples
                  </h3>
                  <div class="space-y-2">
                    <div
                      v-for="example in selectedDetail.document.examples"
                      :key="example.path"
                      class="rounded-lg border border-neutral-800 bg-neutral-950/35 p-3"
                    >
                      <p class="font-mono text-sm text-neutral-100">
                        {{ example.path }}
                      </p>
                      <p v-if="example.description" class="mt-1 text-sm text-neutral-400">
                        {{ example.description }}
                      </p>
                    </div>
                  </div>
                </section>

                <section v-if="selectedDetail.document.submodules.length > 0" class="space-y-3">
                  <h3 class="text-sm font-semibold text-neutral-100">
                    Submodules
                  </h3>
                  <div class="space-y-2">
                    <div
                      v-for="submodule in selectedDetail.document.submodules"
                      :key="submodule.path"
                      class="rounded-lg border border-neutral-800 bg-neutral-950/35 p-3"
                    >
                      <p class="font-mono text-sm text-neutral-100">
                        {{ submodule.path }}
                      </p>
                      <p class="mt-1 text-xs text-neutral-500">
                        {{ Object.keys(submodule.providers || {}).length }} providers
                      </p>
                    </div>
                  </div>
                </section>

                <section v-if="selectedDetail.document.warnings.length > 0" class="space-y-3">
                  <h3 class="text-sm font-semibold text-neutral-100">
                    Warnings
                  </h3>
                  <div class="space-y-2">
                    <div
                      v-for="warning in selectedDetail.document.warnings"
                      :key="warning"
                      class="rounded-lg border border-amber-900/50 bg-amber-950/20 p-3 text-sm text-amber-200"
                    >
                      {{ warning }}
                    </div>
                  </div>
                </section>

                <section class="space-y-3">
                  <div class="flex items-center justify-between">
                    <h3 class="text-sm font-semibold text-neutral-100">
                      Document JSON
                    </h3>
                    <UButton
                      :label="showRawJson ? 'Hide' : 'View raw JSON'"
                      :icon="showRawJson ? 'i-lucide-chevron-up' : 'i-lucide-chevron-down'"
                      color="neutral"
                      variant="ghost"
                      size="xs"
                      @click="showRawJson = !showRawJson"
                    />
                  </div>
                  <pre
                    v-if="showRawJson"
                    class="docs-json max-h-80 overflow-auto rounded-lg border border-neutral-800 bg-neutral-950/60 p-4 text-xs text-neutral-300"
                  >{{ formatDocumentJson(selectedDetail.document) }}</pre>
                </section>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.docs-card {
  background: linear-gradient(145deg, rgba(23, 23, 23, 0.6), rgba(15, 15, 15, 0.8));
  backdrop-filter: blur(8px);
}

.docs-input,
.docs-select,
.docs-number-input {
  background: rgba(38, 38, 38, 0.8);
  border: 1px solid rgba(64, 64, 64, 0.6);
  border-radius: 0.5rem;
  color: #e5e5e5;
  font-size: 0.875rem;
  min-height: 2rem;
  padding: 0.375rem 0.75rem;
  transition: all 150ms;
}

.docs-input:focus,
.docs-select:focus,
.docs-number-input:focus {
  outline: none;
  border-color: rgba(99, 102, 241, 0.6);
  box-shadow: 0 0 0 2px rgba(99, 102, 241, 0.4);
}

.docs-input:disabled,
.docs-select:disabled,
.docs-number-input:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.docs-select option {
  background: #262626;
  color: #e5e5e5;
}

.docs-markdown {
  line-height: 1.65;
  white-space: pre-wrap;
  word-break: break-word;
}

.docs-json {
  line-height: 1.5;
  tab-size: 2;
  white-space: pre;
}
</style>
