// User info type returned from /api/auth/me
export interface UserInfo {
  id: string;
  email: string;
  name: string;
  provider: string;
  avatarUrl: string;
}

// OIDC provider info from /api/auth/providers
export interface OidcProvider {
  name: string;
  displayName: string;
  icon: string;
}

// Dev bypass status from /api/auth/dev-status
export interface DevBypassStatus {
  enabled: boolean;
  environment: string;
}

export const useAuth = () => {
  // Session-based auth (OIDC) - checked via cookie on server
  const isAuthenticated = useState<boolean>("auth-authenticated", () => false);
  const user = useState<UserInfo | null>("auth-user", () => null);
  const providers = useState<OidcProvider[]>("auth-providers", () => []);
  const isLoading = useState<boolean>("auth-loading", () => true);
  const devBypassEnabled = useState<boolean>("auth-dev-bypass", () => false);

  // API token for Terraform CLI operations (stored in cookie)
  const apiToken = useCookie<string | null>("auth-token", {
    default: () => null,
    secure: true,
    sameSite: "strict",
  });

  // Check session status on init
  const checkSession = async () => {
    isLoading.value = true;
    try {
      const response = await $fetch<{ authenticated: boolean }>(
        "/api/auth/session"
      );
      isAuthenticated.value = response.authenticated;

      if (response.authenticated) {
        await fetchUser();
      }
    } catch {
      isAuthenticated.value = false;
      user.value = null;
    } finally {
      isLoading.value = false;
    }
  };

  // Fetch current user info
  const fetchUser = async () => {
    try {
      const userInfo = await $fetch<UserInfo>("/api/auth/me");
      user.value = userInfo;
    } catch {
      user.value = null;
    }
  };

  // Fetch available OIDC providers
  const fetchProviders = async () => {
    try {
      const providerList = await $fetch<OidcProvider[]>("/api/auth/providers");
      providers.value = providerList;
    } catch {
      providers.value = [];
    }
  };

  // Probe whether dev bypass is available. If enabled, also logs in (POST creates a session).
  const checkDevBypass = async () => {
    try {
      await $fetch("/api/auth/dev-login", { method: "POST" });
      // 200 — bypass is enabled, session was created
      devBypassEnabled.value = true;
      isAuthenticated.value = true;
      await fetchUser();
      return true;
    } catch (error: any) {
      // 400 = endpoint exists but not enabled, 404 = not available (production)
      devBypassEnabled.value = false;
      return false;
    }
  };

  // Login via dev bypass
  const loginDevBypass = async (): Promise<boolean> => {
    try {
      await $fetch("/api/auth/dev-login", { method: "POST" });
      isAuthenticated.value = true;
      await fetchUser();
      return true;
    } catch {
      return false;
    }
  };

  // Initiate OIDC login flow
  const loginWithOidc = (provider: string) => {
    window.location.href = `/api/auth/login/${provider}`;
  };

  // Legacy API token login (for admin/fallback)
  const loginWithToken = (token: string) => {
    apiToken.value = token;
  };

  // Logout (clears session cookie)
  const logout = async () => {
    try {
      await $fetch("/api/auth/logout", { method: "POST" });
    } catch {
      // Ignore errors
    }
    user.value = null;
    isAuthenticated.value = false;
    apiToken.value = null;
    navigateTo("/login");
  };

  // Get auth headers for API calls (uses API token, not session)
  const getAuthHeaders = () => {
    return {
      Authorization: `Bearer ${apiToken.value}`,
    };
  };

  // Check if any OIDC providers are available
  const hasOidcProviders = computed(() => providers.value.length > 0);

  return {
    // State
    isAuthenticated: readonly(isAuthenticated),
    user: readonly(user),
    providers: readonly(providers),
    isLoading: readonly(isLoading),
    apiToken: readonly(apiToken),
    devBypassEnabled: readonly(devBypassEnabled),
    hasOidcProviders,

    // Actions
    checkSession,
    fetchUser,
    fetchProviders,
    checkDevBypass,
    loginDevBypass,
    loginWithOidc,
    loginWithToken,
    logout,
    getAuthHeaders,
  };
};

