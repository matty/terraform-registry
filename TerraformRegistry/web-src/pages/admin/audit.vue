<script setup lang="ts">
import { useDashboard } from '~/composables/useDashboard'
import { useAdmin } from '~/composables/useAdmin'
import type { AuditLogEntry } from '~/composables/useAdmin'
import { extractErrorMessage } from "~/composables/useErrorMessage"

definePageMeta({
  middleware: 'auth',
})

const { isSidebarOpen } = useDashboard()
const { listAuditLogs } = useAdmin()

// State
const entries = ref<AuditLogEntry[]>([])
const total = ref(0)
const isLoading = ref(false)
const errorMessage = ref<string | null>(null)
const expandedId = ref<string | null>(null)

// Filters
const filterAction = ref('')
const filterDateFrom = ref('')
const filterDateTo = ref('')

// Pagination
const limit = 25
const offset = ref(0)

const actionOptions = [
  '',
  'module.published',
  'module.deleted',
  'module.restored',
  'module.purged',
  'module.description_updated',
  'webhook.created',
  'webhook.updated',
  'webhook.deleted',
  'vcs.created',
  'vcs.updated',
  'vcs.deleted',
  'user.login',
  'user.logout',
  'user.deleted',
  'role.created',
  'role.updated',
  'role.deleted',
  'role.assigned',
  'role.removed',
]

const currentPage = computed(() => Math.floor(offset.value / limit) + 1)
const totalPages = computed(() => Math.max(1, Math.ceil(total.value / limit)))

const fetchLogs = async () => {
  isLoading.value = true
  errorMessage.value = null
  try {
    const result = await listAuditLogs({
      action: filterAction.value || undefined,
      from: filterDateFrom.value || undefined,
      to: filterDateTo.value || undefined,
      limit,
      offset: offset.value,
    })
    entries.value = result.entries
    total.value = result.total
  }
  catch (e) {
    console.error('Failed to fetch audit logs', e)
    errorMessage.value = extractErrorMessage(e, 'Failed to load audit logs')
  }
  finally {
    isLoading.value = false
  }
}

const applyFilters = () => {
  offset.value = 0
  fetchLogs()
}

const clearFilters = () => {
  filterAction.value = ''
  filterDateFrom.value = ''
  filterDateTo.value = ''
  offset.value = 0
  fetchLogs()
}

const prevPage = () => {
  if (offset.value > 0) {
    offset.value = Math.max(0, offset.value - limit)
    fetchLogs()
  }
}

const nextPage = () => {
  if (offset.value + limit < total.value) {
    offset.value += limit
    fetchLogs()
  }
}

const toggleExpand = (id: string) => {
  expandedId.value = expandedId.value === id ? null : id
}

const filtersOpen = ref(true)

const activeFilterCount = computed(() => {
  let count = 0
  if (filterAction.value) count++
  if (filterDateFrom.value) count++
  if (filterDateTo.value) count++
  return count
})

const actionCategories = computed(() => {
  const groups: Record<string, string[]> = {}
  for (const a of actionOptions.filter(x => x)) {
    const prefix = a.split('.')[0]
    if (!groups[prefix]) groups[prefix] = []
    groups[prefix].push(a)
  }
  return groups
})

function getActionColor(action: string): string {
  if (action.startsWith('module.')) return 'green'
  if (action.startsWith('user.')) return 'blue'
  if (action.startsWith('webhook.')) return 'purple'
  if (action.startsWith('vcs.') || action.startsWith('vcs_connection.')) return 'orange'
  if (action.startsWith('role.')) return 'red'
  if (action.startsWith('api_key.')) return 'amber'
  return 'neutral'
}

function getActionIcon(action: string): string {
  if (action.startsWith('module.')) return 'i-lucide-package'
  if (action.startsWith('user.')) return 'i-lucide-user'
  if (action.startsWith('webhook.')) return 'i-lucide-webhook'
  if (action.startsWith('vcs.') || action.startsWith('vcs_connection.')) return 'i-lucide-git-branch'
  if (action.startsWith('role.')) return 'i-lucide-shield'
  if (action.startsWith('api_key.')) return 'i-lucide-key'
  return 'i-lucide-activity'
}

function getIconBgClass(action: string): string {
  const map: Record<string, string> = {
    green: 'bg-green-500/15 text-green-400 ring-green-500/25',
    blue: 'bg-blue-500/15 text-blue-400 ring-blue-500/25',
    purple: 'bg-purple-500/15 text-purple-400 ring-purple-500/25',
    orange: 'bg-orange-500/15 text-orange-400 ring-orange-500/25',
    red: 'bg-red-500/15 text-red-400 ring-red-500/25',
    amber: 'bg-amber-500/15 text-amber-400 ring-amber-500/25',
    neutral: 'bg-neutral-500/15 text-neutral-400 ring-neutral-500/25',
  }
  return map[getActionColor(action)] || map.neutral
}

function getBadgeClass(action: string): string {
  const map: Record<string, string> = {
    green: 'bg-green-500/15 text-green-300 ring-1 ring-green-500/25',
    blue: 'bg-blue-500/15 text-blue-300 ring-1 ring-blue-500/25',
    purple: 'bg-purple-500/15 text-purple-300 ring-1 ring-purple-500/25',
    orange: 'bg-orange-500/15 text-orange-300 ring-1 ring-orange-500/25',
    red: 'bg-red-500/15 text-red-300 ring-1 ring-red-500/25',
    amber: 'bg-amber-500/15 text-amber-300 ring-1 ring-amber-500/25',
    neutral: 'bg-neutral-500/15 text-neutral-300 ring-1 ring-neutral-500/25',
  }
  return map[getActionColor(action)] || map.neutral
}

function relativeTime(ts: string): string {
  const now = Date.now()
  const then = new Date(ts).getTime()
  const diff = now - then
  const seconds = Math.floor(diff / 1000)
  const minutes = Math.floor(seconds / 60)
  const hours = Math.floor(minutes / 60)
  const days = Math.floor(hours / 24)
  if (seconds < 60) return 'just now'
  if (minutes < 60) return `${minutes}m ago`
  if (hours < 24) return `${hours}h ago`
  if (days < 30) return `${days}d ago`
  return new Date(ts).toLocaleDateString()
}

const formatTimestamp = (ts: string) => {
  return new Date(ts).toLocaleString()
}

const parseDetails = (details: string | null): Record<string, unknown> | null => {
  if (!details) return null
  try {
    return JSON.parse(details)
  }
  catch {
    return null
  }
}

function formatJsonKey(key: string): string {
  return key
}

function getCategoryLabel(prefix: string): string {
  const labels: Record<string, string> = {
    module: 'Module',
    user: 'User',
    webhook: 'Webhook',
    vcs: 'VCS',
    role: 'Role',
    api_key: 'API Key',
  }
  return labels[prefix] || prefix
}

onMounted(() => {
  fetchLogs()
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
      <div class="flex items-center justify-between">
        <div>
          <h1 class="page-header-title">
            Audit Log
          </h1>
          <p class="page-header-subtitle">
            Security event stream across the registry
          </p>
        </div>
        <div class="flex items-center gap-2">
          <span class="text-xs text-neutral-500 tabular-nums">
            {{ total.toLocaleString() }} events
          </span>
        </div>
      </div>
    </div>
    <div class="page-divider" />

    <!-- Body -->
    <div class="flex-1 overflow-y-auto px-6 py-8">
      <div class="max-w-5xl space-y-6">
        <!-- Error Message -->
        <div
          v-if="errorMessage"
          class="p-4 bg-red-900/20 border border-red-800/50 rounded-xl flex items-center gap-3"
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

        <!-- Filters Section -->
        <div class="rounded-xl border border-neutral-800 bg-neutral-900/40 overflow-hidden">
          <!-- Filter Header (clickable toggle) -->
          <button
            class="w-full flex items-center justify-between px-5 py-3.5 hover:bg-neutral-800/30 transition-colors"
            @click="filtersOpen = !filtersOpen"
          >
            <div class="flex items-center gap-2.5">
              <UIcon name="i-lucide-sliders-horizontal" class="text-primary-400" />
              <span class="text-sm font-medium text-neutral-200">Filters</span>
              <span
                v-if="activeFilterCount > 0"
                class="inline-flex items-center justify-center min-w-5 h-5 px-1.5 rounded-full text-[11px] font-semibold bg-primary-500 text-white"
              >
                {{ activeFilterCount }}
              </span>
            </div>
            <div class="flex items-center gap-2">
              <button
                v-if="activeFilterCount > 0"
                class="text-xs text-neutral-400 hover:text-neutral-200 transition-colors"
                @click.stop="clearFilters"
              >
                Clear all
              </button>
              <UIcon
                :name="filtersOpen ? 'i-lucide-chevron-up' : 'i-lucide-chevron-down'"
                class="text-neutral-500 text-lg"
              />
            </div>
          </button>

          <!-- Filter Body -->
          <div v-if="filtersOpen" class="px-5 pb-5 pt-1 border-t border-neutral-800/60">
            <div class="flex flex-wrap gap-4 items-end">
              <!-- Action dropdown -->
              <div class="flex flex-col gap-1.5 min-w-48">
                <label class="text-xs font-medium text-neutral-400 uppercase tracking-wider">Action</label>
                <select
                  v-model="filterAction"
                  class="audit-select"
                >
                  <option value="">
                    All actions
                  </option>
                  <template v-for="(actions, category) in actionCategories" :key="category">
                    <optgroup :label="getCategoryLabel(category)">
                      <option v-for="a in actions" :key="a" :value="a">
                        {{ a }}
                      </option>
                    </optgroup>
                  </template>
                </select>
              </div>

              <!-- Date From -->
              <div class="flex flex-col gap-1.5">
                <label class="text-xs font-medium text-neutral-400 uppercase tracking-wider">From</label>
                <input
                  v-model="filterDateFrom"
                  type="date"
                  class="audit-input"
                >
              </div>

              <!-- Date To -->
              <div class="flex flex-col gap-1.5">
                <label class="text-xs font-medium text-neutral-400 uppercase tracking-wider">To</label>
                <input
                  v-model="filterDateTo"
                  type="date"
                  class="audit-input"
                >
              </div>

              <!-- Apply -->
              <UButton
                icon="i-lucide-search"
                label="Apply"
                color="primary"
                size="sm"
                @click="applyFilters"
              />
            </div>
          </div>
        </div>

        <!-- Loading Skeleton -->
        <div v-if="isLoading" class="space-y-0">
          <div v-for="i in 6" :key="i" class="flex gap-4 py-5 px-1">
            <div class="shrink-0">
              <div class="w-10 h-10 rounded-full bg-neutral-800 animate-pulse" />
            </div>
            <div class="flex-1 space-y-2.5 pt-1">
              <div class="flex items-center gap-3">
                <div class="h-3 w-20 bg-neutral-800 rounded animate-pulse" />
                <div class="h-5 w-32 bg-neutral-800 rounded-full animate-pulse" />
              </div>
              <div class="h-3 w-64 bg-neutral-800/60 rounded animate-pulse" />
            </div>
          </div>
        </div>

        <!-- Empty State -->
        <div
          v-else-if="entries.length === 0"
          class="flex flex-col items-center justify-center py-20"
        >
          <div class="w-16 h-16 rounded-full bg-neutral-800/60 flex items-center justify-center mb-5">
            <UIcon name="i-lucide-scroll-text" class="text-3xl text-neutral-600" />
          </div>
          <h3 class="text-base font-medium text-neutral-300 mb-1.5">
            No events found
          </h3>
          <p class="text-sm text-neutral-500 max-w-sm text-center">
            No audit log entries match your current filters. Try adjusting the date range or clearing filters.
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

        <!-- Timeline Event List -->
        <div v-else>
          <!-- Pagination Header -->
          <div class="flex items-center justify-between mb-4">
            <p class="text-sm text-neutral-500">
              Showing
              <span class="text-neutral-300 font-medium">{{ offset + 1 }}</span>
              &ndash;
              <span class="text-neutral-300 font-medium">{{ Math.min(offset + limit, total) }}</span>
              of
              <span class="text-neutral-300 font-medium">{{ total.toLocaleString() }}</span>
              events
            </p>
            <div class="flex items-center gap-1.5">
              <UButton
                icon="i-lucide-chevron-left"
                color="neutral"
                variant="ghost"
                size="xs"
                :disabled="offset === 0"
                @click="prevPage"
              />
              <span class="text-xs text-neutral-400 tabular-nums px-2">
                {{ currentPage }} / {{ totalPages }}
              </span>
              <UButton
                icon="i-lucide-chevron-right"
                color="neutral"
                variant="ghost"
                size="xs"
                :disabled="offset + limit >= total"
                @click="nextPage"
              />
            </div>
          </div>

          <!-- Timeline -->
          <div class="rounded-xl border border-neutral-800 overflow-hidden">
            <template v-for="(entry, idx) in entries" :key="entry.id">
              <div
                class="flex cursor-pointer transition-colors duration-100 hover:bg-neutral-800/20 group"
                :class="[
                  idx % 2 === 0 ? 'bg-transparent' : 'bg-neutral-900/20',
                  expandedId === entry.id ? '!bg-neutral-900/40' : '',
                ]"
                @click="toggleExpand(entry.id)"
              >
                <!-- Timeline connector line -->
                <div class="relative flex flex-col items-center shrink-0 w-14 py-4">
                  <div
                    v-if="idx > 0"
                    class="w-px flex-1 bg-neutral-800 mb-1.5"
                  />
                  <div
                    class="relative z-10 w-9 h-9 rounded-full flex items-center justify-center shrink-0 ring-1"
                    :class="getIconBgClass(entry.action)"
                  >
                    <UIcon :name="getActionIcon(entry.action)" class="text-base" />
                  </div>
                  <div
                    v-if="idx < entries.length - 1"
                    class="w-px flex-1 bg-neutral-800 mt-1.5"
                  />
                </div>

                <!-- Event content -->
                <div class="flex-1 py-4 pr-5 min-w-0">
                  <!-- Top row: timestamp + action badge -->
                  <div class="flex items-center gap-2.5 flex-wrap">
                    <span
                      class="text-xs text-neutral-500 shrink-0 cursor-default"
                      :title="formatTimestamp(entry.timestamp)"
                    >
                      {{ relativeTime(entry.timestamp) }}
                    </span>
                    <span
                      class="inline-flex items-center px-2 py-0.5 rounded-full text-[11px] font-medium"
                      :class="getBadgeClass(entry.action)"
                    >
                      {{ entry.action }}
                    </span>
                    <UIcon
                      :name="expandedId === entry.id ? 'i-lucide-chevron-up' : 'i-lucide-chevron-down'"
                      class="text-neutral-600 text-xs ml-auto opacity-0 group-hover:opacity-100 transition-opacity"
                    />
                  </div>

                  <!-- Resource + User row -->
                  <div class="flex items-center gap-3 mt-1.5 flex-wrap text-xs">
                    <span v-if="entry.resourceType" class="text-neutral-400">
                      {{ entry.resourceType }}
                    </span>
                    <code v-if="entry.resourceId" class="font-mono text-neutral-500 bg-neutral-800/50 px-1.5 py-0.5 rounded text-[11px]">
                      {{ entry.resourceId }}
                    </code>
                    <span v-if="entry.userId" class="flex items-center gap-1 text-neutral-500">
                      <UIcon name="i-lucide-user" class="text-[11px]" />
                      {{ entry.userId }}
                    </span>
                  </div>

                  <!-- Expanded Details -->
                  <div
                    v-if="expandedId === entry.id"
                    class="mt-3 pt-3 border-t border-neutral-800/50 space-y-2.5"
                    @click.stop
                  >
                    <!-- IP Address -->
                    <div class="flex items-center gap-2 text-xs text-neutral-500">
                      <UIcon name="i-lucide-globe" class="text-sm shrink-0" />
                      <span>{{ entry.ipAddress || 'IP unknown' }}</span>
                    </div>

                    <!-- Full Timestamp -->
                    <div class="flex items-center gap-2 text-xs text-neutral-500">
                      <UIcon name="i-lucide-clock" class="text-sm shrink-0" />
                      <span>{{ formatTimestamp(entry.timestamp) }}</span>
                    </div>

                    <!-- JSON Details -->
                    <div v-if="parseDetails(entry.details)" class="mt-2">
                      <p class="text-[11px] uppercase tracking-wider text-neutral-500 font-medium mb-1.5">
                        Details
                      </p>
                      <div class="audit-json rounded-lg bg-neutral-950/60 border border-neutral-800/50 p-3 overflow-x-auto">
                        <template v-for="(value, key) in parseDetails(entry.details)" :key="key">
                          <div class="flex gap-2 text-xs leading-relaxed">
                            <span class="text-purple-400 shrink-0 font-mono">"{{ formatJsonKey(String(key)) }}"</span>
                            <span class="text-neutral-600">:</span>
                            <span
                              class="font-mono break-all"
                              :class="typeof value === 'string' ? 'text-green-400' : typeof value === 'number' ? 'text-amber-400' : typeof value === 'boolean' ? 'text-blue-400' : 'text-neutral-300'"
                            >
                              <template v-if="typeof value === 'string'">"{{ value }}"</template>
                              <template v-else-if="value === null">
                                <span class="text-neutral-500 italic">null</span>
                              </template>
                              <template v-else>{{ JSON.stringify(value) }}</template>
                            </span>
                          </div>
                        </template>
                      </div>
                    </div>
                    <div v-else class="text-xs text-neutral-600 italic">
                      No additional details recorded.
                    </div>
                  </div>
                </div>
              </div>
            </template>
          </div>

          <!-- Bottom Pagination -->
          <div class="flex items-center justify-between mt-5 pt-4 border-t border-neutral-800/40">
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
                :disabled="offset === 0"
                @click="prevPage"
              />
              <UButton
                label="Next"
                trailing-icon="i-lucide-chevron-right"
                color="neutral"
                variant="outline"
                size="xs"
                :disabled="offset + limit >= total"
                @click="nextPage"
              />
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* Filter inputs */
.audit-select,
.audit-input {
  background: rgba(38, 38, 38, 0.8);
  border: 1px solid rgba(64, 64, 64, 0.6);
  border-radius: 0.5rem;
  padding: 0.375rem 0.75rem;
  font-size: 0.875rem;
  color: #e5e5e5;
  transition: all 150ms;
}

.audit-select:focus,
.audit-input:focus {
  outline: none;
  box-shadow: 0 0 0 2px rgba(99, 102, 241, 0.4);
  border-color: rgba(99, 102, 241, 0.6);
}

.audit-select option,
.audit-select optgroup {
  background: #262626;
  color: #e5e5e5;
}

/* JSON details */
.audit-json {
  font-size: 12px;
  line-height: 1.7;
}
</style>
