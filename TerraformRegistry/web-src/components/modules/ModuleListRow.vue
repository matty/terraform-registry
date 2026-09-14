<script setup lang="ts">
import type { Module } from "~/composables/useModules";
import { useProviderIdentity } from "~/composables/useProviderIdentity";

const props = defineProps<{
  module: Module;
}>();

const { providerIdentity } = useProviderIdentity();
const requestUrl = useRequestURL();

const identity = computed(() => providerIdentity(props.module.provider));

const moduleRoute = computed(
  () =>
    `/modules/${props.module.namespace}/${props.module.name}/${props.module.provider}`
);

const coordinate = computed(
  () => `${props.module.namespace}/${props.module.name}`
);

// The full string an engineer pastes into a module block's `source`.
const moduleSource = computed(() => {
  const host = requestUrl.host || "registry.example.com";
  return `${host}/${props.module.namespace}/${props.module.name}/${props.module.provider}`;
});

const versionCount = computed(() => props.module.versions?.length ?? 1);

const publishedLabel = computed(() => {
  const parsed = new Date(props.module.published_at);
  if (Number.isNaN(parsed.getTime())) return "Unknown";

  return parsed.toLocaleDateString("en-US", {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
});

const copied = ref(false);
let copyResetTimer: ReturnType<typeof setTimeout> | undefined;

const copySource = async () => {
  try {
    await navigator.clipboard.writeText(moduleSource.value);
    copied.value = true;
    clearTimeout(copyResetTimer);
    copyResetTimer = setTimeout(() => {
      copied.value = false;
    }, 1600);
  } catch (err) {
    console.error("Could not copy module source:", err);
  }
};

onBeforeUnmount(() => clearTimeout(copyResetTimer));
</script>

<template>
  <NuxtLink
    :to="moduleRoute"
    class="group block px-5 py-3 transition-colors hover:bg-neutral-800/40 focus-visible:bg-neutral-800/40 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-inset focus-visible:ring-neutral-600"
  >
    <div class="flex gap-3.5">
      <!-- Provider mark: the one place colour appears, because which cloud a
           module targets is what the eye scans for first. -->
      <span
        class="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-neutral-800/70 ring-1 ring-inset ring-neutral-700/60 transition group-hover:ring-neutral-600"
      >
        <UIcon
          :name="identity.icon"
          class="text-lg"
          :style="{ color: identity.color }"
        />
      </span>

      <div class="min-w-0 flex-1">
        <div class="module-row-grid">
          <h3
            class="truncate font-medium text-neutral-100 transition-colors group-hover:text-white"
          >
            {{ module.name }}
          </h3>

          <span class="hidden truncate text-sm text-neutral-400 sm:block">
            {{ module.provider }}
          </span>

          <span
            class="inline-flex items-center justify-self-start rounded-md border border-neutral-700/70 bg-neutral-800/50 px-1.5 py-0.5 font-mono text-xs text-neutral-200 tabular-nums"
          >
            v{{ module.version }}
          </span>

          <span
            class="hidden text-right text-sm tabular-nums lg:block"
            :class="versionCount > 1 ? 'text-neutral-400' : 'text-neutral-600'"
          >
            {{ versionCount }}
          </span>

          <span
            class="hidden text-right text-sm text-neutral-500 tabular-nums lg:block"
          >
            {{ publishedLabel }}
          </span>

          <UIcon
            name="i-lucide-chevron-right"
            class="text-neutral-700 transition-all group-hover:translate-x-0.5 group-hover:text-neutral-400"
          />
        </div>

        <div class="mt-0.5 flex min-w-0 items-baseline gap-2">
          <!-- The coordinate doubles as the copy control, so the icon can stay
               in the layout permanently and nothing shifts on hover. -->
          <button
            type="button"
            class="copy-coordinate shrink-0 rounded font-mono text-xs text-neutral-500 transition hover:text-neutral-300 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-neutral-600"
            :title="copied ? 'Copied' : `Copy ${moduleSource}`"
            :aria-label="copied ? 'Source copied' : `Copy source ${moduleSource}`"
            @click.stop.prevent="copySource"
          >
            {{ coordinate }}<UIcon
              :name="copied ? 'i-lucide-check' : 'i-lucide-copy'"
              class="ms-1.5 inline-block align-[-0.1em] transition-colors"
              :class="copied ? 'text-neutral-300' : 'text-neutral-700'"
            />
          </button>
          <!-- Below sm there is only room for a useless fragment of the
               description, so the coordinate carries the row on its own. -->
          <span class="hidden text-neutral-700 sm:inline">·</span>
          <p class="hidden truncate text-sm text-neutral-400 sm:block">
            {{ module.description || "No description" }}
          </p>
        </div>
      </div>
    </div>
  </NuxtLink>
</template>
