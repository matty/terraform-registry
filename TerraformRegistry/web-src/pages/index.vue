<template>
  <div
    class="min-h-screen bg-gradient-to-br from-blue-50 to-indigo-100 dark:from-gray-900 dark:to-gray-800 flex items-center justify-center"
  >
    <div class="max-w-md w-full mx-4 fade-in">
      <UCard>
        <div class="text-center p-8">
          <div
            class="w-20 h-20 mx-auto mb-6 bg-gradient-to-br from-blue-500 to-indigo-600 rounded-2xl flex items-center justify-center shadow-lg slide-in"
          >
            <Icon
              name="material-symbols:engineering"
              class="text-3xl text-white"
            />
          </div>

          <h1 class="text-3xl font-bold text-gray-900 dark:text-white mb-3">
            Terraform Registry
          </h1>

          <UButton
            to="/login"
            size="lg"
            class="w-full"
            color="primary"
            variant="solid"
          >
            Sign In to Continue
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

// Redirect if already authenticated
const { isAuthenticated } = useAuth();
if (isAuthenticated.value) {
  navigateTo("/modules");
}
</script>
