<template>
  <div class="min-h-screen flex items-center justify-center px-4">
    <div class="max-w-md w-full">
      <!-- Header -->
      <div class="text-center mb-8">
        <h1 class="text-3xl font-bold text-white mb-2">Welcome</h1>
        <p class="text-neutral-400">Sign in to access your Terraform Registry</p>
      </div>

      <!-- Error Alert -->
      <UAlert
        v-if="errorMessage"
        color="error"
        variant="soft"
        :title="errorMessage"
        icon="i-lucide-alert-circle"
        class="mb-4"
      />

      <!-- Login Card -->
      <UCard>
        <!-- Loading State -->
        <div v-if="isLoadingProviders" class="flex justify-center py-8">
          <UIcon
            name="i-lucide-loader-2"
            class="animate-spin text-3xl text-neutral-400"
          />
        </div>

        <!-- OIDC Providers -->
        <div v-else-if="hasOidcProviders" class="space-y-4">
          <UButton
            v-for="provider in providers"
            :key="provider.name"
            :loading="isLoading && selectedProvider === provider.name"
            :disabled="isLoading"
            class="w-full justify-center font-medium"
            size="xl"
            color="neutral"
            variant="solid"
            @click="handleOidcLogin(provider.name)"
          >
            <UIcon :name="provider.icon" class="text-xl mr-2" />
            Continue with {{ provider.displayName }}
          </UButton>
        </div>

        <!-- No providers available -->
        <div v-else-if="!devBypassEnabled" class="text-center py-8">
          <UIcon
            name="i-lucide-triangle-alert"
            class="text-4xl text-amber-500 mb-4"
          />
          <p class="text-neutral-400">No authentication providers configured.</p>
        </div>

        <!-- Dev Bypass Login -->
        <div v-if="devBypassEnabled" class="mt-4">
          <div v-if="hasOidcProviders" class="flex items-center gap-3 my-4">
            <div class="flex-1 h-px bg-neutral-700" />
            <span class="text-xs text-neutral-500 uppercase">or</span>
            <div class="flex-1 h-px bg-neutral-700" />
          </div>
          <UButton
            :loading="isDevLoggingIn"
            class="w-full justify-center font-medium"
            size="xl"
            color="warning"
            variant="soft"
            @click="handleDevLogin"
          >
            <UIcon name="i-lucide-bug" class="text-xl mr-2" />
            Dev Bypass Login
          </UButton>
        </div>
      </UCard>
    </div>
  </div>
</template>

<script setup lang="ts">
definePageMeta({
  layout: false,
});

const route = useRoute();
const {
  loginWithOidc,
  loginDevBypass,
  checkDevBypass,
  isAuthenticated,
  fetchProviders,
  providers,
  hasOidcProviders,
  checkSession,
  devBypassEnabled,
} = useAuth();

const isLoading = ref(false);
const isLoadingProviders = ref(true);
const isDevLoggingIn = ref(false);
const selectedProvider = ref<string | null>(null);
const errorMessage = ref("");

// Handle error from OAuth callback
const errorParam = route.query.error as string | undefined;
if (errorParam) {
  const errorMessages: Record<string, string> = {
    oauth_denied: "Authentication was denied or cancelled",
    invalid_state: "Invalid authentication state. Please try again.",
    no_code: "No authorization code received",
    exchange_failed: "Failed to complete authentication. Please try again.",
    account_link_required: "This email is already linked to a different sign-in method. Contact an administrator to link your account.",
  };
  errorMessage.value = errorMessages[errorParam] || "Authentication failed";
}

// Fetch OIDC providers on mount
onMounted(async () => {
  await fetchProviders();

  // Probe dev bypass — if enabled, this also logs in automatically
  const devLoggedIn = await checkDevBypass();

  isLoadingProviders.value = false;

  // If dev bypass already authenticated, redirect immediately
  if (devLoggedIn && isAuthenticated.value) {
    navigateTo("/");
    return;
  }

  // Otherwise check for existing session (OIDC cookie, etc.)
  await checkSession();
  if (isAuthenticated.value) {
    navigateTo("/");
  }
});

const handleOidcLogin = (provider: string) => {
  isLoading.value = true;
  selectedProvider.value = provider;
  errorMessage.value = "";
  loginWithOidc(provider);
};

const handleDevLogin = async () => {
  isDevLoggingIn.value = true;
  errorMessage.value = "";
  try {
    const success = await loginDevBypass();
    if (success) {
      navigateTo("/");
    } else {
      errorMessage.value = "Dev bypass login failed";
    }
  } catch {
    errorMessage.value = "Dev bypass login failed";
  } finally {
    isDevLoggingIn.value = false;
  }
};
</script>
