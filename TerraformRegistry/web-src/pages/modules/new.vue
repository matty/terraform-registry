<script setup lang="ts">
import { useDashboard } from '~/composables/useDashboard'
import { useVcsSources } from '~/composables/useVcsSources'
import type { VcsSourceCreateResponse } from '~/composables/useVcsSources'

definePageMeta({
  middleware: 'auth',
})

const router = useRouter()
const { isSidebarOpen } = useDashboard()
const { createVcsSource } = useVcsSources()

// Form fields
const namespace = ref('')
const name = ref('')
const provider = ref('')
const description = ref('')

// VCS toggle
const linkToGitHub = ref(false)
const repoOwner = ref('')
const repoName = ref('')
const pat = ref('')

// State
const isSubmitting = ref(false)
const errorMessage = ref<string | null>(null)
const createdVcsSource = ref<VcsSourceCreateResponse | null>(null)
const copiedSecret = ref(false)
const copiedUrl = ref(false)

const canSubmit = computed(() => {
  if (!namespace.value || !name.value || !provider.value) return false
  if (linkToGitHub.value && (!repoOwner.value || !repoName.value)) return false
  return true
})

const handleSubmit = async () => {
  if (!canSubmit.value) return
  isSubmitting.value = true
  errorMessage.value = null

  try {
    if (!linkToGitHub.value) {
      await router.push('/')
      return
    }

    const result = await createVcsSource({
      namespace: namespace.value,
      name: name.value,
      provider: provider.value,
      repoOwner: repoOwner.value,
      repoName: repoName.value,
      pat: pat.value || undefined,
    })

    createdVcsSource.value = result
  } catch (e: any) {
    const msg = e?.data?.message || e?.data?.error || e?.message || 'Failed to create VCS source'
    errorMessage.value = msg
  } finally {
    isSubmitting.value = false
  }
}

const copySecret = async () => {
  if (!createdVcsSource.value) return
  try {
    await navigator.clipboard.writeText(createdVcsSource.value.webhookSecret)
    copiedSecret.value = true
    setTimeout(() => { copiedSecret.value = false }, 2000)
  } catch (err) {
    console.error('Failed to copy:', err)
  }
}

const copyUrl = async () => {
  if (!createdVcsSource.value) return
  try {
    await navigator.clipboard.writeText(createdVcsSource.value.webhookUrl)
    copiedUrl.value = true
    setTimeout(() => { copiedUrl.value = false }, 2000)
  } catch (err) {
    console.error('Failed to copy:', err)
  }
}
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
          <div class="flex items-center gap-2 mb-2">
            <NuxtLink
              to="/"
              class="text-sm text-neutral-500 hover:text-primary-400 transition-colors flex items-center gap-1"
            >
              <UIcon name="i-lucide-chevron-left" class="text-xs" />
              Modules
            </NuxtLink>
          </div>
          <h1 class="page-header-title">Add Module</h1>
          <p class="page-header-subtitle">Register a new module, optionally linked to a GitHub repository</p>
        </div>
      </div>
    </div>
    <div class="page-divider" />

    <!-- Body -->
    <div class="flex-1 overflow-y-auto px-6 py-6">
      <div class="max-w-2xl space-y-6">
        <!-- Error Message -->
        <div
          v-if="errorMessage"
          class="p-4 bg-red-900/20 border border-red-800/50 rounded-xl flex items-center gap-3"
        >
          <UIcon name="i-lucide-alert-circle" class="text-red-500 text-xl" />
          <p class="text-sm text-red-300 flex-1">{{ errorMessage }}</p>
          <UButton
            icon="i-lucide-x"
            color="neutral"
            variant="ghost"
            size="sm"
            @click="errorMessage = null"
          />
        </div>

        <!-- Success Panel (shown after VCS source creation) -->
        <div
          v-if="createdVcsSource"
          class="space-y-4"
        >
          <div class="p-5 bg-green-900/20 border border-green-800/50 rounded-xl">
            <div class="flex items-start gap-3">
              <UIcon name="i-lucide-check-circle" class="text-green-500 text-xl mt-0.5" />
              <div class="flex-1 space-y-4">
                <div>
                  <h3 class="font-medium text-green-200">VCS Source Created</h3>
                  <p class="text-sm text-green-300/80 mt-1">
                    Copy the webhook secret and URL below — the secret will not be shown again.
                  </p>
                </div>

                <!-- Webhook Secret -->
                <div>
                  <p class="text-xs text-neutral-400 mb-1.5">Webhook Secret</p>
                  <div class="flex items-center gap-2">
                    <code class="flex-1 p-2.5 bg-neutral-900 rounded-lg border border-green-800/40 font-mono text-sm break-all text-green-200">
                      {{ createdVcsSource.webhookSecret }}
                    </code>
                    <UButton
                      :icon="copiedSecret ? 'i-lucide-check' : 'i-lucide-copy'"
                      :color="copiedSecret ? 'success' : 'neutral'"
                      variant="soft"
                      size="sm"
                      @click="copySecret"
                    />
                  </div>
                </div>

                <!-- Webhook URL -->
                <div>
                  <p class="text-xs text-neutral-400 mb-1.5">Webhook URL</p>
                  <div class="flex items-center gap-2">
                    <code class="flex-1 p-2.5 bg-neutral-900 rounded-lg border border-green-800/40 font-mono text-sm break-all text-green-200">
                      {{ createdVcsSource.webhookUrl }}
                    </code>
                    <UButton
                      :icon="copiedUrl ? 'i-lucide-check' : 'i-lucide-copy'"
                      :color="copiedUrl ? 'success' : 'neutral'"
                      variant="soft"
                      size="sm"
                      @click="copyUrl"
                    />
                  </div>
                </div>

                <!-- Instructions -->
                <div class="p-3 bg-neutral-800/50 rounded-lg border border-neutral-700/50">
                  <p class="text-sm text-neutral-300 leading-relaxed">
                    Add a webhook in your GitHub repository settings
                    (<span class="text-neutral-200">Settings</span> →
                    <span class="text-neutral-200">Webhooks</span> →
                    <span class="text-neutral-200">Add webhook</span>).
                    Set the Payload URL and Secret, choose content type
                    <code class="text-primary-300 text-xs">application/json</code>,
                    and select "Just the push event".
                  </p>
                </div>
              </div>
            </div>
          </div>

          <div class="flex justify-end">
            <UButton
              label="Done"
              color="primary"
              icon="i-lucide-check"
              @click="router.push('/')"
            />
          </div>
        </div>

        <!-- Create Form (hidden after successful VCS creation) -->
        <template v-if="!createdVcsSource">
          <!-- Module Info -->
          <div class="p-5 bg-neutral-900/60 rounded-xl border border-neutral-800 ring-1 ring-neutral-800/50">
            <h3 class="text-sm font-semibold mb-4 text-neutral-200 flex items-center gap-2">
              <UIcon name="i-lucide-package" class="text-primary-400" />
              Module Details
            </h3>
            <div class="flex flex-col gap-4">
              <div>
                <label class="block text-xs text-neutral-400 mb-1.5">Namespace <span class="text-red-400">*</span></label>
                <UInput
                  v-model="namespace"
                  placeholder="e.g. myorg"
                />
              </div>
              <div>
                <label class="block text-xs text-neutral-400 mb-1.5">Name <span class="text-red-400">*</span></label>
                <UInput
                  v-model="name"
                  placeholder="e.g. vpc"
                />
              </div>
              <div>
                <label class="block text-xs text-neutral-400 mb-1.5">Provider <span class="text-red-400">*</span></label>
                <UInput
                  v-model="provider"
                  placeholder="e.g. aws"
                />
              </div>
              <div>
                <label class="block text-xs text-neutral-400 mb-1.5">Description</label>
                <UTextarea
                  v-model="description"
                  placeholder="Optional module description"
                  :rows="2"
                />
              </div>
            </div>
          </div>

          <!-- VCS Toggle Section -->
          <div class="p-5 bg-neutral-900/60 rounded-xl border border-neutral-800 ring-1 ring-neutral-800/50">
            <div class="flex items-center justify-between mb-4">
              <h3 class="text-sm font-semibold text-neutral-200 flex items-center gap-2">
                <UIcon name="i-lucide-github" class="text-primary-400" />
                GitHub Integration
              </h3>
              <label class="flex items-center gap-2 text-sm text-neutral-400 cursor-pointer">
                <span>Link to GitHub Repository</span>
                <input
                  v-model="linkToGitHub"
                  type="checkbox"
                  class="accent-neutral-500 rounded"
                />
              </label>
            </div>

            <div v-if="linkToGitHub" class="flex flex-col gap-4">
              <div>
                <label class="block text-xs text-neutral-400 mb-1.5">Repository Owner <span class="text-red-400">*</span></label>
                <UInput
                  v-model="repoOwner"
                  placeholder="e.g. acme"
                />
              </div>
              <div>
                <label class="block text-xs text-neutral-400 mb-1.5">Repository Name <span class="text-red-400">*</span></label>
                <UInput
                  v-model="repoName"
                  placeholder="e.g. terraform-vpc"
                />
              </div>
              <div>
                <label class="block text-xs text-neutral-400 mb-1.5">Personal Access Token</label>
                <UInput
                  v-model="pat"
                  type="password"
                  placeholder="Optional — required for private repos"
                />
                <p class="text-xs text-neutral-500 mt-1">Only needed if the repository is private.</p>
              </div>
            </div>

            <div v-else class="text-sm text-neutral-500">
              Enable this to automatically publish versions when you push Git tags.
            </div>
          </div>

          <!-- Submit -->
          <div class="flex justify-end gap-3">
            <UButton
              label="Cancel"
              color="neutral"
              variant="ghost"
              @click="router.push('/')"
            />
            <UButton
              :label="linkToGitHub ? 'Create & Link Repository' : 'Continue'"
              color="primary"
              :loading="isSubmitting"
              :disabled="!canSubmit"
              @click="handleSubmit"
            />
          </div>
        </template>
      </div>
    </div>
  </div>
</template>
