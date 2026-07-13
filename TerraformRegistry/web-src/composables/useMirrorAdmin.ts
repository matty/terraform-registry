import { useAuth } from './useAuth'

export interface MirrorConfig {
  enabled: boolean
  providers: { enabled: boolean, requireAuthentication: boolean }
  modules: { enabled: boolean, requireAuthentication: boolean }
  limits: { maxConcurrentDownloads: number, maxConcurrentDownloadsPerCoordinate: number, maxTotalCachedBytes: number, negativeCacheTtlSeconds: number }
}

export interface MirrorConfigResponse {
  effective: MirrorConfig
  hasRuntimeOverride: boolean
  updatedAt: string | null
  updatedBy: string | null
}

export interface MirrorProviderCacheEntry {
  hostname: string
  namespace: string
  type: string
  version: string
  os: string
  arch: string
  state: string
  packageStoragePath: string | null
  cacheSizeBytes: number | null
  lastError: string | null
  lastSyncAt: string | null
}

export interface MirrorModuleCacheEntry {
  hostname: string
  namespace: string
  name: string
  provider: string
  version: string
  state: string
  packageStoragePath: string | null
  cacheSizeBytes: number | null
  lastError: string | null
  lastSyncAt: string | null
}

export interface MirrorLease {
  leaseKey: string
  operationType: string
  ownerInstanceId: string
  expiresAt: string
  heartbeatAt: string | null
}

export function useMirrorAdmin() {
  const { getAuthHeaders } = useAuth()

  const query = (params: Record<string, string | number | undefined>) => {
    const values = new URLSearchParams()
    for (const [key, value] of Object.entries(params)) {
      if (value !== undefined && value !== '') values.set(key, String(value))
    }
    const result = values.toString()
    return result ? `?${result}` : ''
  }

  const getConfig = () => $fetch<MirrorConfigResponse>('/api/admin/mirror/config', { headers: getAuthHeaders() })
  const updateConfig = (config: MirrorConfig) => $fetch<MirrorConfigResponse>('/api/admin/mirror/config', {
    method: 'PUT', headers: getAuthHeaders(), body: config,
  })
  const listProviders = (params: { q?: string, state?: string, limit?: number, offset?: number } = {}) =>
    $fetch<MirrorProviderCacheEntry[]>(`/api/admin/mirror/providers${query(params)}`, { headers: getAuthHeaders() })
  const listModules = (params: { q?: string, state?: string, limit?: number, offset?: number } = {}) =>
    $fetch<MirrorModuleCacheEntry[]>(`/api/admin/mirror/modules${query(params)}`, { headers: getAuthHeaders() })
  const listLeases = (params: { limit?: number, offset?: number } = {}) =>
    $fetch<MirrorLease[]>(`/api/admin/mirror/leases${query(params)}`, { headers: getAuthHeaders() })
  const purgeProvider = (entry: MirrorProviderCacheEntry) => $fetch(
    `/api/admin/mirror/providers/${[entry.hostname, entry.namespace, entry.type, entry.version, entry.os, entry.arch].map(encodeURIComponent).join('/')}`,
    { method: 'DELETE', headers: getAuthHeaders() },
  )
  const purgeModule = (entry: MirrorModuleCacheEntry) => $fetch(
    `/api/admin/mirror/modules/${[entry.hostname, entry.namespace, entry.name, entry.provider, entry.version].map(encodeURIComponent).join('/')}`,
    { method: 'DELETE', headers: getAuthHeaders() },
  )

  return { getConfig, updateConfig, listProviders, listModules, listLeases, purgeProvider, purgeModule }
}
