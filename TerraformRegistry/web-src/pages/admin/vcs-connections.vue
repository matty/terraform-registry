<script setup lang="ts">
import { useDashboard } from '~/composables/useDashboard'
import { useVcsConnections } from '~/composables/useVcsConnections'
import type { VcsConnection, VcsConnectionCreateResponse } from '~/composables/useVcsConnections'

definePageMeta({
  middleware: 'auth',
})

const { isSidebarOpen } = useDashboard()
const { listConnections, createConnection, updateConnection, deleteConnection } = useVcsConnections()

// State
const connections = ref<VcsConnection[]>([])
const isLoading = ref(false)
const isCreating = ref(false)
const errorMessage = ref<string | null>(null)

// Create form
const newLabel = ref('')
const newProvider = ref('GitHub')
const newPat = ref('')
const newDefaultOrg = ref('')

// Create success state
const createdConnection = ref<VcsConnectionCreateResponse | null>(null)
const copiedSecret = ref(false)
const copiedUrl = ref(false)
const showSuccessAnimation = ref(false)

// Edit state
const editingConnection = ref<VcsConnection | null>(null)
const editLabel = ref('')
const editPat = ref('')
const editDefaultOrg = ref('')
const editIsActive = ref(true)
const isEditModalOpen = ref(false)

// Delete state
const isDeleteModalOpen = ref(false)
const connectionToDelete = ref<VcsConnection | null>(null)

// PAT tooltip
const showPatTooltip = ref(false)

const providerOptions = ['GitHub']

const fetchConnections = async () => {
  isLoading.value = true
  errorMessage.value = null
  try {
    connections.value = await listConnections()
  }
  catch (e) {
    console.error('Failed to fetch VCS connections', e)
    errorMessage.value = 'Failed to load VCS connections.'
  }
  finally {
    isLoading.value = false
  }
}

const handleCreate = async () => {
  if (!newLabel.value) return
  isCreating.value = true
  errorMessage.value = null
  try {
    const result = await createConnection({
      label: newLabel.value,
      provider: newProvider.value || undefined,
      pat: newPat.value || undefined,
      defaultOrg: newDefaultOrg.value || undefined,
    })
    createdConnection.value = result
    newLabel.value = ''
    newProvider.value = 'GitHub'
    newPat.value = ''
    newDefaultOrg.value = ''
    // Trigger success animation
    showSuccessAnimation.value = true
    setTimeout(() => { showSuccessAnimation.value = false }, 2000)
    await fetchConnections()
  }
  catch (e: any) {
    console.error('Failed to create VCS connection', e)
    errorMessage.value = e?.data?.detail || e?.data?.message || 'Failed to create VCS connection.'
  }
  finally {
    isCreating.value = false
  }
}

const dismissCreatedConnection = () => {
  createdConnection.value = null
  copiedSecret.value = false
  copiedUrl.value = false
}

const copySecret = async () => {
  if (!createdConnection.value) return
  try {
    await navigator.clipboard.writeText(createdConnection.value.webhookSecret)
    copiedSecret.value = true
    setTimeout(() => { copiedSecret.value = false }, 2000)
  }
  catch (err) {
    console.error('Failed to copy:', err)
  }
}

const copyUrl = async () => {
  if (!createdConnection.value) return
  try {
    await navigator.clipboard.writeText(createdConnection.value.webhookUrl)
    copiedUrl.value = true
    setTimeout(() => { copiedUrl.value = false }, 2000)
  }
  catch (err) {
    console.error('Failed to copy:', err)
  }
}

const openEdit = (conn: VcsConnection) => {
  editingConnection.value = conn
  editLabel.value = conn.label
  editPat.value = ''
  editDefaultOrg.value = conn.defaultOrg || ''
  editIsActive.value = conn.isActive
  isEditModalOpen.value = true
}

const handleUpdate = async () => {
  if (!editingConnection.value) return
  errorMessage.value = null
  try {
    await updateConnection(editingConnection.value.id, {
      label: editLabel.value || undefined,
      pat: editPat.value || undefined,
      defaultOrg: editDefaultOrg.value || undefined,
      isActive: editIsActive.value,
    })
    isEditModalOpen.value = false
    editingConnection.value = null
    await fetchConnections()
  }
  catch (e: any) {
    console.error('Failed to update VCS connection', e)
    errorMessage.value = e?.data?.detail || e?.data?.message || 'Failed to update VCS connection.'
  }
}

const confirmDelete = (conn: VcsConnection) => {
  connectionToDelete.value = conn
  isDeleteModalOpen.value = true
}

const handleDelete = async () => {
  if (!connectionToDelete.value) return
  errorMessage.value = null
  try {
    await deleteConnection(connectionToDelete.value.id)
    await fetchConnections()
  }
  catch (e: any) {
    console.error('Failed to delete VCS connection', e)
    errorMessage.value = e?.data?.detail || e?.data?.message || 'Failed to delete VCS connection. It may still be referenced by modules.'
  }
  finally {
    isDeleteModalOpen.value = false
    connectionToDelete.value = null
  }
}

const formatDate = (dateString: string) => {
  return new Date(dateString).toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

onMounted(() => {
  fetchConnections()
})
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
          <h1 class="page-header-title">
            VCS Connections
          </h1>
          <p class="page-header-subtitle">
            Manage version control system connections for module publishing
          </p>
        </div>
      </div>
    </div>
    <div class="page-divider" />

    <!-- Body -->
    <div class="flex-1 overflow-y-auto px-6 py-6">
      <div class="max-w-4xl space-y-8">
        <!-- Error Message -->
        <div
          v-if="errorMessage"
          class="p-4 bg-red-900/20 border border-red-800/50 rounded-xl flex items-center gap-3 backdrop-blur-sm"
        >
          <UIcon name="i-lucide-alert-circle" class="text-red-500 text-xl shrink-0" />
          <p class="text-sm text-red-300">
            {{ errorMessage }}
          </p>
          <UButton
            icon="i-lucide-x"
            color="neutral"
            variant="ghost"
            size="sm"
            class="ml-auto"
            @click="errorMessage = null"
          />
        </div>

        <!-- Success Panel -->
        <Transition name="success-panel">
          <div v-if="createdConnection" class="success-card rounded-2xl border border-green-700/40 overflow-hidden">
            <!-- Celebration header -->
            <div class="px-6 py-5 border-b border-green-800/30 bg-green-900/20 flex items-center gap-4">
              <div :class="['success-check w-12 h-12 rounded-xl bg-green-500/20 flex items-center justify-center', { 'animate-success': showSuccessAnimation }]">
                <UIcon name="i-lucide-check-circle" class="text-green-400 text-2xl" />
              </div>
              <div>
                <h3 class="text-lg font-semibold text-green-200">Connection Created Successfully</h3>
                <p class="text-sm text-green-300/70 mt-0.5">Save the webhook credentials below -- the secret will not be shown again</p>
              </div>
            </div>

            <div class="p-6 space-y-5">
              <!-- Webhook Secret -->
              <div class="space-y-2">
                <label class="text-xs font-medium text-neutral-400 uppercase tracking-wider">Webhook Secret</label>
                <div class="secret-block group flex items-center gap-3 p-3 rounded-xl bg-neutral-900/80 border border-green-600/30 transition-all hover:border-green-500/50">
                  <code class="flex-1 font-mono text-sm text-green-300 break-all leading-relaxed">{{ createdConnection.webhookSecret }}</code>
                  <UButton
                    :icon="copiedSecret ? 'i-lucide-check' : 'i-lucide-copy'"
                    :color="copiedSecret ? 'success' : 'neutral'"
                    variant="soft"
                    size="sm"
                    :label="copiedSecret ? 'Copied' : 'Copy'"
                    @click="copySecret"
                  />
                </div>
              </div>

              <!-- Webhook URL -->
              <div class="space-y-2">
                <label class="text-xs font-medium text-neutral-400 uppercase tracking-wider">Webhook URL</label>
                <div class="secret-block group flex items-center gap-3 p-3 rounded-xl bg-neutral-900/80 border border-green-600/30 transition-all hover:border-green-500/50">
                  <code class="flex-1 font-mono text-sm text-green-300 break-all leading-relaxed">{{ createdConnection.webhookUrl }}</code>
                  <UButton
                    :icon="copiedUrl ? 'i-lucide-check' : 'i-lucide-copy'"
                    :color="copiedUrl ? 'success' : 'neutral'"
                    variant="soft"
                    size="sm"
                    :label="copiedUrl ? 'Copied' : 'Copy'"
                    @click="copyUrl"
                  />
                </div>
              </div>

              <!-- Setup guide -->
              <div class="rounded-xl border border-neutral-700/50 bg-neutral-900/40 overflow-hidden">
                <div class="px-4 py-3 border-b border-neutral-800/60 flex items-center gap-2">
                  <UIcon name="i-lucide-book-open" class="text-primary-400" />
                  <span class="text-sm font-medium text-neutral-300">GitHub Webhook Setup</span>
                </div>
                <div class="p-4 space-y-3">
                  <div class="flex gap-3">
                    <span class="flex items-center justify-center w-6 h-6 rounded-full bg-primary-500/15 text-primary-400 text-xs font-bold shrink-0 mt-0.5">1</span>
                    <p class="text-sm text-neutral-400">
                      Navigate to your repository on GitHub and open
                      <span class="text-neutral-200 font-medium">Settings</span> then
                      <span class="text-neutral-200 font-medium">Webhooks</span>
                    </p>
                  </div>
                  <div class="flex gap-3">
                    <span class="flex items-center justify-center w-6 h-6 rounded-full bg-primary-500/15 text-primary-400 text-xs font-bold shrink-0 mt-0.5">2</span>
                    <p class="text-sm text-neutral-400">
                      Click <span class="text-neutral-200 font-medium">Add webhook</span> and paste the
                      <span class="text-green-300">Webhook URL</span> into the Payload URL field
                    </p>
                  </div>
                  <div class="flex gap-3">
                    <span class="flex items-center justify-center w-6 h-6 rounded-full bg-primary-500/15 text-primary-400 text-xs font-bold shrink-0 mt-0.5">3</span>
                    <p class="text-sm text-neutral-400">
                      Paste the <span class="text-green-300">Webhook Secret</span> into the Secret field
                    </p>
                  </div>
                  <div class="flex gap-3">
                    <span class="flex items-center justify-center w-6 h-6 rounded-full bg-primary-500/15 text-primary-400 text-xs font-bold shrink-0 mt-0.5">4</span>
                    <p class="text-sm text-neutral-400">
                      Set Content type to <code class="px-1.5 py-0.5 rounded bg-neutral-800 text-primary-300 text-xs">application/json</code>
                      and select <span class="text-neutral-200 font-medium">Just the push event</span>
                    </p>
                  </div>
                  <div class="flex gap-3">
                    <span class="flex items-center justify-center w-6 h-6 rounded-full bg-green-500/15 text-green-400 text-xs font-bold shrink-0 mt-0.5">5</span>
                    <p class="text-sm text-neutral-400">
                      Click <span class="text-neutral-200 font-medium">Add webhook</span> to save
                    </p>
                  </div>
                </div>
              </div>

              <div class="flex justify-end">
                <UButton label="Dismiss" color="neutral" variant="soft" @click="dismissCreatedConnection" />
              </div>
            </div>
          </div>
        </Transition>

        <!-- Create Connection Form -->
        <div class="create-card rounded-2xl border border-neutral-800/80 overflow-hidden">
          <!-- GitHub hero area -->
          <div class="px-6 py-8 border-b border-neutral-800/60 bg-neutral-900/40 text-center">
            <div class="inline-flex items-center justify-center w-16 h-16 rounded-2xl bg-neutral-800 border border-neutral-700/50 mb-4">
              <UIcon name="i-lucide-github" class="text-4xl text-neutral-200" />
            </div>
            <h3 class="text-lg font-semibold text-neutral-100">New VCS Connection</h3>
            <p class="text-sm text-neutral-500 mt-1">Connect a GitHub account to enable module publishing via webhooks</p>
          </div>

          <div class="p-6 space-y-5">
            <!-- Label + Provider row -->
            <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div class="space-y-1.5">
                <label class="block text-xs font-medium text-neutral-400">
                  Label <span class="text-red-400">*</span>
                </label>
                <UInput
                  v-model="newLabel"
                  placeholder="e.g. Production GitHub"
                  size="lg"
                />
              </div>
              <div class="space-y-1.5">
                <label class="block text-xs font-medium text-neutral-400">Provider</label>
                <USelect
                  v-model="newProvider"
                  :items="providerOptions"
                  size="lg"
                />
              </div>
            </div>

            <!-- PAT -->
            <div class="space-y-1.5">
              <div class="flex items-center gap-2">
                <label class="block text-xs font-medium text-neutral-400">Personal Access Token</label>
                <button
                  type="button"
                  class="relative text-neutral-500 hover:text-neutral-400 transition-colors"
                  @mouseenter="showPatTooltip = true"
                  @mouseleave="showPatTooltip = false"
                >
                  <UIcon name="i-lucide-circle-help" class="text-sm" />
                  <!-- Tooltip -->
                  <Transition name="fade">
                    <div
                      v-if="showPatTooltip"
                      class="absolute bottom-full left-1/2 -translate-x-1/2 mb-2 w-72 p-3 rounded-xl bg-neutral-800 border border-neutral-700 shadow-xl z-50 text-left"
                    >
                      <p class="text-xs text-neutral-300 leading-relaxed">
                        A <span class="text-primary-400 font-medium">Personal Access Token</span> (PAT) allows the registry to access private repositories.
                        Generate one in GitHub under
                        <span class="text-neutral-200">Settings</span> /
                        <span class="text-neutral-200">Developer settings</span> /
                        <span class="text-neutral-200">Personal access tokens</span>.
                        Only <code class="px-1 py-0.5 rounded bg-neutral-900 text-primary-300 text-[10px]">repo</code> scope is required.
                      </p>
                      <div class="absolute top-full left-1/2 -translate-x-1/2 w-2 h-2 bg-neutral-800 border-r border-b border-neutral-700 rotate-45 -mt-1" />
                    </div>
                  </Transition>
                </button>
              </div>
              <UInput
                v-model="newPat"
                type="password"
                placeholder="ghp_... (optional, for private repos)"
              />
            </div>

            <!-- Default Org -->
            <div class="space-y-1.5">
              <label class="block text-xs font-medium text-neutral-400">Default Organization</label>
              <UInput
                v-model="newDefaultOrg"
                placeholder="Optional -- e.g. acme-corp"
              />
            </div>

            <div class="flex justify-end pt-2 border-t border-neutral-800/50">
              <UButton
                icon="i-lucide-plug"
                label="Create Connection"
                color="primary"
                size="lg"
                :loading="isCreating"
                :disabled="!newLabel"
                @click="handleCreate"
              />
            </div>
          </div>
        </div>

        <!-- Connections List -->
        <div class="space-y-4">
          <h2 class="text-base font-semibold text-neutral-200 flex items-center gap-3">
            <div class="w-8 h-8 rounded-lg bg-neutral-800 flex items-center justify-center">
              <UIcon name="i-lucide-git-branch" class="text-primary-400" />
            </div>
            All Connections
            <span v-if="connections.length > 0" class="ml-1 px-2 py-0.5 rounded-full bg-neutral-800 text-neutral-400 text-xs font-medium">
              {{ connections.length }}
            </span>
          </h2>

          <div v-if="isLoading" class="py-12 text-center">
            <UIcon
              name="i-lucide-loader-2"
              class="animate-spin text-3xl text-primary-400"
            />
          </div>

          <div
            v-else-if="connections.length === 0"
            class="py-12 text-center rounded-2xl border border-dashed border-neutral-800 bg-neutral-900/20"
          >
            <UIcon name="i-lucide-git-branch" class="text-4xl text-neutral-700 mb-3" />
            <p class="text-neutral-500">No VCS connections found</p>
            <p class="text-sm text-neutral-600 mt-1">Create one above to get started</p>
          </div>

          <div v-else class="space-y-3">
            <div
              v-for="conn in connections"
              :key="conn.id"
              class="connection-card rounded-xl border border-neutral-800 transition-all duration-200 hover:border-neutral-700 overflow-hidden"
            >
              <div class="p-5">
                <div class="flex items-start justify-between gap-4">
                  <div class="flex items-start gap-4 min-w-0 flex-1">
                    <!-- GitHub icon -->
                    <div class="w-11 h-11 rounded-xl bg-neutral-800 border border-neutral-700/50 flex items-center justify-center shrink-0">
                      <UIcon name="i-lucide-github" class="text-xl text-neutral-300" />
                    </div>
                    <div class="min-w-0 flex-1 space-y-2">
                      <!-- Header row -->
                      <div class="flex items-center gap-2.5 flex-wrap">
                        <span class="font-semibold text-neutral-100">{{ conn.label }}</span>
                        <span class="px-2.5 py-0.5 rounded-full text-[11px] font-semibold bg-indigo-900/40 text-indigo-300 uppercase tracking-wide">
                          {{ conn.provider || 'GitHub' }}
                        </span>
                        <span
                          :class="[
                            'flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[11px] font-medium',
                            conn.isActive
                              ? 'bg-green-900/40 text-green-300'
                              : 'bg-neutral-800 text-neutral-400'
                          ]"
                        >
                          <span :class="['w-1.5 h-1.5 rounded-full', conn.isActive ? 'bg-green-400 animate-pulse' : 'bg-neutral-500']" />
                          {{ conn.isActive ? 'Active' : 'Inactive' }}
                        </span>
                      </div>
                      <!-- Meta row -->
                      <div class="flex items-center gap-4 text-xs text-neutral-500">
                        <span v-if="conn.defaultOrg" class="flex items-center gap-1.5">
                          <UIcon name="i-lucide-building-2" class="text-[12px]" />
                          {{ conn.defaultOrg }}
                        </span>
                        <span class="flex items-center gap-1.5">
                          <UIcon name="i-lucide-calendar" class="text-[12px]" />
                          {{ formatDate(conn.createdAt) }}
                        </span>
                      </div>
                    </div>
                  </div>
                </div>
                <!-- Action toolbar -->
                <div class="flex items-center justify-end mt-4 pt-3 border-t border-neutral-800/50">
                  <div class="flex items-center gap-1">
                    <UButton
                      icon="i-lucide-pencil"
                      color="neutral"
                      variant="ghost"
                      size="xs"
                      label="Edit"
                      @click="openEdit(conn)"
                    />
                    <UButton
                      icon="i-lucide-trash-2"
                      color="error"
                      variant="ghost"
                      size="xs"
                      @click="confirmDelete(conn)"
                    />
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Edit Connection Modal -->
    <UModal v-model:open="isEditModalOpen">
      <template #content>
        <div class="p-6 space-y-5">
          <div class="flex items-center gap-3">
            <div class="w-12 h-12 rounded-xl bg-primary-600/20 flex items-center justify-center">
              <UIcon name="i-lucide-pencil" class="text-2xl text-primary-400" />
            </div>
            <div>
              <h3 class="text-lg font-semibold text-neutral-100">
                Edit Connection
              </h3>
              <p class="text-sm text-neutral-400">
                Update connection settings
              </p>
            </div>
          </div>
          <div class="flex flex-col gap-4">
            <div>
              <label class="block text-xs font-medium text-neutral-400 mb-1.5">Label</label>
              <UInput v-model="editLabel" placeholder="Connection label" />
            </div>
            <div>
              <label class="block text-xs font-medium text-neutral-400 mb-1.5">Personal Access Token</label>
              <UInput v-model="editPat" type="password" placeholder="Leave blank to keep current" />
            </div>
            <div>
              <label class="block text-xs font-medium text-neutral-400 mb-1.5">Default Organization</label>
              <UInput v-model="editDefaultOrg" placeholder="Optional" />
            </div>
            <div>
              <label class="flex items-center gap-2 text-sm text-neutral-300 cursor-pointer">
                <input v-model="editIsActive" type="checkbox" class="accent-primary-500 rounded" />
                Active
              </label>
            </div>
          </div>
          <div class="flex justify-end gap-2 pt-2 border-t border-neutral-800/50">
            <UButton
              color="neutral"
              variant="ghost"
              label="Cancel"
              @click="isEditModalOpen = false"
            />
            <UButton
              color="primary"
              label="Save Changes"
              :disabled="!editLabel"
              @click="handleUpdate"
            />
          </div>
        </div>
      </template>
    </UModal>

    <!-- Delete Confirmation Modal -->
    <UModal v-model:open="isDeleteModalOpen">
      <template #content>
        <div class="p-6">
          <div class="flex items-center gap-3 mb-4">
            <div class="w-12 h-12 rounded-xl bg-red-600/20 flex items-center justify-center">
              <UIcon name="i-lucide-trash-2" class="text-2xl text-red-500" />
            </div>
            <div>
              <h3 class="text-lg font-semibold text-neutral-100">
                Delete Connection
              </h3>
              <p class="text-sm text-neutral-400">
                This action cannot be undone
              </p>
            </div>
          </div>
          <p class="text-neutral-300 mb-6">
            Are you sure you want to delete the connection <strong>{{ connectionToDelete?.label }}</strong>? This will fail if any modules still reference it.
          </p>
          <div class="flex justify-end gap-2">
            <UButton
              color="neutral"
              variant="ghost"
              label="Cancel"
              @click="isDeleteModalOpen = false"
            />
            <UButton
              color="error"
              label="Delete Connection"
              @click="handleDelete"
            />
          </div>
        </div>
      </template>
    </UModal>
  </div>
</template>

<style scoped>
.create-card {
  background: linear-gradient(145deg, rgba(23, 23, 23, 0.8), rgba(10, 10, 10, 0.9));
  backdrop-filter: blur(12px);
}

.success-card {
  background: linear-gradient(145deg, rgba(20, 30, 20, 0.9), rgba(10, 15, 10, 0.95));
  backdrop-filter: blur(12px);
}

.connection-card {
  background: linear-gradient(145deg, rgba(23, 23, 23, 0.6), rgba(15, 15, 15, 0.8));
  backdrop-filter: blur(8px);
}

.connection-card:hover {
  background: linear-gradient(145deg, rgba(28, 28, 28, 0.7), rgba(18, 18, 18, 0.9));
}

.secret-block:hover {
  box-shadow: 0 0 20px rgba(34, 197, 94, 0.08);
}

.animate-success {
  animation: successPop 0.6s ease-out;
}

@keyframes successPop {
  0% { transform: scale(0.5); opacity: 0; }
  50% { transform: scale(1.15); }
  100% { transform: scale(1); opacity: 1; }
}

.success-panel-enter-active {
  transition: all 0.4s ease-out;
}

.success-panel-leave-active {
  transition: all 0.3s ease-in;
}

.success-panel-enter-from {
  opacity: 0;
  transform: translateY(-16px) scale(0.98);
}

.success-panel-leave-to {
  opacity: 0;
  transform: translateY(-8px);
}

.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.15s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>
