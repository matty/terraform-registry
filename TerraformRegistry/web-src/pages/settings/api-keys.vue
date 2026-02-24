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
  }
};

onMounted(() => {
  fetchKeys();
  fetchSharedKeys();
});
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
      <div class="flex items-center justify-between">
        <div>
          <h1 class="page-header-title">API Keys</h1>
          <p class="page-header-subtitle">Manage your personal access tokens for the Terraform CLI</p>
        </div>
      </div>
    </div>
    <div class="page-divider" />

    <!-- Body -->
    <div class="flex-1 overflow-y-auto px-6 py-6">
      <div class="max-w-4xl space-y-6">

        <!-- Create Key Form -->
        <div class="p-5 bg-neutral-900/60 rounded-xl border border-neutral-800 ring-1 ring-neutral-800/50">
          <h3 class="text-sm font-semibold mb-3 text-neutral-200 flex items-center gap-2">
            <UIcon name="i-lucide-plus-circle" class="text-primary-400" />
            Generate New Key
          </h3>
          <div class="flex flex-col gap-3">
            <div class="flex gap-2">
              <UInput
                v-model="newKeyDescription"
                placeholder="Key description (e.g. Laptop CLI)"
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
            <label class="flex items-center gap-2 text-sm text-neutral-400">
              <input
                type="checkbox"
                v-model="newKeyShared"
                class="accent-neutral-500 rounded"
              />
              Make this a shared key
            </label>
          </div>
        </div>

        <!-- Generated Token Alert -->
        <div
          v-if="generatedToken"
          class="p-4 bg-green-900/20 border border-green-800/50 rounded-xl"
        >
          <div class="flex items-start gap-3">
            <UIcon
              name="i-lucide-check-circle"
              class="text-green-500 text-xl mt-0.5"
            />
            <div class="flex-1">
              <h3 class="font-medium text-green-200">
                API Key Generated
              </h3>
              <p class="text-sm text-green-300/80 mt-1">
                Copy your access token now — you won't be able to see it again.
              </p>
              <div class="mt-3 flex items-center gap-2">
                <code
                  class="flex-1 p-2.5 bg-neutral-900 rounded-lg border border-green-800/40 font-mono text-sm break-all text-green-200"
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

        <!-- Your Keys -->
        <div>
          <h2 class="text-base font-semibold text-neutral-200 mb-3 flex items-center gap-2">
            <UIcon name="i-lucide-key-round" class="text-primary-400" />
            Your Keys
          </h2>

          <div v-if="isLoading" class="py-8 text-center">
            <UIcon
              name="i-lucide-loader-2"
              class="animate-spin text-2xl text-primary-400"
            />
          </div>

          <div
            v-else-if="apiKeys.length === 0"
            class="py-8 text-center text-neutral-500"
          >
            <p>No API keys found. Generate one to get started.</p>
          </div>

          <div v-else class="space-y-2">
            <div
              v-for="key in apiKeys"
              :key="key.id"
              class="flex items-center justify-between p-4 rounded-xl bg-neutral-900/40 border border-neutral-800 hover:border-neutral-700 transition-colors"
            >
              <div class="min-w-0">
                <div class="font-medium text-neutral-100 truncate">
                  {{ key.description }}
                </div>
                <div class="text-xs text-neutral-500 flex items-center gap-3 mt-1.5">
                  <span class="font-mono bg-neutral-800 px-1.5 py-0.5 rounded">
                    {{ key.prefix }}...
                  </span>
                  <span
                    class="flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px]"
                    :class="
                      key.isShared
                        ? 'bg-primary-900/40 text-primary-300'
                        : 'bg-neutral-800 text-neutral-400'
                    "
                  >
                    <UIcon
                      :name="key.isShared ? 'i-lucide-users' : 'i-lucide-user'"
                      class="text-[13px]"
                    />
                    {{ key.isShared ? "Shared" : "Private" }}
                  </span>
                  <span>Created {{ new Date(key.createdAt).toLocaleDateString() }}</span>
                  <span v-if="key.lastUsedAt">
                    Last used {{ new Date(key.lastUsedAt).toLocaleDateString() }}
                  </span>
                </div>
              </div>
              <div class="flex items-center gap-2">
                <label class="flex items-center gap-2 text-xs text-neutral-400">
                  Shared
                  <input
                    type="checkbox"
                    :checked="key.isShared"
                    @change="(e: Event) => updateKey(key, { isShared: (e.target as HTMLInputElement).checked })"
                    class="accent-neutral-500"
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
        </div>

        <!-- Shared Keys -->
        <div>
          <h2 class="text-base font-semibold text-neutral-200 mb-3 flex items-center gap-2">
            <UIcon name="i-lucide-users" class="text-primary-400" />
            Shared Keys
          </h2>

          <div v-if="isLoadingShared" class="py-4 text-center text-neutral-400">
            Loading shared keys...
          </div>
          <div
            v-else-if="sharedApiKeys.length === 0"
            class="py-4 text-center text-neutral-500"
          >
            No shared API keys yet.
          </div>
          <div v-else class="space-y-2">
            <div
              v-for="key in sharedApiKeys"
              :key="key.id"
              class="p-4 rounded-xl bg-neutral-900/40 border border-neutral-800"
            >
              <div class="flex items-center justify-between">
                <div class="min-w-0">
                  <div class="font-medium text-neutral-100 truncate">
                    {{ key.description }}
                  </div>
                  <div class="text-xs text-neutral-500 flex items-center gap-3 mt-1.5">
                    <span class="font-mono bg-neutral-800 px-1.5 py-0.5 rounded">
                      {{ key.prefix }}...
                    </span>
                    <span class="flex items-center gap-1 text-primary-300">
                      <UIcon name="i-lucide-user" class="text-[13px]" />
                      {{
                        key.ownerDisplay ||
                        key.ownerUsername ||
                        key.ownerEmail ||
                        "Unknown owner"
                      }}
                    </span>
                    <span>Created {{ new Date(key.createdAt).toLocaleDateString() }}</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Revoke Key Confirmation Modal -->
    <UModal v-model:open="isRevokeModalOpen">
      <template #content>
        <div class="p-6">
          <div class="flex items-center gap-3 mb-4">
            <div class="w-12 h-12 rounded-xl bg-red-600/20 flex items-center justify-center">
              <UIcon name="i-lucide-trash-2" class="text-2xl text-red-500" />
            </div>
            <div>
              <h3 class="text-lg font-semibold text-neutral-100">Revoke API Key</h3>
              <p class="text-sm text-neutral-400">This action cannot be undone</p>
            </div>
          </div>
          <p class="text-neutral-300 mb-6">
            Are you sure you want to revoke this API key? Any applications using it will lose access.
          </p>
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
        </div>
      </template>
    </UModal>
  </div>
</template>
