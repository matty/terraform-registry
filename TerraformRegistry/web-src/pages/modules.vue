<template>
  <div class="min-h-screen bg-gray-50 dark:bg-gray-900">
    <!-- Header Section -->
    <div
      class="bg-white dark:bg-gray-800 border-b border-gray-200 dark:border-gray-700"
    >
      <div class="max-w-7xl mx-auto px-4 py-6">
        <div class="flex justify-between items-center">
          <div class="flex items-center space-x-4">
            <div
              class="w-12 h-12 bg-gradient-to-br from-blue-500 to-indigo-600 rounded-xl flex items-center justify-center shadow-lg"
            >
              <Icon
                name="material-symbols:engineering"
                class="text-xl text-white"
              />
            </div>
            <div>
              <h1 class="text-3xl font-bold text-gray-900 dark:text-white">
                Terraform Modules
              </h1>
              <p class="text-gray-600 dark:text-gray-400 mt-1">
                Manage and browse your infrastructure modules
              </p>
            </div>
          </div>

          <UButton @click="logout" variant="outline" color="error" size="lg">
            Logout
          </UButton>
        </div>
      </div>
    </div>

    <!-- Main Content -->
    <div class="max-w-7xl mx-auto px-4 py-8">
      <UCard class="shadow-lg">
        <template #header>
          <div
            class="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4"
          >
            <div class="flex items-center space-x-3">
              <div
                class="w-8 h-8 bg-blue-100 dark:bg-blue-900 rounded-lg flex items-center justify-center"
              >
                <Icon
                  name="material-symbols:package-2"
                  class="text-blue-600 dark:text-blue-400"
                />
              </div>
              <h2 class="text-xl font-semibold text-gray-900 dark:text-white">
                Module Registry
              </h2>
            </div>

            <div
              class="flex flex-col sm:flex-row items-stretch sm:items-center gap-3 w-full sm:w-auto"
            >
              <UInput
                v-model="searchQuery"
                placeholder="Search modules..."
                size="md"
                class="w-full sm:w-72"
              />
              <UButton
                @click="refreshModules"
                :loading="isLoading"
                variant="outline"
                size="md"
              >
                Refresh
              </UButton>
            </div>
          </div>
        </template>

        <!-- Error State -->
        <UAlert
          v-if="error"
          color="error"
          variant="soft"
          :title="error"
          class="mb-6"
        />
        <!-- Loading State -->
        <div
          v-if="isLoading && !modules.length"
          class="flex flex-col justify-center items-center py-16"
        >
          <Icon
            name="material-symbols:refresh"
            class="animate-spin text-4xl mb-4 text-blue-500"
          />
          <p class="text-gray-600 dark:text-gray-400 text-lg">
            Loading modules...
          </p>
        </div>
        <!-- Empty State -->
        <div
          v-else-if="!filteredModules.length && !isLoading"
          class="text-center py-16"
        >
          <div
            class="w-20 h-20 mx-auto mb-6 bg-gray-100 dark:bg-gray-800 rounded-2xl flex items-center justify-center"
          >
            <Icon
              name="material-symbols:package-2"
              class="text-4xl text-gray-400"
            />
          </div>
          <h3 class="text-xl font-semibold text-gray-900 dark:text-white mb-2">
            No modules found
          </h3>
          <p class="text-gray-600 dark:text-gray-400">
            {{
              searchQuery
                ? "Try adjusting your search terms"
                : "Get started by adding your first module"
            }}
          </p>
        </div>

        <!-- Modules Table -->
        <div v-else class="overflow-hidden">
          <div class="overflow-x-auto">
            <table
              class="min-w-full divide-y divide-gray-200 dark:divide-gray-700"
            >
              <thead class="bg-gray-50 dark:bg-gray-800">
                <tr>
                  <th
                    class="px-6 py-4 text-left text-xs font-semibold text-gray-600 dark:text-gray-300 uppercase tracking-wider"
                  >
                    Module
                  </th>
                  <th
                    class="px-6 py-4 text-left text-xs font-semibold text-gray-600 dark:text-gray-300 uppercase tracking-wider"
                  >
                    Provider
                  </th>
                  <th
                    class="px-6 py-4 text-left text-xs font-semibold text-gray-600 dark:text-gray-300 uppercase tracking-wider"
                  >
                    Description
                  </th>
                  <th
                    class="px-6 py-4 text-left text-xs font-semibold text-gray-600 dark:text-gray-300 uppercase tracking-wider"
                  >
                    Published
                  </th>
                  <th
                    class="px-6 py-4 text-left text-xs font-semibold text-gray-600 dark:text-gray-300 uppercase tracking-wider"
                  >
                    Versions
                  </th>
                  <th
                    class="px-6 py-4 text-right text-xs font-semibold text-gray-600 dark:text-gray-300 uppercase tracking-wider"
                  >
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody
                class="bg-white dark:bg-gray-900 divide-y divide-gray-200 dark:divide-gray-700"
              >
                <tr
                  v-for="module in filteredModules"
                  :key="module.id"
                  class="hover:bg-gray-50 dark:hover:bg-gray-800 transition-colors"
                >
                  <td class="px-6 py-4">
                    <div class="flex items-center">
                      <div
                        class="w-10 h-10 bg-gradient-to-br from-blue-500 to-indigo-600 rounded-lg flex items-center justify-center mr-4"
                      >
                        <div class="text-white text-sm font-bold">
                          {{ module.name.charAt(0).toUpperCase() }}
                        </div>
                      </div>
                      <div>
                        <div
                          class="text-sm font-semibold text-gray-900 dark:text-white"
                        >
                          {{ module.name }}
                        </div>
                        <div class="text-sm text-gray-500 dark:text-gray-400">
                          {{ module.namespace }}
                        </div>
                        <UBadge variant="subtle" size="xs" class="mt-1">
                          v{{ module.version }}
                        </UBadge>
                      </div>
                    </div>
                  </td>
                  <td class="px-6 py-4">
                    <UBadge variant="outline" color="primary">
                      {{ module.provider }}
                    </UBadge>
                  </td>
                  <td class="px-6 py-4">
                    <p
                      class="text-sm text-gray-900 dark:text-white max-w-xs"
                      :title="module.description"
                    >
                      {{ module.description || "No description available" }}
                    </p>
                  </td>
                  <td class="px-6 py-4">
                    <div class="text-sm text-gray-600 dark:text-gray-400">
                      {{ formatDate(module.published_at) }}
                    </div>
                  </td>
                  <td class="px-6 py-4">
                    <div class="flex flex-wrap gap-1 max-w-32">
                      <UBadge
                        v-for="version in module.versions.slice(0, 3)"
                        :key="version"
                        variant="outline"
                        size="xs"
                        color="neutral"
                      >
                        {{ version }}
                      </UBadge>
                      <UBadge
                        v-if="module.versions.length > 3"
                        variant="outline"
                        size="xs"
                        color="neutral"
                      >
                        +{{ module.versions.length - 3 }}
                      </UBadge>
                    </div>
                  </td>
                  <td class="px-6 py-4 text-right">
                    <UButton
                      :to="module.download_url"
                      external
                      target="_blank"
                      variant="ghost"
                      size="sm"
                      color="primary"
                    >
                      ⬇ Download
                    </UButton>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <template #footer v-if="modules.length > 0">
          <div
            class="flex flex-col sm:flex-row justify-between items-center gap-4 bg-gray-50 dark:bg-gray-800 px-6 py-4 rounded-b-lg"
          >
            <div class="flex items-center space-x-2">
              <div class="w-2 h-2 bg-green-500 rounded-full"></div>
              <p class="text-sm text-gray-600 dark:text-gray-400">
                Showing {{ filteredModules.length }} of
                {{ modules.length }} modules
              </p>
            </div>
            <UButton
              @click="loadMoreModules"
              :loading="isLoadingMore"
              variant="outline"
              size="sm"
            >
              Load More Modules
            </UButton>
          </div>
        </template>
      </UCard>
    </div>
  </div>
</template>

<script setup lang="ts">
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

const { logout, getAuthHeaders } = useAuth();

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
