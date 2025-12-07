export default defineNuxtRouteMiddleware(async (to) => {
  // Skip middleware for login and callback pages
  if (to.path === "/login" || to.path === "/callback") {
    return;
  }

  const { checkSession, isAuthenticated, apiToken, isLoading } = useAuth();

  // Wait for loading to complete if on client side
  if (import.meta.client && isLoading.value) {
    await checkSession();
  }

  // Check for either OIDC session or API token
  const hasSession = isAuthenticated.value;
  const hasApiToken = !!apiToken.value;

  // If not authenticated, redirect to login
  if (!hasSession && !hasApiToken) {
    return navigateTo("/login");
  }
});
