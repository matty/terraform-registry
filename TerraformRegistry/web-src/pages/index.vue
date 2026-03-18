<script setup lang="ts">
import { useDashboard } from "~/composables/useDashboard";
import type { Module, ModulesResponse } from "~/composables/useModules";
import { useVcsSources } from "~/composables/useVcsSources";
import { useVcsConnections } from "~/composables/useVcsConnections";
import type { VcsConnectionSummary } from "~/composables/useVcsConnections";

definePageMeta({
  middleware: "auth",
});

const { getAuthHeaders } = useAuth();
const { isSidebarOpen } = useDashboard();
const { createVcsSource } = useVcsSources();
const { listConnectionSummaries } = useVcsConnections();
const { featureCreateModule } = useRuntimeConfig().public;

const modules = ref<Module[]>([]);
const isLoading = ref(false);
const isLoadingMore = ref(false);
const error = ref("");
const searchQuery = ref("");
const currentOffset = ref(0);
const limit = 10;

// VCS connection summaries for the Add Module dropdown
const connectionSummaries = ref<VcsConnectionSummary[]>([]);

// Add Module modal state
const isAddModuleOpen = ref(false);
const newNamespace = ref("");
const newName = ref("");
const newProvider = ref("");
const newDescription = ref("");
const linkToGitHub = ref(false);
const repoOwner = ref("");
const repoName = ref("");
const selectedConnectionId = ref("");
const isSubmitting = ref(false);
const addModuleError = ref<string | null>(null);

const connectionOptions = computed(() =>
  connectionSummaries.value.map(c => ({ label: c.label, value: c.id }))
);

const canSubmit = computed(() => {
  if (!newNamespace.value || !newName.value || !newProvider.value) return false;
  if (linkToGitHub.value && (!repoOwner.value || !repoName.value || !selectedConnectionId.value)) return false;
  return true;
});

const resetAddModuleForm = () => {
  newNamespace.value = "";
  newName.value = "";
  newProvider.value = "";
  newDescription.value = "";
  linkToGitHub.value = false;
  repoOwner.value = "";
  repoName.value = "";
  selectedConnectionId.value = "";
  addModuleError.value = null;
};

const openAddModule = () => {
  resetAddModuleForm();
  isAddModuleOpen.value = true;
};

// Pre-fill repo owner when a connection with defaultOrg is selected
watch(selectedConnectionId, (connId) => {
  if (!connId) return;
  const conn = connectionSummaries.value.find(c => c.id === connId);
  if (conn?.defaultOrg && !repoOwner.value) {
    repoOwner.value = conn.defaultOrg;
  }
});

const handleAddModule = async () => {
  if (!canSubmit.value) return;
  isSubmitting.value = true;
  addModuleError.value = null;

  if (!linkToGitHub.value) {
    isAddModuleOpen.value = false;
    resetAddModuleForm();
    refreshModules();
    return;
  }

  try {
    await createVcsSource({
      namespace: newNamespace.value,
      name: newName.value,
      provider: newProvider.value,
      repoOwner: repoOwner.value,
      repoName: repoName.value,
      connectionId: selectedConnectionId.value,
    });

    isAddModuleOpen.value = false;
    resetAddModuleForm();
    refreshModules();
  } catch (e: any) {
    const msg = e?.data?.message || e?.data?.error || e?.message || "Failed to create module";
    addModuleError.value = msg;
  } finally {
    isSubmitting.value = false;
  }
};

const filteredModules = computed(() => {
  if (!searchQuery.value) return modules.value;

  const query = searchQuery.value.toLowerCase();
  return modules.value.filter(
    (module) =>
      module.name.toLowerCase().includes(query) ||
      module.namespace.toLowerCase().includes(query) ||
      module.provider.toLowerCase().includes(query) ||
      module.description.toLowerCase().includes(query)
  );
});

const formatDate = (dateString: string) => {
  return new Date(dateString).toLocaleDateString("en-US", {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
};

const fetchModules = async (offset = 0, append = false) => {
  try {
    if (!append) {
      isLoading.value = true;
    } else {
      isLoadingMore.value = true;
    }
    error.value = "";

    const response = await $fetch<ModulesResponse>(
      `/v1/modules?offset=${offset}&limit=${limit}`,
      {
        headers: getAuthHeaders(),
      }
    );

    if (append) {
      modules.value.push(...response.modules);
    } else {
      modules.value = response.modules;
    }

    currentOffset.value = offset + limit;
  } catch (err: any) {
    error.value = err.message || "Failed to fetch modules";
    console.error("Error fetching modules:", err);
  } finally {
    isLoading.value = false;
    isLoadingMore.value = false;
  }
};

const refreshModules = () => {
  currentOffset.value = 0;
  fetchModules(0, false);
};

const loadMoreModules = () => {
  fetchModules(currentOffset.value, true);
};

// Load modules on component mount
const route = useRoute();
onMounted(async () => {
  fetchModules();
  if (featureCreateModule) {
    try {
      connectionSummaries.value = await listConnectionSummaries();
    } catch (e) {
      console.error('Failed to load VCS connection summaries', e);
    }
  }
  // Auto-open Add Module modal if ?addModule=1 query param is present
  if (featureCreateModule && route.query.addModule === '1') {
    openAddModule();
  }
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
          <h1 class="page-header-title">Modules</h1>
          <p class="page-header-subtitle">Browse and manage your Terraform modules</p>
        </div>
        <div class="hidden md:flex items-center gap-2 px-3 py-1.5 bg-neutral-800/60 rounded-lg">
          <UIcon name="i-lucide-package" class="text-primary-400" />
          <span class="text-sm font-medium text-neutral-300">{{ modules.length }}</span>
        </div>
      </div>
      <div class="page-header-actions">
        <UInput
          v-model="searchQuery"
          placeholder="Search modules..."
          icon="i-lucide-search"
          class="w-64"
          size="sm"
        />
        <UButton
          @click="refreshModules"
          :loading="isLoading"
          icon="i-lucide-refresh-cw"
          color="neutral"
          variant="ghost"
          size="sm"
        />
        <UButton
          v-if="featureCreateModule"
          label="Add Module"
          icon="i-lucide-plus"
          color="primary"
          size="sm"
          @click="openAddModule"
        />
      </div>
    </div>
    <div class="page-divider" />

    <!-- Body -->
    <div class="flex-1 overflow-y-auto">
      <div class="p-6">
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
          v-if="isLoading && !modules.length"
          class="flex flex-col justify-center items-center py-20"
        >
          <div class="relative">
            <div class="w-16 h-16 border-4 border-primary-500/20 rounded-full"></div>
            <div class="w-16 h-16 border-4 border-transparent border-t-primary-500 rounded-full animate-spin absolute inset-0"></div>
          </div>
          <p class="text-neutral-400 text-lg mt-6">Loading modules...</p>
        </div>

        <!-- Empty State -->
        <div
          v-else-if="!filteredModules.length && !isLoading"
          class="text-center py-20 px-6"
        >
          <div
            class="w-24 h-24 mx-auto mb-6 bg-gradient-to-br from-neutral-800 to-neutral-900 rounded-3xl flex items-center justify-center ring-1 ring-neutral-700"
          >
            <UIcon name="i-lucide-package" class="text-5xl text-neutral-500" />
          </div>
          <h3 class="text-xl font-semibold text-neutral-100 mb-2">
            No modules found
          </h3>
          <p class="text-neutral-400 max-w-sm mx-auto">
            {{
              searchQuery
                ? "Try adjusting your search terms"
                : "Get started by uploading your first module"
            }}
          </p>
        </div>

        <!-- Modules Grid -->
        <div v-else>
          <div class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            <div
              v-for="module in filteredModules"
              :key="module.id"
              class="group relative overflow-hidden rounded-2xl bg-neutral-900/50 border border-neutral-800 p-5 hover:border-primary-500/30 hover:ring-1 hover:ring-primary-500/10 hover:bg-neutral-800/50 transition-all cursor-pointer"
              @click="navigateTo(`/modules/${module.namespace}/${module.name}/${module.provider}`)"
            >
              <!-- Decorative gradient -->
              <div class="absolute top-0 right-0 w-32 h-32 bg-primary-500/5 rounded-full blur-2xl -tranneutral-y-16 tranneutral-x-16 group-hover:bg-primary-500/10 transition-colors"></div>
              
              <div class="relative flex items-start gap-4">
                <div
                  class="w-12 h-12 bg-gradient-to-br from-neutral-600 to-neutral-800 rounded-xl flex items-center justify-center flex-shrink-0 shadow-lg shadow-black/30"
                >
                  <span class="text-white font-bold text-lg">{{
                    module.name.charAt(0).toUpperCase()
                  }}</span>
                </div>
                <div class="flex-1 min-w-0">
                  <h3 class="font-semibold text-white truncate group-hover:text-primary-300 transition-colors">
                    {{ module.name }}
                  </h3>
                  <p class="text-sm text-neutral-500 truncate">
                    {{ module.namespace }}
                  </p>
                </div>
                <div class="flex flex-col items-end gap-1">
                  <UBadge variant="soft" color="primary" size="xs">
                    v{{ module.version }}
                  </UBadge>
                  <span v-if="module.versions && module.versions.length > 1" class="text-xs text-neutral-500">
                    {{ module.versions.length }} versions
                  </span>
                </div>
              </div>

              <p class="relative mt-3 text-sm text-neutral-400 line-clamp-2">
                {{ module.description || "No description available" }}
              </p>

              <div class="relative mt-4 flex items-center justify-between">
                <div class="flex items-center gap-2">
                  <div class="flex items-center gap-1.5 px-2 py-1 bg-neutral-800/50 rounded-md">
                    <UIcon name="i-lucide-cloud" class="text-primary-400 text-xs" />
                    <span class="text-xs text-neutral-400">{{ module.provider }}</span>
                  </div>
                  <span class="text-xs text-neutral-600">{{
                    formatDate(module.published_at)
                  }}</span>
                </div>
                <UIcon 
                  name="i-lucide-chevron-right" 
                  class="text-neutral-600 group-hover:text-primary-400 group-hover:tranneutral-x-1 transition-all" 
                />
              </div>
            </div>
          </div>

          <!-- Load More -->
          <div
            v-if="modules.length > 0"
            class="flex justify-center items-center gap-4 mt-8 pt-6 border-t border-neutral-800"
          >
            <p class="text-sm text-neutral-500">
              Showing {{ filteredModules.length }} of {{ modules.length }} modules
            </p>
            <UButton
              @click="loadMoreModules"
              :loading="isLoadingMore"
              variant="soft"
              size="sm"
            >
              Load More
            </UButton>
          </div>
        </div>
      </div>
    </div>
    <!-- Add Module Modal -->
    <UModal v-if="featureCreateModule" v-model:open="isAddModuleOpen">
      <template #content>
        <div class="p-6 max-h-[80vh] overflow-y-auto">
          <!-- Header -->
          <div class="flex items-center gap-3 mb-5">
            <div class="w-12 h-12 rounded-xl bg-primary-600/20 flex items-center justify-center">
              <UIcon name="i-lucide-package-plus" class="text-2xl text-primary-400" />
            </div>
            <div>
              <h3 class="text-lg font-semibold text-neutral-100">Add Module</h3>
              <p class="text-sm text-neutral-400">Register a new module, optionally linked to GitHub</p>
            </div>
          </div>

          <!-- Error -->
          <div
            v-if="addModuleError"
            class="mb-4 p-3 bg-red-900/20 border border-red-800/50 rounded-lg flex items-center gap-2"
          >
            <UIcon name="i-lucide-alert-circle" class="text-red-500" />
            <p class="text-sm text-red-300 flex-1">{{ addModuleError }}</p>
            <UButton icon="i-lucide-x" color="neutral" variant="ghost" size="xs" @click="addModuleError = null" />
          </div>

          <!-- Form -->
          <div class="space-y-5">
            <!-- Module Details -->
            <div class="space-y-3">
              <h4 class="text-xs font-semibold text-neutral-400 uppercase tracking-wide">Module Details</h4>
              <div class="grid grid-cols-3 gap-3">
                <div>
                  <label class="block text-xs text-neutral-400 mb-1">Namespace <span class="text-red-400">*</span></label>
                  <UInput v-model="newNamespace" placeholder="myorg" size="sm" />
                </div>
                <div>
                  <label class="block text-xs text-neutral-400 mb-1">Name <span class="text-red-400">*</span></label>
                  <UInput v-model="newName" placeholder="vpc" size="sm" />
                </div>
                <div>
                  <label class="block text-xs text-neutral-400 mb-1">Provider <span class="text-red-400">*</span></label>
                  <UInput v-model="newProvider" placeholder="aws" size="sm" />
                </div>
              </div>
              <div>
                <label class="block text-xs text-neutral-400 mb-1">Description</label>
                <UTextarea v-model="newDescription" placeholder="Optional module description" :rows="2" size="sm" class="w-full" />
              </div>
            </div>

            <!-- GitHub Integration -->
            <div class="border-t border-neutral-800 pt-4">
              <div class="flex items-center justify-between mb-3">
                <h4 class="text-xs font-semibold text-neutral-400 uppercase tracking-wide flex items-center gap-1.5">
                  <UIcon name="i-lucide-github" />
                  GitHub Integration
                </h4>
                <label class="flex items-center gap-2 text-xs text-neutral-400 cursor-pointer">
                  <span>Link to GitHub</span>
                  <input v-model="linkToGitHub" type="checkbox" class="accent-primary-500 rounded" />
                </label>
              </div>

              <div v-if="linkToGitHub" class="space-y-3">
                <div v-if="connectionOptions.length === 0" class="p-3 bg-amber-900/20 border border-amber-800/50 rounded-lg">
                  <p class="text-xs text-amber-300">No VCS connections configured. Ask an admin to set one up in Admin → VCS Connections.</p>
                </div>
                <template v-else>
                  <div>
                    <label class="block text-xs text-neutral-400 mb-1">VCS Connection <span class="text-red-400">*</span></label>
                    <USelect
                      v-model="selectedConnectionId"
                      :items="connectionOptions"
                      value-key="value"
                      label-key="label"
                      placeholder="Select a connection..."
                      size="sm"
                    />
                  </div>
                  <div class="grid grid-cols-2 gap-3">
                    <div>
                      <label class="block text-xs text-neutral-400 mb-1">Owner <span class="text-red-400">*</span></label>
                      <UInput v-model="repoOwner" placeholder="acme" size="sm" />
                    </div>
                    <div>
                      <label class="block text-xs text-neutral-400 mb-1">Repository <span class="text-red-400">*</span></label>
                      <UInput v-model="repoName" placeholder="terraform-vpc" size="sm" />
                    </div>
                  </div>
                </template>
              </div>
              <p v-else class="text-xs text-neutral-500">Enable to auto-publish versions on Git tag push.</p>
            </div>

            <!-- Actions -->
            <div class="flex justify-end gap-2 border-t border-neutral-800 pt-4">
              <UButton label="Cancel" color="neutral" variant="ghost" size="sm" @click="isAddModuleOpen = false" />
              <UButton
                :label="linkToGitHub ? 'Create & Link' : 'Create Module'"
                color="primary"
                size="sm"
                :loading="isSubmitting"
                :disabled="!canSubmit"
                @click="handleAddModule"
              />
            </div>
          </div>
        </div>
      </template>
    </UModal>
  </div>
</template>

