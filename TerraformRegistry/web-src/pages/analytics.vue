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
    { label: 'Total Downloads', value: summary.value.totalDownloads, icon: 'i-lucide-download', gradient: 'from-blue-500/15 to-blue-600/5', border: 'border-blue-500/20', iconColor: 'text-blue-400' },
    { label: 'Today', value: summary.value.downloadsToday, icon: 'i-lucide-zap', gradient: 'from-emerald-500/15 to-emerald-600/5', border: 'border-emerald-500/20', iconColor: 'text-emerald-400' },
    { label: 'This Week', value: summary.value.downloadsThisWeek, icon: 'i-lucide-calendar-days', gradient: 'from-violet-500/15 to-violet-600/5', border: 'border-violet-500/20', iconColor: 'text-violet-400' },
    { label: 'This Month', value: summary.value.downloadsThisMonth, icon: 'i-lucide-calendar-range', gradient: 'from-amber-500/15 to-amber-600/5', border: 'border-amber-500/20', iconColor: 'text-amber-400' },
  ]
})

const maxDownloads = computed(() => {
  if (!topModules.value.length) return 1
  return Math.max(...topModules.value.map(m => m.downloads))
})

function formatNumber(n: number): string {
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`
  if (n >= 1_000) return `${(n / 1_000).toFixed(1)}K`
  return n.toLocaleString()
}

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
      </div>
    </div>
    <div class="page-divider" />

    <!-- Body -->
    <div class="flex-1 overflow-y-auto">
      <div class="px-6 py-8 max-w-6xl space-y-8">
        <!-- Error State -->
        <UAlert
          v-if="error"
          color="error"
          variant="soft"
          :title="error"
          icon="i-lucide-alert-circle"
        />

        <!-- Loading Skeleton State -->
        <template v-if="isLoading">
          <!-- Skeleton Summary Cards -->
          <div class="grid grid-cols-2 lg:grid-cols-4 gap-5">
            <div
              v-for="i in 4"
              :key="i"
              class="relative overflow-hidden rounded-xl border border-neutral-800/60 bg-neutral-900/40 p-6"
            >
              <div class="space-y-3">
                <div class="h-4 w-24 rounded-md bg-neutral-800/80 animate-pulse" />
                <div class="h-9 w-20 rounded-md bg-neutral-800/60 animate-pulse" />
              </div>
              <div class="absolute top-5 right-5 h-10 w-10 rounded-lg bg-neutral-800/40 animate-pulse" />
            </div>
          </div>
          <!-- Skeleton Chart -->
          <div class="rounded-xl border border-neutral-800/60 bg-neutral-900/40 p-6">
            <div class="h-5 w-40 rounded-md bg-neutral-800/80 animate-pulse mb-6" />
            <div class="h-80 rounded-lg bg-neutral-800/30 animate-pulse" />
          </div>
          <!-- Skeleton Table -->
          <div class="rounded-xl border border-neutral-800/60 bg-neutral-900/40 p-6">
            <div class="h-5 w-32 rounded-md bg-neutral-800/80 animate-pulse mb-6" />
            <div class="space-y-3">
              <div v-for="i in 5" :key="i" class="h-10 rounded-md bg-neutral-800/30 animate-pulse" />
            </div>
          </div>
        </template>

        <template v-else-if="!error">
          <!-- Summary Cards -->
          <div class="grid grid-cols-2 lg:grid-cols-4 gap-5">
            <div
              v-for="card in summaryCards"
              :key="card.label"
              class="stat-card group relative overflow-hidden rounded-xl border p-6 transition-all duration-300 "
              :class="[`bg-gradient-to-br ${card.gradient}`, card.border]"
            >
              <!-- Decorative icon -->
              <div class="absolute top-4 right-4 flex items-center justify-center w-10 h-10 rounded-lg bg-white/[0.04] transition-colors group-hover:bg-white/[0.07]">
                <UIcon :name="card.icon" class="text-xl" :class="card.iconColor" />
              </div>
              <!-- Content -->
              <p class="text-sm font-medium text-neutral-400 mb-2">
                {{ card.label }}
              </p>
              <p class="text-3xl font-bold tracking-tight text-neutral-50 tabular-nums">
                {{ card.value.toLocaleString() }}
              </p>
            </div>
          </div>

          <!-- Download Trends Chart -->
          <div class="rounded-xl border border-neutral-800/60 bg-neutral-900/40 backdrop-blur-sm">
            <div class="flex items-center justify-between px-6 pt-6 pb-2">
              <h2 class="text-base font-semibold text-neutral-200 flex items-center gap-2.5">
                <div class="flex items-center justify-center w-8 h-8 rounded-lg bg-primary-500/10">
                  <UIcon name="i-lucide-trending-up" class="text-primary-400 text-lg" />
                </div>
                Download Trends
              </h2>
              <!-- Period Selector Pills -->
              <div class="flex items-center gap-1 p-1 rounded-lg bg-neutral-800/50">
                <button
                  v-for="opt in periodOptions"
                  :key="opt.value"
                  class="px-3.5 py-1.5 text-xs font-medium rounded-md transition-all duration-200"
                  :class="selectedPeriod === opt.value
                    ? 'bg-primary-500 text-white shadow-sm shadow-primary-500/25'
                    : 'text-neutral-400 hover:text-neutral-200 hover:bg-neutral-700/50'"
                  @click="selectedPeriod = opt.value"
                >
                  {{ opt.label }}
                </button>
              </div>
            </div>
            <div v-if="trendData.length" class="px-6 pb-6 pt-2">
              <div class="h-80">
                <Line :data="chartData" :options="chartOptions" />
              </div>
            </div>
            <div v-else class="px-6 pb-6">
              <div class="flex flex-col items-center justify-center py-16 rounded-lg border border-dashed border-neutral-800">
                <UIcon name="i-lucide-bar-chart-3" class="text-3xl text-neutral-600 mb-3" />
                <p class="text-neutral-500 text-sm">
                  No trend data available for this period.
                </p>
              </div>
            </div>
          </div>

          <!-- Top Modules Table -->
          <div class="rounded-xl border border-neutral-800/60 bg-neutral-900/40 backdrop-blur-sm">
            <div class="px-6 pt-6 pb-4">
              <h2 class="text-base font-semibold text-neutral-200 flex items-center gap-2.5">
                <div class="flex items-center justify-center w-8 h-8 rounded-lg bg-amber-500/10">
                  <UIcon name="i-lucide-trophy" class="text-amber-400 text-lg" />
                </div>
                Top Modules
              </h2>
            </div>
            <div v-if="topModules.length" class="overflow-x-auto">
              <table class="w-full text-sm">
                <thead>
                  <tr class="border-y border-neutral-800/60 text-neutral-500 text-xs uppercase tracking-wider">
                    <th class="text-left py-3 pl-6 pr-3 font-semibold w-12">
                      Rank
                    </th>
                    <th class="text-left py-3 px-3 font-semibold">
                      Module
                    </th>
                    <th class="text-left py-3 px-3 font-semibold">
                      Provider
                    </th>
                    <th class="text-right py-3 px-3 font-semibold w-28">
                      Downloads
                    </th>
                    <th class="py-3 pl-3 pr-6 font-semibold w-48">
                      <span class="sr-only">Bar</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  <tr
                    v-for="(mod, idx) in topModules"
                    :key="`${mod.namespace}/${mod.name}/${mod.provider}`"
                    class="group border-b border-neutral-800/30 transition-colors duration-150 hover:bg-white/[0.02]"
                    :class="idx % 2 === 1 ? 'bg-white/[0.01]' : ''"
                  >
                    <td class="py-3.5 pl-6 pr-3">
                      <span
                        class="inline-flex items-center justify-center w-7 h-7 rounded-md text-xs font-bold"
                        :class="idx === 0 ? 'bg-amber-500/15 text-amber-400'
                          : idx === 1 ? 'bg-neutral-400/10 text-neutral-400'
                            : idx === 2 ? 'bg-orange-500/10 text-orange-400'
                              : 'bg-neutral-800/40 text-neutral-500'"
                      >
                        {{ idx + 1 }}
                      </span>
                    </td>
                    <td class="py-3.5 px-3">
                      <div class="flex flex-col">
                        <span class="font-semibold text-neutral-100 group-hover:text-white transition-colors">{{ mod.name }}</span>
                        <span class="text-xs text-neutral-500">{{ mod.namespace }}</span>
                      </div>
                    </td>
                    <td class="py-3.5 px-3">
                      <span class="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md bg-neutral-800/60 text-neutral-300 text-xs font-medium ring-1 ring-neutral-700/40">
                        <UIcon name="i-lucide-cloud" class="text-neutral-500 text-xs" />
                        {{ mod.provider }}
                      </span>
                    </td>
                    <td class="py-3.5 px-3 text-right font-mono text-sm font-semibold text-neutral-200 tabular-nums">
                      {{ formatNumber(mod.downloads) }}
                    </td>
                    <td class="py-3.5 pl-3 pr-6">
                      <div class="w-full h-2 rounded-full bg-neutral-800/60 overflow-hidden">
                        <div
                          class="h-full rounded-full bg-gradient-to-r from-primary-500 to-primary-400 transition-all duration-500"
                          :style="{ width: `${(mod.downloads / maxDownloads) * 100}%` }"
                        />
                      </div>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
            <div v-else class="px-6 pb-6">
              <div class="flex flex-col items-center justify-center py-16 rounded-lg border border-dashed border-neutral-800">
                <UIcon name="i-lucide-package" class="text-3xl text-neutral-600 mb-3" />
                <p class="text-neutral-500 text-sm">
                  No module download data available for this period.
                </p>
              </div>
            </div>
          </div>
        </template>
      </div>
    </div>
  </div>
</template>
