<script setup lang="ts">
import { useDashboard } from "~/composables/useDashboard";
import { useModules, type Module } from "~/composables/useModules";

definePageMeta({
  middleware: "auth",
});

const { isSidebarOpen } = useDashboard();
const { listDeletedModules, restoreModuleVersion, purgeModuleVersion } = useModules();

const modules = ref<Module[]>([]);
const isLoading = ref(false);
const isLoadingMore = ref(false);
const error = ref("");
const searchQuery = ref("");
const currentOffset = ref(0);
const limit = 10;

// Modal state
const showConfirmModal = ref(false);
const confirmAction = ref<"restore" | "purge">("restore");
const selectedModule = ref<Module | null>(null);
const isProcessing = ref(false);

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

const fetchDeletedModules = async (offset = 0, append = false) => {
  try {
    if (!append) {
      isLoading.value = true;
    } else {
      isLoadingMore.value = true;
    }
    error.value = "";

    const response = await listDeletedModules(offset, limit);

    if (append) {
      modules.value.push(...response.modules);
    } else {
      modules.value = response.modules;
    }

    currentOffset.value = offset + limit;
  } catch (err: any) {
    error.value = err.message || "Failed to fetch deleted modules";
    console.error("Error fetching deleted modules:", err);
  } finally {
    isLoading.value = false;
    isLoadingMore.value = false;
  }
};

const refreshModules = () => {
  currentOffset.value = 0;
  fetchDeletedModules(0, false);
};

const loadMoreModules = () => {
  fetchDeletedModules(currentOffset.value, true);
};

const openConfirmModal = (module: Module, action: "restore" | "purge") => {
  selectedModule.value = module;
  confirmAction.value = action;
  showConfirmModal.value = true;
};

const handleConfirm = async () => {
  if (!selectedModule.value) return;

  isProcessing.value = true;
  const module = selectedModule.value;

  try {
    let success = false;
    if (confirmAction.value === "restore") {
      success = await restoreModuleVersion(
        module.namespace,
        module.name,
        module.provider,
        module.version
      );
    } else {
      success = await purgeModuleVersion(
        module.namespace,
        module.name,
        module.provider,
        module.version
      );
    }

    if (success) {
      modules.value = modules.value.filter((m) => m.id !== module.id);
    } else {
      error.value = `Failed to ${confirmAction.value} module`;
    }
  } catch (err: any) {
    error.value = err.message || `Failed to ${confirmAction.value} module`;
  } finally {
    isProcessing.value = false;
    showConfirmModal.value = false;
    selectedModule.value = null;
  }
};

onMounted(() => {
  fetchDeletedModules();
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
          <h1 class="page-header-title">Trash</h1>
          <p class="page-header-subtitle">Deleted modules available for restore or permanent deletion</p>
        </div>
        <div class="flex items-center gap-3">
          <div class="hidden md:flex items-center gap-2 px-3 py-1.5 bg-neutral-800/60 rounded-lg">
            <UIcon name="i-lucide-trash-2" class="text-red-400" />
            <span class="text-sm font-medium text-neutral-300">{{ modules.length }}</span>
          </div>
        </div>
      </div>
      <div class="page-header-actions">
        <UInput
          v-model="searchQuery"
          placeholder="Search deleted modules..."
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
          <p class="text-neutral-400 text-lg mt-6">Loading deleted modules...</p>
        </div>

        <!-- Empty State -->
        <div
          v-else-if="!filteredModules.length && !isLoading"
          class="text-center py-20 px-6"
        >
          <div
            class="w-24 h-24 mx-auto mb-6 bg-gradient-to-br from-neutral-800 to-neutral-900 rounded-3xl flex items-center justify-center ring-1 ring-neutral-700"
          >
            <UIcon name="i-lucide-trash-2" class="text-5xl text-neutral-500" />
          </div>
          <h3 class="text-xl font-semibold text-neutral-100 mb-2">
            Trash is empty
          </h3>
          <p class="text-neutral-400 max-w-sm mx-auto">
            {{
              searchQuery
                ? "No deleted modules match your search"
                : "Deleted modules will appear here"
            }}
          </p>
        </div>

        <!-- Modules Grid -->
        <div v-else>
          <div class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            <div
              v-for="module in filteredModules"
              :key="module.id"
              class="group relative overflow-hidden rounded-2xl bg-neutral-900/50 border border-neutral-800 p-5 opacity-75 hover:opacity-100 hover:border-primary-500/20 transition-all"
            >
              <!-- Decorative gradient -->
              <div class="absolute top-0 right-0 w-32 h-32 bg-primary-500/5 rounded-full blur-2xl -tranneutral-y-16 tranneutral-x-16"></div>
              
              <div class="relative flex items-start gap-4">
                <div
                  class="w-12 h-12 bg-gradient-to-br from-red-500/50 to-orange-600/50 rounded-xl flex items-center justify-center flex-shrink-0"
                >
                  <span class="text-white font-bold text-lg">{{
                    module.name.charAt(0).toUpperCase()
                  }}</span>
                </div>
                <div class="flex-1 min-w-0">
                  <h3 class="font-semibold text-neutral-300 truncate">
                    {{ module.name }}
                  </h3>
                  <p class="text-sm text-neutral-500 truncate">
                    {{ module.namespace }}
                  </p>
                </div>
                <UBadge variant="soft" color="error" size="xs">
                  v{{ module.version }}
                </UBadge>
              </div>

              <p class="relative mt-3 text-sm text-neutral-500 line-clamp-2">
                {{ module.description || "No description available" }}
              </p>

              <div class="relative mt-4 flex items-center justify-between">
                <div class="flex items-center gap-2">
                  <div class="flex items-center gap-1.5 px-2 py-1 bg-neutral-800/50 rounded-md">
                    <UIcon name="i-lucide-cloud" class="text-primary-400/50 text-xs" />
                    <span class="text-xs text-neutral-500">{{ module.provider }}</span>
                  </div>
                  <span class="text-xs text-neutral-600">{{
                    formatDate(module.published_at || module.publishedAt || '')
                  }}</span>
                </div>
                <div class="flex items-center gap-1 opacity-50 group-hover:opacity-100 transition-opacity">
                  <UButton
                    @click="openConfirmModal(module, 'restore')"
                    variant="soft"
                    size="xs"
                    color="success"
                    icon="i-lucide-undo-2"
                  >
                    Restore
                  </UButton>
                  <UButton
                    @click="openConfirmModal(module, 'purge')"
                    variant="ghost"
                    size="xs"
                    color="error"
                    icon="i-lucide-trash"
                  />
                </div>
              </div>
            </div>
          </div>

          <!-- Load More -->
          <div
            v-if="modules.length > 0"
            class="flex justify-center items-center gap-4 mt-8 pt-6 border-t border-neutral-800"
          >
            <p class="text-sm text-neutral-500">
              Showing {{ filteredModules.length }} of {{ modules.length }} deleted modules
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

    <!-- Confirmation Modal -->
    <UModal v-model:open="showConfirmModal">
      <template #content>
        <div class="w-full">
          <!-- Header -->
          <div class="flex items-center gap-4 px-6 py-5 border-b border-neutral-800/60">
            <div
              :class="[
                'w-12 h-12 rounded-xl flex items-center justify-center shrink-0',
                confirmAction === 'restore' ? 'bg-green-500/15' : 'bg-red-500/15'
              ]"
            >
              <UIcon
                :name="confirmAction === 'restore' ? 'i-lucide-undo-2' : 'i-lucide-triangle-alert'"
                :class="[
                  'text-2xl',
                  confirmAction === 'restore' ? 'text-green-400' : 'text-red-400'
                ]"
              />
            </div>
            <div>
              <h3 class="text-lg font-semibold text-neutral-100">
                {{ confirmAction === 'restore' ? 'Restore Module' : 'Permanently Delete' }}
              </h3>
              <p class="text-sm text-neutral-500">
                {{ confirmAction === 'restore' ? 'Make this module version available again' : 'This action is permanent and cannot be undone' }}
              </p>
            </div>
          </div>

          <!-- Body -->
          <div class="px-6 py-5">
            <div class="mb-4 px-3 py-2 rounded-lg bg-neutral-900/60 border border-neutral-800/60">
              <code class="text-sm text-neutral-200 font-medium">{{ selectedModule?.namespace }}/{{ selectedModule?.name }}/{{ selectedModule?.provider }}/{{ selectedModule?.version }}</code>
            </div>
            <p class="text-sm text-neutral-300 leading-relaxed">
              <template v-if="confirmAction === 'restore'">
                This will restore the module version and make it available for use in Terraform configurations again.
              </template>
              <template v-else>
                This will <span class="text-red-300 font-medium">permanently delete</span> the module version and all associated data. This action cannot be undone.
              </template>
            </p>
          </div>

          <!-- Footer -->
          <div class="flex justify-end gap-3 px-6 py-4 border-t border-neutral-800/60">
            <UButton
              variant="ghost"
              color="neutral"
              :disabled="isProcessing"
              @click="showConfirmModal = false"
            >
              Cancel
            </UButton>
            <UButton
              :loading="isProcessing"
              :color="confirmAction === 'restore' ? 'success' : 'error'"
              :icon="confirmAction === 'restore' ? 'i-lucide-undo-2' : 'i-lucide-trash'"
              @click="handleConfirm"
            >
              {{ confirmAction === 'restore' ? 'Restore' : 'Delete Forever' }}
            </UButton>
          </div>
        </div>
      </template>
    </UModal>
  </div>
</template>

<style scoped>
</style>
