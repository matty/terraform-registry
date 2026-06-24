<script setup lang="ts">
import PublishModuleModal from "~/components/modules/PublishModuleModal.vue";
import { useDashboard } from "~/composables/useDashboard";
import type { Module, ModulesResponse } from "~/composables/useModules";
import type { VcsSourceCreateResponse } from "~/composables/useVcsSources";

definePageMeta({
  middleware: "auth",
});

const { getAuthHeaders } = useAuth();
const { hasPermission } = usePermissions();
const { isSidebarOpen } = useDashboard();
const { featureCreateModule } = useRuntimeConfig().public;
const canUploadModule = computed(() => hasPermission("modules.upload"));
const canManageVcs = computed(() => hasPermission("vcs.manage"));
const canOpenPublishModal = computed(() =>
  featureCreateModule && (canUploadModule.value || canManageVcs.value)
);

const modules = ref<Module[]>([]);
const isLoading = ref(false);
const isLoadingMore = ref(false);
const error = ref("");
const searchQuery = ref("");
const currentOffset = ref(0);
const canLoadMoreModules = ref(true);
const pageSizeOptions = [
  { label: "10", value: 10 },
  { label: "25", value: 25 },
  { label: "50", value: 50 },
];
const pageSize = ref(10);
const totalModules = ref(0);
const publishModalOpen = ref(false);

const openPublishModal = () => {
  if (!canOpenPublishModal.value) return;
  publishModalOpen.value = true;
};

const handleLinked = async (source: VcsSourceCreateResponse) => {
  refreshModules();
  await navigateTo(`/modules/${source.namespace}/${source.name}/${source.provider}`);
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

const moduleCountLabel = computed(() => {
  if (totalModules.value <= 0) return `${modules.value.length} / 0`;
  return `${Math.min(modules.value.length, totalModules.value)} / ${totalModules.value}`;
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
      `/v1/modules?offset=${offset}&limit=${pageSize.value}`,
      {
        headers: getAuthHeaders(),
      }
    );

    if (append) {
      modules.value.push(...response.modules);
    } else {
      modules.value = response.modules;
    }

    currentOffset.value = modules.value.length;
    const parsedTotal = Number.parseInt(response.meta?.total ?? "", 10);
    totalModules.value = Number.isNaN(parsedTotal) ? modules.value.length : parsedTotal;
    canLoadMoreModules.value = modules.value.length < totalModules.value;
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

const handlePageSizeChanged = () => {
  currentOffset.value = 0;
  fetchModules(0, false);
};

// Load modules on component mount
const route = useRoute();
onMounted(async () => {
  fetchModules();
  // Auto-open Add Module modal if ?addModule=1 query param is present
  if (canOpenPublishModal.value && route.query.addModule === '1') {
    openPublishModal();
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
        <USelect
          v-model="pageSize"
          :items="pageSizeOptions"
          value-key="value"
          label-key="label"
          size="sm"
          class="w-24"
          :disabled="isLoading || isLoadingMore"
          @update:model-value="handlePageSizeChanged"
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
          v-if="canOpenPublishModal"
          label="Add Module"
          icon="i-lucide-plus"
          color="primary"
          size="sm"
          @click="openPublishModal"
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
              {{ moduleCountLabel }} modules
            </p>
            <UButton
              v-if="canLoadMoreModules"
              @click="loadMoreModules"
              :loading="isLoadingMore"
              icon="i-lucide-plus"
              color="primary"
              variant="soft"
              size="sm"
            >
              Load more modules
            </UButton>
          </div>
        </div>
      </div>
    </div>
    <PublishModuleModal
      v-model:open="publishModalOpen"
      :allow-manual-upload="canUploadModule"
      :allow-vcs-link="canManageVcs"
      @published="refreshModules"
      @linked="handleLinked"
    />
  </div>
</template>
