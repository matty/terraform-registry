<script setup lang="ts">
import type { Ref } from "vue";

defineProps<{
  namespaces: string[];
  providers: string[];
}>();

const search = defineModel<string>("search", { required: true });
const namespace = defineModel<string>("namespace", { required: true });
const provider = defineModel<string>("provider", { required: true });
const sort = defineModel<string>("sort", { required: true });

// Reka UI rejects an empty-string select value, and "*" can never be a valid
// Terraform namespace or provider, so it is safe to stand in for "no filter".
const ALL = "*";

const selection = (model: Ref<string>) =>
  computed({
    get: () => model.value || ALL,
    set: (value: string) => (model.value = value === ALL ? "" : value),
  });

const namespaceSelection = selection(namespace);
const providerSelection = selection(provider);

const sortOptions = [
  { label: "Name A–Z", value: "name:asc" },
  { label: "Name Z–A", value: "name:desc" },
  { label: "Newest first", value: "published:desc" },
  { label: "Oldest first", value: "published:asc" },
  { label: "Most versions", value: "versions:desc" },
];

const toOptions = (values: string[], allLabel: string) => [
  { label: allLabel, value: ALL },
  ...values.map((value) => ({ label: value, value })),
];

const activeFilters = computed(() => {
  const active: { key: string; label: string; clear: () => void }[] = [];

  if (search.value)
    active.push({
      key: "search",
      label: `“${search.value}”`,
      clear: () => (search.value = ""),
    });

  if (namespace.value)
    active.push({
      key: "namespace",
      label: namespace.value,
      clear: () => (namespace.value = ""),
    });

  if (provider.value)
    active.push({
      key: "provider",
      label: provider.value,
      clear: () => (provider.value = ""),
    });

  return active;
});

const clearAll = () => {
  search.value = "";
  namespace.value = "";
  provider.value = "";
};
</script>

<template>
  <div class="px-6 py-4">
    <div class="flex flex-wrap items-center gap-3">
      <UInput
        v-model="search"
        placeholder="Search modules"
        icon="i-lucide-search"
        size="sm"
        class="w-full sm:w-72"
        :ui="{ trailing: 'pe-1' }"
      >
        <template v-if="search" #trailing>
          <UButton
            icon="i-lucide-x"
            color="neutral"
            variant="link"
            size="xs"
            aria-label="Clear search"
            @click="search = ''"
          />
        </template>
      </UInput>

      <USelect
        v-model="namespaceSelection"
        :items="toOptions(namespaces, 'All namespaces')"
        size="sm"
        icon="i-lucide-folder"
        class="w-44"
      />

      <USelect
        v-model="providerSelection"
        :items="toOptions(providers, 'All providers')"
        size="sm"
        icon="i-lucide-cloud"
        class="w-44"
      />

      <USelect
        v-model="sort"
        :items="sortOptions"
        size="sm"
        icon="i-lucide-arrow-up-down"
        class="w-40 sm:ml-auto"
      />
    </div>

    <div v-if="activeFilters.length" class="mt-3 flex flex-wrap items-center gap-2">
      <span
        v-for="filter in activeFilters"
        :key="filter.key"
        class="inline-flex items-center gap-1.5 rounded-full border border-neutral-700/70 bg-neutral-800/50 py-1 pe-1 ps-2.5 text-xs text-neutral-300"
      >
        {{ filter.label }}
        <button
          type="button"
          class="rounded-full p-0.5 text-neutral-500 transition hover:bg-neutral-700/60 hover:text-neutral-200 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-neutral-500"
          :aria-label="`Remove filter ${filter.label}`"
          @click="filter.clear()"
        >
          <UIcon name="i-lucide-x" class="block text-xs" />
        </button>
      </span>

      <UButton
        v-if="activeFilters.length > 1"
        label="Clear all"
        color="neutral"
        variant="link"
        size="xs"
        @click="clearAll"
      />
    </div>
  </div>
</template>
