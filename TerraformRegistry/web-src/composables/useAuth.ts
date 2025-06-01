export const useAuth = () => {
  const token = useCookie<string | null>('auth-token', {
    default: () => null,
    secure: true,
    sameSite: 'strict'
  })

  const isAuthenticated = computed(() => {
    return !!token.value
  })

  const login = (authToken: string) => {
    token.value = authToken
  }

  const logout = () => {
    token.value = null
    navigateTo('/login')
  }

  const getAuthHeaders = () => {
    return {
      'Authorization': `Bearer ${token.value}`
    }
  }

  return {
    token: readonly(token),
    isAuthenticated: readonly(isAuthenticated),
    login,
    logout,
    getAuthHeaders
  }
}
