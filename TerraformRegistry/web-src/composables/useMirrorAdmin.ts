import { useAuth } from './useAuth'

export interface MirrorRuntimeOptions {
  enabled: boolean
  upstreamRegistryBaseUrl: string
  providers: MirrorProviderRuntimeOptions
  modules: MirrorModuleRuntimeOptions
  limits: MirrorLimitRuntimeOptions
}

export interface MirrorProviderRuntimeOptions {
  enabled: boolean
  requireAuthentication: boolean
  allowedHostnames: string[]
  allowedArtifactHosts: string[]
  allowlist: string[]
  denylist: string[]
  platforms: string[]
  maxPackageBytes: number
  maxChecksumBytes: number
  maxRedirects: number
  metadataTtlMinutes: number
  downloadTimeoutSeconds: number
}

export interface MirrorModuleRuntimeOptions {
  enabled: boolean
  requireAuthentication: boolean
  allowedNamespaces: string[]
  allowedArchiveHosts: string[]
  allowlist: string[]
  denylist: string[]
  maxPackageBytes: number
  maxRedirects: number
  metadataTtlMinutes: number
  downloadTimeoutSeconds: number
}

export interface MirrorLimitRuntimeOptions {
  maxConcurrentDownloads: number
  maxConcurrentDownloadsPerCoordinate: number
  maxTotalCachedBytes: number
  negativeCacheTtlSeconds: number
}

export interface MirrorConfigResponse {
  effective: MirrorRuntimeOptions
  hasRuntimeOverride: boolean
  updatedAt: string | null
  updatedBy: string | null
}

export interface MirrorAdminCacheSummary {
  total: number
  ready: number
  pending: number
  failed: number
  lastSyncAt: string | null
}

export interface MirrorAdminSummaryResponse {
  providers: MirrorAdminCacheSummary
  modules: MirrorAdminCacheSummary
  total: MirrorAdminCacheSummary
}

export interface MirrorProviderPackage {
  hostname: string
  namespace: string
  type: string
  version: string
  os: string
  arch: string
  downloadUrl: string
  filename: string | null
  packageStoragePath: string | null
  sizeBytes: number | null
  state: string
  lastError: string | null
  httpStatusCode: number | null
  lastSyncAt: string | null
  updatedAt: string
}

export interface MirrorModulePackage {
  hostname: string
  namespace: string
  name: string
  provider: string
  version: string
  downloadUrl: string
  source: string | null
  packageStoragePath: string | null
  sizeBytes: number | null
  metadataJson: string | null
  state: string
  lastError: string | null
  httpStatusCode: number | null
  lastSyncAt: string | null
  updatedAt: string
}

export interface MirrorEntriesResponse {
  providers: MirrorProviderPackage[]
  modules: MirrorModulePackage[]
  limit: number
  offset: number
}

export type MirrorEntry =
  | ({ kind: 'provider' } & MirrorProviderPackage)
  | ({ kind: 'module' } & MirrorModulePackage)

export function useMirrorAdmin() {
  const { getAuthHeaders } = useAuth()

  async function getSummary(): Promise<MirrorAdminSummaryResponse> {
    return await $fetch('/api/admin/mirror/summary', { headers: getAuthHeaders() })
  }

  async function getConfig(): Promise<MirrorConfigResponse> {
    return await $fetch('/api/admin/mirror/config', { headers: getAuthHeaders() })
  }

  async function updateConfig(config: MirrorRuntimeOptions): Promise<MirrorConfigResponse> {
    return await $fetch('/api/admin/mirror/config', {
      method: 'PUT',
      headers: getAuthHeaders(),
      body: {
        enabled: config.enabled,
        providers: config.providers,
        modules: config.modules,
        limits: config.limits,
      },
    })
  }

  async function listEntries(params?: {
    kind?: string
    q?: string
    state?: string
    limit?: number
    offset?: number
  }): Promise<MirrorEntriesResponse> {
    const query = new URLSearchParams()
    if (params?.kind) query.set('kind', params.kind)
    if (params?.q) query.set('q', params.q)
    if (params?.state) query.set('state', params.state)
    if (params?.limit !== undefined) query.set('limit', String(params.limit))
    if (params?.offset !== undefined) query.set('offset', String(params.offset))
    const qs = query.toString()
    return await $fetch(`/api/admin/mirror/entries${qs ? `?${qs}` : ''}`, {
      headers: getAuthHeaders(),
    })
  }

  async function retryEntry(entry: MirrorEntry): Promise<{ retried: boolean }> {
    return await $fetch('/api/admin/mirror/retry', {
      method: 'POST',
      headers: getAuthHeaders(),
      body: entry.kind === 'provider'
        ? {
            kind: 'provider',
            hostname: entry.hostname,
            namespace: entry.namespace,
            type: entry.type,
            version: entry.version,
          }
        : {
            kind: 'module',
            hostname: entry.hostname,
            namespace: entry.namespace,
            name: entry.name,
            provider: entry.provider,
            version: entry.version,
          },
    })
  }

  async function deleteEntry(entry: MirrorEntry): Promise<void> {
    const segments = entry.kind === 'provider'
      ? [entry.hostname, entry.namespace, entry.type, entry.version]
      : [entry.hostname, entry.namespace, entry.name, entry.provider, entry.version]
    const base = entry.kind === 'provider'
      ? '/api/admin/mirror/providers'
      : '/api/admin/mirror/modules'
    await $fetch(`${base}/${segments.map(encodeURIComponent).join('/')}`, {
      method: 'DELETE',
      headers: getAuthHeaders(),
    })
  }

  return {
    getSummary,
    getConfig,
    updateConfig,
    listEntries,
    retryEntry,
    deleteEntry,
  }
}
