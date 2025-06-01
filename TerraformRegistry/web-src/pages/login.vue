<template>
  <div
    class="min-h-screen bg-gradient-to-br from-blue-50 to-indigo-100 dark:from-gray-900 dark:to-gray-800 flex items-center justify-center px-4"
  >
    <div class="max-w-md w-full fade-in">
      <div class="text-center mb-8 slide-in">
        <div
          class="w-16 h-16 mx-auto mb-4 bg-gradient-to-br from-blue-500 to-indigo-600 rounded-xl flex items-center justify-center shadow-lg"
        >
          <Icon name="material-symbols:lock" class="text-2xl text-white" />
        </div>
        <h1 class="text-3xl font-bold text-gray-900 dark:text-white mb-2">
          Welcome
        </h1>
        <p class="text-gray-600 dark:text-gray-400">
          Sign in to access your Terraform Registry
        </p>
      </div>
      <UCard>
        <div class="p-6">
          <form @submit.prevent="handleLogin" class="space-y-6">
            <UFormGroup label="API Token" name="token" required>
              <UInput
                v-model="apiToken"
                type="password"
                placeholder="Enter your API token"
                size="lg"
                required
                :loading="isLoading"
                class="w-full"
              />
            </UFormGroup>
            <UAlert
              v-if="error"
              color="error"
              variant="soft"
              :title="error"
              class="mb-4"
            />

            <UButton
              type="submit"
              :loading="isLoading"
              :disabled="!apiToken.trim()"
              class="w-full"
              size="lg"
              color="primary"
            >
              Sign In
            </UButton>
          </form>
        </div>
      </UCard>

      <div class="text-center mt-6">
        <UButton to="/" variant="ghost" color="neutral" size="sm">
          ← Back to Home
        </UButton>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
definePageMeta({
  layout: false,
});

const { login } = useAuth();
const router = useRouter();

const apiToken = ref("");
const isLoading = ref(false);
const error = ref("");

const handleLogin = async () => {
  if (!apiToken.value.trim()) {
    error.value = "Please enter a valid API token";
    return;
  }

  isLoading.value = true;
  error.value = "";

  try {
    // Simulate a brief loading state
    await new Promise((resolve) => setTimeout(resolve, 500));

    login(apiToken.value.trim());
    await router.push("/modules");
  } catch (err) {
    error.value = "An error occurred. Please try again.";
  } finally {
    isLoading.value = false;
  }
};

// Redirect if already authenticated
const { isAuthenticated } = useAuth();
if (isAuthenticated.value) {
  navigateTo("/modules");
}
</script>
