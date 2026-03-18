import { useAuth } from './useAuth'

export interface AdminRole {
  id: string
  name: string
  description: string | null
  permissions: string[]
  isSystem: boolean
  createdAt: string
}

export interface AdminUser {
  id: string
  email: string
  provider: string
  createdAt: string
}

export interface AuditLogEntry {
  id: string
  userId: string | null
  action: string
  resourceType: string
  resourceId: string | null
  details: string | null
  ipAddress: string | null
  timestamp: string
}

export interface AuditLogPage {
  entries: AuditLogEntry[]
  total: number
}

export function useAdmin() {
  const { getAuthHeaders } = useAuth()

  async function listRoles(): Promise<AdminRole[]> {
    return await $fetch('/api/admin/roles', { headers: getAuthHeaders() })
  }

  async function createRole(params: { name: string, description?: string, permissions: string[] }): Promise<AdminRole> {
    return await $fetch('/api/admin/roles', {
      method: 'POST',
      headers: getAuthHeaders(),
      body: params,
    })
  }

  async function updateRole(id: string, data: { name?: string, description?: string, permissions?: string[] }): Promise<AdminRole> {
    return await $fetch(`/api/admin/roles/${id}`, {
      method: 'PUT',
      headers: getAuthHeaders(),
      body: data,
    })
  }

  async function deleteRole(id: string): Promise<void> {
    await $fetch(`/api/admin/roles/${id}`, {
      method: 'DELETE',
      headers: getAuthHeaders(),
    })
  }

  async function listUsers(): Promise<AdminUser[]> {
    return await $fetch('/api/admin/users', { headers: getAuthHeaders() })
  }

  async function getUserRoles(userId: string): Promise<AdminRole[]> {
    return await $fetch(`/api/admin/users/${userId}/roles`, { headers: getAuthHeaders() })
  }

  async function assignRole(userId: string, roleId: string): Promise<void> {
    await $fetch(`/api/admin/users/${userId}/roles`, {
      method: 'POST',
      headers: getAuthHeaders(),
      body: { roleId },
    })
  }

  async function removeRole(userId: string, roleId: string): Promise<void> {
    await $fetch(`/api/admin/users/${userId}/roles/${roleId}`, {
      method: 'DELETE',
      headers: getAuthHeaders(),
    })
  }

  async function listAuditLogs(params?: { action?: string, userId?: string, resourceType?: string, from?: string, to?: string, limit?: number, offset?: number }): Promise<AuditLogPage> {
    const query = new URLSearchParams()
    if (params?.action) query.set('action', params.action)
    if (params?.userId) query.set('userId', params.userId)
    if (params?.resourceType) query.set('resourceType', params.resourceType)
    if (params?.from) query.set('from', params.from)
    if (params?.to) query.set('to', params.to)
    if (params?.limit) query.set('limit', String(params.limit))
    if (params?.offset) query.set('offset', String(params.offset))
    const qs = query.toString()
    return await $fetch(`/api/admin/audit${qs ? `?${qs}` : ''}`, { headers: getAuthHeaders() })
  }

  return { listRoles, createRole, updateRole, deleteRole, listUsers, getUserRoles, assignRole, removeRole, listAuditLogs }
}
