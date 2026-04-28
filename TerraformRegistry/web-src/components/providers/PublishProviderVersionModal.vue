<script setup lang="ts">
import { extractErrorMessage } from "~/composables/useErrorMessage"
import type {
  CreateProviderGpgKeyRequest,
  ProviderGpgKey,
  TerraformProvider,
} from "~/composables/useProviders"
import { useProviders } from "~/composables/useProviders"

type PublishMode = "new" | "existing"
type StepKey = "provider" | "key" | "version" | "checksums" | "signature" | "platforms"
type StepState = "pending" | "active" | "complete" | "error"

interface PlatformDraft {
  id: string
  os: string
  arch: string
  filename: string
  shasum: string
  file: File | null
  uploaded: boolean
  error: string
}

const props = withDefaults(defineProps<{
  open: boolean
  mode: PublishMode
  provider?: TerraformProvider | null
  existingKeys?: ProviderGpgKey[]
  allowCreateProvider?: boolean
  allowManageKeys?: boolean
}>(), {
  provider: null,
  existingKeys: () => [],
  allowCreateProvider: false,
  allowManageKeys: false,
})

const emit = defineEmits<{
  "update:open": [boolean]
  published: [{ namespace: string, type: string, version: string }]
  providerCreated: [TerraformProvider]
}>()

const {
  createProvider,
  addGpgKey,
  createVersion,
  uploadShasums,
  uploadShasumsSignature,
  createPlatform,
  uploadPlatformPackage,
} = useProviders()

const modalOpen = computed({
  get: () => props.open,
  set: (value: boolean) => emit("update:open", value),
})

const stepOrder: StepKey[] = ["provider", "key", "version", "checksums", "signature", "platforms"]
const stepLabels: Record<StepKey, string> = {
  provider: "Provider",
  key: "Signing Key",
  version: "Version",
  checksums: "Checksums",
  signature: "Signature",
  platforms: "Platforms",
}

const activeStep = ref<StepKey>("provider")
const submitting = ref(false)
const formError = ref("")
const completedSteps = ref<StepKey[]>([])

const namespace = ref("")
const type = ref("")
const displayName = ref("")
const description = ref("")
const sourceRepositoryUrl = ref("")
const createdProvider = ref<TerraformProvider | null>(null)

const keyMode = ref<"existing" | "new">("existing")
const selectedKeyId = ref("")
const keyId = ref("")
const asciiArmor = ref("")
const trustSignature = ref("")
const keySource = ref("")
const keySourceUrl = ref("")

const version = ref("")
const protocols = ref("5.0")
const shasumsFile = ref<File | null>(null)
const signatureFile = ref<File | null>(null)
const platforms = ref<PlatformDraft[]>([])

const providerContext = computed(() => createdProvider.value ?? props.provider ?? null)
const effectiveNamespace = computed(() => providerContext.value?.namespace || namespace.value.trim())
const effectiveType = computed(() => providerContext.value?.type || type.value.trim())
const availableKeys = computed(() => props.existingKeys.filter(key => !key.revoked_at))
const canUseExistingKey = computed(() => props.mode === "existing" && availableKeys.value.length > 0)
const selectedOrNewKeyId = computed(() =>
  keyMode.value === "new" ? keyId.value.trim() : selectedKeyId.value
)
const parsedProtocols = computed(() =>
  protocols.value
    .split(",")
    .map(protocol => protocol.trim())
    .filter(Boolean)
)

const keyOptions = computed(() =>
  availableKeys.value.map(key => ({
    label: key.key_id,
    value: key.key_id,
  }))
)

const canSubmitProvider = computed(() => {
  if (props.mode === "existing") {
    return Boolean(providerContext.value)
  }

  return Boolean(props.allowCreateProvider && namespace.value.trim() && type.value.trim())
})

const canSubmitKey = computed(() => {
  if (!effectiveNamespace.value || !effectiveType.value) {
    return false
  }

  if (keyMode.value === "existing") {
    return Boolean(selectedKeyId.value)
  }

  return Boolean(props.allowManageKeys && keyId.value.trim() && asciiArmor.value.trim())
})

const canSubmitVersion = computed(() =>
  Boolean(version.value.trim() && selectedOrNewKeyId.value && parsedProtocols.value.length)
)
const canSubmitChecksums = computed(() => Boolean(shasumsFile.value))
const canSubmitSignature = computed(() => Boolean(signatureFile.value))
const canSubmitPlatforms = computed(() =>
  platforms.value.length > 0 && platforms.value.every(platform =>
    platform.uploaded || Boolean(
      platform.os.trim()
      && platform.arch.trim()
      && platform.filename.trim()
      && platform.shasum.trim()
      && platform.file
    )
  )
)

const activeStepCanSubmit = computed(() => {
  if (activeStep.value === "provider") return canSubmitProvider.value
  if (activeStep.value === "key") return canSubmitKey.value
  if (activeStep.value === "version") return canSubmitVersion.value
  if (activeStep.value === "checksums") return canSubmitChecksums.value
  if (activeStep.value === "signature") return canSubmitSignature.value
  return canSubmitPlatforms.value
})

const submitLabel = computed(() =>
  activeStep.value === "platforms" ? "Publish Release" : "Save And Continue"
)

function rowId() {
  if (typeof crypto !== "undefined" && "randomUUID" in crypto) {
    return crypto.randomUUID()
  }

  return `platform-${Date.now()}-${Math.random().toString(36).slice(2)}`
}

function newPlatformDraft(): PlatformDraft {
  return {
    id: rowId(),
    os: "linux",
    arch: "amd64",
    filename: "",
    shasum: "",
    file: null,
    uploaded: false,
    error: "",
  }
}

function stepState(step: StepKey): StepState {
  if (activeStep.value === step && formError.value) return "error"
  if (activeStep.value === step) return "active"
  if (completedSteps.value.includes(step)) return "complete"
  return "pending"
}

function markComplete(step: StepKey) {
  if (!completedSteps.value.includes(step)) {
    completedSteps.value = [...completedSteps.value, step]
  }
}

function moveTo(step: StepKey) {
  activeStep.value = step
  formError.value = ""
}

function moveToNextStep() {
  const index = stepOrder.indexOf(activeStep.value)
  if (index >= 0 && index < stepOrder.length - 1) {
    moveTo(stepOrder[index + 1])
  }
}

function moveToPreviousStep() {
  const index = stepOrder.indexOf(activeStep.value)
  if (index > 0) {
    moveTo(stepOrder[index - 1])
  }
}

function resetForm() {
  const existingProvider = props.provider ?? null

  activeStep.value = props.mode === "new" ? "provider" : "key"
  submitting.value = false
  formError.value = ""
  completedSteps.value = props.mode === "existing" && existingProvider ? ["provider"] : []
  namespace.value = existingProvider?.namespace ?? ""
  type.value = existingProvider?.type ?? ""
  displayName.value = existingProvider?.display_name ?? ""
  description.value = existingProvider?.description ?? ""
  sourceRepositoryUrl.value = existingProvider?.source_repository_url ?? ""
  createdProvider.value = null
  keyMode.value = canUseExistingKey.value ? "existing" : "new"
  selectedKeyId.value = availableKeys.value[0]?.key_id ?? ""
  keyId.value = ""
  asciiArmor.value = ""
  trustSignature.value = ""
  keySource.value = ""
  keySourceUrl.value = ""
  version.value = ""
  protocols.value = "5.0"
  shasumsFile.value = null
  signatureFile.value = null
  platforms.value = [newPlatformDraft()]
}

function addPlatform() {
  platforms.value = [...platforms.value, newPlatformDraft()]
}

function removePlatform(id: string) {
  if (platforms.value.length === 1) {
    return
  }

  platforms.value = platforms.value.filter(platform => platform.id !== id)
}

function updateFile(target: "shasums" | "signature", event: Event) {
  const input = event.target as HTMLInputElement | null
  const file = input?.files?.[0] ?? null

  if (target === "shasums") {
    shasumsFile.value = file
    return
  }

  signatureFile.value = file
}

function updatePlatformFile(platform: PlatformDraft, event: Event) {
  const input = event.target as HTMLInputElement | null
  const file = input?.files?.[0] ?? null

  platform.file = file
  if (file && !platform.filename.trim()) {
    platform.filename = file.name
  }
}

async function submitProviderStep() {
  if (props.mode === "existing") {
    if (!providerContext.value) {
      throw new Error("Provider context is required.")
    }

    markComplete("provider")
    moveTo("key")
    return
  }

  const provider = await createProvider({
    namespace: namespace.value.trim(),
    type: type.value.trim(),
    display_name: displayName.value.trim() || undefined,
    description: description.value.trim() || undefined,
    source_repository_url: sourceRepositoryUrl.value.trim() || undefined,
  })

  createdProvider.value = provider
  emit("providerCreated", provider)
  markComplete("provider")
  moveTo("key")
}

async function submitKeyStep() {
  if (keyMode.value === "new") {
    const request: CreateProviderGpgKeyRequest = {
      key_id: keyId.value.trim(),
      ascii_armor: asciiArmor.value.trim(),
      trust_signature: trustSignature.value.trim() || undefined,
      source: keySource.value.trim() || undefined,
      source_url: keySourceUrl.value.trim() || undefined,
    }
    const key = await addGpgKey(effectiveNamespace.value, effectiveType.value, request)
    selectedKeyId.value = key.key_id
  }

  markComplete("key")
  moveTo("version")
}

async function submitVersionStep() {
  await createVersion(effectiveNamespace.value, effectiveType.value, {
    version: version.value.trim(),
    protocols: parsedProtocols.value,
    key_id: selectedOrNewKeyId.value,
  })

  markComplete("version")
  moveTo("checksums")
}

async function submitChecksumsStep() {
  if (!shasumsFile.value) {
    throw new Error("SHA256SUMS file is required.")
  }

  await uploadShasums(effectiveNamespace.value, effectiveType.value, version.value.trim(), shasumsFile.value)
  markComplete("checksums")
  moveTo("signature")
}

async function submitSignatureStep() {
  if (!signatureFile.value) {
    throw new Error("Detached signature file is required.")
  }

  await uploadShasumsSignature(
    effectiveNamespace.value,
    effectiveType.value,
    version.value.trim(),
    signatureFile.value
  )
  markComplete("signature")
  moveTo("platforms")
}

async function submitPlatformsStep() {
  for (const platform of platforms.value) {
    if (platform.uploaded) {
      continue
    }

    if (!platform.file) {
      throw new Error(`${platform.os || "Platform"} package file is required.`)
    }

    platform.error = ""

    try {
      await createPlatform(effectiveNamespace.value, effectiveType.value, version.value.trim(), {
        os: platform.os.trim(),
        arch: platform.arch.trim(),
        filename: platform.filename.trim(),
        shasum: platform.shasum.trim(),
      })
      await uploadPlatformPackage(
        effectiveNamespace.value,
        effectiveType.value,
        version.value.trim(),
        platform.os.trim(),
        platform.arch.trim(),
        platform.file
      )
      platform.uploaded = true
    } catch (error) {
      platform.error = extractErrorMessage(error, `Failed to upload ${platform.os}/${platform.arch}`)
      throw error
    }
  }

  markComplete("platforms")
  emit("published", {
    namespace: effectiveNamespace.value,
    type: effectiveType.value,
    version: version.value.trim(),
  })
  modalOpen.value = false
}

async function submitActiveStep() {
  formError.value = ""
  submitting.value = true

  try {
    if (activeStep.value === "provider") await submitProviderStep()
    else if (activeStep.value === "key") await submitKeyStep()
    else if (activeStep.value === "version") await submitVersionStep()
    else if (activeStep.value === "checksums") await submitChecksumsStep()
    else if (activeStep.value === "signature") await submitSignatureStep()
    else await submitPlatformsStep()
  } catch (error) {
    formError.value = extractErrorMessage(error, "Provider publishing step failed.")
  } finally {
    submitting.value = false
  }
}

watch(() => props.open, (open) => {
  if (open) {
    resetForm()
  }
})

watch(() => props.existingKeys, () => {
  if (!props.open || selectedKeyId.value || !availableKeys.value.length) {
    return
  }

  selectedKeyId.value = availableKeys.value[0].key_id
  if (props.mode === "existing") {
    keyMode.value = "existing"
  }
}, { deep: true })
</script>

<template>
  <UModal v-model:open="modalOpen" class="sm:max-w-5xl">
    <template #content>
      <div class="max-h-[88vh] overflow-y-auto p-6">
        <div class="flex items-start justify-between gap-4 mb-5">
          <div class="flex items-center gap-3">
            <div class="w-12 h-12 rounded-2xl bg-primary-600/15 border border-primary-500/20 flex items-center justify-center">
              <UIcon name="i-lucide-package-check" class="text-2xl text-primary-300" />
            </div>
            <div>
              <h3 class="text-lg font-semibold text-neutral-100">
                {{ mode === "new" ? "Add Provider" : "Publish Provider Version" }}
              </h3>
              <p class="text-sm text-neutral-400">
                {{ effectiveNamespace && effectiveType ? `${effectiveNamespace}/${effectiveType}` : "Provider release" }}
              </p>
            </div>
          </div>
          <UButton
            icon="i-lucide-x"
            color="neutral"
            variant="ghost"
            size="sm"
            :disabled="submitting"
            @click="modalOpen = false"
          />
        </div>

        <div class="grid gap-6 lg:grid-cols-[230px_minmax(0,1fr)]">
          <nav class="rounded-xl border border-neutral-800 bg-neutral-900/60 p-2 space-y-1 h-fit">
            <button
              v-for="step in stepOrder"
              :key="step"
              type="button"
              class="w-full flex items-center gap-2 px-3 py-2 rounded-lg text-sm text-left transition-colors"
              :class="stepState(step) === 'active'
                ? 'bg-primary-500/15 text-primary-200'
                : stepState(step) === 'complete'
                  ? 'text-green-300 hover:bg-neutral-800'
                  : stepState(step) === 'error'
                    ? 'text-red-300 bg-red-500/10'
                    : 'text-neutral-400 hover:bg-neutral-800'"
              :disabled="submitting"
              @click="moveTo(step)"
            >
              <UIcon
                :name="stepState(step) === 'complete'
                  ? 'i-lucide-check-circle'
                  : stepState(step) === 'error'
                    ? 'i-lucide-alert-circle'
                    : 'i-lucide-circle'"
                class="text-base"
              />
              <span>{{ stepLabels[step] }}</span>
            </button>
          </nav>

          <section class="rounded-xl border border-neutral-800 bg-neutral-900/50 overflow-hidden">
            <div
              v-if="formError"
              class="m-5 p-3 rounded-lg border border-red-800/50 bg-red-900/20 text-sm text-red-200 flex gap-2"
            >
              <UIcon name="i-lucide-alert-circle" class="mt-0.5 shrink-0" />
              <span>{{ formError }}</span>
            </div>

            <div v-if="activeStep === 'provider'" class="p-5 space-y-4">
              <h4 class="text-sm font-semibold text-neutral-200">Provider Metadata</h4>
              <div
                v-if="mode === 'new' && !allowCreateProvider"
                class="p-3 rounded-lg border border-amber-800/50 bg-amber-950/20 text-sm text-amber-200"
              >
                Provider creation is not available for this session.
              </div>
              <div class="grid gap-3 sm:grid-cols-2">
                <div>
                  <label class="block text-xs text-neutral-400 mb-1">Namespace</label>
                  <UInput v-model="namespace" :disabled="mode === 'existing' || submitting" placeholder="acme" size="sm" />
                </div>
                <div>
                  <label class="block text-xs text-neutral-400 mb-1">Type</label>
                  <UInput v-model="type" :disabled="mode === 'existing' || submitting" placeholder="example" size="sm" />
                </div>
                <div>
                  <label class="block text-xs text-neutral-400 mb-1">Display Name</label>
                  <UInput v-model="displayName" :disabled="mode === 'existing' || submitting" placeholder="Example" size="sm" />
                </div>
                <div>
                  <label class="block text-xs text-neutral-400 mb-1">Source Repository URL</label>
                  <UInput
                    v-model="sourceRepositoryUrl"
                    :disabled="mode === 'existing' || submitting"
                    placeholder="https://github.com/acme/terraform-provider-example"
                    size="sm"
                  />
                </div>
              </div>
              <div>
                <label class="block text-xs text-neutral-400 mb-1">Description</label>
                <UTextarea
                  v-model="description"
                  :disabled="mode === 'existing' || submitting"
                  placeholder="Optional provider summary"
                  :rows="3"
                  class="w-full"
                />
              </div>
            </div>

            <div v-else-if="activeStep === 'key'" class="p-5 space-y-4">
              <div class="flex items-center justify-between gap-3">
                <h4 class="text-sm font-semibold text-neutral-200">Signing Key</h4>
                <div class="flex gap-2">
                  <UButton
                    v-if="canUseExistingKey"
                    label="Existing"
                    size="xs"
                    :variant="keyMode === 'existing' ? 'soft' : 'ghost'"
                    :disabled="submitting"
                    @click="keyMode = 'existing'"
                  />
                  <UButton
                    v-if="allowManageKeys"
                    label="New Key"
                    size="xs"
                    :variant="keyMode === 'new' ? 'soft' : 'ghost'"
                    :disabled="submitting"
                    @click="keyMode = 'new'"
                  />
                </div>
              </div>

              <div v-if="keyMode === 'existing'" class="space-y-3">
                <div
                  v-if="!availableKeys.length"
                  class="p-3 rounded-lg border border-amber-800/50 bg-amber-950/20 text-sm text-amber-200"
                >
                  No active signing keys are available.
                </div>
                <div v-else>
                  <label class="block text-xs text-neutral-400 mb-1">Active Key</label>
                  <USelect
                    v-model="selectedKeyId"
                    :items="keyOptions"
                    value-key="value"
                    label-key="label"
                    placeholder="Select signing key"
                    size="sm"
                    class="w-full"
                    :disabled="submitting"
                  />
                </div>
              </div>

              <div v-else-if="allowManageKeys" class="space-y-3">
                <div>
                  <label class="block text-xs text-neutral-400 mb-1">Key ID</label>
                  <UInput v-model="keyId" :disabled="submitting" placeholder="ABC123DEF456" size="sm" />
                </div>
                <div>
                  <label class="block text-xs text-neutral-400 mb-1">ASCII Armor</label>
                  <UTextarea
                    v-model="asciiArmor"
                    :disabled="submitting"
                    placeholder="-----BEGIN PGP PUBLIC KEY BLOCK-----"
                    :rows="8"
                    class="w-full"
                  />
                </div>
                <div class="grid gap-3 sm:grid-cols-3">
                  <div>
                    <label class="block text-xs text-neutral-400 mb-1">Trust Signature</label>
                    <UInput v-model="trustSignature" :disabled="submitting" placeholder="Optional" size="sm" />
                  </div>
                  <div>
                    <label class="block text-xs text-neutral-400 mb-1">Source</label>
                    <UInput v-model="keySource" :disabled="submitting" placeholder="keybase" size="sm" />
                  </div>
                  <div>
                    <label class="block text-xs text-neutral-400 mb-1">Source URL</label>
                    <UInput v-model="keySourceUrl" :disabled="submitting" placeholder="https://..." size="sm" />
                  </div>
                </div>
              </div>

              <div
                v-else
                class="p-3 rounded-lg border border-amber-800/50 bg-amber-950/20 text-sm text-amber-200"
              >
                Signing key management is not available for this session.
              </div>
            </div>

            <div v-else-if="activeStep === 'version'" class="p-5 space-y-4">
              <h4 class="text-sm font-semibold text-neutral-200">Version</h4>
              <div class="grid gap-3 sm:grid-cols-2">
                <div>
                  <label class="block text-xs text-neutral-400 mb-1">Version</label>
                  <UInput v-model="version" :disabled="submitting" placeholder="1.0.0" size="sm" />
                </div>
                <div>
                  <label class="block text-xs text-neutral-400 mb-1">Protocols</label>
                  <UInput v-model="protocols" :disabled="submitting" placeholder="5.0, 6.0" size="sm" />
                </div>
              </div>
              <p class="text-xs text-neutral-500">Signing key: {{ selectedOrNewKeyId || "not selected" }}</p>
            </div>

            <div v-else-if="activeStep === 'checksums'" class="p-5 space-y-4">
              <h4 class="text-sm font-semibold text-neutral-200">SHA256SUMS</h4>
              <label class="flex items-center justify-between gap-3 px-3 py-2.5 rounded-xl border border-dashed border-neutral-700 bg-neutral-950/50 cursor-pointer hover:border-primary-500/40 transition-colors">
                <span class="text-sm text-neutral-300 truncate">
                  {{ shasumsFile?.name || "Choose SHA256SUMS file" }}
                </span>
                <span class="text-xs text-neutral-500 uppercase tracking-wide">Browse</span>
                <input class="hidden" type="file" :disabled="submitting" @change="updateFile('shasums', $event)">
              </label>
            </div>

            <div v-else-if="activeStep === 'signature'" class="p-5 space-y-4">
              <h4 class="text-sm font-semibold text-neutral-200">Detached Signature</h4>
              <label class="flex items-center justify-between gap-3 px-3 py-2.5 rounded-xl border border-dashed border-neutral-700 bg-neutral-950/50 cursor-pointer hover:border-primary-500/40 transition-colors">
                <span class="text-sm text-neutral-300 truncate">
                  {{ signatureFile?.name || "Choose signature file" }}
                </span>
                <span class="text-xs text-neutral-500 uppercase tracking-wide">Browse</span>
                <input class="hidden" type="file" :disabled="submitting" @change="updateFile('signature', $event)">
              </label>
            </div>

            <div v-else class="p-5 space-y-4">
              <div class="flex items-center justify-between gap-3">
                <h4 class="text-sm font-semibold text-neutral-200">Platform Packages</h4>
                <UButton
                  label="Add Platform"
                  icon="i-lucide-plus"
                  size="xs"
                  variant="soft"
                  :disabled="submitting"
                  @click="addPlatform"
                />
              </div>

              <div class="space-y-3">
                <div
                  v-for="platform in platforms"
                  :key="platform.id"
                  class="rounded-xl border border-neutral-800 bg-neutral-950/40 p-3 space-y-3"
                >
                  <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                    <div>
                      <label class="block text-xs text-neutral-400 mb-1">OS</label>
                      <UInput v-model="platform.os" :disabled="platform.uploaded || submitting" placeholder="linux" size="sm" />
                    </div>
                    <div>
                      <label class="block text-xs text-neutral-400 mb-1">Arch</label>
                      <UInput v-model="platform.arch" :disabled="platform.uploaded || submitting" placeholder="amd64" size="sm" />
                    </div>
                    <div>
                      <label class="block text-xs text-neutral-400 mb-1">Filename</label>
                      <UInput
                        v-model="platform.filename"
                        :disabled="platform.uploaded || submitting"
                        placeholder="terraform-provider-example_1.0.0_linux_amd64.zip"
                        size="sm"
                      />
                    </div>
                    <div>
                      <label class="block text-xs text-neutral-400 mb-1">SHA256</label>
                      <UInput v-model="platform.shasum" :disabled="platform.uploaded || submitting" placeholder="SHA256" size="sm" />
                    </div>
                  </div>

                  <div class="flex items-center gap-3">
                    <label
                      class="min-w-0 flex-1 flex items-center justify-between gap-3 px-3 py-2.5 rounded-xl border border-dashed border-neutral-700 bg-neutral-950/50 transition-colors"
                      :class="platform.uploaded || submitting ? 'opacity-60' : 'cursor-pointer hover:border-primary-500/40'"
                    >
                      <span class="text-sm text-neutral-300 truncate">
                        {{ platform.file?.name || "Choose package file" }}
                      </span>
                      <span class="text-xs text-neutral-500 uppercase tracking-wide">Browse</span>
                      <input
                        class="hidden"
                        type="file"
                        accept=".zip,application/zip"
                        :disabled="platform.uploaded || submitting"
                        @change="updatePlatformFile(platform, $event)"
                      >
                    </label>
                    <UButton
                      icon="i-lucide-trash-2"
                      color="error"
                      variant="ghost"
                      size="xs"
                      :disabled="platform.uploaded || submitting || platforms.length === 1"
                      @click="removePlatform(platform.id)"
                    />
                  </div>

                  <p v-if="platform.uploaded" class="text-xs text-green-400">Uploaded</p>
                  <p v-if="platform.error" class="text-xs text-red-400">{{ platform.error }}</p>
                </div>
              </div>
            </div>

            <div class="px-5 py-4 border-t border-neutral-800 flex justify-between gap-3">
              <UButton
                label="Back"
                color="neutral"
                variant="ghost"
                :disabled="stepOrder.indexOf(activeStep) === 0 || submitting"
                @click="moveToPreviousStep"
              />
              <UButton
                :label="submitLabel"
                color="primary"
                :loading="submitting"
                :disabled="!activeStepCanSubmit"
                @click="submitActiveStep"
              />
            </div>
          </section>
        </div>
      </div>
    </template>
  </UModal>
</template>
