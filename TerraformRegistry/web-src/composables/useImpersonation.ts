export interface ImpersonatedUser {
  id: string
  email: string
  permissions: string[]
  roles: string[]
}

// Global state — shared across all components via useState
export function useImpersonation() {
  const impersonatedUser = useState<ImpersonatedUser | null>('impersonated-user', () => null)

  const isImpersonating = computed(() => impersonatedUser.value !== null)

  const startImpersonation = (user: ImpersonatedUser) => {
    impersonatedUser.value = user
  }

  const stopImpersonation = () => {
    impersonatedUser.value = null
  }

  return {
    impersonatedUser: readonly(impersonatedUser),
    isImpersonating,
    startImpersonation,
    stopImpersonation,
  }
}
