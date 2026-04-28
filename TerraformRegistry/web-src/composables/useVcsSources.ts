import { useAuth } from './useAuth'

export interface VcsSource {
  id: string
  namespace: string
  name: string
  provider: string
  repoOwner: string
  repoName: string
  connectionId: string
  isActive: boolean
  tagPattern: string
  lastPublishedVersion: string | null
  lastSyncStatus: string
  lastSyncAt: string | null
  lastSyncError: string | null
  createdAt: string
  updatedAt: string
}

export interface VcsSyncResult {
  status: string
  publishedCount: number
  skippedCount: number
  latestVersion: string | null
  error: string | null
}

export interface VcsSourceCreateResponse extends VcsSource {
  sync?: VcsSyncResult | null
}

export function useVcsSources() {
  const { getAuthHeaders } = useAuth()

  async function listVcsSources(): Promise<VcsSource[]> {
    return await $fetch('/api/vcs/sources', { headers: getAuthHeaders() })
  }

  async function createVcsSource(params: {
    namespace: string
    name: string
    provider: string
    repoOwner: string
    repoName: string
    connectionId: string
    syncExistingTags?: boolean
  }): Promise<VcsSourceCreateResponse> {
    return await $fetch('/api/vcs/sources', {
      method: 'POST',
      headers: getAuthHeaders(),
      body: params,
    })
  }

  async function updateVcsSource(id: string, data: {
    repoOwner?: string
    repoName?: string
    connectionId?: string
    isActive?: boolean
  }): Promise<VcsSource> {
    return await $fetch(`/api/vcs/sources/${id}`, {
      method: 'PUT',
      headers: getAuthHeaders(),
      body: data,
    })
  }

  async function deleteVcsSource(id: string): Promise<void> {
    await $fetch(`/api/vcs/sources/${id}`, {
      method: 'DELETE',
      headers: getAuthHeaders(),
    })
  }

  async function getVcsSourceByModule(
    namespace: string,
    name: string,
    provider: string
  ): Promise<VcsSource | null> {
    try {
      return await $fetch(`/api/vcs/sources/module/${namespace}/${name}/${provider}`, {
        headers: getAuthHeaders(),
      })
    } catch {
      return null
    }
  }

  async function syncVcsSource(
    id: string,
    data: { tag?: string; replace?: boolean }
  ): Promise<VcsSyncResult> {
    return await $fetch(`/api/vcs/sources/${id}/sync`, {
      method: 'POST',
      headers: getAuthHeaders(),
      body: data,
    })
  }

  return {
    listVcsSources,
    createVcsSource,
    updateVcsSource,
    deleteVcsSource,
    getVcsSourceByModule,
    syncVcsSource,
  }
}
