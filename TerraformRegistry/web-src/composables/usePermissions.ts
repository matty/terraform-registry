import { useAuth } from './useAuth'
import { useImpersonation } from './useImpersonation'

export function usePermissions() {
  const { permissions: realPermissions } = useAuth()
  const { impersonatedUser, isImpersonating } = useImpersonation()

  const effectivePermissions = computed(() =>
    isImpersonating.value
      ? impersonatedUser.value?.permissions ?? []
      : realPermissions.value
  )

  const hasPermission = (p: string): boolean => effectivePermissions.value.includes(p)
  const hasAnyPermission = (...ps: string[]): boolean => ps.some(p => effectivePermissions.value.includes(p))
  const isAdmin = computed(() => hasAnyPermission('admin.roles', 'admin.users', 'admin.audit'))
  const hasAdminSection = computed(() => hasAnyPermission(
    'admin.roles', 'admin.users', 'admin.audit',
    'webhooks.manage', 'vcs.manage',
  ))

  return { hasPermission, hasAnyPermission, isAdmin, hasAdminSection, permissions: effectivePermissions }
}
