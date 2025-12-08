<template>
  <div class="min-h-screen flex items-center justify-center px-4">
    <div class="max-w-md w-full">
      <!-- Header -->
      <div class="text-center mb-8">
        <div
          class="w-16 h-16 mx-auto mb-6 bg-neutral-800 rounded-2xl flex items-center justify-center"
        >
          <UIcon name="i-lucide-box" class="text-3xl text-white" />
        </div>
        <h1 class="text-3xl font-bold text-slate-100 mb-2">Welcome</h1>
        <p class="text-slate-400">Sign in to access your Terraform Registry</p>
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
            class="animate-spin text-3xl text-blue-500"
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
        <div v-else class="text-center py-8">
          <UIcon
            name="i-lucide-triangle-alert"
            class="text-4xl text-amber-500 mb-4"
          />
          <p class="text-slate-400">No authentication providers configured.</p>
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
  isAuthenticated,
  fetchProviders,
  providers,
  hasOidcProviders,
  checkSession,
} = useAuth();

const isLoading = ref(false);
const isLoadingProviders = ref(true);
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
  };
  errorMessage.value = errorMessages[errorParam] || "Authentication failed";
}

// Fetch OIDC providers on mount
onMounted(async () => {
  await fetchProviders();
  isLoadingProviders.value = false;

  // Check if already authenticated
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
</script>
