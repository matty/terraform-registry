<script setup lang="ts">
import { useDashboard } from "~/composables/useDashboard";

definePageMeta({
  middleware: "auth",
});

interface Module {
  id: string;
  owner: string;
  namespace: string;
  name: string;
  version: string;
  provider: string;
  description: string;
  published_at: string;
  versions: string[];
  download_url: string;
}

interface ModulesResponse {
  modules: Module[];
}

const { getAuthHeaders } = useAuth();
const { isSidebarOpen } = useDashboard();

const modules = ref<Module[]>([]);
const isLoading = ref(false);
const isLoadingMore = ref(false);
const error = ref("");
const searchQuery = ref("");
const currentOffset = ref(0);
const limit = 10;

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
onMounted(() => {
  fetchModules();
});
</script>

<template>
  <div class="flex flex-col h-full">
    <!-- Header -->
    <header
      class="flex items-center justify-between px-4 py-3 border-b border-neutral-200 dark:border-neutral-800 bg-white/50 dark:bg-neutral-900/50 backdrop-blur sticky top-0 z-10"
    >
      <div class="flex items-center gap-3">
        <UButton
          icon="i-lucide-menu"
          variant="ghost"
          color="neutral"
          class="lg:hidden"
          @click="isSidebarOpen = true"
        />
        <h1 class="text-xl font-semibold text-slate-900 dark:text-slate-100">
          Modules
        </h1>
      </div>

      <div class="flex items-center gap-2">
        <UInput
          v-model="searchQuery"
          placeholder="Search modules..."
          icon="i-lucide-search"
          class="hidden sm:block w-64"
        />
        <UButton
          @click="refreshModules"
          :loading="isLoading"
          icon="i-lucide-refresh-cw"
          color="neutral"
          variant="ghost"
        />
      </div>
    </header>

    <!-- Body -->
    <div class="p-4 flex-1">
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
        <UIcon
          name="i-lucide-loader-2"
          class="animate-spin text-5xl mb-4 text-blue-500"
        />
        <p class="text-slate-400 text-lg">Loading modules...</p>
      </div>

      <!-- Empty State -->
      <div
        v-else-if="!filteredModules.length && !isLoading"
        class="text-center py-20 px-6"
      >
        <div
          class="w-24 h-24 mx-auto mb-6 bg-neutral-800 rounded-3xl flex items-center justify-center"
        >
          <UIcon name="i-lucide-package" class="text-5xl text-slate-500" />
        </div>
        <h3 class="text-xl font-semibold text-slate-100 mb-2">
          No modules found
        </h3>
        <p class="text-slate-400 max-w-sm mx-auto">
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
          <UCard
            v-for="module in filteredModules"
            :key="module.id"
            class="hover:ring-1 hover:ring-blue-500/50 transition-all"
          >
            <div class="flex items-start gap-4">
              <div
                class="w-12 h-12 bg-blue-600 rounded-xl flex items-center justify-center flex-shrink-0"
              >
                <span class="text-white font-bold text-lg">{{
                  module.name.charAt(0).toUpperCase()
                }}</span>
              </div>
              <div class="flex-1 min-w-0">
                <h3 class="font-semibold text-slate-100 truncate">
                  {{ module.name }}
                </h3>
                <p class="text-sm text-slate-400 truncate">
                  {{ module.namespace }}
                </p>
              </div>
              <UBadge variant="subtle" color="primary" size="sm">
                v{{ module.version }}
              </UBadge>
            </div>

            <p class="mt-3 text-sm text-slate-400 line-clamp-2">
              {{ module.description || "No description available" }}
            </p>

            <div class="mt-4 flex items-center justify-between">
              <div class="flex items-center gap-2">
                <UBadge variant="outline" color="neutral" size="xs">
                  {{ module.provider }}
                </UBadge>
                <span class="text-xs text-slate-500">{{
                  formatDate(module.published_at)
                }}</span>
              </div>
              <UButton
                :to="module.download_url"
                external
                target="_blank"
                variant="ghost"
                size="xs"
                color="primary"
                icon="i-lucide-download"
              >
                Download
              </UButton>
            </div>
          </UCard>
        </div>

        <!-- Load More -->
        <div
          v-if="modules.length > 0"
          class="flex justify-center items-center gap-4 mt-8 pt-6 border-t border-neutral-700"
        >
          <p class="text-sm text-slate-400">
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
</template>
