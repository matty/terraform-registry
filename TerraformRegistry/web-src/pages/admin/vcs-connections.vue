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
      <div class="max-w-4xl space-y-6">
        <!-- Error Message -->
        <div
          v-if="errorMessage"
          class="p-4 bg-red-900/20 border border-red-800/50 rounded-xl flex items-center gap-3"
        >
          <UIcon name="i-lucide-alert-circle" class="text-red-500 text-xl" />
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

        <!-- Webhook Success Panel -->
        <div v-if="createdConnection" class="p-5 bg-neutral-900/60 rounded-xl border border-green-800/50 ring-1 ring-green-800/30">
          <div class="flex items-start gap-3">
            <UIcon name="i-lucide-check-circle" class="text-green-500 text-xl mt-0.5" />
            <div class="flex-1 space-y-4">
              <div>
                <h4 class="font-medium text-green-200">Connection Created</h4>
                <p class="text-sm text-green-300/80 mt-1">Copy the webhook secret and URL below. The secret will not be shown again.</p>
              </div>

              <div>
                <p class="text-xs text-neutral-400 mb-1.5">Webhook Secret</p>
                <div class="flex items-center gap-2">
                  <code class="flex-1 p-2 bg-neutral-900 rounded-lg border border-green-800/40 font-mono text-xs break-all text-green-200">{{ createdConnection.webhookSecret }}</code>
                  <UButton :icon="copiedSecret ? 'i-lucide-check' : 'i-lucide-copy'" :color="copiedSecret ? 'success' : 'neutral'" variant="soft" size="xs" @click="copySecret" />
                </div>
              </div>

              <div>
                <p class="text-xs text-neutral-400 mb-1.5">Webhook URL</p>
                <div class="flex items-center gap-2">
                  <code class="flex-1 p-2 bg-neutral-900 rounded-lg border border-green-800/40 font-mono text-xs break-all text-green-200">{{ createdConnection.webhookUrl }}</code>
                  <UButton :icon="copiedUrl ? 'i-lucide-check' : 'i-lucide-copy'" :color="copiedUrl ? 'success' : 'neutral'" variant="soft" size="xs" @click="copyUrl" />
                </div>
              </div>

              <div class="p-3 bg-neutral-800/50 rounded-lg border border-neutral-700/50">
                <p class="text-xs text-neutral-300 leading-relaxed">
                  Add a webhook in your GitHub repo settings
                  (<span class="text-neutral-200">Settings</span> →
                  <span class="text-neutral-200">Webhooks</span> →
                  <span class="text-neutral-200">Add webhook</span>).
                  Set the Payload URL and Secret, choose
                  <code class="text-primary-300">application/json</code>,
                  and select "Just the push event".
                </p>
              </div>

              <div class="flex justify-end">
                <UButton label="Dismiss" color="neutral" variant="ghost" size="sm" @click="dismissCreatedConnection" />
              </div>
            </div>
          </div>
        </div>

        <!-- Create Connection Form -->
        <div class="p-5 bg-neutral-900/60 rounded-xl border border-neutral-800 ring-1 ring-neutral-800/50">
          <h3 class="text-sm font-semibold mb-3 text-neutral-200 flex items-center gap-2">
            <UIcon name="i-lucide-plus-circle" class="text-primary-400" />
            Create Connection
          </h3>
          <div class="flex flex-col gap-3">
            <div class="grid grid-cols-2 gap-3">
              <div>
                <label class="block text-xs text-neutral-400 mb-1">Label <span class="text-red-400">*</span></label>
                <UInput
                  v-model="newLabel"
                  placeholder="e.g. Production GitHub"
                />
              </div>
              <div>
                <label class="block text-xs text-neutral-400 mb-1">Provider</label>
                <USelect
                  v-model="newProvider"
                  :items="providerOptions"
                />
              </div>
            </div>
            <div>
              <label class="block text-xs text-neutral-400 mb-1">Personal Access Token</label>
              <UInput
                v-model="newPat"
                type="password"
                placeholder="Optional — for private repos"
              />
            </div>
            <div>
              <label class="block text-xs text-neutral-400 mb-1">Default Organization</label>
              <UInput
                v-model="newDefaultOrg"
                placeholder="Optional — e.g. acme-corp"
              />
            </div>
            <div class="flex justify-end">
              <UButton
                label="Create Connection"
                color="primary"
                :loading="isCreating"
                :disabled="!newLabel"
                @click="handleCreate"
              />
            </div>
          </div>
        </div>

        <!-- Connections List -->
        <div>
          <h2 class="text-base font-semibold text-neutral-200 mb-3 flex items-center gap-2">
            <UIcon name="i-lucide-git-branch" class="text-primary-400" />
            All Connections
          </h2>

          <div v-if="isLoading" class="py-8 text-center">
            <UIcon
              name="i-lucide-loader-2"
              class="animate-spin text-2xl text-primary-400"
            />
          </div>

          <div
            v-else-if="connections.length === 0"
            class="py-8 text-center text-neutral-500"
          >
            <p>No VCS connections found.</p>
          </div>

          <div v-else class="space-y-2">
            <div
              v-for="conn in connections"
              :key="conn.id"
              class="flex items-center justify-between p-4 rounded-xl bg-neutral-900/40 border border-neutral-800 hover:border-neutral-700 transition-colors"
            >
              <div class="min-w-0 flex-1">
                <div class="flex items-center gap-2">
                  <span class="font-medium text-neutral-100 text-sm">{{ conn.label }}</span>
                  <span class="px-2 py-0.5 rounded-full text-[11px] font-medium bg-indigo-900/40 text-indigo-300">
                    {{ conn.provider || 'GitHub' }}
                  </span>
                  <span
                    :class="[
                      'px-2 py-0.5 rounded-full text-[11px] font-medium',
                      conn.isActive
                        ? 'bg-green-900/40 text-green-300'
                        : 'bg-neutral-800 text-neutral-400'
                    ]"
                  >
                    {{ conn.isActive ? 'Active' : 'Inactive' }}
                  </span>
                </div>
                <div class="flex items-center gap-3 mt-1.5">
                  <span v-if="conn.defaultOrg" class="text-xs text-neutral-500 flex items-center gap-1">
                    <UIcon name="i-lucide-building-2" class="text-[11px]" />
                    {{ conn.defaultOrg }}
                  </span>
                  <span class="text-xs text-neutral-600">
                    Created {{ formatDate(conn.createdAt) }}
                  </span>
                </div>
              </div>
              <div class="flex items-center gap-2 ml-4">
                <UButton
                  icon="i-lucide-pencil"
                  color="neutral"
                  variant="ghost"
                  size="sm"
                  title="Edit"
                  @click="openEdit(conn)"
                />
                <UButton
                  icon="i-lucide-trash-2"
                  color="error"
                  variant="ghost"
                  size="sm"
                  title="Delete"
                  @click="confirmDelete(conn)"
                />
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Edit Connection Modal -->
    <UModal v-model:open="isEditModalOpen">
      <template #content>
        <div class="p-6">
          <div class="flex items-center gap-3 mb-4">
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
          <div class="flex flex-col gap-3 mb-6">
            <div>
              <label class="block text-xs text-neutral-400 mb-1">Label</label>
              <UInput v-model="editLabel" placeholder="Connection label" />
            </div>
            <div>
              <label class="block text-xs text-neutral-400 mb-1">Personal Access Token</label>
              <UInput v-model="editPat" type="password" placeholder="Leave blank to keep current" />
            </div>
            <div>
              <label class="block text-xs text-neutral-400 mb-1">Default Organization</label>
              <UInput v-model="editDefaultOrg" placeholder="Optional" />
            </div>
            <div>
              <label class="flex items-center gap-2 text-sm text-neutral-300 cursor-pointer">
                <input v-model="editIsActive" type="checkbox" class="accent-primary-500 rounded" />
                Active
              </label>
            </div>
          </div>
          <div class="flex justify-end gap-2">
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
