import { useAuth } from './useAuth'

export interface VcsConnection {
  id: string
  label: string
  provider: string
  defaultOrg: string | null
  isActive: boolean
  createdBy: string | null
  createdAt: string
}

export interface VcsConnectionCreateResponse extends VcsConnection {
  webhookSecret: string
  webhookUrl: string
}

export interface VcsConnectionSummary {
  id: string
  label: string
  provider: string
  defaultOrg: string | null
}

export function useVcsConnections() {
  const { getAuthHeaders } = useAuth()

  async function listConnections(): Promise<VcsConnection[]> {
    return await $fetch('/api/admin/vcs-connections', { headers: getAuthHeaders() })
  }

  async function createConnection(params: { label: string, provider?: string, pat?: string, defaultOrg?: string }): Promise<VcsConnectionCreateResponse> {
    return await $fetch('/api/admin/vcs-connections', { method: 'POST', headers: getAuthHeaders(), body: params })
  }

  async function updateConnection(id: string, data: { label?: string, pat?: string, defaultOrg?: string, isActive?: boolean }): Promise<VcsConnection> {
    return await $fetch(`/api/admin/vcs-connections/${id}`, { method: 'PUT', headers: getAuthHeaders(), body: data })
  }

  async function deleteConnection(id: string): Promise<void> {
    await $fetch(`/api/admin/vcs-connections/${id}`, { method: 'DELETE', headers: getAuthHeaders() })
  }

  async function listConnectionSummaries(): Promise<VcsConnectionSummary[]> {
    return await $fetch('/api/vcs/connections', { headers: getAuthHeaders() })
  }

  return { listConnections, createConnection, updateConnection, deleteConnection, listConnectionSummaries }
}
