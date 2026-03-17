import { useAuth } from './useAuth'

export function usePermissions() {
  const { permissions } = useAuth()

  const hasPermission = (p: string): boolean => permissions.value.includes(p)
  const hasAnyPermission = (...ps: string[]): boolean => ps.some(p => permissions.value.includes(p))
  const isAdmin = computed(() => hasAnyPermission('admin.roles', 'admin.users', 'admin.audit'))

  return { hasPermission, hasAnyPermission, isAdmin, permissions }
}
