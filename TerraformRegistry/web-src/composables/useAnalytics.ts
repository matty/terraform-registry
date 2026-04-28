import { useAuth } from './useAuth'

export interface DownloadSummary {
  totalDownloads: number
  downloadsToday: number
  downloadsThisWeek: number
  downloadsThisMonth: number
  uniqueModules: number
}

export interface TopModuleEntry {
  namespace: string
  name: string
  provider: string
  downloads: number
}

export interface TrendEntry {
  date: string
  downloads: number
}

export interface VersionDownloads {
  version: string
  downloads: number
}

export interface ModuleAnalytics {
  namespace: string
  name: string
  provider: string
  totalDownloads: number
  versions: VersionDownloads[]
  trend: TrendEntry[]
}

export function useAnalytics() {
  const { getAuthHeaders } = useAuth()

  async function getSummary(): Promise<DownloadSummary> {
    return await $fetch('/api/analytics/downloads/summary', {
      headers: getAuthHeaders(),
    })
  }

  async function getTopModules(limit = 10, period = '30d'): Promise<{ period: string, modules: TopModuleEntry[] }> {
    return await $fetch(`/api/analytics/downloads/top?limit=${limit}&period=${period}`, {
      headers: getAuthHeaders(),
    })
  }

  async function getTrends(period = '30d', interval = 'day'): Promise<{ period: string, interval: string, data: TrendEntry[] }> {
    return await $fetch(`/api/analytics/downloads/trends?period=${period}&interval=${interval}`, {
      headers: getAuthHeaders(),
    })
  }

  async function getModuleAnalytics(ns: string, name: string, provider: string, period = '30d'): Promise<ModuleAnalytics> {
    return await $fetch(`/api/analytics/downloads/module/${ns}/${name}/${provider}?period=${period}`, {
      headers: getAuthHeaders(),
    })
  }

  return { getSummary, getTopModules, getTrends, getModuleAnalytics }
}
