<script setup lang="ts">
import { useDashboard } from "~/composables/useDashboard";

definePageMeta({
  middleware: "auth",
});

const { isSidebarOpen } = useDashboard();

const isDeleteModalOpen = ref(false);
const isKeysWarningModalOpen = ref(false);

// Check for existing keys before allowing deletion
const checkAndDelete = async () => {
  try {
    const keys = await $fetch<any[]>("/api/keys");
    if (keys.length > 0) {
      isKeysWarningModalOpen.value = true;
      return;
    }
    isDeleteModalOpen.value = true;
  } catch (e) {
    isDeleteModalOpen.value = true;
  }
};

const deleteAccount = async () => {
  try {
    await $fetch("/api/auth/me", { method: "DELETE" });
    window.location.href = "/";
  } catch (e: any) {
    console.error("Failed to delete account", e);
    if (e.statusCode === 409) {
      isDeleteModalOpen.value = false;
      isKeysWarningModalOpen.value = true;
    } else {
      alert(e.data?.error || "Failed to delete account");
    }
  } finally {
    isDeleteModalOpen.value = false;
  }
};
</script>

<template>
  <div class="flex flex-col h-full">
    <!-- Mobile menu button -->
    <div class="lg:hidden px-4 pt-4">
      <UButton
        icon="i-lucide-menu"
        variant="ghost"
        color="neutral"
        @click="isSidebarOpen = true"
      />
    </div>

    <!-- Page Header -->
    <div class="page-header">
      <h1 class="page-header-title">Account</h1>
      <p class="page-header-subtitle">Manage your account settings</p>
    </div>
    <div class="page-divider" />

    <!-- Body -->
    <div class="flex-1 overflow-y-auto px-6 py-6">
      <div class="max-w-4xl space-y-8">

        <!-- Account Info (placeholder for future expansion) -->
        <div class="p-5 bg-neutral-900/40 rounded-xl border border-neutral-800">
          <h2 class="text-base font-semibold text-neutral-200 mb-1">Profile</h2>
          <p class="text-sm text-neutral-500">
            Your account is managed through your authentication provider.
          </p>
        </div>

        <!-- Danger Zone -->
        <div>
          <h2 class="text-base font-semibold text-red-400 mb-3 flex items-center gap-2">
            <UIcon name="i-lucide-alert-triangle" class="text-red-500" />
            Danger Zone
          </h2>
          <div
            class="bg-red-900/10 border border-red-900/30 rounded-xl p-5 flex items-center justify-between"
          >
            <div>
              <h3 class="font-medium text-red-200">
                Delete Account
              </h3>
              <p class="text-sm text-red-300/70 mt-1">
                Permanently delete your account and all associated data. You must delete all API keys first.
              </p>
            </div>
            <UButton
              color="error"
              variant="solid"
              label="Delete Account"
              @click="checkAndDelete"
            />
          </div>
        </div>
      </div>
    </div>

    <!-- Keys Warning Modal -->
    <UModal v-model:open="isKeysWarningModalOpen">
      <template #content>
        <div class="p-6">
          <div class="flex items-center gap-3 mb-4">
            <div class="w-12 h-12 rounded-xl bg-amber-600/20 flex items-center justify-center">
              <UIcon name="i-lucide-alert-circle" class="text-2xl text-amber-500" />
            </div>
            <div>
              <h3 class="text-lg font-semibold text-neutral-100">Cannot Delete Account</h3>
            </div>
          </div>
          <p class="text-neutral-300 mb-6">
            You must revoke all API keys before you can delete your account.
            Go to <NuxtLink to="/settings/api-keys" class="text-primary-400 hover:underline" @click="isKeysWarningModalOpen = false">API Keys</NuxtLink> to manage them.
          </p>
          <div class="flex justify-end">
            <UButton
              color="neutral"
              label="Close"
              @click="isKeysWarningModalOpen = false"
            />
          </div>
        </div>
      </template>
    </UModal>

    <!-- Delete Confirmation Modal -->
    <UModal v-model:open="isDeleteModalOpen">
      <template #content>
        <div class="p-6">
          <div class="flex items-center gap-3 mb-4">
            <div class="w-12 h-12 rounded-xl bg-red-600/20 flex items-center justify-center">
              <UIcon name="i-lucide-alert-triangle" class="text-2xl text-red-500" />
            </div>
            <div>
              <h3 class="text-lg font-semibold text-neutral-100">Delete Account</h3>
              <p class="text-sm text-neutral-400">This action cannot be undone</p>
            </div>
          </div>
          <p class="text-neutral-300 mb-6">
            Are you sure you want to delete your account? All your data will be permanently removed.
          </p>
          <div class="flex justify-end gap-2">
            <UButton
              color="neutral"
              variant="ghost"
              label="Cancel"
              @click="isDeleteModalOpen = false"
            />
            <UButton
              color="error"
              label="Delete Account"
              @click="deleteAccount"
            />
          </div>
        </div>
      </template>
    </UModal>
  </div>
</template>
