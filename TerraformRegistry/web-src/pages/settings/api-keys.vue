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
          <p class="page-header-subtitle">Manage personal access tokens for the Terraform CLI and API</p>
        </div>
      </div>
    </div>
    <div class="page-divider" />

    <!-- Body -->
    <div class="flex-1 overflow-y-auto px-6 py-8">
      <div class="max-w-4xl space-y-8">

        <!-- Generated Token Success Panel -->
        <Transition name="success-panel">
          <div v-if="generatedToken" class="success-card rounded-2xl border border-green-700/40 overflow-hidden">
            <!-- Celebration header -->
            <div class="px-6 py-5 border-b border-green-800/30 bg-green-900/20 flex items-center gap-4">
              <div class="w-12 h-12 rounded-xl bg-green-500/20 flex items-center justify-center">
                <UIcon name="i-lucide-check-circle" class="text-green-400 text-2xl" />
              </div>
              <div class="flex-1">
                <h3 class="text-lg font-semibold text-green-200">API Key Generated</h3>
                <p class="text-sm text-green-300/70 mt-0.5">Copy your access token now -- it will not be shown again</p>
              </div>
              <UButton
                icon="i-lucide-x"
                color="neutral"
                variant="ghost"
                size="sm"
                @click="generatedToken = null"
              />
            </div>

            <div class="p-6">
              <div class="space-y-2">
                <label class="text-xs font-medium text-neutral-400 uppercase tracking-wider">Access Token</label>
                <div class="token-block group flex items-center gap-3 p-3 rounded-xl bg-neutral-900/80 border border-green-600/30 transition-all hover:border-green-500/50">
                  <code class="flex-1 font-mono text-sm text-green-300 break-all leading-relaxed select-all">{{ generatedToken }}</code>
                  <UButton
                    icon="i-lucide-copy"
                    color="neutral"
                    variant="soft"
                    size="sm"
                    label="Copy"
                    @click="copyToken"
                  />
                </div>
              </div>

              <div class="flex justify-end mt-5">
                <UButton label="Dismiss" color="neutral" variant="soft" @click="generatedToken = null" />
              </div>
            </div>
          </div>
        </Transition>

        <!-- Create Key Form -->
        <div class="create-card rounded-2xl border border-neutral-800/80 overflow-hidden">
          <!-- Hero header -->
          <div class="relative px-8 py-10 border-b border-neutral-800/60 overflow-hidden">
            <div class="absolute inset-0 opacity-[0.03]" style="background-image: radial-gradient(circle at 1px 1px, white 1px, transparent 0); background-size: 24px 24px;" />
            <div class="absolute top-0 right-0 w-64 h-64 bg-primary-500/5 rounded-full blur-3xl -translate-y-32 translate-x-32" />

            <div class="relative flex items-center gap-5">
              <div class="w-16 h-16 rounded-2xl bg-neutral-800/80 border border-neutral-700/50 flex items-center justify-center shadow-lg shadow-black/20">
                <UIcon name="i-lucide-key-round" class="text-4xl text-neutral-200" />
              </div>
              <div>
                <h3 class="text-xl font-semibold text-neutral-100">Generate New Key</h3>
                <p class="text-sm text-neutral-500 mt-1">Create an access token for Terraform CLI or API integrations</p>
              </div>
            </div>
          </div>

          <div class="p-8 space-y-6">
            <!-- Step 1: Description -->
            <div>
              <div class="flex items-center gap-2 mb-4">
                <div class="w-6 h-6 rounded-md bg-primary-500/10 flex items-center justify-center">
                  <span class="text-xs font-bold text-primary-400">1</span>
                </div>
                <h4 class="text-sm font-medium text-neutral-300">Description</h4>
              </div>
              <div class="pl-8 space-y-1.5">
                <label class="block text-xs font-medium text-neutral-400">
                  Key Name <span class="text-red-400">*</span>
                </label>
                <UInput
                  v-model="newKeyDescription"
                  placeholder="e.g. CLI, Pipeline"
                  icon="i-lucide-tag"
                  class="w-full"
                  @keyup.enter="createKey"
                />
                <p class="text-[11px] text-neutral-600">A friendly name to identify where this key is used</p>
              </div>
            </div>

            <!-- Step 2: Share with organization -->
            <div>
              <div class="flex items-center gap-2 mb-4">
                <div class="w-6 h-6 rounded-md bg-amber-500/10 flex items-center justify-center">
                  <span class="text-xs font-bold text-amber-400">2</span>
                </div>
                <h4 class="text-sm font-medium text-neutral-300">Sharing</h4>
                <span class="text-[10px] text-neutral-600 bg-neutral-800 px-2 py-0.5 rounded-full">Optional</span>
              </div>
              <div class="pl-8">
                <label class="flex items-center gap-3 cursor-pointer group">
                  <input
                    v-model="newKeyShared"
                    type="checkbox"
                    class="w-4 h-4 accent-primary-500 rounded"
                  />
                  <div>
                    <span class="text-sm text-neutral-300 group-hover:text-neutral-100 transition-colors">Share with organization</span>
                    <p class="text-[11px] text-neutral-600">Makes this key visible to all users. Leave unchecked for a personal key.</p>
                  </div>
                </label>
              </div>
            </div>

            <!-- Submit -->
            <div class="flex items-center justify-between pt-4 border-t border-neutral-800/50">
              <p class="text-xs text-neutral-600">
                <UIcon name="i-lucide-shield-check" class="inline text-green-600 mr-1" />
                Tokens are hashed and cannot be retrieved after creation
              </p>
              <UButton
                icon="i-lucide-key-round"
                label="Generate Key"
                color="primary"
                size="lg"
                :loading="isCreating"
                :disabled="!newKeyDescription"
                @click="createKey"
              />
            </div>
          </div>
        </div>

        <!-- Personal Keys Section -->
        <div class="space-y-4">
          <h2 class="text-base font-semibold text-neutral-200 flex items-center gap-3">
            <div class="w-8 h-8 rounded-lg bg-neutral-800 flex items-center justify-center">
              <UIcon name="i-lucide-key-round" class="text-primary-400" />
            </div>
            Your Keys
            <span v-if="apiKeys.length > 0" class="ml-1 px-2 py-0.5 rounded-full bg-neutral-800 text-neutral-400 text-xs font-medium">
              {{ apiKeys.length }}
            </span>
          </h2>

          <div v-if="isLoading" class="py-12 text-center">
            <UIcon
              name="i-lucide-loader-2"
              class="animate-spin text-3xl text-primary-400"
            />
          </div>

          <div
            v-else-if="apiKeys.length === 0"
            class="py-12 text-center rounded-2xl border border-dashed border-neutral-800 bg-neutral-900/20"
          >
            <UIcon name="i-lucide-key-round" class="text-4xl text-neutral-700 mb-3" />
            <p class="text-neutral-500">No API keys found</p>
            <p class="text-sm text-neutral-600 mt-1">Generate one above to get started</p>
          </div>

          <div v-else class="space-y-3">
            <div
              v-for="key in apiKeys"
              :key="key.id"
              class="key-card rounded-xl border border-neutral-800 transition-all duration-200 hover:border-neutral-700 overflow-hidden"
            >
              <div class="p-5">
                <div class="flex items-start gap-4 min-w-0">
                  <!-- Key icon -->
                  <div class="w-11 h-11 rounded-xl bg-neutral-800 border border-neutral-700/50 flex items-center justify-center shrink-0">
                    <UIcon name="i-lucide-key-round" class="text-xl text-neutral-300" />
                  </div>
                  <div class="min-w-0 flex-1 space-y-2">
                    <!-- Header row -->
                    <div class="flex items-center gap-2.5 flex-wrap">
                      <span class="font-semibold text-neutral-100">{{ key.description }}</span>
                      <span
                        :class="[
                          'flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[11px] font-medium',
                          key.isShared
                            ? 'bg-primary-900/40 text-primary-300'
                            : 'bg-neutral-800 text-neutral-400'
                        ]"
                      >
                        <UIcon
                          :name="key.isShared ? 'i-lucide-users' : 'i-lucide-user'"
                          class="text-[13px]"
                        />
                        {{ key.isShared ? "Shared" : "Private" }}
                      </span>
                    </div>
                    <!-- Meta row -->
                    <div class="flex items-center gap-4 text-xs text-neutral-500">
                      <span class="font-mono bg-neutral-800 px-1.5 py-0.5 rounded text-neutral-400">
                        {{ key.prefix }}...
                      </span>
                      <span class="flex items-center gap-1.5">
                        <UIcon name="i-lucide-calendar" class="text-[12px]" />
                        Created {{ new Date(key.createdAt).toLocaleDateString() }}
                      </span>
                      <span v-if="key.lastUsedAt" class="flex items-center gap-1.5">
                        <UIcon name="i-lucide-clock" class="text-[12px]" />
                        Last used {{ new Date(key.lastUsedAt).toLocaleDateString() }}
                      </span>
                    </div>
                  </div>
                </div>
                <!-- Action toolbar -->
                <div class="flex items-center justify-between mt-4 pt-3 border-t border-neutral-800/50 pl-15">
                  <label class="flex items-center gap-2 text-xs text-neutral-500 cursor-pointer hover:text-neutral-400 transition-colors">
                    <input
                      type="checkbox"
                      :checked="key.isShared"
                      class="accent-primary-500 rounded"
                      @change="(e: Event) => updateKey(key, { isShared: (e.target as HTMLInputElement).checked })"
                    />
                    Share with organization
                  </label>
                  <div class="flex items-center gap-1">
                    <UButton
                      icon="i-lucide-trash-2"
                      color="error"
                      variant="ghost"
                      size="xs"
                      label="Revoke"
                      @click="revokeKey(key.id)"
                    />
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- Shared Keys Section -->
        <div class="space-y-4">
          <h2 class="text-base font-semibold text-neutral-200 flex items-center gap-3">
            <div class="w-8 h-8 rounded-lg bg-primary-900/40 flex items-center justify-center">
              <UIcon name="i-lucide-users" class="text-primary-400" />
            </div>
            Shared Keys
            <span v-if="sharedApiKeys.length > 0" class="ml-1 px-2 py-0.5 rounded-full bg-primary-900/30 text-primary-300 text-xs font-medium">
              {{ sharedApiKeys.length }}
            </span>
          </h2>

          <div v-if="isLoadingShared" class="py-12 text-center">
            <UIcon
              name="i-lucide-loader-2"
              class="animate-spin text-3xl text-primary-400"
            />
          </div>

          <div
            v-else-if="sharedApiKeys.length === 0"
            class="py-12 text-center rounded-2xl border border-dashed border-primary-900/40 bg-primary-900/5"
          >
            <UIcon name="i-lucide-users" class="text-4xl text-neutral-700 mb-3" />
            <p class="text-neutral-500">No shared keys yet</p>
            <p class="text-sm text-neutral-600 mt-1">Shared keys are visible to all organization members</p>
          </div>

          <div v-else class="space-y-3">
            <div
              v-for="key in sharedApiKeys"
              :key="key.id"
              class="shared-key-card rounded-xl border border-l-4 border-neutral-800 border-l-primary-500/50 transition-all duration-200 hover:border-neutral-700 overflow-hidden"
            >
              <div class="p-5">
                <div class="flex items-start gap-4 min-w-0">
                  <!-- Shared icon -->
                  <div class="w-11 h-11 rounded-xl bg-primary-900/30 border border-primary-700/30 flex items-center justify-center shrink-0">
                    <UIcon name="i-lucide-users" class="text-xl text-primary-400" />
                  </div>
                  <div class="min-w-0 flex-1 space-y-2">
                    <!-- Header row -->
                    <div class="flex items-center gap-2.5 flex-wrap">
                      <span class="font-semibold text-neutral-100">{{ key.description }}</span>
                      <span class="px-2.5 py-0.5 rounded-full text-[11px] font-semibold bg-primary-900/40 text-primary-300 uppercase tracking-wide">
                        Shared
                      </span>
                    </div>
                    <!-- Meta row -->
                    <div class="flex items-center gap-4 text-xs text-neutral-500">
                      <span class="font-mono bg-neutral-800 px-1.5 py-0.5 rounded text-neutral-400">
                        {{ key.prefix }}...
                      </span>
                      <span class="flex items-center gap-1.5 text-primary-300/80">
                        <UIcon name="i-lucide-user" class="text-[12px]" />
                        {{
                          key.ownerDisplay ||
                          key.ownerUsername ||
                          key.ownerEmail ||
                          "Unknown owner"
                        }}
                      </span>
                      <span class="flex items-center gap-1.5">
                        <UIcon name="i-lucide-calendar" class="text-[12px]" />
                        Created {{ new Date(key.createdAt).toLocaleDateString() }}
                      </span>
                    </div>
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
        <div class="w-full">
          <!-- Header -->
          <div class="flex items-center gap-4 px-6 py-5 border-b border-neutral-800/60">
            <div class="w-12 h-12 rounded-xl bg-red-500/15 flex items-center justify-center shrink-0">
              <UIcon name="i-lucide-key-round" class="text-2xl text-red-400" />
            </div>
            <div>
              <h3 class="text-lg font-semibold text-neutral-100">Revoke API Key</h3>
              <p class="text-sm text-neutral-500">This action is permanent and cannot be undone</p>
            </div>
          </div>

          <!-- Body -->
          <div class="px-6 py-5">
            <p class="text-sm text-neutral-300 leading-relaxed">
              Are you sure you want to revoke this API key? Any applications or CLI sessions using it will
              <span class="text-red-300 font-medium">immediately lose access</span>.
            </p>
          </div>

          <!-- Footer -->
          <div class="flex justify-end gap-3 px-6 py-4 border-t border-neutral-800/60">
            <UButton
              color="neutral"
              variant="ghost"
              label="Cancel"
              @click="isRevokeModalOpen = false"
            />
            <UButton
              color="error"
              label="Revoke Key"
              icon="i-lucide-trash-2"
              @click="confirmRevokeKey"
            />
          </div>
        </div>
      </template>
    </UModal>
  </div>
</template>

<style scoped>
.create-card {
  background: linear-gradient(145deg, rgba(23, 23, 23, 0.8), rgba(10, 10, 10, 0.9));
  backdrop-filter: blur(12px);
}

.success-card {
  background: linear-gradient(145deg, rgba(20, 30, 20, 0.9), rgba(10, 15, 10, 0.95));
  backdrop-filter: blur(12px);
}

.key-card {
  background: linear-gradient(145deg, rgba(23, 23, 23, 0.6), rgba(15, 15, 15, 0.8));
  backdrop-filter: blur(8px);
}

.key-card:hover {
  background: linear-gradient(145deg, rgba(28, 28, 28, 0.7), rgba(18, 18, 18, 0.9));
}

.shared-key-card {
  background: linear-gradient(145deg, rgba(23, 23, 28, 0.6), rgba(15, 15, 20, 0.8));
  backdrop-filter: blur(8px);
}

.shared-key-card:hover {
  background: linear-gradient(145deg, rgba(28, 28, 33, 0.7), rgba(18, 18, 23, 0.9));
}

.token-block:hover {
  box-shadow: 0 0 20px rgba(34, 197, 94, 0.08);
}

.success-panel-enter-active {
  transition: all 0.4s ease-out;
}

.success-panel-leave-active {
  transition: all 0.3s ease-in;
}

.success-panel-enter-from {
  opacity: 0;
  transform: translateY(-16px) scale(0.98);
}

.success-panel-leave-to {
  opacity: 0;
  transform: translateY(-8px);
}
</style>
