<script setup lang="ts">
import { useDashboard } from '~/composables/useDashboard'
import { useAdmin } from '~/composables/useAdmin'
import type { AuditLogEntry } from '~/composables/useAdmin'

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
    errorMessage.value = 'Failed to load audit logs.'
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
            Track actions performed across the registry
          </p>
        </div>
      </div>
    </div>
    <div class="page-divider" />

    <!-- Body -->
    <div class="flex-1 overflow-y-auto px-6 py-6">
      <div class="max-w-6xl space-y-6">
        <!-- Error Message -->
        <div
          v-if="errorMessage"
          class="p-4 bg-red-900/20 border border-red-800/50 rounded-xl flex items-center gap-3"
        >
          <UIcon name="i-lucide-alert-circle" class="text-red-500 text-xl" />
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

        <!-- Filters -->
        <div class="p-5 bg-neutral-900/60 rounded-xl border border-neutral-800 ring-1 ring-neutral-800/50">
          <h3 class="text-sm font-semibold mb-3 text-neutral-200 flex items-center gap-2">
            <UIcon name="i-lucide-filter" class="text-primary-400" />
            Filters
          </h3>
          <div class="flex flex-wrap gap-3 items-end">
            <div class="flex flex-col gap-1">
              <label class="text-xs text-neutral-400">Action</label>
              <select
                v-model="filterAction"
                class="bg-neutral-800 border border-neutral-700 rounded-lg px-3 py-1.5 text-sm text-neutral-200 focus:outline-none focus:ring-1 focus:ring-primary-500"
              >
                <option value="">
                  All actions
                </option>
                <option v-for="a in actionOptions.filter(x => x)" :key="a" :value="a">
                  {{ a }}
                </option>
              </select>
            </div>
            <div class="flex flex-col gap-1">
              <label class="text-xs text-neutral-400">From</label>
              <input
                v-model="filterDateFrom"
                type="date"
                class="bg-neutral-800 border border-neutral-700 rounded-lg px-3 py-1.5 text-sm text-neutral-200 focus:outline-none focus:ring-1 focus:ring-primary-500"
              >
            </div>
            <div class="flex flex-col gap-1">
              <label class="text-xs text-neutral-400">To</label>
              <input
                v-model="filterDateTo"
                type="date"
                class="bg-neutral-800 border border-neutral-700 rounded-lg px-3 py-1.5 text-sm text-neutral-200 focus:outline-none focus:ring-1 focus:ring-primary-500"
              >
            </div>
            <UButton
              label="Apply"
              color="primary"
              size="sm"
              @click="applyFilters"
            />
            <UButton
              label="Clear"
              color="neutral"
              variant="ghost"
              size="sm"
              @click="clearFilters"
            />
          </div>
        </div>

        <!-- Results -->
        <div>
          <div class="flex items-center justify-between mb-3">
            <h2 class="text-base font-semibold text-neutral-200 flex items-center gap-2">
              <UIcon name="i-lucide-scroll-text" class="text-primary-400" />
              Entries
              <span class="text-xs text-neutral-500 font-normal">({{ total }} total)</span>
            </h2>
            <div class="flex items-center gap-2 text-sm text-neutral-400">
              <UButton
                icon="i-lucide-chevron-left"
                color="neutral"
                variant="ghost"
                size="xs"
                :disabled="offset === 0"
                @click="prevPage"
              />
              <span>Page {{ currentPage }} of {{ totalPages }}</span>
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

          <div v-if="isLoading" class="py-8 text-center">
            <UIcon
              name="i-lucide-loader-2"
              class="animate-spin text-2xl text-primary-400"
            />
          </div>

          <div
            v-else-if="entries.length === 0"
            class="py-8 text-center text-neutral-500"
          >
            <p>No audit log entries found.</p>
          </div>

          <!-- Table -->
          <div v-else class="overflow-x-auto rounded-xl border border-neutral-800">
            <table class="w-full text-sm">
              <thead>
                <tr class="bg-neutral-900/80 text-neutral-400 text-left">
                  <th class="px-4 py-3 font-medium w-8" />
                  <th class="px-4 py-3 font-medium">
                    Timestamp
                  </th>
                  <th class="px-4 py-3 font-medium">
                    User
                  </th>
                  <th class="px-4 py-3 font-medium">
                    Action
                  </th>
                  <th class="px-4 py-3 font-medium">
                    Resource Type
                  </th>
                  <th class="px-4 py-3 font-medium">
                    Resource ID
                  </th>
                </tr>
              </thead>
              <tbody>
                <template v-for="entry in entries" :key="entry.id">
                  <tr
                    class="border-t border-neutral-800 hover:bg-neutral-900/40 cursor-pointer transition-colors"
                    @click="toggleExpand(entry.id)"
                  >
                    <td class="px-4 py-3">
                      <UIcon
                        :name="expandedId === entry.id ? 'i-lucide-chevron-down' : 'i-lucide-chevron-right'"
                        class="text-neutral-500 text-xs"
                      />
                    </td>
                    <td class="px-4 py-3 text-neutral-300 whitespace-nowrap">
                      {{ formatTimestamp(entry.timestamp) }}
                    </td>
                    <td class="px-4 py-3 text-neutral-300 font-mono text-xs">
                      {{ entry.userId || '-' }}
                    </td>
                    <td class="px-4 py-3">
                      <span class="px-2 py-0.5 rounded-full text-[11px] font-medium bg-primary-900/40 text-primary-300">
                        {{ entry.action }}
                      </span>
                    </td>
                    <td class="px-4 py-3 text-neutral-400">
                      {{ entry.resourceType }}
                    </td>
                    <td class="px-4 py-3 text-neutral-400 font-mono text-xs">
                      {{ entry.resourceId || '-' }}
                    </td>
                  </tr>
                  <!-- Expanded details row -->
                  <tr v-if="expandedId === entry.id" class="border-t border-neutral-800/50">
                    <td colspan="6" class="px-4 py-4 bg-neutral-900/30">
                      <div class="space-y-2">
                        <div class="flex items-center gap-2 text-xs text-neutral-500">
                          <UIcon name="i-lucide-globe" class="text-sm" />
                          <span>IP: {{ entry.ipAddress || 'Unknown' }}</span>
                        </div>
                        <div v-if="parseDetails(entry.details)">
                          <p class="text-xs text-neutral-500 mb-1">
                            Details:
                          </p>
                          <pre class="text-xs text-neutral-300 bg-neutral-800/50 rounded-lg p-3 overflow-x-auto">{{ JSON.stringify(parseDetails(entry.details), null, 2) }}</pre>
                        </div>
                        <div v-else class="text-xs text-neutral-500">
                          No additional details.
                        </div>
                      </div>
                    </td>
                  </tr>
                </template>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
