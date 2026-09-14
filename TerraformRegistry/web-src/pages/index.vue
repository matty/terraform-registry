<script setup lang="ts">
import ModuleListRow from "~/components/modules/ModuleListRow.vue";
import ModuleListToolbar from "~/components/modules/ModuleListToolbar.vue";
import PublishModuleModal from "~/components/modules/PublishModuleModal.vue";
import { useDashboard } from "~/composables/useDashboard";
import { extractErrorMessage } from "~/composables/useErrorMessage";
import type {
  Module,
  ModuleSortField,
  ModuleSortOrder,
} from "~/composables/useModules";
import { useModules } from "~/composables/useModules";
import type { VcsSourceCreateResponse } from "~/composables/useVcsSources";

definePageMeta({
  middleware: "auth",
});

const DEFAULT_SORT = "name:asc";
const DEFAULT_PAGE_SIZE = 25;
const PAGE_SIZES = [10, 25, 50, 100];

const route = useRoute();
const router = useRouter();
const { hasPermission } = usePermissions();
const { isSidebarOpen } = useDashboard();
const { listModules, getModuleFacets } = useModules();
const { featureCreateModule } = useRuntimeConfig().public;

const canUploadModule = computed(() => hasPermission("modules.upload"));
const canManageVcs = computed(() => hasPermission("vcs.manage"));
const canOpenPublishModal = computed(
  () => featureCreateModule && (canUploadModule.value || canManageVcs.value)
);

const readQuery = (key: string, fallback = "") => {
  const value = route.query[key];
  return typeof value === "string" ? value : fallback;
};

const readNumericQuery = (key: string, fallback: number, allowed?: number[]) => {
  const parsed = Number(readQuery(key));
  if (!Number.isFinite(parsed) || parsed < 1) return fallback;
  if (allowed && !allowed.includes(parsed)) return fallback;
  return Math.floor(parsed);
};

// Filter state mirrors the URL so a filtered view can be shared and survives a reload.
const searchInput = ref(readQuery("q"));
const search = refDebounced(searchInput, 300);
const namespace = ref(readQuery("namespace"));
const provider = ref(readQuery("provider"));
const sort = ref(readQuery("sort", DEFAULT_SORT));
const pageSize = ref(
  readNumericQuery("size", DEFAULT_PAGE_SIZE, PAGE_SIZES)
);
const page = ref(readNumericQuery("page", 1));

const modules = ref<Module[]>([]);
const namespaces = ref<string[]>([]);
const providers = ref<string[]>([]);
const total = ref(0);
const isLoading = ref(false);
const hasLoadedOnce = ref(false);
const error = ref("");
const publishModalOpen = ref(false);

const pageCount = computed(() =>
  Math.max(1, Math.ceil(total.value / pageSize.value))
);

const rangeStart = computed(() =>
  total.value === 0 ? 0 : (page.value - 1) * pageSize.value + 1
);

const rangeEnd = computed(() =>
  Math.min(page.value * pageSize.value, total.value)
);

const hasActiveFilters = computed(
  () => Boolean(search.value || namespace.value || provider.value)
);

const pageSizeOptions = PAGE_SIZES.map((size) => ({
  label: `${size} per page`,
  value: size,
}));

const fetchModules = async () => {
  isLoading.value = true;
  error.value = "";

  const [sortField, sortOrder] = sort.value.split(":");

  try {
    const response = await listModules({
      q: search.value || undefined,
      namespace: namespace.value || undefined,
      provider: provider.value || undefined,
      sort: sortField as ModuleSortField,
      order: sortOrder as ModuleSortOrder,
      offset: (page.value - 1) * pageSize.value,
      limit: pageSize.value,
    });

    modules.value = response.modules;
    const parsedTotal = Number.parseInt(response.meta?.total ?? "", 10);
    total.value = Number.isNaN(parsedTotal) ? response.modules.length : parsedTotal;

    // A stale deep link can point past the end of the results; pull it back.
    if (!modules.value.length && total.value > 0 && page.value > pageCount.value) {
      page.value = pageCount.value;
    }
  } catch (err) {
    error.value = extractErrorMessage(err, "Failed to fetch modules");
    modules.value = [];
    total.value = 0;
    console.error("Error fetching modules:", err);
  } finally {
    isLoading.value = false;
    hasLoadedOnce.value = true;
  }
};

const fetchFacets = async () => {
  const facets = await getModuleFacets();
  namespaces.value = facets.namespaces;
  providers.value = facets.providers;
};

const refresh = async () => {
  await Promise.all([fetchModules(), fetchFacets()]);
};

const clearFilters = () => {
  searchInput.value = "";
  namespace.value = "";
  provider.value = "";
};

const openPublishModal = () => {
  if (!canOpenPublishModal.value) return;
  publishModalOpen.value = true;
};

const handlePublished = async () => {
  await refresh();
};

const handleLinked = async (source: VcsSourceCreateResponse) => {
  await navigateTo(
    `/modules/${source.namespace}/${source.name}/${source.provider}`
  );
};

// Changing what is being looked at always returns to the first page; the page
// watcher then performs the single fetch.
watch([search, namespace, provider, sort, pageSize], () => {
  if (page.value !== 1) {
    page.value = 1;
    return;
  }
  fetchModules();
});

watch(page, () => fetchModules());

watch([search, namespace, provider, sort, pageSize, page], () => {
  const query: Record<string, string> = {};

  if (search.value) query.q = search.value;
  if (namespace.value) query.namespace = namespace.value;
  if (provider.value) query.provider = provider.value;
  if (sort.value !== DEFAULT_SORT) query.sort = sort.value;
  if (pageSize.value !== DEFAULT_PAGE_SIZE) query.size = String(pageSize.value);
  if (page.value !== 1) query.page = String(page.value);

  router.replace({ query });
});

onMounted(async () => {
  await refresh();

  if (canOpenPublishModal.value && route.query.addModule === "1") {
    openPublishModal();
  }
});
</script>

<template>
  <div class="flex h-full flex-col">
    <!-- Mobile menu button -->
    <div class="px-4 pt-4 lg:hidden">
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
          <p class="page-header-subtitle">
            Browse and manage your Terraform modules
          </p>
        </div>
        <div class="flex items-center gap-2">
          <div
            class="hidden items-center gap-2 rounded-lg bg-neutral-800/60 px-3 py-1.5 md:flex"
          >
            <UIcon name="i-lucide-package" class="text-primary-400" />
            <span class="text-sm font-medium text-neutral-300 tabular-nums">
              {{ total }}
            </span>
          </div>
          <UButton
            :loading="isLoading"
            icon="i-lucide-refresh-cw"
            color="neutral"
            variant="ghost"
            size="sm"
            aria-label="Refresh modules"
            @click="refresh"
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
    </div>
    <div class="page-divider" />

    <ModuleListToolbar
      v-model:search="searchInput"
      v-model:namespace="namespace"
      v-model:provider="provider"
      v-model:sort="sort"
      :namespaces="namespaces"
      :providers="providers"
    />

    <!-- Body -->
    <div class="flex-1 overflow-y-auto">
      <div class="px-6 pb-6">
        <UAlert
          v-if="error"
          color="error"
          variant="soft"
          :title="error"
          icon="i-lucide-alert-circle"
          class="mb-6"
        />

        <div
          class="overflow-hidden rounded-xl border border-neutral-800 bg-neutral-900/50"
        >
          <!-- Column header strip; pointless above an empty body -->
          <div
            v-if="modules.length || (isLoading && !hasLoadedOnce)"
            class="border-b border-neutral-800 bg-neutral-900/60 px-5 py-2.5 text-xs font-medium uppercase tracking-wider text-neutral-500"
          >
            <div class="module-row-grid module-row-indent">
              <span>Module</span>
              <span class="hidden sm:block">Provider</span>
              <span>Latest</span>
              <span class="hidden text-right lg:block">Versions</span>
              <span class="hidden text-right lg:block">Published</span>
              <span />
            </div>
          </div>

          <!-- Loading skeletons (first load only; later fetches keep rows in place) -->
          <div v-if="isLoading && !hasLoadedOnce" class="divide-y divide-neutral-800">
            <div v-for="n in 8" :key="n" class="flex gap-3.5 px-5 py-3.5">
              <USkeleton class="h-9 w-9 shrink-0 rounded-lg" />
              <div class="flex-1 space-y-2 py-1">
                <USkeleton class="h-3.5 w-48" />
                <USkeleton class="h-3 w-3/4" />
              </div>
            </div>
          </div>

          <!-- Empty states -->
          <div
            v-else-if="!modules.length"
            class="px-6 py-16 text-center"
          >
            <div
              class="mx-auto mb-5 flex h-16 w-16 items-center justify-center rounded-2xl bg-neutral-800/70 ring-1 ring-inset ring-neutral-700/60"
            >
              <UIcon
                :name="hasActiveFilters ? 'i-lucide-search-x' : 'i-lucide-package'"
                class="text-3xl text-neutral-500"
              />
            </div>
            <h3 class="mb-1.5 text-base font-semibold text-neutral-100">
              {{ hasActiveFilters ? "No modules match these filters" : "No modules yet" }}
            </h3>
            <p class="mx-auto max-w-sm text-sm text-neutral-400">
              {{
                hasActiveFilters
                  ? "Widen the search or clear a filter to see more."
                  : "Publish a module to make it available to your Terraform configurations."
              }}
            </p>
            <UButton
              v-if="hasActiveFilters"
              label="Clear filters"
              color="neutral"
              variant="soft"
              size="sm"
              class="mt-5"
              @click="clearFilters"
            />
            <UButton
              v-else-if="canOpenPublishModal"
              label="Add Module"
              icon="i-lucide-plus"
              color="primary"
              size="sm"
              class="mt-5"
              @click="openPublishModal"
            />
          </div>

          <!-- Rows -->
          <div
            v-else
            class="divide-y divide-neutral-800 transition-opacity"
            :class="isLoading ? 'opacity-60' : 'opacity-100'"
          >
            <ModuleListRow
              v-for="module in modules"
              :key="module.id"
              :module="module"
            />
          </div>
        </div>

        <!-- Pagination -->
        <div
          v-if="modules.length"
          class="mt-4 flex flex-wrap items-center justify-between gap-4"
        >
          <p class="text-sm text-neutral-500 tabular-nums">
            Showing {{ rangeStart }}–{{ rangeEnd }} of {{ total }}
          </p>

          <div class="flex items-center gap-3">
            <USelect
              v-model="pageSize"
              :items="pageSizeOptions"
              size="sm"
              class="w-36"
            />
            <UPagination
              v-if="pageCount > 1"
              v-model:page="page"
              :total="total"
              :items-per-page="pageSize"
              :sibling-count="1"
              size="sm"
              color="neutral"
              variant="ghost"
              active-color="neutral"
              active-variant="subtle"
            />
          </div>
        </div>
      </div>
    </div>

    <PublishModuleModal
      v-model:open="publishModalOpen"
      :allow-manual-upload="canUploadModule"
      :allow-vcs-link="canManageVcs"
      @published="handlePublished"
      @linked="handleLinked"
    />
  </div>
</template>
