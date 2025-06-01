export default defineNuxtRouteMiddleware((to) => {
  const { isAuthenticated } = useAuth()
  
  if (!isAuthenticated.value) {
    return navigateTo('/login')
  }
})
