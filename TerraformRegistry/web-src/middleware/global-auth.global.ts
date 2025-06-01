export default defineNuxtRouteMiddleware((to) => {
  // Skip middleware for login page
  if (to.path === '/login') {
    return
  }

  const { isAuthenticated } = useAuth()
  
  // If not authenticated and trying to access protected routes, redirect to login
  if (!isAuthenticated.value && to.path !== '/') {
    return navigateTo('/login')
  }
  
  // If authenticated and on root, redirect to modules
  if (isAuthenticated.value && to.path === '/') {
    return navigateTo('/modules')
  }
})
