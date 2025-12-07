<template>
  <div class="min-h-screen bg-slate-900 flex items-center justify-center px-4">
    <div class="text-center">
      <div
        class="w-16 h-16 mx-auto mb-4 bg-blue-600 rounded-xl flex items-center justify-center"
      >
        <UIcon
          v-if="!hasError"
          name="i-lucide-loader-2"
          class="text-2xl text-white animate-spin"
        />
        <UIcon v-else name="i-lucide-x-circle" class="text-2xl text-white" />
      </div>

      <h1 class="text-2xl font-bold text-slate-100 mb-2">
        {{ hasError ? "Authentication Failed" : "Completing Sign In..." }}
      </h1>

      <p class="text-slate-400 mb-6">
        {{
          hasError
            ? "There was a problem signing you in."
            : "Please wait while we complete your authentication."
        }}
      </p>

      <UButton v-if="hasError" to="/login" color="primary" size="lg">
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
