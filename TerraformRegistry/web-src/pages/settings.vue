<script setup lang="ts">
import { useDashboard } from "~/composables/useDashboard";

definePageMeta({
  middleware: "auth",
});

const { isSidebarOpen } = useDashboard();

interface ApiKey {
  id: string;
  description: string;
  prefix: string;
  createdAt: string;
  lastUsedAt?: string;
  isShared: boolean;
  ownerDisplay?: string;
  ownerEmail?: string;
  ownerUsername?: string;
}

const apiKeys = ref<ApiKey[]>([]);
const sharedApiKeys = ref<ApiKey[]>([]);
const isLoading = ref(false);
const isLoadingShared = ref(false);
const newKeyDescription = ref("");
const newKeyShared = ref(false);
const generatedToken = ref<string | null>(null);
const isCreating = ref(false);

const fetchKeys = async () => {
  isLoading.value = true;
  try {
    const keys = await $fetch<ApiKey[]>("/api/keys");
    apiKeys.value = keys;
  } catch (e) {
    console.error("Failed to fetch keys", e);
  } finally {
    isLoading.value = false;
  }
};

const fetchSharedKeys = async () => {
  isLoadingShared.value = true;
  try {
    const keys = await $fetch<ApiKey[]>("/api/keys/shared");
    sharedApiKeys.value = keys;
  } catch (e) {
    console.error("Failed to fetch shared keys", e);
  } finally {
    isLoadingShared.value = false;
  }
};

const createKey = async () => {
  if (!newKeyDescription.value) return;
  isCreating.value = true;
  try {
    const response = await $fetch<{ token: string; apiKey: ApiKey }>(
      "/api/keys",
      {
        method: "POST",
        body: {
          description: newKeyDescription.value,
          isShared: newKeyShared.value,
        },
      }
    );
    generatedToken.value = response.token;
    newKeyDescription.value = "";
    newKeyShared.value = false;
    await fetchKeys();
    await fetchSharedKeys();
  } catch (e) {
    console.error("Failed to create key", e);
  } finally {
    isCreating.value = false;
  }
};

const isRevokeModalOpen = ref(false);
const keyToRevoke = ref<string | null>(null);

const revokeKey = (id: string) => {
  keyToRevoke.value = id;
  isRevokeModalOpen.value = true;
};

const confirmRevokeKey = async () => {
  if (!keyToRevoke.value) return;
  try {
    await $fetch(`/api/keys/${keyToRevoke.value}`, { method: "DELETE" });
    await fetchKeys();
    await fetchSharedKeys();
  } catch (e) {
    console.error("Failed to revoke key", e);
  } finally {
    isRevokeModalOpen.value = false;
    keyToRevoke.value = null;
  }
};

const updateKey = async (
  key: ApiKey,
  payload: { description?: string; isShared?: boolean }
) => {
  try {
    await $fetch<ApiKey>(`/api/keys/${key.id}`, {
      method: "PUT",
      body: {
        description: payload.description ?? key.description,
        isShared: payload.isShared ?? key.isShared,
      },
    });
    await Promise.all([fetchKeys(), fetchSharedKeys()]);
  } catch (e) {
    console.error("Failed to update key", e);
  }
};

const copyToken = () => {
  if (generatedToken.value) {
    navigator.clipboard.writeText(generatedToken.value);
    // Optional: Show toast
  }
};

const isDeleteModalOpen = ref(false);
const isKeysWarningModalOpen = ref(false);

const confirmDeleteAccount = () => {
  if (apiKeys.value.length > 0) {
    isKeysWarningModalOpen.value = true;
    return;
  }
  isDeleteModalOpen.value = true;
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

onMounted(() => {
  fetchKeys();
  fetchSharedKeys();
});
</script>

<template>
  <div class="flex flex-col h-full">
    <!-- Header -->
    <header
      class="flex items-center gap-3 px-4 py-3 border-b border-neutral-200 dark:border-neutral-800 bg-white/50 dark:bg-neutral-900/50 backdrop-blur sticky top-0 z-10"
    >
      <UButton
        icon="i-lucide-menu"
        variant="ghost"
        color="neutral"
        class="lg:hidden"
        @click="isSidebarOpen = true"
      />
      <h1 class="text-xl font-semibold text-slate-900 dark:text-slate-100">
        Settings
      </h1>
    </header>

    <!-- Body -->
    <div class="p-4 flex-1 overflow-y-auto">
      <div class="max-w-4xl mx-auto space-y-6">
        <!-- API Keys Section -->
        <UCard>
          <template #header>
            <div class="flex items-center justify-between">
              <div class="flex items-center gap-3">
                <UIcon name="i-lucide-key" class="text-xl text-slate-400" />
                <div>
                  <h2 class="font-semibold text-slate-100">API Keys</h2>
                  <p class="text-sm text-slate-400">
                    Manage your personal access tokens for the Terraform CLI.
                  </p>
                </div>
              </div>
            </div>
          </template>

          <!-- Create Key Form -->
          <div
            class="mb-6 p-4 bg-slate-50 dark:bg-slate-800/50 rounded-lg border border-slate-200 dark:border-slate-700"
          >
            <h3
              class="text-sm font-medium mb-3 text-slate-700 dark:text-slate-300"
            >
              Generate New Key
            </h3>
            <div class="flex flex-col gap-2">
              <div class="flex gap-2">
                <UInput
                  v-model="newKeyDescription"
                  placeholder="Key Description (e.g. Laptop CLI)"
                  class="flex-1"
                  @keyup.enter="createKey"
                />
                <UButton
                  label="Generate"
                  color="primary"
                  :loading="isCreating"
                  :disabled="!newKeyDescription"
                  @click="createKey"
                />
              </div>
              <label
                class="flex items-center gap-2 text-sm text-slate-600 dark:text-slate-300"
              >
                <input
                  type="checkbox"
                  v-model="newKeyShared"
                  class="accent-blue-500"
                />
                Shared
              </label>
            </div>
          </div>

          <!-- Generated Token Alert -->
          <div
            v-if="generatedToken"
            class="mb-6 p-4 bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-800 rounded-lg"
          >
            <div class="flex items-start gap-3">
              <UIcon
                name="i-lucide-check-circle"
                class="text-green-500 text-xl mt-0.5"
              />
              <div class="flex-1">
                <h3 class="font-medium text-green-800 dark:text-green-200">
                  API Key Generated
                </h3>
                <p class="text-sm text-green-700 dark:text-green-300 mt-1">
                  Make sure to copy your personal access token now. You won't be
                  able to see it again!
                </p>
                <div class="mt-3 flex items-center gap-2">
                  <code
                    class="flex-1 p-2 bg-white dark:bg-slate-900 rounded border border-green-200 dark:border-green-800 font-mono text-sm break-all"
                  >
                    {{ generatedToken }}
                  </code>
                  <UButton
                    icon="i-lucide-copy"
                    color="neutral"
                    variant="soft"
                    size="sm"
                    @click="copyToken"
                  />
                </div>
              </div>
              <UButton
                icon="i-lucide-x"
                color="neutral"
                variant="ghost"
                size="sm"
                @click="generatedToken = null"
              />
            </div>
          </div>

          <!-- Keys List -->
          <div v-if="isLoading" class="py-8 text-center">
            <UIcon
              name="i-lucide-loader-2"
              class="animate-spin text-2xl text-slate-400"
            />
          </div>

          <div
            v-else-if="apiKeys.length === 0"
            class="py-8 text-center text-slate-400"
          >
            <p>No API keys found. Generate one to get started.</p>
          </div>

          <div v-else class="space-y-1">
            <div
              v-for="key in apiKeys"
              :key="key.id"
              class="flex items-center justify-between p-3 rounded-lg hover:bg-slate-50 dark:hover:bg-slate-800/50 transition-colors"
            >
              <div class="min-w-0">
                <div
                  class="font-medium text-slate-900 dark:text-slate-100 truncate"
                >
                  {{ key.description }}
                </div>
                <div
                  class="text-xs text-slate-500 flex items-center gap-3 mt-1"
                >
                  <span
                    class="font-mono bg-slate-100 dark:bg-slate-800 px-1.5 py-0.5 rounded"
                  >
                    {{ key.prefix }}...
                  </span>
                  <span
                    class="flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px]"
                    :class="
                      key.isShared
                        ? 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-200'
                        : 'bg-slate-100 text-slate-600 dark:bg-slate-800 dark:text-slate-300'
                    "
                  >
                    <UIcon
                      :name="key.isShared ? 'i-lucide-users' : 'i-lucide-user'"
                      class="text-[13px]"
                    />
                    {{ key.isShared ? "Shared" : "Private" }}
                  </span>
                  <span
                    >Created
                    {{ new Date(key.createdAt).toLocaleDateString() }}</span
                  >
                  <span v-if="key.lastUsedAt">
                    Last used
                    {{ new Date(key.lastUsedAt).toLocaleDateString() }}
                  </span>
                </div>
              </div>
              <div class="flex items-center gap-2">
                <label
                  class="flex items-center gap-2 text-xs text-slate-500 dark:text-slate-300"
                >
                  Shared
                  <input
                    type="checkbox"
                    :checked="key.isShared"
                    @change="(e: Event) => updateKey(key, { isShared: (e.target as HTMLInputElement).checked })"
                    class="accent-blue-500"
                  />
                </label>
                <UButton
                  icon="i-lucide-trash-2"
                  color="error"
                  variant="ghost"
                  size="sm"
                  @click="revokeKey(key.id)"
                />
              </div>
            </div>
          </div>
        </UCard>

        <!-- Shared API Keys -->
        <UCard>
          <template #header>
            <div class="flex items-center justify-between">
              <div class="flex items-center gap-3">
                <UIcon name="i-lucide-users" class="text-xl text-slate-400" />
                <div>
                  <h2 class="font-semibold text-slate-100">Shared API Keys</h2>
                  <p class="text-sm text-slate-400">
                    Keys marked as shared are visible to everyone with ownership
                    shown.
                  </p>
                </div>
              </div>
            </div>
          </template>

          <div v-if="isLoadingShared" class="py-4 text-center text-slate-400">
            Loading shared keys...
          </div>
          <div
            v-else-if="sharedApiKeys.length === 0"
            class="py-4 text-center text-slate-400"
          >
            No shared API keys yet.
          </div>
          <div v-else class="space-y-1">
            <div
              v-for="key in sharedApiKeys"
              :key="key.id"
              class="p-3 rounded-lg border border-slate-200 dark:border-slate-800 bg-slate-50/70 dark:bg-slate-800/40"
            >
              <div class="flex items-center justify-between">
                <div class="min-w-0">
                  <div
                    class="font-medium text-slate-900 dark:text-slate-100 truncate"
                  >
                    {{ key.description }}
                  </div>
                  <div
                    class="text-xs text-slate-500 flex items-center gap-3 mt-1"
                  >
                    <span
                      class="font-mono bg-slate-100 dark:bg-slate-800 px-1.5 py-0.5 rounded"
                    >
                      {{ key.prefix }}...
                    </span>
                    <span
                      class="flex items-center gap-1 text-blue-700 dark:text-blue-200"
                    >
                      <UIcon name="i-lucide-user" class="text-[13px]" />
                      {{
                        key.ownerDisplay ||
                        key.ownerUsername ||
                        key.ownerEmail ||
                        "Unknown owner"
                      }}
                    </span>
                    <span
                      >Created
                      {{ new Date(key.createdAt).toLocaleDateString() }}</span
                    >
                  </div>
                </div>
              </div>
            </div>
          </div>
        </UCard>

        <!-- Danger Zone -->
        <div
          class="mt-8 pt-8 border-t border-neutral-200 dark:border-neutral-800"
        >
          <h2 class="text-lg font-semibold text-red-600 dark:text-red-400 mb-4">
            Danger Zone
          </h2>
          <div
            class="bg-red-50 dark:bg-red-900/10 border border-red-200 dark:border-red-900/50 rounded-lg p-4 flex items-center justify-between"
          >
            <div>
              <h3 class="font-medium text-red-900 dark:text-red-200">
                Delete Account
              </h3>
              <p class="text-sm text-red-700 dark:text-red-300 mt-1">
                Permanently delete your account and all associated data. You
                must delete all API keys first.
              </p>
            </div>
            <UButton
              color="error"
              variant="solid"
              label="Delete Account"
              @click="confirmDeleteAccount"
            />
          </div>
        </div>

        <!-- Keys Warning Modal -->
        <UModal v-model:open="isKeysWarningModalOpen">
          <template #content>
            <UCard>
              <template #header>
                <div class="flex items-center gap-2 text-amber-600">
                  <UIcon name="i-lucide-alert-circle" class="text-xl" />
                  <h3 class="font-semibold">Cannot Delete Account</h3>
                </div>
              </template>

              <p class="text-slate-600 dark:text-slate-300">
                You must delete all API keys before you can delete your account.
                Please revoke all active keys and try again.
              </p>

              <template #footer>
                <div class="flex justify-end">
                  <UButton
                    color="neutral"
                    label="Close"
                    @click="isKeysWarningModalOpen = false"
                  />
                </div>
              </template>
            </UCard>
          </template>
        </UModal>

        <!-- Revoke Key Confirmation Modal -->
        <UModal v-model:open="isRevokeModalOpen">
          <template #content>
            <UCard>
              <template #header>
                <div class="flex items-center gap-2 text-red-600">
                  <UIcon name="i-lucide-trash-2" class="text-xl" />
                  <h3 class="font-semibold">Revoke API Key</h3>
                </div>
              </template>

              <p class="text-slate-600 dark:text-slate-300">
                Are you sure you want to revoke this API key? This action cannot
                be undone.
              </p>

              <template #footer>
                <div class="flex justify-end gap-2">
                  <UButton
                    color="neutral"
                    variant="ghost"
                    label="Cancel"
                    @click="isRevokeModalOpen = false"
                  />
                  <UButton
                    color="error"
                    label="Revoke Key"
                    @click="confirmRevokeKey"
                  />
                </div>
              </template>
            </UCard>
          </template>
        </UModal>

        <!-- Delete Confirmation Modal -->
        <UModal v-model:open="isDeleteModalOpen">
          <template #content>
            <UCard>
              <template #header>
                <div class="flex items-center gap-2 text-red-600">
                  <UIcon name="i-lucide-alert-triangle" class="text-xl" />
                  <h3 class="font-semibold">Delete Account</h3>
                </div>
              </template>

              <p class="text-slate-600 dark:text-slate-300">
                Are you sure you want to delete your account? This action cannot
                be undone.
              </p>

              <template #footer>
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
              </template>
            </UCard>
          </template>
        </UModal>
      </div>
    </div>
  </div>
</template>
