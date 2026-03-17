import { useAuth } from './useAuth'

export interface VcsSource {
  id: string
  namespace: string
  name: string
  provider: string
  repoOwner: string
  repoName: string
  isActive: boolean
  createdAt: string
  updatedAt: string
}

export interface VcsSourceCreateResponse extends VcsSource {
  webhookSecret: string
  webhookUrl: string
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
    pat?: string
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
    pat?: string
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

  return { listVcsSources, createVcsSource, updateVcsSource, deleteVcsSource }
}
