<script setup lang="ts">
import { useDashboard } from '~/composables/useDashboard'
import { useAdmin } from '~/composables/useAdmin'
import type { AdminRole } from '~/composables/useAdmin'

definePageMeta({
  middleware: 'auth',
})

const { isSidebarOpen } = useDashboard()
const { listRoles, createRole, updateRole, deleteRole } = useAdmin()

// State
const roles = ref<AdminRole[]>([])
const isLoading = ref(false)
const isCreating = ref(false)
const errorMessage = ref<string | null>(null)

// Permission categories
const permissionCategories = [
  {
    label: 'Modules',
    permissions: [
      { value: 'modules.read', label: 'Read' },
      { value: 'modules.upload', label: 'Upload' },
      { value: 'modules.delete', label: 'Delete' },
      { value: 'modules.restore', label: 'Restore' },
      { value: 'modules.purge', label: 'Purge' },
      { value: 'modules.description', label: 'Description' },
    ],
  },
  {
    label: 'Webhooks',
    permissions: [
      { value: 'webhooks.manage', label: 'Manage' },
    ],
  },
  {
    label: 'VCS',
    permissions: [
      { value: 'vcs.manage', label: 'Manage' },
    ],
  },
  {
    label: 'API Keys',
    permissions: [
      { value: 'api_keys.manage', label: 'Manage' },
      { value: 'api_keys.shared', label: 'Shared' },
    ],
  },
  {
    label: 'Analytics',
    permissions: [
      { value: 'analytics.view', label: 'View' },
    ],
  },
  {
    label: 'Admin',
    permissions: [
      { value: 'admin.roles', label: 'Roles' },
      { value: 'admin.users', label: 'Users' },
      { value: 'admin.audit', label: 'Audit' },
    ],
  },
]

// Create form
const newName = ref('')
const newDescription = ref('')
const newPermissions = ref<string[]>([])

// Edit state
const editingRole = ref<AdminRole | null>(null)
const editName = ref('')
const editDescription = ref('')
const editPermissions = ref<string[]>([])
const isEditModalOpen = ref(false)

// Delete confirmation
const isDeleteModalOpen = ref(false)
const roleToDelete = ref<AdminRole | null>(null)

const fetchRoles = async () => {
  isLoading.value = true
  errorMessage.value = null
  try {
    roles.value = await listRoles()
  }
  catch (e) {
    console.error('Failed to fetch roles', e)
    errorMessage.value = 'Failed to load roles.'
  }
  finally {
    isLoading.value = false
  }
}

const handleCreate = async () => {
  if (!newName.value || newPermissions.value.length === 0) return
  isCreating.value = true
  errorMessage.value = null
  try {
    await createRole({
      name: newName.value,
      description: newDescription.value || undefined,
      permissions: newPermissions.value,
    })
    newName.value = ''
    newDescription.value = ''
    newPermissions.value = []
    await fetchRoles()
  }
  catch (e: any) {
    console.error('Failed to create role', e)
    errorMessage.value = e?.data?.detail || 'Failed to create role.'
  }
  finally {
    isCreating.value = false
  }
}

const openEdit = (role: AdminRole) => {
  editingRole.value = role
  editName.value = role.name
  editDescription.value = role.description || ''
  editPermissions.value = [...role.permissions]
  isEditModalOpen.value = true
}

const handleUpdate = async () => {
  if (!editingRole.value) return
  errorMessage.value = null
  try {
    await updateRole(editingRole.value.id, {
      name: editName.value,
      description: editDescription.value || undefined,
      permissions: editPermissions.value,
    })
    isEditModalOpen.value = false
    editingRole.value = null
    await fetchRoles()
  }
  catch (e: any) {
    console.error('Failed to update role', e)
    errorMessage.value = e?.data?.detail || 'Failed to update role.'
  }
}

const confirmDelete = (role: AdminRole) => {
  roleToDelete.value = role
  isDeleteModalOpen.value = true
}

const handleDelete = async () => {
  if (!roleToDelete.value) return
  errorMessage.value = null
  try {
    await deleteRole(roleToDelete.value.id)
    await fetchRoles()
  }
  catch (e: any) {
    console.error('Failed to delete role', e)
    errorMessage.value = e?.data?.detail || 'Failed to delete role.'
  }
  finally {
    isDeleteModalOpen.value = false
    roleToDelete.value = null
  }
}

onMounted(() => {
  fetchRoles()
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
            Roles
          </h1>
          <p class="page-header-subtitle">
            Manage roles and their permissions
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

        <!-- Create Role Form -->
        <div class="p-5 bg-neutral-900/60 rounded-xl border border-neutral-800 ring-1 ring-neutral-800/50">
          <h3 class="text-sm font-semibold mb-3 text-neutral-200 flex items-center gap-2">
            <UIcon name="i-lucide-plus-circle" class="text-primary-400" />
            Create Role
          </h3>
          <div class="flex flex-col gap-3">
            <UInput
              v-model="newName"
              placeholder="Role name"
            />
            <UInput
              v-model="newDescription"
              placeholder="Description (optional)"
            />
            <div>
              <p class="text-xs text-neutral-400 mb-2">
                Permissions
              </p>
              <div class="space-y-3">
                <div v-for="category in permissionCategories" :key="category.label">
                  <p class="text-xs font-medium text-neutral-500 uppercase tracking-wider mb-1">
                    {{ category.label }}
                  </p>
                  <div class="flex flex-wrap gap-x-4 gap-y-1">
                    <label
                      v-for="perm in category.permissions"
                      :key="perm.value"
                      class="flex items-center gap-1.5 text-sm text-neutral-300 cursor-pointer"
                    >
                      <input
                        v-model="newPermissions"
                        type="checkbox"
                        :value="perm.value"
                        class="accent-neutral-500 rounded"
                      >
                      {{ perm.label }}
                    </label>
                  </div>
                </div>
              </div>
            </div>
            <div class="flex justify-end">
              <UButton
                label="Create Role"
                color="primary"
                :loading="isCreating"
                :disabled="!newName || newPermissions.length === 0"
                @click="handleCreate"
              />
            </div>
          </div>
        </div>

        <!-- Roles List -->
        <div>
          <h2 class="text-base font-semibold text-neutral-200 mb-3 flex items-center gap-2">
            <UIcon name="i-lucide-shield" class="text-primary-400" />
            All Roles
          </h2>

          <div v-if="isLoading" class="py-8 text-center">
            <UIcon
              name="i-lucide-loader-2"
              class="animate-spin text-2xl text-primary-400"
            />
          </div>

          <div
            v-else-if="roles.length === 0"
            class="py-8 text-center text-neutral-500"
          >
            <p>No roles found.</p>
          </div>

          <div v-else class="space-y-2">
            <div
              v-for="role in roles"
              :key="role.id"
              class="flex items-center justify-between p-4 rounded-xl bg-neutral-900/40 border border-neutral-800 hover:border-neutral-700 transition-colors"
            >
              <div class="min-w-0 flex-1">
                <div class="flex items-center gap-2">
                  <span class="font-medium text-neutral-100 text-sm">{{ role.name }}</span>
                  <span
                    v-if="role.isSystem"
                    class="flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium bg-amber-900/40 text-amber-300"
                  >
                    <UIcon name="i-lucide-lock" class="text-[11px]" />
                    System
                  </span>
                  <span
                    v-else
                    class="px-2 py-0.5 rounded-full text-[11px] font-medium bg-neutral-800 text-neutral-400"
                  >
                    Custom
                  </span>
                </div>
                <p v-if="role.description" class="text-xs text-neutral-500 mt-1">
                  {{ role.description }}
                </p>
                <div class="flex flex-wrap items-center gap-2 mt-2">
                  <span
                    class="px-2 py-0.5 rounded-full text-[11px] font-medium bg-primary-900/40 text-primary-300"
                  >
                    {{ role.permissions.length }} permission{{ role.permissions.length !== 1 ? 's' : '' }}
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
                  @click="openEdit(role)"
                />
                <UButton
                  icon="i-lucide-trash-2"
                  color="error"
                  variant="ghost"
                  size="sm"
                  title="Delete"
                  :disabled="role.isSystem"
                  @click="confirmDelete(role)"
                />
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Edit Role Modal -->
    <UModal v-model:open="isEditModalOpen">
      <template #content>
        <div class="p-6">
          <div class="flex items-center gap-3 mb-4">
            <div class="w-12 h-12 rounded-xl bg-primary-600/20 flex items-center justify-center">
              <UIcon name="i-lucide-pencil" class="text-2xl text-primary-400" />
            </div>
            <div>
              <h3 class="text-lg font-semibold text-neutral-100">
                Edit Role
              </h3>
              <p class="text-sm text-neutral-400">
                Update role configuration
              </p>
            </div>
          </div>
          <div class="flex flex-col gap-3 mb-6">
            <UInput
              v-model="editName"
              placeholder="Role name"
              :readonly="editingRole?.isSystem"
            />
            <UInput
              v-model="editDescription"
              placeholder="Description (optional)"
            />
            <div>
              <p class="text-xs text-neutral-400 mb-2">
                Permissions
              </p>
              <div class="space-y-3">
                <div v-for="category in permissionCategories" :key="category.label">
                  <p class="text-xs font-medium text-neutral-500 uppercase tracking-wider mb-1">
                    {{ category.label }}
                  </p>
                  <div class="flex flex-wrap gap-x-4 gap-y-1">
                    <label
                      v-for="perm in category.permissions"
                      :key="perm.value"
                      class="flex items-center gap-1.5 text-sm text-neutral-300 cursor-pointer"
                    >
                      <input
                        v-model="editPermissions"
                        type="checkbox"
                        :value="perm.value"
                        class="accent-neutral-500 rounded"
                      >
                      {{ perm.label }}
                    </label>
                  </div>
                </div>
              </div>
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
              :disabled="!editName || editPermissions.length === 0"
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
                Delete Role
              </h3>
              <p class="text-sm text-neutral-400">
                This action cannot be undone
              </p>
            </div>
          </div>
          <p class="text-neutral-300 mb-6">
            Are you sure you want to delete the role <strong>{{ roleToDelete?.name }}</strong>? Users assigned this role will lose its permissions.
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
              label="Delete Role"
              @click="handleDelete"
            />
          </div>
        </div>
      </template>
    </UModal>
  </div>
</template>
