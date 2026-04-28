<script setup lang="ts">
definePageMeta({
  middleware: "auth",
});

// Redirect /settings to /settings/api-keys
navigateTo("/settings/api-keys", { replace: true });
</script>

<template>
  <div></div>
</template>
