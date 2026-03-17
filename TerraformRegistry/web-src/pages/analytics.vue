<script setup lang="ts">
import { Line } from 'vue-chartjs'
import { Chart as ChartJS, CategoryScale, LinearScale, PointElement, LineElement, Title, Tooltip, Filler } from 'chart.js'
import { useDashboard } from '~/composables/useDashboard'
import { useAnalytics, type DownloadSummary, type TopModuleEntry, type TrendEntry } from '~/composables/useAnalytics'

ChartJS.register(CategoryScale, LinearScale, PointElement, LineElement, Title, Tooltip, Filler)

definePageMeta({
  middleware: 'auth',
})

const { isSidebarOpen } = useDashboard()
const { getSummary, getTopModules, getTrends } = useAnalytics()

const isLoading = ref(true)
const error = ref('')

const summary = ref<DownloadSummary | null>(null)
const topModules = ref<TopModuleEntry[]>([])
const trendData = ref<TrendEntry[]>([])

const periodOptions = [
  { label: '7d', value: '7d' },
  { label: '30d', value: '30d' },
  { label: '90d', value: '90d' },
  { label: 'All', value: 'all' },
]
const selectedPeriod = ref('30d')

const chartData = computed(() => ({
  labels: trendData.value.map(t => t.date),
  datasets: [
    {
      label: 'Downloads',
      data: trendData.value.map(t => t.downloads),
      borderColor: 'rgb(99, 102, 241)',
      backgroundColor: 'rgba(99, 102, 241, 0.1)',
      fill: true,
      tension: 0.3,
      pointRadius: 2,
      pointHoverRadius: 5,
    },
  ],
}))

const chartOptions = {
  responsive: true,
  maintainAspectRatio: false,
  plugins: {
    tooltip: {
      mode: 'index' as const,
      intersect: false,
    },
  },
  scales: {
    x: {
      grid: { color: 'rgba(255,255,255,0.05)' },
      ticks: { color: 'rgba(255,255,255,0.4)' },
    },
    y: {
      beginAtZero: true,
      grid: { color: 'rgba(255,255,255,0.05)' },
      ticks: { color: 'rgba(255,255,255,0.4)' },
    },
  },
}

const summaryCards = computed(() => {
  if (!summary.value) return []
  return [
    { label: 'Total Downloads', value: summary.value.totalDownloads, icon: 'i-lucide-download' },
    { label: 'Today', value: summary.value.downloadsToday, icon: 'i-lucide-calendar' },
    { label: 'This Week', value: summary.value.downloadsThisWeek, icon: 'i-lucide-calendar-days' },
    { label: 'This Month', value: summary.value.downloadsThisMonth, icon: 'i-lucide-calendar-range' },
  ]
})

async function fetchAll() {
  isLoading.value = true
  error.value = ''
  try {
    const [summaryRes, topRes, trendRes] = await Promise.all([
      getSummary(),
      getTopModules(10, selectedPeriod.value),
      getTrends(selectedPeriod.value, 'day'),
    ])
    summary.value = summaryRes
    topModules.value = topRes.modules
    trendData.value = trendRes.data
  }
  catch (err: any) {
    error.value = err?.data?.message || err?.message || 'Failed to load analytics data'
    console.error('Error fetching analytics:', err)
  }
  finally {
    isLoading.value = false
  }
}

watch(selectedPeriod, () => {
  fetchAll()
})

onMounted(() => {
  fetchAll()
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
            Analytics
          </h1>
          <p class="page-header-subtitle">
            Module download statistics and trends
          </p>
        </div>
        <div class="flex items-center gap-2">
          <UButtonGroup>
            <UButton
              v-for="opt in periodOptions"
              :key="opt.value"
              :label="opt.label"
              size="sm"
              :variant="selectedPeriod === opt.value ? 'solid' : 'ghost'"
              :color="selectedPeriod === opt.value ? 'primary' : 'neutral'"
              @click="selectedPeriod = opt.value"
            />
          </UButtonGroup>
        </div>
      </div>
    </div>
    <div class="page-divider" />

    <!-- Body -->
    <div class="flex-1 overflow-y-auto">
      <div class="p-6 max-w-6xl space-y-6">
        <!-- Error State -->
        <UAlert
          v-if="error"
          color="error"
          variant="soft"
          :title="error"
          icon="i-lucide-alert-circle"
        />

        <!-- Loading State -->
        <div
          v-if="isLoading"
          class="flex flex-col justify-center items-center py-20"
        >
          <div class="relative">
            <div class="w-16 h-16 border-4 border-primary-500/20 rounded-full" />
            <div class="w-16 h-16 border-4 border-transparent border-t-primary-500 rounded-full animate-spin absolute inset-0" />
          </div>
          <p class="text-neutral-400 text-lg mt-6">
            Loading analytics...
          </p>
        </div>

        <template v-else-if="!error">
          <!-- Summary Cards -->
          <div class="grid grid-cols-2 lg:grid-cols-4 gap-4">
            <div
              v-for="card in summaryCards"
              :key="card.label"
              class="p-5 bg-neutral-900/60 rounded-xl border border-neutral-800 ring-1 ring-neutral-800/50"
            >
              <div class="flex items-center gap-3 mb-2">
                <UIcon :name="card.icon" class="text-lg text-primary-400" />
                <span class="text-sm text-neutral-400">{{ card.label }}</span>
              </div>
              <p class="text-2xl font-bold text-neutral-100">
                {{ card.value.toLocaleString() }}
              </p>
            </div>
          </div>

          <!-- Download Trends Chart -->
          <div class="p-5 bg-neutral-900/60 rounded-xl border border-neutral-800 ring-1 ring-neutral-800/50">
            <h2 class="text-base font-semibold text-neutral-200 mb-4 flex items-center gap-2">
              <UIcon name="i-lucide-trending-up" class="text-primary-400" />
              Download Trends
            </h2>
            <div v-if="trendData.length" class="h-72">
              <Line :data="chartData" :options="chartOptions" />
            </div>
            <div v-else class="py-12 text-center text-neutral-500">
              No trend data available for this period.
            </div>
          </div>

          <!-- Top Modules Table -->
          <div class="p-5 bg-neutral-900/60 rounded-xl border border-neutral-800 ring-1 ring-neutral-800/50">
            <h2 class="text-base font-semibold text-neutral-200 mb-4 flex items-center gap-2">
              <UIcon name="i-lucide-trophy" class="text-primary-400" />
              Top Modules
            </h2>
            <div v-if="topModules.length" class="overflow-x-auto">
              <table class="w-full text-sm">
                <thead>
                  <tr class="border-b border-neutral-800 text-neutral-400">
                    <th class="text-left py-2 pr-4 font-medium">
                      #
                    </th>
                    <th class="text-left py-2 pr-4 font-medium">
                      Namespace
                    </th>
                    <th class="text-left py-2 pr-4 font-medium">
                      Name
                    </th>
                    <th class="text-left py-2 pr-4 font-medium">
                      Provider
                    </th>
                    <th class="text-right py-2 font-medium">
                      Downloads
                    </th>
                  </tr>
                </thead>
                <tbody>
                  <tr
                    v-for="(mod, idx) in topModules"
                    :key="`${mod.namespace}/${mod.name}/${mod.provider}`"
                    class="border-b border-neutral-800/50 hover:bg-neutral-800/30 transition-colors"
                  >
                    <td class="py-2.5 pr-4 text-neutral-500">
                      {{ idx + 1 }}
                    </td>
                    <td class="py-2.5 pr-4 text-neutral-300">
                      {{ mod.namespace }}
                    </td>
                    <td class="py-2.5 pr-4 font-medium text-neutral-100">
                      {{ mod.name }}
                    </td>
                    <td class="py-2.5 pr-4">
                      <span class="px-2 py-0.5 bg-neutral-800/50 rounded text-neutral-400 text-xs">
                        {{ mod.provider }}
                      </span>
                    </td>
                    <td class="py-2.5 text-right font-mono text-neutral-200">
                      {{ mod.downloads.toLocaleString() }}
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
            <div v-else class="py-12 text-center text-neutral-500">
              No module download data available for this period.
            </div>
          </div>
        </template>
      </div>
    </div>
  </div>
</template>
