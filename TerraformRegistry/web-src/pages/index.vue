<script setup lang="ts">
import { useDashboard } from "~/composables/useDashboard";
import type { Module } from "~/composables/useModules";
import { useModules } from "~/composables/useModules";
import { useVcsSources } from "~/composables/useVcsSources";
import { useVcsConnections } from "~/composables/useVcsConnections";
import type { VcsConnectionSummary } from "~/composables/useVcsConnections";

definePageMeta({
  middleware: "auth",
});

const { hasPermission } = usePermissions();
const { isSidebarOpen } = useDashboard();
const { listModules, uploadModule } = useModules();
const { createVcsSource } = useVcsSources();
const { listConnectionSummaries } = useVcsConnections();
const { featureCreateModule } = useRuntimeConfig().public;
const canUploadModules = computed(() => hasPermission("modules.upload"));
const canManageVcs = computed(() => hasPermission("vcs.manage"));
const canAddModule = computed(() => featureCreateModule && (canUploadModules.value || canManageVcs.value));
const canCreateVcsModule = computed(() => canAddModule.value && canManageVcs.value);
const addModuleMode = ref<"upload" | "github">("upload");

const modules = ref<Module[]>([]);
const isLoading = ref(false);
const isLoadingMore = ref(false);
const error = ref("");
const searchQuery = ref("");
const requiredProviderFilter = ref("");
const currentOffset = ref(0);
const limit = 10;
const totalCount = ref(0);
const hasMore = ref(false);
let searchDebounce: ReturnType<typeof setTimeout> | null = null;

// VCS connection summaries for the Add Module dropdown
const connectionSummaries = ref<VcsConnectionSummary[]>([]);

// Add Module modal state
const isAddModuleOpen = ref(false);
const newNamespace = ref("");
const newName = ref("");
const newProvider = ref("");
const newVersion = ref("");
const newDescription = ref("");
const selectedModuleFile = ref<File | null>(null);
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
  if (linkToGitHub.value) {
    return !!repoOwner.value && !!repoName.value && !!selectedConnectionId.value;
  }

  return !!newVersion.value && !!selectedModuleFile.value;
});

const resetAddModuleForm = () => {
  newNamespace.value = "";
  newName.value = "";
  newProvider.value = "";
  newVersion.value = "";
  newDescription.value = "";
  selectedModuleFile.value = null;
  addModuleMode.value = canUploadModules.value ? "upload" : "github";
  linkToGitHub.value = !canUploadModules.value && canManageVcs.value;
  repoOwner.value = "";
  repoName.value = "";
  selectedConnectionId.value = "";
  addModuleError.value = null;
};

const openAddModule = () => {
  if (!canAddModule.value) return;
  resetAddModuleForm();
  isAddModuleOpen.value = true;
};

watch(addModuleMode, (mode) => {
  linkToGitHub.value = mode === "github";
  addModuleError.value = null;
});

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
  const namespace = newNamespace.value;
  const name = newName.value;
  const provider = newProvider.value;
  const version = newVersion.value;
  const isGitHubMode = linkToGitHub.value;

  try {
    if (isGitHubMode) {
      await createVcsSource({
        namespace,
        name,
        provider,
        repoOwner: repoOwner.value,
        repoName: repoName.value,
        connectionId: selectedConnectionId.value,
      });
    } else {
      await uploadModule({
        namespace,
        name,
        provider,
        version,
        description: newDescription.value,
        moduleFile: selectedModuleFile.value!,
      });
    }

    isAddModuleOpen.value = false;
    resetAddModuleForm();
    if (isGitHubMode) {
      refreshModules();
    } else {
      await navigateTo(`/modules/${namespace}/${name}/${provider}`);
    }
  } catch (e: any) {
    const msg = e?.data?.message || e?.data?.error || e?.message || "Failed to create module";
    addModuleError.value = msg;
  } finally {
    isSubmitting.value = false;
  }
};

const onModuleFileSelected = (event: Event) => {
  const target = event.target as HTMLInputElement;
  selectedModuleFile.value = target.files?.[0] ?? null;
  addModuleError.value = null;
};

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

    const response = await listModules({
      q: searchQuery.value,
      requiredProvider: requiredProviderFilter.value,
      offset,
      limit,
    });

    if (append) {
      modules.value.push(...response.modules);
    } else {
      modules.value = response.modules;
    }

    totalCount.value = Number.parseInt(response.meta?.total_count ?? String(modules.value.length), 10);
    hasMore.value = response.meta?.has_more === "true";
    currentOffset.value = Number.parseInt(response.meta?.next_offset ?? String(offset + response.modules.length), 10);
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
  totalCount.value = 0;
  hasMore.value = false;
  fetchModules(0, false);
};

const loadMoreModules = () => {
  if (!hasMore.value) return;
  fetchModules(currentOffset.value, true);
};

watch([searchQuery, requiredProviderFilter], () => {
  if (searchDebounce) {
    clearTimeout(searchDebounce);
  }

  searchDebounce = setTimeout(() => {
    refreshModules();
  }, 250);
});

onBeforeUnmount(() => {
  if (searchDebounce) {
    clearTimeout(searchDebounce);
  }
});

// Load modules on component mount
const route = useRoute();
onMounted(async () => {
  fetchModules();
  if (canCreateVcsModule.value) {
    try {
      connectionSummaries.value = await listConnectionSummaries();
    } catch (e) {
      console.error('Failed to load VCS connection summaries', e);
    }
  }
  // Auto-open Add Module modal if ?addModule=1 query param is present
  if (canAddModule.value && route.query.addModule === '1') {
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
        <UInput
          v-model="requiredProviderFilter"
          placeholder="Required provider..."
          icon="i-lucide-filter"
          class="w-48"
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
          v-if="canAddModule"
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
          v-else-if="!modules.length && !isLoading"
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
              searchQuery || requiredProviderFilter
                ? "Try adjusting your search terms"
                : "Get started by uploading your first module"
            }}
          </p>
        </div>

        <!-- Modules Grid -->
        <div v-else>
          <div class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            <div
              v-for="module in modules"
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
              Showing {{ modules.length }} of {{ totalCount }} modules
            </p>
            <UButton
              v-if="hasMore"
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
    <UModal v-if="canAddModule" v-model:open="isAddModuleOpen">
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

            <div class="border-t border-neutral-800 pt-4 space-y-4">
              <div class="flex items-center justify-between gap-3">
                <h4 class="text-xs font-semibold text-neutral-400 uppercase tracking-wide">Publish Method</h4>
                <div class="flex gap-2">
                  <UButton
                    v-if="canUploadModules"
                    label="Upload Zip"
                    size="xs"
                    :color="addModuleMode === 'upload' ? 'primary' : 'neutral'"
                    :variant="addModuleMode === 'upload' ? 'solid' : 'soft'"
                    @click="addModuleMode = 'upload'"
                  />
                  <UButton
                    v-if="canManageVcs"
                    label="Link GitHub"
                    size="xs"
                    icon="i-lucide-github"
                    :color="addModuleMode === 'github' ? 'primary' : 'neutral'"
                    :variant="addModuleMode === 'github' ? 'solid' : 'soft'"
                    @click="addModuleMode = 'github'"
                  />
                </div>
              </div>

              <div v-if="!linkToGitHub" class="space-y-3">
                <div class="grid grid-cols-2 gap-3">
                  <div>
                    <label class="block text-xs text-neutral-400 mb-1">Version <span class="text-red-400">*</span></label>
                    <UInput v-model="newVersion" placeholder="1.0.0" size="sm" />
                  </div>
                  <div>
                    <label class="block text-xs text-neutral-400 mb-1">Module Archive <span class="text-red-400">*</span></label>
                    <input
                      type="file"
                      accept=".zip,application/zip"
                      class="block w-full rounded-md border border-neutral-700 bg-neutral-900 px-3 py-2 text-sm text-neutral-200 file:mr-3 file:rounded-md file:border-0 file:bg-primary-500/20 file:px-3 file:py-1.5 file:text-primary-200"
                      @change="onModuleFileSelected"
                    />
                  </div>
                </div>
                <div class="rounded-lg border border-neutral-800 bg-neutral-900/60 p-3 text-xs text-neutral-400">
                  Upload a `.zip` archive containing your Terraform module. The archive must be readable and include at least one `.tf` file.
                </div>
                <p v-if="selectedModuleFile" class="text-xs text-neutral-500">
                  Selected file: <span class="text-neutral-300">{{ selectedModuleFile.name }}</span>
                </p>
              </div>

              <!-- GitHub Integration -->
              <div v-else class="space-y-3">
                <h4 class="text-xs font-semibold text-neutral-400 uppercase tracking-wide flex items-center gap-1.5">
                  <UIcon name="i-lucide-github" />
                  GitHub Integration
                </h4>
                <p class="text-xs text-neutral-500">Link a repository to auto-publish versions on Git tag push.</p>
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
            </div>

            <!-- Actions -->
            <div class="flex justify-end gap-2 border-t border-neutral-800 pt-4">
              <UButton label="Cancel" color="neutral" variant="ghost" size="sm" @click="isAddModuleOpen = false" />
              <UButton
                :label="linkToGitHub ? 'Create & Link' : 'Upload Module'"
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
