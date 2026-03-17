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

  return { listRoles, createRole, updateRole, deleteRole, listUsers, getUserRoles, assignRole, removeRole }
}
