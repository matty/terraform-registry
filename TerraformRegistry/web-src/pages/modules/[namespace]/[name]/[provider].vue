<script setup lang="ts">
import { useDashboard } from "~/composables/useDashboard";
import { useModules, type Module, type ModulesResponse } from "~/composables/useModules";
import { useVcsSources } from "~/composables/useVcsSources";
import type { VcsSource } from "~/composables/useVcsSources";

definePageMeta({
  middleware: "auth",
});

const route = useRoute();
const { isSidebarOpen } = useDashboard();
const { getModuleVersions, deleteModuleVersion, updateModuleDescription } = useModules();
const { listVcsSources, deleteVcsSource } = useVcsSources();
const { getAuthHeaders } = useAuth();
const { hasPermission } = usePermissions();
const canManageVcs = computed(() => hasPermission("vcs.manage"));

// Route params
const namespace = computed(() => route.params.namespace as string);
const name = computed(() => route.params.name as string);
const provider = computed(() => route.params.provider as string);

// State
const versions = ref<{ version: string }[]>([]);
const moduleInfo = ref<Module | null>(null);
const isLoading = ref(true);
const error = ref("");
const copied = ref(false);

// VCS state
const vcsSource = ref<VcsSource | null>(null);
const isDisconnectingVcs = ref(false);

const fetchVcsSource = async () => {
  if (!canManageVcs.value) {
    vcsSource.value = null;
    return;
  }

  try {
    const sources = await listVcsSources();
    vcsSource.value = sources.find(
      s => s.namespace === namespace.value && s.name === name.value && s.provider === provider.value
    ) || null;
  } catch (e) {
    // VCS info is non-critical, silently ignore
    vcsSource.value = null;
  }
};

const disconnectVcs = async () => {
  if (!vcsSource.value) return;
  isDisconnectingVcs.value = true;
  try {
    await deleteVcsSource(vcsSource.value.id);
    vcsSource.value = null;
  } catch (e) {
    console.error("Failed to disconnect VCS source", e);
  } finally {
    isDisconnectingVcs.value = false;
  }
};

// Description editing state
const isEditingDescription = ref(false);
const editDescription = ref("");
const isSavingDescription = ref(false);
const descriptionError = ref("");

const startEditingDescription = () => {
  editDescription.value = moduleInfo.value?.description || "";
  descriptionError.value = "";
  isEditingDescription.value = true;
};

const cancelEditingDescription = () => {
  isEditingDescription.value = false;
  editDescription.value = "";
  descriptionError.value = "";
};

const saveDescription = async () => {
  isSavingDescription.value = true;
  descriptionError.value = "";
  try {
    const success = await updateModuleDescription(
      namespace.value,
      name.value,
      provider.value,
      editDescription.value
    );
    if (success) {
      if (moduleInfo.value) {
        moduleInfo.value.description = editDescription.value;
      }
      isEditingDescription.value = false;
    } else {
      descriptionError.value = "Failed to update description";
    }
  } catch (err) {
    descriptionError.value = "An error occurred while saving";
  } finally {
    isSavingDescription.value = false;
  }
};

// Delete modal state
const showDeleteModal = ref(false);
const versionToDelete = ref<string | null>(null);
const isDeleting = ref(false);

// Generate module source path (SSR-safe)
const requestURL = useRequestURL();
const moduleSource = computed(() => {
  const host = requestURL.host || 'registry.example.com';
  return `${host}/${namespace.value}/${name.value}/${provider.value}`;
});

const copyModuleSource = async () => {
  try {
    await navigator.clipboard.writeText(`source = "${moduleSource.value}"`);
    copied.value = true;
    setTimeout(() => {
      copied.value = false;
    }, 2000);
  } catch (err) {
    console.error("Failed to copy:", err);
  }
};

const formatDate = (dateString: string) => {
  return new Date(dateString).toLocaleDateString("en-US", {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
};

const fetchVersions = async () => {
  isLoading.value = true;
  error.value = "";

  try {
    const response = await getModuleVersions(
      namespace.value,
      name.value,
      provider.value
    );

    if (response && response.modules && response.modules.length > 0) {
      versions.value = response.modules[0].versions || [];
    } else {
      versions.value = [];
    }

    // Get full module info for the latest version
    if (versions.value.length > 0) {
      const latestVersion = versions.value[0].version;
      const moduleResponse = await $fetch<Module>(
        `/v1/modules/${namespace.value}/${name.value}/${provider.value}/${latestVersion}`,
        { headers: getAuthHeaders() }
      );
      moduleInfo.value = moduleResponse;
    }
  } catch (err: any) {
    error.value = err.message || "Failed to fetch module versions";
    console.error("Error fetching module versions:", err);
  } finally {
    isLoading.value = false;
  }
};

const openDeleteModal = (version: string) => {
  versionToDelete.value = version;
  showDeleteModal.value = true;
};

const confirmDelete = async () => {
  if (!versionToDelete.value) return;

  isDeleting.value = true;

  try {
    const success = await deleteModuleVersion(
      namespace.value,
      name.value,
      provider.value,
      versionToDelete.value
    );

    if (success) {
      versions.value = versions.value.filter(
        (v) => v.version !== versionToDelete.value
      );
    } else {
      error.value = "Failed to delete module version";
    }
  } catch (err: any) {
    error.value = err.message || "Failed to delete module version";
  } finally {
    isDeleting.value = false;
    showDeleteModal.value = false;
    versionToDelete.value = null;
  }
};

const getDownloadUrl = (version: string) => {
  return `/v1/modules/${namespace.value}/${name.value}/${provider.value}/${version}/download`;
};

onMounted(() => {
  fetchVersions();
  fetchVcsSource();
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
          <div class="flex items-center gap-2 mb-2">
            <NuxtLink
              to="/"
              class="text-sm text-neutral-500 hover:text-primary-400 transition-colors flex items-center gap-1"
            >
              <UIcon name="i-lucide-chevron-left" class="text-xs" />
              Modules
            </NuxtLink>
            <span class="text-neutral-600 text-sm">/</span>
            <span class="text-sm text-neutral-400">{{ namespace }} / {{ name }}</span>
          </div>
          <h1 class="page-header-title">{{ name }}</h1>
          <p class="page-header-subtitle">
            {{ namespace }} / <span class="text-primary-400">{{ provider }}</span>
          </p>
        </div>
        <div class="flex items-center gap-3">
          <div class="hidden sm:flex items-center gap-2 px-3 py-1.5 bg-neutral-800/60 rounded-lg">
            <UIcon name="i-lucide-layers" class="text-primary-400" />
            <span class="text-sm font-medium text-neutral-300">
              {{ versions.length }} version{{ versions.length !== 1 ? "s" : "" }}
            </span>
          </div>
          <UButton
            @click="fetchVersions"
            :loading="isLoading"
            icon="i-lucide-refresh-cw"
            color="neutral"
            variant="ghost"
            size="sm"
          />
        </div>
      </div>
    </div>
    <div class="page-divider" />

    <!-- Body -->
    <div class="flex-1 overflow-y-auto">
      <div class="p-6 max-w-4xl mx-auto">
        <!-- Error State -->
        <UAlert
          v-if="error"
          color="error"
          variant="soft"
          :title="error"
          icon="i-lucide-alert-circle"
          class="mb-6"
        />

        <!-- Loading State -->
        <div
          v-if="isLoading"
          class="flex flex-col justify-center items-center py-20"
        >
          <div class="relative">
            <div class="w-16 h-16 border-4 border-primary-500/20 rounded-full"></div>
            <div class="w-16 h-16 border-4 border-transparent border-t-primary-500 rounded-full animate-spin absolute inset-0"></div>
          </div>
          <p class="text-neutral-400 text-lg mt-6">Loading versions...</p>
        </div>

        <!-- Empty State -->
        <div
          v-else-if="!versions.length"
          class="text-center py-20 px-6"
        >
          <div
            class="w-24 h-24 mx-auto mb-6 bg-gradient-to-br from-neutral-800 to-neutral-900 rounded-3xl flex items-center justify-center ring-1 ring-neutral-700"
          >
            <UIcon name="i-lucide-package-x" class="text-5xl text-neutral-500" />
          </div>
          <h3 class="text-xl font-semibold text-neutral-100 mb-2">
            No versions found
          </h3>
          <p class="text-neutral-400 max-w-sm mx-auto">
            This module has no published versions.
          </p>
        </div>

        <!-- Module Info & Versions List -->
        <div v-else class="space-y-6">
          <!-- Module Description Card -->
          <div 
            v-if="moduleInfo"
            class="relative overflow-hidden rounded-2xl bg-gradient-to-br from-neutral-900 via-neutral-900 to-neutral-800 border border-neutral-800 p-6"
          >
            <!-- Decorative element -->
            <div class="absolute top-0 right-0 w-64 h-64 bg-primary-500/5 rounded-full blur-3xl -translate-y-32 translate-x-32"></div>
            
            <div class="relative flex items-start gap-5">
              <div
                class="w-16 h-16 bg-gradient-to-br from-neutral-600 to-neutral-800 rounded-2xl flex items-center justify-center flex-shrink-0 shadow-xl shadow-black/30"
              >
                <span class="text-white font-bold text-2xl">{{
                  name.charAt(0).toUpperCase()
                }}</span>
              </div>
              <div class="flex-1 min-w-0">
                <div class="flex items-center gap-3 mb-2">
                  <h2 class="text-xl font-bold text-white">{{ name }}</h2>
                  <UBadge variant="soft" color="success" size="xs">
                    <UIcon name="i-lucide-check-circle" class="mr-1" />
                    Active
                  </UBadge>
                  <UBadge v-if="canManageVcs && vcsSource" variant="soft" color="info" size="xs">
                    <UIcon name="i-lucide-git-branch" class="mr-1" />
                    VCS
                  </UBadge>
                </div>
                <!-- Description: display mode -->
                <div v-if="!isEditingDescription" class="group/desc flex items-start gap-2">
                  <p class="text-neutral-400 leading-relaxed">
                    {{ moduleInfo.description || "No description available" }}
                  </p>
                  <UButton
                    icon="i-lucide-pencil"
                    variant="ghost"
                    color="neutral"
                    size="xs"
                    class="opacity-0 group-hover/desc:opacity-100 transition-opacity flex-shrink-0 mt-0.5"
                    @click="startEditingDescription"
                  />
                </div>
                
                <!-- Description: edit mode -->
                <div v-else class="space-y-3">
                  <UTextarea
                    v-model="editDescription"
                    placeholder="Enter module description..."
                    :rows="3"
                    autofocus
                    class="w-full"
                  />
                  <div v-if="descriptionError" class="text-sm text-red-400">
                    {{ descriptionError }}
                  </div>
                  <div class="flex items-center gap-2">
                    <UButton
                      @click="saveDescription"
                      :loading="isSavingDescription"
                      icon="i-lucide-check"
                      size="xs"
                      color="primary"
                    >
                      Save
                    </UButton>
                    <UButton
                      @click="cancelEditingDescription"
                      :disabled="isSavingDescription"
                      icon="i-lucide-x"
                      variant="ghost"
                      color="neutral"
                      size="xs"
                    >
                      Cancel
                    </UButton>
                  </div>
                </div>
                <div class="mt-4 flex flex-wrap items-center gap-3">
                  <div class="flex items-center gap-2 px-3 py-1.5 bg-neutral-800/50 rounded-lg">
                    <UIcon name="i-lucide-cloud" class="text-primary-400" />
                    <span class="text-sm text-neutral-300">{{ provider }}</span>
                  </div>
                  <div class="flex items-center gap-2 px-3 py-1.5 bg-neutral-800/50 rounded-lg">
                    <UIcon name="i-lucide-calendar" class="text-neutral-400" />
                    <span class="text-sm text-neutral-300">
                      {{ formatDate(moduleInfo.published_at || moduleInfo.publishedAt || '') }}
                    </span>
                  </div>
                  <div class="flex items-center gap-2 px-3 py-1.5 bg-neutral-800/50 rounded-lg">
                    <UIcon name="i-lucide-tag" class="text-green-400" />
                    <span class="text-sm text-neutral-300">v{{ versions[0]?.version }}</span>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- Module Source Snippet -->
          <div class="rounded-2xl bg-neutral-900/50 border border-neutral-800 overflow-hidden">
            <div class="px-5 py-3 border-b border-neutral-800 flex items-center justify-between">
              <div class="flex items-center gap-2">
                <UIcon name="i-lucide-terminal" class="text-neutral-400" />
                <span class="text-sm font-medium text-neutral-300">Usage</span>
              </div>
              <UButton
                @click="copyModuleSource"
                :icon="copied ? 'i-lucide-check' : 'i-lucide-copy'"
                :color="copied ? 'success' : 'neutral'"
                variant="ghost"
                size="xs"
              >
                {{ copied ? 'Copied!' : 'Copy' }}
              </UButton>
            </div>
            <div class="px-5 py-4 bg-neutral-950/50">
              <code class="text-sm font-mono text-primary-300">
                source = "<span class="text-neutral-300">{{ moduleSource }}</span>"
              </code>
            </div>
          </div>

          <!-- VCS Source Section -->
          <div
            v-if="canManageVcs && vcsSource"
            class="rounded-2xl bg-neutral-900/50 border border-neutral-800 overflow-hidden"
          >
            <div class="px-5 py-4 flex items-center justify-between">
              <div class="flex items-center gap-3">
                <div class="w-10 h-10 bg-neutral-800 rounded-xl flex items-center justify-center">
                  <UIcon name="i-lucide-github" class="text-xl text-neutral-300" />
                </div>
                <div>
                  <div class="flex items-center gap-2">
                    <span class="text-sm font-medium text-neutral-200">
                      Linked to {{ vcsSource.repoOwner }}/{{ vcsSource.repoName }}
                    </span>
                    <UBadge
                      :variant="'soft'"
                      :color="vcsSource.isActive ? 'success' : 'neutral'"
                      size="xs"
                    >
                      {{ vcsSource.isActive ? 'Active' : 'Inactive' }}
                    </UBadge>
                  </div>
                  <p class="text-xs text-neutral-500 mt-0.5">
                    Versions are published automatically when Git tags are pushed
                  </p>
                </div>
              </div>
              <UButton
                label="Disconnect"
                icon="i-lucide-unlink"
                color="error"
                variant="ghost"
                size="xs"
                :loading="isDisconnectingVcs"
                @click="disconnectVcs"
              />
            </div>
          </div>

          <!-- Link to GitHub (when no VCS source) -->
          <div
            v-else-if="canManageVcs && !isLoading && useRuntimeConfig().public.featureCreateModule"
            class="flex justify-end"
          >
            <NuxtLink :to="{ path: '/', query: { addModule: '1' } }">
              <UButton
                label="Link to GitHub"
                icon="i-lucide-github"
                color="neutral"
                variant="soft"
                size="xs"
              />
            </NuxtLink>
          </div>

          <!-- Versions List -->
          <div class="rounded-2xl bg-neutral-900/50 border border-neutral-800 overflow-hidden">
            <div class="px-5 py-4 border-b border-neutral-800 flex items-center justify-between">
              <div class="flex items-center gap-2">
                <UIcon name="i-lucide-git-branch" class="text-neutral-400" />
                <h3 class="font-semibold text-neutral-100">Version History</h3>
              </div>
              <span class="text-xs text-neutral-500 uppercase tracking-wide">
                {{ versions.length }} release{{ versions.length !== 1 ? "s" : "" }}
              </span>
            </div>

            <div class="divide-y divide-neutral-800/50">
              <div
                v-for="(v, index) in versions"
                :key="v.version"
                class="group flex items-center justify-between px-5 py-4 hover:bg-neutral-800/30 transition-colors"
              >
                <div class="flex items-center gap-4">
                  <!-- Version indicator -->
                  <div class="relative">
                    <div 
                      :class="[
                        'w-3 h-3 rounded-full',
                        index === 0 ? 'bg-green-500 shadow-lg shadow-green-500/50' : 'bg-neutral-600'
                      ]"
                    ></div>
                    <div 
                      v-if="index !== versions.length - 1"
                      class="absolute top-3 left-1/2 -translate-x-1/2 w-px h-8 bg-neutral-700"
                    ></div>
                  </div>
                  
                  <div class="flex items-center gap-3">
                    <span 
                      :class="[
                        'font-mono font-medium',
                        index === 0 ? 'text-white' : 'text-neutral-400'
                      ]"
                    >
                      v{{ v.version }}
                    </span>
                    <UBadge 
                      v-if="index === 0" 
                      variant="soft" 
                      color="success" 
                      size="xs"
                    >
                      Latest
                    </UBadge>
                  </div>
                </div>

                <div class="flex items-center gap-2 opacity-50 group-hover:opacity-100 transition-opacity">
                  <UButton
                    :to="getDownloadUrl(v.version)"
                    external
                    target="_blank"
                    variant="soft"
                    size="xs"
                    color="primary"
                    icon="i-lucide-download"
                  >
                    Download
                  </UButton>
                  <UButton
                    @click="openDeleteModal(v.version)"
                    variant="ghost"
                    size="xs"
                    color="error"
                    icon="i-lucide-trash-2"
                  />
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Delete Confirmation Modal -->
    <UModal v-model:open="showDeleteModal">
      <template #content>
        <div class="p-6">
          <div class="flex items-center gap-3 mb-4">
            <div class="w-12 h-12 bg-red-600/20 rounded-xl flex items-center justify-center">
              <UIcon name="i-lucide-trash-2" class="text-2xl text-red-500" />
            </div>
            <div>
              <h3 class="text-lg font-semibold text-neutral-100">Delete Version</h3>
              <p class="text-sm text-neutral-400">
                {{ namespace }}/{{ name }}/{{ provider }}/{{ versionToDelete }}
              </p>
            </div>
          </div>

          <p class="text-neutral-300 mb-6">
            This will move version <strong class="text-white">v{{ versionToDelete }}</strong> to trash. You can restore it later from the Trash page.
          </p>

          <div class="flex justify-end gap-2">
            <UButton
              @click="showDeleteModal = false"
              variant="ghost"
              color="neutral"
              :disabled="isDeleting"
            >
              Cancel
            </UButton>
            <UButton
              @click="confirmDelete"
              :loading="isDeleting"
              color="error"
            >
              Move to Trash
            </UButton>
          </div>
        </div>
      </template>
    </UModal>
  </div>
</template>
