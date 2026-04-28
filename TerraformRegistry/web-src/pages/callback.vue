<template>
  <div class="min-h-screen bg-neutral-950 flex items-center justify-center px-4">
    <div class="text-center">
      <div class="w-12 h-12 mx-auto mb-6">
        <UIcon
          v-if="!hasError"
          name="i-lucide-loader-2"
          class="text-4xl text-neutral-400 animate-spin"
        />
        <UIcon v-else name="i-lucide-x-circle" class="text-4xl text-red-400" />
      </div>

      <h1 class="text-2xl font-bold text-white mb-2">
        {{ hasError ? "Authentication Failed" : "Completing Sign In..." }}
      </h1>

      <p class="text-neutral-400 mb-6">
        {{
          hasError
            ? "There was a problem signing you in."
            : "Please wait while we complete your authentication."
        }}
      </p>

      <UButton v-if="hasError" to="/login" color="neutral" variant="solid" size="lg">
        Back to Login
      </UButton>
    </div>
  </div>
</template>

<script setup lang="ts">
definePageMeta({
  layout: false,
});

const { checkSession, isAuthenticated } = useAuth();
const hasError = ref(false);

onMounted(async () => {
  try {
    // Check session after OAuth callback
    await checkSession();

    if (isAuthenticated.value) {
      // Successfully authenticated, redirect to home
      await navigateTo("/");
    } else {
      // No session found, likely an error
      hasError.value = true;
    }
  } catch (error) {
    hasError.value = true;
  }
});
</script>
