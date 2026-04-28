export default defineNuxtRouteMiddleware(async (to) => {
  // Skip middleware for login and callback pages
  if (to.path === "/login" || to.path === "/callback") {
    return;
  }

  const { checkSession, isAuthenticated, apiToken, isLoading, loginDevBypass } = useAuth();

  // Wait for loading to complete if on client side
  if (import.meta.client && isLoading.value) {
    await checkSession();
  }

  // Check for either OIDC session or API token
  const hasSession = isAuthenticated.value;
  const hasApiToken = !!apiToken.value;

  // Not authenticated — try dev bypass before redirecting to login
  if (!hasSession && !hasApiToken) {
    if (import.meta.dev) {
      // This is a no-op in production
      const devBypassSuccess = await loginDevBypass();
      if (devBypassSuccess) {
        return;
      }
    }

    return navigateTo("/login");
  }
});

