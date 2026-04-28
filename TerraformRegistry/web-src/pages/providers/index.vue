<script setup lang="ts">
import PublishProviderVersionModal from "~/components/providers/PublishProviderVersionModal.vue";
import { extractErrorMessage } from "~/composables/useErrorMessage";
import { useDashboard } from "~/composables/useDashboard";
import type { TerraformProvider } from "~/composables/useProviders";
import { useProviders } from "~/composables/useProviders";

definePageMeta({
  middleware: "auth",
});

const { hasPermission } = usePermissions();
const { isSidebarOpen } = useDashboard();
const { listProviders } = useProviders();
const requestUrl = useRequestURL();

const providers = ref<TerraformProvider[]>([]);
const searchQuery = ref("");
const isLoading = ref(false);
const isLoadingMore = ref(false);
const error = ref("");
const currentOffset = ref(0);
const limit = 20;
const publishModalOpen = ref(false);

const canPublish = computed(() => hasPermission("providers.publish"));
const canDeleteProviders = computed(() => hasPermission("providers.delete"));

const filteredProviders = computed(() => {
  const query = searchQuery.value.trim().toLowerCase();
  if (!query) return providers.value;

  return providers.value.filter((provider) =>
    [
      provider.namespace,
      provider.type,
      provider.display_name,
      provider.description,
      provider.source_repository_url,
    ].some((value) => value?.toLowerCase().includes(query))
  );
});

const providerTitle = (provider: TerraformProvider) =>
  provider.display_name?.trim() || provider.type;

const providerSource = (provider: TerraformProvider) => {
  const host = requestUrl.host || "registry.example.com";
  return `${host}/${provider.namespace}/${provider.type}`;
};

const fetchProviders = async (offset = 0, append = false) => {
  try {
    if (append) {
      isLoadingMore.value = true;
    } else {
      isLoading.value = true;
    }
    error.value = "";

    const response = await listProviders(searchQuery.value, offset, limit);

    if (append) {
      providers.value.push(...response.providers);
    } else {
      providers.value = response.providers;
    }

    currentOffset.value = offset + limit;
  } catch (err) {
    error.value = extractErrorMessage(err, "Failed to fetch providers");
    console.error("Error fetching providers:", err);
  } finally {
    isLoading.value = false;
    isLoadingMore.value = false;
  }
};

const refreshProviders = async () => {
  currentOffset.value = 0;
  await fetchProviders(0, false);
};

const loadMoreProviders = async () => {
  await fetchProviders(currentOffset.value, true);
};

const handlePublished = async ({
  namespace,
  type,
  version,
}: {
  namespace: string;
  type: string;
  version: string;
}) => {
  void version;
  await refreshProviders();
  await navigateTo(`/providers/${namespace}/${type}`);
};

onMounted(async () => {
  await fetchProviders();
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
          <h1 class="page-header-title">Providers</h1>
          <p class="page-header-subtitle">
            Browse and publish Terraform providers
          </p>
        </div>
        <div
          class="hidden md:flex items-center gap-2 px-3 py-1.5 bg-neutral-800/60 rounded-lg"
        >
          <UIcon name="i-lucide-box" class="text-primary-400" />
          <span class="text-sm font-medium text-neutral-300">
            {{ providers.length }}
          </span>
        </div>
      </div>
      <div class="page-header-actions">
        <UInput
          v-model="searchQuery"
          placeholder="Search providers..."
          icon="i-lucide-search"
          class="w-64"
          size="sm"
          @keyup.enter="refreshProviders"
        />
        <UButton
          :loading="isLoading"
          icon="i-lucide-refresh-cw"
          color="neutral"
          variant="ghost"
          size="sm"
          @click="refreshProviders"
        />
        <UButton
          v-if="canPublish"
          label="Add Provider"
          icon="i-lucide-plus"
          color="primary"
          size="sm"
          @click="publishModalOpen = true"
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
          v-if="isLoading && !providers.length"
          class="flex flex-col justify-center items-center py-20"
        >
          <div class="relative">
            <div
              class="w-16 h-16 border-4 border-primary-500/20 rounded-full"
            />
            <div
              class="w-16 h-16 border-4 border-transparent border-t-primary-500 rounded-full animate-spin absolute inset-0"
            />
          </div>
          <p class="text-neutral-400 text-lg mt-6">Loading providers...</p>
        </div>

        <!-- Empty State -->
        <div
          v-else-if="!filteredProviders.length && !isLoading"
          class="text-center py-20 px-6"
        >
          <div
            class="w-24 h-24 mx-auto mb-6 bg-gradient-to-br from-neutral-800 to-neutral-900 rounded-3xl flex items-center justify-center ring-1 ring-neutral-700"
          >
            <UIcon name="i-lucide-box" class="text-5xl text-neutral-500" />
          </div>
          <h3 class="text-xl font-semibold text-neutral-100 mb-2">
            No providers found
          </h3>
          <p class="text-neutral-400 max-w-sm mx-auto">
            {{
              searchQuery
                ? "Try adjusting your search terms"
                : "Get started by publishing your first provider"
            }}
          </p>
        </div>

        <!-- Providers Grid -->
        <div v-else>
          <div class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            <div
              v-for="provider in filteredProviders"
              :key="provider.id"
              class="group relative overflow-hidden rounded-2xl bg-neutral-900/50 border border-neutral-800 p-5 hover:border-primary-500/30 hover:ring-1 hover:ring-primary-500/10 hover:bg-neutral-800/50 transition-all cursor-pointer"
              @click="
                navigateTo(`/providers/${provider.namespace}/${provider.type}`)
              "
            >
              <div class="relative flex items-start gap-4">
                <div
                  class="w-12 h-12 bg-gradient-to-br from-neutral-600 to-neutral-800 rounded-xl flex items-center justify-center flex-shrink-0 shadow-lg shadow-black/30"
                >
                  <span class="text-white font-bold text-lg">
                    {{ providerTitle(provider).charAt(0).toUpperCase() }}
                  </span>
                </div>
                <div class="flex-1 min-w-0">
                  <h3
                    class="font-semibold text-white truncate group-hover:text-primary-300 transition-colors"
                  >
                    {{ providerTitle(provider) }}
                  </h3>
                  <p class="text-sm text-neutral-500 truncate">
                    {{ provider.namespace }}/{{ provider.type }}
                  </p>
                </div>
                <UIcon
                  name="i-lucide-chevron-right"
                  class="text-neutral-600 group-hover:text-primary-400 group-hover:translate-x-1 transition-all flex-shrink-0"
                />
              </div>

              <p class="relative mt-3 text-sm text-neutral-400 line-clamp-2">
                {{ provider.description || "No description available" }}
              </p>

              <div class="relative mt-4 space-y-2">
                <div
                  class="flex items-center gap-1.5 px-2 py-1 bg-neutral-800/50 rounded-md min-w-0"
                >
                  <UIcon
                    name="i-lucide-globe"
                    class="text-primary-400 text-xs flex-shrink-0"
                  />
                  <span class="text-xs text-neutral-400 truncate">
                    {{ providerSource(provider) }}
                  </span>
                </div>
                <a
                  v-if="provider.source_repository_url"
                  :href="provider.source_repository_url"
                  target="_blank"
                  rel="noopener noreferrer"
                  class="flex items-center gap-1.5 text-xs text-neutral-500 hover:text-primary-300 transition-colors min-w-0"
                  @click.stop
                >
                  <UIcon name="i-lucide-git-branch" class="flex-shrink-0" />
                  <span class="truncate">
                    {{ provider.source_repository_url }}
                  </span>
                </a>
              </div>
            </div>
          </div>

          <!-- Load More -->
          <div
            v-if="providers.length > 0"
            class="flex justify-center items-center gap-4 mt-8 pt-6 border-t border-neutral-800"
          >
            <p class="text-sm text-neutral-500">
              Showing {{ filteredProviders.length }} of
              {{ providers.length }} providers
            </p>
            <UButton
              :loading="isLoadingMore"
              variant="soft"
              size="sm"
              @click="loadMoreProviders"
            >
              Load More
            </UButton>
          </div>
        </div>
      </div>
    </div>

    <PublishProviderVersionModal
      v-model:open="publishModalOpen"
      mode="new"
      :allow-create-provider="canPublish"
      :allow-manage-keys="hasPermission('providers.keys.manage')"
      :allow-delete-platforms="canDeleteProviders"
      @published="handlePublished"
      @provider-created="refreshProviders"
    />
  </div>
</template>
