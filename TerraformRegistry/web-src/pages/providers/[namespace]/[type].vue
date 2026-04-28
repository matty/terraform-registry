<script setup lang="ts">
import PublishProviderVersionModal from "~/components/providers/PublishProviderVersionModal.vue";
import { extractErrorMessage } from "~/composables/useErrorMessage";
import { useDashboard } from "~/composables/useDashboard";
import type {
  ProviderGpgKey,
  ProviderVersionEntry,
  TerraformProvider,
} from "~/composables/useProviders";
import { useProviders } from "~/composables/useProviders";

definePageMeta({
  middleware: "auth",
});

type CopyTarget = "source" | "snippet";

const route = useRoute();
const requestUrl = useRequestURL();
const { isSidebarOpen } = useDashboard();
const { hasPermission } = usePermissions();
const {
  getProvider,
  updateProvider,
  deleteProvider,
  listVersions,
  deleteVersion,
  deletePlatform,
  listGpgKeys,
  revokeGpgKey,
} = useProviders();

const namespace = computed(() => route.params.namespace as string);
const type = computed(() => route.params.type as string);

const canPublish = computed(() => hasPermission("providers.publish"));
const canManageKeys = computed(() => hasPermission("providers.keys.manage"));
const canEditDescription = computed(() => hasPermission("providers.description"));
const canDelete = computed(() => hasPermission("providers.delete"));

const provider = ref<TerraformProvider | null>(null);
const versions = ref<ProviderVersionEntry[]>([]);
const gpgKeys = ref<ProviderGpgKey[]>([]);
const isLoading = ref(true);
const error = ref("");

const copiedSource = ref(false);
const copiedSnippet = ref(false);
const copyTimers: Record<CopyTarget, ReturnType<typeof setTimeout> | null> = {
  source: null,
  snippet: null,
};

const publishModalOpen = ref(false);

const isEditingMetadata = ref(false);
const editDisplayName = ref("");
const editDescription = ref("");
const editSourceRepositoryUrl = ref("");
const isSavingMetadata = ref(false);
const metadataError = ref("");

const confirmOpen = ref(false);
const confirmTitle = ref("");
const confirmMessage = ref("");
const confirmActionLabel = ref("");
const confirmAction = ref<(() => Promise<void> | void) | null>(null);
const confirmLoading = ref(false);

const providerTitle = computed(() =>
  provider.value?.display_name?.trim() || type.value
);

const providerSource = computed(() => {
  const host = requestUrl.host || "registry.example.com";
  return `${host}/${namespace.value}/${type.value}`;
});

const sortedVersions = computed(() =>
  [...versions.value].sort((a, b) => compareVersionsDesc(a.version, b.version))
);

const latestVersion = computed(() => sortedVersions.value[0] ?? null);

const requiredProvidersSnippet = computed(() => {
  const versionLine = latestVersion.value
    ? `      version = "${latestVersion.value.version}"\n`
    : "";

  return `terraform {
  required_providers {
    ${type.value} = {
      source = "${providerSource.value}"
${versionLine}    }
  }
}`;
});

const activeGpgKeys = computed(() =>
  gpgKeys.value.filter((key) => !key.revoked_at)
);

function compareVersionsDesc(a: string, b: string) {
  return compareVersions(b, a);
}

function compareVersions(a: string, b: string) {
  const parsedA = parseVersion(a);
  const parsedB = parseVersion(b);
  const maxLength = Math.max(parsedA.core.length, parsedB.core.length);

  for (let index = 0; index < maxLength; index += 1) {
    const diff = (parsedA.core[index] ?? 0) - (parsedB.core[index] ?? 0);
    if (diff !== 0) return diff;
  }

  if (!parsedA.preRelease && parsedB.preRelease) return 1;
  if (parsedA.preRelease && !parsedB.preRelease) return -1;
  if (!parsedA.preRelease && !parsedB.preRelease) return 0;

  return (parsedA.preRelease ?? "").localeCompare(parsedB.preRelease ?? "", undefined, {
    numeric: true,
    sensitivity: "base",
  });
}

function parseVersion(version: string) {
  const [rawCore, preRelease] = version.replace(/^v/i, "").split("-", 2);
  const core = rawCore
    .split(".")
    .map((part) => Number.parseInt(part, 10))
    .map((part) => (Number.isNaN(part) ? 0 : part));

  return { core, preRelease };
}

function formatDate(dateString?: string | null) {
  if (!dateString) return "";

  return new Date(dateString).toLocaleDateString("en-US", {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

function formatSize(sizeBytes?: number | null) {
  if (sizeBytes === undefined || sizeBytes === null) return "Uploaded";

  const units = ["B", "KB", "MB", "GB"];
  let size = sizeBytes;
  let unitIndex = 0;

  while (size >= 1024 && unitIndex < units.length - 1) {
    size /= 1024;
    unitIndex += 1;
  }

  return `${size.toFixed(size >= 10 || unitIndex === 0 ? 0 : 1)} ${units[unitIndex]}`;
}

async function copyText(text: string, target: CopyTarget) {
  try {
    await navigator.clipboard.writeText(text);

    if (target === "source") {
      copiedSource.value = true;
    } else {
      copiedSnippet.value = true;
    }

    if (copyTimers[target]) {
      clearTimeout(copyTimers[target] as ReturnType<typeof setTimeout>);
    }

    copyTimers[target] = setTimeout(() => {
      if (target === "source") {
        copiedSource.value = false;
      } else {
        copiedSnippet.value = false;
      }
      copyTimers[target] = null;
    }, 2000);
  } catch (err) {
    console.error("Failed to copy provider text:", err);
  }
}

async function fetchDetail() {
  isLoading.value = true;
  error.value = "";

  try {
    const [providerResponse, versionsResponse, keysResponse] = await Promise.all([
      getProvider(namespace.value, type.value),
      listVersions(namespace.value, type.value),
      listGpgKeys(namespace.value, type.value),
    ]);

    provider.value = providerResponse;
    versions.value = versionsResponse.versions ?? [];
    gpgKeys.value = keysResponse.gpg_keys ?? [];
  } catch (err) {
    error.value = extractErrorMessage(err, "Failed to load provider details");
    console.error("Error loading provider details:", err);
  } finally {
    isLoading.value = false;
  }
}

function startEditingMetadata() {
  editDisplayName.value = provider.value?.display_name || "";
  editDescription.value = provider.value?.description || "";
  editSourceRepositoryUrl.value = provider.value?.source_repository_url || "";
  metadataError.value = "";
  isEditingMetadata.value = true;
}

function cancelEditingMetadata() {
  isEditingMetadata.value = false;
  metadataError.value = "";
}

async function saveMetadata() {
  isSavingMetadata.value = true;
  metadataError.value = "";

  try {
    provider.value = await updateProvider(namespace.value, type.value, {
      display_name: editDisplayName.value || null,
      description: editDescription.value || null,
      source_repository_url: editSourceRepositoryUrl.value || null,
    });
    isEditingMetadata.value = false;
  } catch (err) {
    metadataError.value = extractErrorMessage(err, "Failed to update provider metadata");
  } finally {
    isSavingMetadata.value = false;
  }
}

function openConfirm(
  title: string,
  message: string,
  actionLabel: string,
  action: () => Promise<void> | void
) {
  confirmTitle.value = title;
  confirmMessage.value = message;
  confirmActionLabel.value = actionLabel;
  confirmAction.value = action;
  confirmOpen.value = true;
}

async function runConfirmAction() {
  if (!confirmAction.value) return;

  confirmLoading.value = true;
  error.value = "";

  try {
    await confirmAction.value();
    confirmOpen.value = false;
    confirmAction.value = null;
  } catch (err) {
    error.value = extractErrorMessage(err, "Failed to complete action");
  } finally {
    confirmLoading.value = false;
  }
}

function confirmDeleteProvider() {
  openConfirm(
    "Delete Provider",
    `Delete provider ${namespace.value}/${type.value}? This is an API-backed delete of the current provider and is not recoverable through Trash restore.`,
    "Delete Provider",
    async () => {
      await deleteProvider(namespace.value, type.value);
      await navigateTo("/providers");
    }
  );
}

function confirmDeleteVersion(version: string) {
  openConfirm(
    "Delete Version",
    `Delete version ${version} from ${namespace.value}/${type.value}? This is an API-backed delete of the current version and is not recoverable through Trash restore.`,
    "Delete Version",
    async () => {
      await deleteVersion(namespace.value, type.value, version);
      await fetchDetail();
    }
  );
}

function confirmDeletePlatform(version: string, os: string, arch: string) {
  openConfirm(
    "Delete Platform",
    `Delete platform ${os}/${arch} from ${namespace.value}/${type.value} ${version}? This is an API-backed delete of the current platform package metadata and is not recoverable through Trash restore.`,
    "Delete Platform",
    async () => {
      await deletePlatform(namespace.value, type.value, version, os, arch);
      await fetchDetail();
    }
  );
}

function confirmRevokeKey(keyId: string) {
  openConfirm(
    "Revoke GPG Key",
    `Revoke GPG key ${keyId} for ${namespace.value}/${type.value}? This API-backed revocation affects current provider signing-key availability and is not recoverable through Trash restore.`,
    "Revoke Key",
    async () => {
      await revokeGpgKey(namespace.value, type.value, keyId);
      await fetchDetail();
    }
  );
}

async function handlePublished() {
  await fetchDetail();
}

onMounted(() => {
  fetchDetail();
});
</script>

<template>
  <div class="flex flex-col h-full">
    <PublishProviderVersionModal
      v-model:open="publishModalOpen"
      mode="existing"
      :provider="provider"
      :existing-keys="gpgKeys"
      :allow-create-provider="false"
      :allow-manage-keys="canManageKeys"
      :allow-delete-platforms="canDelete"
      @published="handlePublished"
    />

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
      <div class="flex items-center justify-between gap-4">
        <div class="min-w-0">
          <div class="flex items-center gap-2 mb-2">
            <NuxtLink
              to="/providers"
              class="text-sm text-neutral-500 hover:text-primary-400 transition-colors flex items-center gap-1"
            >
              <UIcon name="i-lucide-chevron-left" class="text-xs" />
              Providers
            </NuxtLink>
            <span class="text-neutral-600 text-sm">/</span>
            <span class="text-sm text-neutral-400 truncate">
              {{ namespace }} / {{ type }}
            </span>
          </div>
          <h1 class="page-header-title truncate">{{ providerTitle }}</h1>
          <p class="page-header-subtitle">
            {{ namespace }} / <span class="text-primary-400">{{ type }}</span>
          </p>
        </div>
        <div class="flex items-center gap-2 shrink-0">
          <div class="hidden sm:flex items-center gap-2 px-3 py-1.5 bg-neutral-800/60 rounded-lg">
            <UIcon name="i-lucide-package" class="text-primary-400" />
            <span class="text-sm font-medium text-neutral-300">
              {{ versions.length }} version{{ versions.length !== 1 ? "s" : "" }}
            </span>
          </div>
          <UButton
            :loading="isLoading"
            icon="i-lucide-refresh-cw"
            color="neutral"
            variant="ghost"
            size="sm"
            @click="fetchDetail"
          />
          <UButton
            v-if="canPublish"
            label="Publish Version"
            icon="i-lucide-upload"
            color="primary"
            variant="soft"
            size="sm"
            @click="publishModalOpen = true"
          />
          <UButton
            v-if="canDelete"
            icon="i-lucide-trash-2"
            color="error"
            variant="ghost"
            size="sm"
            aria-label="Delete provider"
            @click="confirmDeleteProvider"
          />
        </div>
      </div>
    </div>
    <div class="page-divider" />

    <!-- Body -->
    <div class="flex-1 overflow-y-auto">
      <div class="p-6 max-w-5xl mx-auto">
        <UAlert
          v-if="error"
          color="error"
          variant="soft"
          :title="error"
          icon="i-lucide-alert-circle"
          class="mb-6"
        />

        <div
          v-if="isLoading"
          class="flex flex-col justify-center items-center py-20"
        >
          <div class="relative">
            <div class="w-16 h-16 border-4 border-primary-500/20 rounded-full" />
            <div class="w-16 h-16 border-4 border-transparent border-t-primary-500 rounded-full animate-spin absolute inset-0" />
          </div>
          <p class="text-neutral-400 text-lg mt-6">Loading provider...</p>
        </div>

        <div v-else-if="provider" class="space-y-6">
          <!-- Provider metadata -->
          <section class="rounded-2xl bg-neutral-900/50 border border-neutral-800 overflow-hidden">
            <div class="px-5 py-4 border-b border-neutral-800 flex items-center justify-between gap-4">
              <div class="flex items-center gap-2">
                <UIcon name="i-lucide-info" class="text-neutral-400" />
                <h2 class="font-semibold text-neutral-100">Provider Metadata</h2>
              </div>
              <UButton
                v-if="canEditDescription && !isEditingMetadata"
                label="Edit"
                icon="i-lucide-pencil"
                color="neutral"
                variant="ghost"
                size="xs"
                @click="startEditingMetadata"
              />
            </div>

            <div v-if="!isEditingMetadata" class="p-5 space-y-4">
              <div>
                <p class="text-xs uppercase tracking-wide text-neutral-500 mb-1">
                  Description
                </p>
                <p class="text-sm text-neutral-300 leading-relaxed">
                  {{ provider.description || "No description available" }}
                </p>
              </div>
              <div>
                <p class="text-xs uppercase tracking-wide text-neutral-500 mb-1">
                  Source Repository
                </p>
                <a
                  v-if="provider.source_repository_url"
                  :href="provider.source_repository_url"
                  target="_blank"
                  rel="noopener noreferrer"
                  class="inline-flex min-w-0 items-center gap-2 text-sm text-primary-300 hover:text-primary-200"
                >
                  <UIcon name="i-lucide-git-branch" class="shrink-0" />
                  <span class="truncate">{{ provider.source_repository_url }}</span>
                </a>
                <p v-else class="text-sm text-neutral-500">No source repository URL set</p>
              </div>
              <div class="flex flex-wrap gap-2">
                <UBadge variant="soft" color="neutral">
                  Created {{ formatDate(provider.created_at) || "unknown" }}
                </UBadge>
                <UBadge v-if="provider.updated_at" variant="soft" color="neutral">
                  Updated {{ formatDate(provider.updated_at) }}
                </UBadge>
              </div>
            </div>

            <div v-else class="p-5 space-y-4">
              <div class="grid gap-4 sm:grid-cols-2">
                <div>
                  <label class="block text-xs text-neutral-400 mb-1">Display Name</label>
                  <UInput
                    v-model="editDisplayName"
                    placeholder="Provider display name"
                    size="sm"
                    :disabled="isSavingMetadata"
                  />
                </div>
                <div>
                  <label class="block text-xs text-neutral-400 mb-1">Source Repository URL</label>
                  <UInput
                    v-model="editSourceRepositoryUrl"
                    placeholder="https://github.com/example/provider"
                    size="sm"
                    :disabled="isSavingMetadata"
                  />
                </div>
              </div>
              <div>
                <label class="block text-xs text-neutral-400 mb-1">Description</label>
                <UTextarea
                  v-model="editDescription"
                  placeholder="Provider description"
                  :rows="4"
                  class="w-full"
                  :disabled="isSavingMetadata"
                />
              </div>
              <UAlert
                v-if="metadataError"
                color="error"
                variant="soft"
                :title="metadataError"
                icon="i-lucide-alert-circle"
              />
              <div class="flex items-center justify-end gap-2">
                <UButton
                  label="Cancel"
                  icon="i-lucide-x"
                  variant="ghost"
                  color="neutral"
                  size="sm"
                  :disabled="isSavingMetadata"
                  @click="cancelEditingMetadata"
                />
                <UButton
                  label="Save"
                  icon="i-lucide-check"
                  color="primary"
                  size="sm"
                  :loading="isSavingMetadata"
                  @click="saveMetadata"
                />
              </div>
            </div>
          </section>

          <!-- Usage -->
          <section class="rounded-2xl bg-neutral-900/50 border border-neutral-800 overflow-hidden">
            <div class="px-5 py-4 border-b border-neutral-800 flex items-center justify-between gap-4">
              <div class="flex items-center gap-2">
                <UIcon name="i-lucide-terminal" class="text-neutral-400" />
                <h2 class="font-semibold text-neutral-100">Usage</h2>
              </div>
            </div>
            <div class="divide-y divide-neutral-800/70">
              <div class="p-5">
                <div class="flex items-center justify-between gap-3 mb-3">
                  <p class="text-sm font-medium text-neutral-300">Provider Source</p>
                  <UButton
                    :label="copiedSource ? 'Copied' : 'Copy'"
                    :icon="copiedSource ? 'i-lucide-check' : 'i-lucide-copy'"
                    :color="copiedSource ? 'success' : 'neutral'"
                    variant="ghost"
                    size="xs"
                    @click="copyText(providerSource, 'source')"
                  />
                </div>
                <code class="block overflow-x-auto rounded-lg bg-neutral-950/70 px-4 py-3 text-sm text-primary-300">
                  {{ providerSource }}
                </code>
              </div>
              <div class="p-5">
                <div class="flex items-center justify-between gap-3 mb-3">
                  <p class="text-sm font-medium text-neutral-300">required_providers</p>
                  <UButton
                    :label="copiedSnippet ? 'Copied' : 'Copy'"
                    :icon="copiedSnippet ? 'i-lucide-check' : 'i-lucide-copy'"
                    :color="copiedSnippet ? 'success' : 'neutral'"
                    variant="ghost"
                    size="xs"
                    @click="copyText(requiredProvidersSnippet, 'snippet')"
                  />
                </div>
                <pre class="overflow-x-auto rounded-lg bg-neutral-950/70 px-4 py-3 text-sm text-neutral-300"><code>{{ requiredProvidersSnippet }}</code></pre>
              </div>
            </div>
          </section>

          <!-- Versions -->
          <section class="rounded-2xl bg-neutral-900/50 border border-neutral-800 overflow-hidden">
            <div class="px-5 py-4 border-b border-neutral-800 flex items-center justify-between gap-4">
              <div class="flex items-center gap-2">
                <UIcon name="i-lucide-git-branch" class="text-neutral-400" />
                <h2 class="font-semibold text-neutral-100">Versions</h2>
              </div>
              <span class="text-xs uppercase tracking-wide text-neutral-500">
                {{ sortedVersions.length }} release{{ sortedVersions.length !== 1 ? "s" : "" }}
              </span>
            </div>

            <div v-if="!sortedVersions.length" class="px-5 py-12 text-center">
              <UIcon name="i-lucide-package-x" class="text-4xl text-neutral-600 mb-3" />
              <p class="text-neutral-300 font-medium">No versions published</p>
              <p class="text-sm text-neutral-500 mt-1">
                Publish a version to make this provider installable.
              </p>
            </div>

            <div v-else class="divide-y divide-neutral-800/70">
              <article
                v-for="version in sortedVersions"
                :key="version.version"
                class="p-5 space-y-4"
              >
                <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                  <div class="min-w-0">
                    <div class="flex flex-wrap items-center gap-2">
                      <h3 class="text-lg font-semibold text-neutral-100">
                        v{{ version.version }}
                      </h3>
                      <UBadge
                        v-for="protocol in version.protocols"
                        :key="protocol"
                        variant="soft"
                        color="primary"
                        size="xs"
                      >
                        protocol {{ protocol }}
                      </UBadge>
                    </div>
                    <div class="mt-2 flex flex-wrap items-center gap-3 text-xs text-neutral-500">
                      <span v-if="version.key_id" class="inline-flex items-center gap-1">
                        <UIcon name="i-lucide-key-round" />
                        {{ version.key_id }}
                      </span>
                      <span v-if="version.published_at" class="inline-flex items-center gap-1">
                        <UIcon name="i-lucide-calendar" />
                        {{ formatDate(version.published_at) }}
                      </span>
                      <span
                        v-if="version.has_shasums !== undefined"
                        class="inline-flex items-center gap-1"
                        :class="version.has_shasums ? 'text-green-400' : 'text-amber-400'"
                      >
                        <UIcon :name="version.has_shasums ? 'i-lucide-file-check' : 'i-lucide-file-warning'" />
                        SHA256SUMS {{ version.has_shasums ? "uploaded" : "missing" }}
                      </span>
                      <span
                        v-if="version.has_shasums_signature !== undefined"
                        class="inline-flex items-center gap-1"
                        :class="version.has_shasums_signature ? 'text-green-400' : 'text-amber-400'"
                      >
                        <UIcon :name="version.has_shasums_signature ? 'i-lucide-shield-check' : 'i-lucide-shield-alert'" />
                        signature {{ version.has_shasums_signature ? "uploaded" : "missing" }}
                      </span>
                    </div>
                  </div>
                  <UButton
                    v-if="canDelete"
                    label="Delete"
                    icon="i-lucide-trash-2"
                    color="error"
                    variant="ghost"
                    size="xs"
                    @click="confirmDeleteVersion(version.version)"
                  />
                </div>

                <div class="overflow-hidden rounded-xl border border-neutral-800">
                  <div class="grid grid-cols-[1fr_1fr_auto] gap-3 bg-neutral-950/60 px-4 py-2 text-xs uppercase tracking-wide text-neutral-500 sm:grid-cols-[140px_minmax(0,1fr)_minmax(0,1fr)_150px_auto]">
                    <span>Platform</span>
                    <span class="hidden sm:block">Filename</span>
                    <span class="hidden sm:block">SHA sum</span>
                    <span>Package</span>
                    <span v-if="canDelete" class="text-right">Action</span>
                  </div>
                  <div
                    v-if="!version.platforms.length"
                    class="px-4 py-5 text-sm text-neutral-500"
                  >
                    No platforms uploaded for this version.
                  </div>
                  <div
                    v-for="platform in version.platforms"
                    :key="`${version.version}-${platform.os}-${platform.arch}`"
                    class="grid grid-cols-[1fr_1fr_auto] gap-3 px-4 py-3 text-sm text-neutral-300 border-t border-neutral-800/70 sm:grid-cols-[140px_minmax(0,1fr)_minmax(0,1fr)_150px_auto]"
                  >
                    <div class="min-w-0">
                      <div class="font-medium text-neutral-100">
                        {{ platform.os }}/{{ platform.arch }}
                      </div>
                      <div class="sm:hidden text-xs text-neutral-500 truncate">
                        {{ platform.filename }}
                      </div>
                    </div>
                    <div class="hidden sm:block min-w-0 truncate text-neutral-400">
                      {{ platform.filename }}
                    </div>
                    <code class="hidden sm:block min-w-0 truncate text-xs text-neutral-500">
                      {{ platform.shasum }}
                    </code>
                    <div>
                      <UBadge
                        variant="soft"
                        :color="platform.has_package ? 'success' : 'warning'"
                        size="xs"
                      >
                        {{ platform.has_package ? formatSize(platform.size_bytes) : "Missing" }}
                      </UBadge>
                    </div>
                    <div v-if="canDelete" class="text-right">
                      <UButton
                        icon="i-lucide-trash-2"
                        color="error"
                        variant="ghost"
                        size="xs"
                        :aria-label="`Delete ${platform.os}/${platform.arch}`"
                        @click="confirmDeletePlatform(version.version, platform.os, platform.arch)"
                      />
                    </div>
                  </div>
                </div>
              </article>
            </div>
          </section>

          <!-- GPG keys -->
          <section class="rounded-2xl bg-neutral-900/50 border border-neutral-800 overflow-hidden">
            <div class="px-5 py-4 border-b border-neutral-800 flex items-center justify-between gap-4">
              <div class="flex items-center gap-2">
                <UIcon name="i-lucide-key-round" class="text-neutral-400" />
                <h2 class="font-semibold text-neutral-100">GPG Keys</h2>
              </div>
              <span class="text-xs uppercase tracking-wide text-neutral-500">
                {{ activeGpgKeys.length }} active
              </span>
            </div>

            <div v-if="!activeGpgKeys.length" class="px-5 py-10 text-center">
              <UIcon name="i-lucide-key-round" class="text-4xl text-neutral-600 mb-3" />
              <p class="text-neutral-300 font-medium">No active GPG keys</p>
              <p class="text-sm text-neutral-500 mt-1">
                Active signing keys will appear here.
              </p>
            </div>

            <div v-else class="divide-y divide-neutral-800/70">
              <div
                v-for="key in activeGpgKeys"
                :key="key.id"
                class="px-5 py-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between"
              >
                <div class="min-w-0">
                  <div class="flex items-center gap-2">
                    <p class="font-medium text-neutral-100 truncate">{{ key.key_id }}</p>
                    <UBadge variant="soft" color="success" size="xs">Active</UBadge>
                  </div>
                  <div class="mt-1 flex flex-wrap items-center gap-3 text-xs text-neutral-500">
                    <span>Created {{ formatDate(key.created_at) }}</span>
                    <span v-if="key.source">{{ key.source }}</span>
                    <a
                      v-if="key.source_url"
                      :href="key.source_url"
                      target="_blank"
                      rel="noopener noreferrer"
                      class="text-primary-300 hover:text-primary-200"
                    >
                      Source
                    </a>
                  </div>
                </div>
                <UButton
                  v-if="canManageKeys"
                  label="Revoke"
                  icon="i-lucide-ban"
                  color="error"
                  variant="ghost"
                  size="xs"
                  @click="confirmRevokeKey(key.key_id)"
                />
              </div>
            </div>
          </section>
        </div>
      </div>
    </div>

    <!-- Confirmation Modal -->
    <UModal v-model:open="confirmOpen">
      <template #content>
        <div class="w-full">
          <div class="flex items-center gap-4 px-6 py-5 border-b border-neutral-800/60">
            <div class="w-12 h-12 rounded-xl bg-red-500/15 flex items-center justify-center shrink-0">
              <UIcon name="i-lucide-triangle-alert" class="text-2xl text-red-400" />
            </div>
            <div>
              <h3 class="text-lg font-semibold text-neutral-100">{{ confirmTitle }}</h3>
              <p class="text-sm text-neutral-500">
                API-backed current action, no Trash restore
              </p>
            </div>
          </div>

          <div class="px-6 py-5">
            <p class="text-sm text-neutral-300 leading-relaxed">
              {{ confirmMessage }}
            </p>
          </div>

          <div class="flex justify-end gap-3 px-6 py-4 border-t border-neutral-800/60">
            <UButton
              color="neutral"
              variant="ghost"
              label="Cancel"
              :disabled="confirmLoading"
              @click="confirmOpen = false"
            />
            <UButton
              color="error"
              :label="confirmActionLabel"
              icon="i-lucide-trash-2"
              :loading="confirmLoading"
              @click="runConfirmAction"
            />
          </div>
        </div>
      </template>
    </UModal>
  </div>
</template>
