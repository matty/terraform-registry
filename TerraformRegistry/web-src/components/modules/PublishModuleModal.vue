<script setup lang="ts">
import type { VcsConnectionSummary } from "~/composables/useVcsConnections";
import { useModulePublishing } from "~/composables/useModulePublishing";
import { useVcsConnections } from "~/composables/useVcsConnections";
import { useVcsSources } from "~/composables/useVcsSources";

const props = withDefaults(defineProps<{
  open: boolean
  allowManualUpload: boolean
  allowVcsLink: boolean
  initialNamespace?: string
  initialName?: string
  initialProvider?: string
}>(), {
  initialNamespace: "",
  initialName: "",
  initialProvider: "",
})

const emit = defineEmits<{
  "update:open": [boolean]
  published: []
  linked: []
}>()

const { uploadModule } = useModulePublishing()
const { listConnectionSummaries } = useVcsConnections()
const { createVcsSource } = useVcsSources()

const modalOpen = computed({
  get: () => props.open,
  set: (value: boolean) => emit("update:open", value),
})

const connections = ref<VcsConnectionSummary[]>([])
const isLoadingConnections = ref(false)
const mode = ref<"upload" | "github">("upload")
const namespace = ref("")
const name = ref("")
const provider = ref("")
const version = ref("")
const description = ref("")
const replace = ref(false)
const file = ref<File | null>(null)
const repoOwner = ref("")
const repoName = ref("")
const connectionId = ref("")
const syncExistingTags = ref(true)
const submitting = ref(false)
const error = ref("")

const hasModeToggle = computed(() => props.allowManualUpload && props.allowVcsLink)

const canSubmitUpload = computed(() =>
  Boolean(
    namespace.value
    && name.value
    && provider.value
    && version.value
    && file.value
    && file.value.name.toLowerCase().endsWith(".zip")
  )
)

const canSubmitGitHub = computed(() =>
  Boolean(
    namespace.value
    && name.value
    && provider.value
    && repoOwner.value
    && repoName.value
    && connectionId.value
  )
)

const canSubmit = computed(() => {
  if (mode.value === "upload") {
    return props.allowManualUpload && canSubmitUpload.value
  }

  return props.allowVcsLink && canSubmitGitHub.value
})

const submitLabel = computed(() =>
  mode.value === "upload" ? "Upload Module" : "Link Repository"
)

function resetForm() {
  mode.value = props.allowManualUpload ? "upload" : "github"
  namespace.value = props.initialNamespace
  name.value = props.initialName
  provider.value = props.initialProvider
  version.value = ""
  description.value = ""
  replace.value = false
  file.value = null
  repoOwner.value = ""
  repoName.value = ""
  connectionId.value = ""
  syncExistingTags.value = true
  submitting.value = false
  error.value = ""
}

async function loadConnections() {
  if (!props.allowVcsLink) return

  isLoadingConnections.value = true
  try {
    connections.value = await listConnectionSummaries()
  } catch (err: any) {
    error.value = err?.data?.error || err?.message || "Failed to load VCS connections"
    connections.value = []
  } finally {
    isLoadingConnections.value = false
  }
}

watch(
  () => props.open,
  async (value) => {
    if (!value) {
      return
    }

    resetForm()
    if (props.allowVcsLink) {
      await loadConnections()
    }
  }
)

watch(connectionId, (selectedId) => {
  if (!selectedId || repoOwner.value) return

  const selectedConnection = connections.value.find(connection => connection.id === selectedId)
  if (selectedConnection?.defaultOrg) {
    repoOwner.value = selectedConnection.defaultOrg
  }
})

function closeModal() {
  modalOpen.value = false
}

function handleFileChange(event: Event) {
  const target = event.target as HTMLInputElement | null
  file.value = target?.files?.[0] ?? null
}

async function submit() {
  error.value = ""
  submitting.value = true

  try {
    if (mode.value === "upload") {
      if (!file.value || !file.value.name.toLowerCase().endsWith(".zip")) {
        throw new Error("A .zip module archive is required.")
      }

      await uploadModule({
        namespace: namespace.value,
        name: name.value,
        provider: provider.value,
        version: version.value,
        description: description.value,
        file: file.value,
        replace: replace.value,
      })

      emit("published")
      closeModal()
      return
    }

    await createVcsSource({
      namespace: namespace.value,
      name: name.value,
      provider: provider.value,
      repoOwner: repoOwner.value,
      repoName: repoName.value,
      connectionId: connectionId.value,
      syncExistingTags: syncExistingTags.value,
    })

    emit("linked")
    closeModal()
  } catch (err: any) {
    error.value = err?.data?.error || err?.message || "Publishing request failed"
  } finally {
    submitting.value = false
  }
}

const connectionOptions = computed(() =>
  connections.value.map(connection => ({
    label: connection.label,
    value: connection.id,
  }))
)
</script>

<template>
  <UModal v-model:open="modalOpen" class="sm:max-w-2xl">
    <template #content>
      <div class="p-6 max-h-[85vh] overflow-y-auto">
        <div class="flex items-start justify-between gap-4 mb-5">
          <div class="flex items-center gap-3">
            <div class="w-12 h-12 rounded-2xl bg-primary-600/15 border border-primary-500/20 flex items-center justify-center">
              <UIcon
                :name="mode === 'upload' ? 'i-lucide-upload' : 'i-simple-icons-github'"
                class="text-2xl text-primary-300"
              />
            </div>
            <div>
              <h3 class="text-lg font-semibold text-neutral-100">
                {{ mode === "upload" ? "Publish Module" : "Link GitHub Repository" }}
              </h3>
              <p class="text-sm text-neutral-400">
                {{ mode === "upload"
                  ? "Upload a new module version through the registry portal."
                  : "Connect a module coordinate to a repository and optionally backfill tags." }}
              </p>
            </div>
          </div>
          <UButton
            icon="i-lucide-x"
            color="neutral"
            variant="ghost"
            size="sm"
            @click="closeModal"
          />
        </div>

        <div
          v-if="hasModeToggle"
          class="mb-5 p-1 rounded-xl bg-neutral-900/80 border border-neutral-800 grid grid-cols-2 gap-1"
        >
          <UButton
            label="Manual Upload"
            icon="i-lucide-package-open"
            :color="mode === 'upload' ? 'primary' : 'neutral'"
            :variant="mode === 'upload' ? 'soft' : 'ghost'"
            block
            @click="mode = 'upload'"
          />
          <UButton
            label="GitHub Link"
            icon="i-simple-icons-github"
            :color="mode === 'github' ? 'primary' : 'neutral'"
            :variant="mode === 'github' ? 'soft' : 'ghost'"
            block
            @click="mode = 'github'"
          />
        </div>

        <div
          v-if="error"
          class="mb-4 p-3 bg-red-900/20 border border-red-800/50 rounded-xl flex items-start gap-2"
        >
          <UIcon name="i-lucide-alert-circle" class="text-red-400 mt-0.5" />
          <p class="text-sm text-red-200">{{ error }}</p>
        </div>

        <div class="space-y-5">
          <section class="space-y-3">
            <div class="flex items-center justify-between">
              <h4 class="text-xs font-semibold text-neutral-400 uppercase tracking-[0.2em]">
                Module Coordinates
              </h4>
              <span class="text-xs text-neutral-500">
                Required for both workflows
              </span>
            </div>

            <div class="grid gap-3 sm:grid-cols-3">
              <div>
                <label class="block text-xs text-neutral-400 mb-1">Namespace</label>
                <UInput v-model="namespace" placeholder="acme" size="sm" />
              </div>
              <div>
                <label class="block text-xs text-neutral-400 mb-1">Name</label>
                <UInput v-model="name" placeholder="network" size="sm" />
              </div>
              <div>
                <label class="block text-xs text-neutral-400 mb-1">Provider</label>
                <UInput v-model="provider" placeholder="aws" size="sm" />
              </div>
            </div>
          </section>

          <section v-if="mode === 'upload'" class="space-y-3 border-t border-neutral-800 pt-5">
            <div class="flex items-center justify-between">
              <h4 class="text-xs font-semibold text-neutral-400 uppercase tracking-[0.2em]">
                Archive Upload
              </h4>
              <span class="text-xs text-neutral-500">`.zip` only</span>
            </div>

            <div class="grid gap-3 sm:grid-cols-2">
              <div>
                <label class="block text-xs text-neutral-400 mb-1">Version</label>
                <UInput v-model="version" placeholder="1.2.3" size="sm" />
              </div>
              <div>
                <label class="block text-xs text-neutral-400 mb-1">Module Archive</label>
                <label class="flex items-center justify-between gap-3 px-3 py-2.5 rounded-xl border border-dashed border-neutral-700 bg-neutral-950/50 cursor-pointer hover:border-primary-500/40 transition-colors">
                  <span class="text-sm text-neutral-300 truncate">
                    {{ file?.name || "Choose a .zip file" }}
                  </span>
                  <span class="text-xs text-neutral-500 uppercase tracking-wide">Browse</span>
                  <input
                    class="hidden"
                    type="file"
                    accept=".zip,application/zip"
                    @change="handleFileChange"
                  />
                </label>
              </div>
            </div>

            <div>
              <label class="block text-xs text-neutral-400 mb-1">Description</label>
              <UTextarea
                v-model="description"
                placeholder="Optional summary shown in module listings"
                :rows="3"
                class="w-full"
              />
            </div>

            <label class="flex items-center gap-3 text-sm text-neutral-300">
              <input
                v-model="replace"
                type="checkbox"
                class="rounded border-neutral-700 bg-neutral-900 text-primary-500"
              >
              Replace an existing version if the registry already has it
            </label>
          </section>

          <section v-else class="space-y-3 border-t border-neutral-800 pt-5">
            <div class="flex items-center justify-between">
              <h4 class="text-xs font-semibold text-neutral-400 uppercase tracking-[0.2em]">
                GitHub Repository
              </h4>
              <span class="text-xs text-neutral-500">Requires `vcs.manage`</span>
            </div>

            <div
              v-if="!isLoadingConnections && !connectionOptions.length"
              class="p-3 rounded-xl border border-amber-800/50 bg-amber-950/20 text-sm text-amber-200"
            >
              No VCS connections are available. Configure one in Admin → VCS Connections first.
            </div>

            <template v-else>
              <div>
                <label class="block text-xs text-neutral-400 mb-1">VCS Connection</label>
                <USelect
                  v-model="connectionId"
                  :items="connectionOptions"
                  value-key="value"
                  label-key="label"
                  :loading="isLoadingConnections"
                  placeholder="Select a connection..."
                  size="sm"
                />
              </div>

              <div class="grid gap-3 sm:grid-cols-2">
                <div>
                  <label class="block text-xs text-neutral-400 mb-1">Owner</label>
                  <UInput v-model="repoOwner" placeholder="acme" size="sm" />
                </div>
                <div>
                  <label class="block text-xs text-neutral-400 mb-1">Repository</label>
                  <UInput v-model="repoName" placeholder="terraform-network" size="sm" />
                </div>
              </div>

              <label class="flex items-center gap-3 text-sm text-neutral-300">
                <input
                  v-model="syncExistingTags"
                  type="checkbox"
                  class="rounded border-neutral-700 bg-neutral-900 text-primary-500"
                >
                Import existing semantic-version tags immediately after linking
              </label>
            </template>
          </section>

          <div class="flex justify-end gap-2 border-t border-neutral-800 pt-5">
            <UButton
              label="Cancel"
              color="neutral"
              variant="ghost"
              :disabled="submitting"
              @click="closeModal"
            />
            <UButton
              :label="submitLabel"
              color="primary"
              :loading="submitting"
              :disabled="!canSubmit"
              @click="submit"
            />
          </div>
        </div>
      </div>
    </template>
  </UModal>
</template>
