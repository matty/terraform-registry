<script setup lang="ts">
import { useDashboard } from '~/composables/useDashboard'
import { useAdmin } from '~/composables/useAdmin'
import type { AdminRole } from '~/composables/useAdmin'
import { extractErrorMessage } from "~/composables/useErrorMessage"

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

// Permission categories with icons and colors
const permissionCategories = [
  {
    label: 'Modules',
    icon: 'i-lucide-package',
    color: 'blue',
    bgColor: 'bg-blue-500/10',
    borderColor: 'border-blue-500/20',
    iconColor: 'text-blue-400',
    headerBg: 'bg-blue-500/5',
    pillActive: 'bg-blue-500/20 border-blue-400/50 text-blue-300',
    dotColor: 'bg-blue-400',
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
    label: 'Providers',
    icon: 'i-lucide-plug',
    color: 'sky',
    bgColor: 'bg-sky-500/10',
    borderColor: 'border-sky-500/20',
    iconColor: 'text-sky-400',
    headerBg: 'bg-sky-500/5',
    pillActive: 'bg-sky-500/20 border-sky-400/50 text-sky-300',
    dotColor: 'bg-sky-400',
    permissions: [
      { value: 'providers.read', label: 'Read' },
      { value: 'providers.publish', label: 'Publish' },
      { value: 'providers.delete', label: 'Delete' },
      { value: 'providers.purge', label: 'Purge' },
      { value: 'providers.keys.manage', label: 'Manage Keys' },
      { value: 'providers.description', label: 'Description' },
    ],
  },
  {
    label: 'Webhooks',
    icon: 'i-lucide-webhook',
    color: 'purple',
    bgColor: 'bg-purple-500/10',
    borderColor: 'border-purple-500/20',
    iconColor: 'text-purple-400',
    headerBg: 'bg-purple-500/5',
    pillActive: 'bg-purple-500/20 border-purple-400/50 text-purple-300',
    dotColor: 'bg-purple-400',
    permissions: [
      { value: 'webhooks.manage', label: 'Manage' },
    ],
  },
  {
    label: 'VCS',
    icon: 'i-lucide-git-branch',
    color: 'green',
    bgColor: 'bg-green-500/10',
    borderColor: 'border-green-500/20',
    iconColor: 'text-green-400',
    headerBg: 'bg-green-500/5',
    pillActive: 'bg-green-500/20 border-green-400/50 text-green-300',
    dotColor: 'bg-green-400',
    permissions: [
      { value: 'vcs.manage', label: 'Manage' },
    ],
  },
  {
    label: 'API Keys',
    icon: 'i-lucide-key-round',
    color: 'amber',
    bgColor: 'bg-amber-500/10',
    borderColor: 'border-amber-500/20',
    iconColor: 'text-amber-400',
    headerBg: 'bg-amber-500/5',
    pillActive: 'bg-amber-500/20 border-amber-400/50 text-amber-300',
    dotColor: 'bg-amber-400',
    permissions: [
      { value: 'api_keys.manage', label: 'Manage' },
      { value: 'api_keys.shared', label: 'Shared' },
    ],
  },
  {
    label: 'Analytics',
    icon: 'i-lucide-bar-chart-3',
    color: 'cyan',
    bgColor: 'bg-cyan-500/10',
    borderColor: 'border-cyan-500/20',
    iconColor: 'text-cyan-400',
    headerBg: 'bg-cyan-500/5',
    pillActive: 'bg-cyan-500/20 border-cyan-400/50 text-cyan-300',
    dotColor: 'bg-cyan-400',
    permissions: [
      { value: 'analytics.view', label: 'View' },
    ],
  },
  {
    label: 'Module Docs',
    icon: 'i-lucide-file-search',
    color: 'teal',
    bgColor: 'bg-teal-500/10',
    borderColor: 'border-teal-500/20',
    iconColor: 'text-teal-400',
    headerBg: 'bg-teal-500/5',
    pillActive: 'bg-teal-500/20 border-teal-400/50 text-teal-300',
    dotColor: 'bg-teal-400',
    permissions: [
      { value: 'module_docs.read', label: 'Read' },
      { value: 'module_docs.manage', label: 'Manage' },
      { value: 'module_docs.configure', label: 'Configure' },
    ],
  },
  {
    label: 'Admin',
    icon: 'i-lucide-shield',
    color: 'red',
    bgColor: 'bg-red-500/10',
    borderColor: 'border-red-500/20',
    iconColor: 'text-red-400',
    headerBg: 'bg-red-500/5',
    pillActive: 'bg-red-500/20 border-red-400/50 text-red-300',
    dotColor: 'bg-red-400',
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

// Expanded roles in list
const expandedRoleIds = ref<string[]>([])

const toggleRoleExpand = (id: string) => {
  if (expandedRoleIds.value.includes(id)) {
    expandedRoleIds.value = expandedRoleIds.value.filter(r => r !== id)
  }
  else {
    expandedRoleIds.value = [...expandedRoleIds.value, id]
  }
}

const isRoleExpanded = (id: string) => expandedRoleIds.value.includes(id)

const fetchRoles = async () => {
  isLoading.value = true
  errorMessage.value = null
  try {
    roles.value = await listRoles()
  }
  catch (e) {
    console.error('Failed to fetch roles', e)
    errorMessage.value = extractErrorMessage(e, 'Failed to load roles')
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
    errorMessage.value = extractErrorMessage(e, 'Failed to create role')
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
    errorMessage.value = extractErrorMessage(e, 'Failed to update role')
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
    errorMessage.value = extractErrorMessage(e, 'Failed to delete role')
  }
  finally {
    isDeleteModalOpen.value = false
    roleToDelete.value = null
  }
}

// Toggle permission in a target ref
const toggleNewPermission = (value: string) => {
  if (newPermissions.value.includes(value)) {
    newPermissions.value = newPermissions.value.filter(v => v !== value)
  }
  else {
    newPermissions.value = [...newPermissions.value, value]
  }
}

const toggleEditPermission = (value: string) => {
  if (editPermissions.value.includes(value)) {
    editPermissions.value = editPermissions.value.filter(v => v !== value)
  }
  else {
    editPermissions.value = [...editPermissions.value, value]
  }
}

const selectAllNewCategory = (category: typeof permissionCategories[number]) => {
  const toAdd = category.permissions.map(p => p.value).filter(v => !newPermissions.value.includes(v))
  newPermissions.value = [...newPermissions.value, ...toAdd]
}

const clearNewCategory = (category: typeof permissionCategories[number]) => {
  newPermissions.value = newPermissions.value.filter(p => !category.permissions.some(cp => cp.value === p))
}

const selectAllEditCategory = (category: typeof permissionCategories[number]) => {
  const toAdd = category.permissions.map(p => p.value).filter(v => !editPermissions.value.includes(v))
  editPermissions.value = [...editPermissions.value, ...toAdd]
}

const clearEditCategory = (category: typeof permissionCategories[number]) => {
  editPermissions.value = editPermissions.value.filter(p => !category.permissions.some(cp => cp.value === p))
}

const categorySelectedCount = (perms: string[], category: typeof permissionCategories[number]) => {
  return category.permissions.filter(p => perms.includes(p.value)).length
}

// Get category info for a permission value
const getCategoryForPermission = (permValue: string) => {
  return permissionCategories.find(c => c.permissions.some(p => p.value === permValue))
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
    <div class="flex-1 overflow-y-auto px-6 py-8">
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

        <!-- Create Role Form -->
        <div class="create-card rounded-2xl border border-neutral-800/80 overflow-hidden">
          <!-- Card header -->
          <div class="px-6 py-5 border-b border-neutral-800/60 bg-neutral-900/40">
            <h3 class="text-base font-semibold text-neutral-100 flex items-center gap-3">
              <div class="w-9 h-9 rounded-xl bg-primary-500/15 flex items-center justify-center">
                <UIcon name="i-lucide-plus" class="text-primary-400 text-lg" />
              </div>
              Create Role
            </h3>
          </div>

          <div class="p-6 space-y-6">
            <!-- Name + Description -->
            <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div class="space-y-1.5">
                <label class="block text-xs font-medium text-neutral-400">
                  Role Name <span class="text-red-400">*</span>
                </label>
                <UInput
                  v-model="newName"
                  placeholder="e.g. Module Publisher"
                  size="lg"
                />
              </div>
              <div class="space-y-1.5">
                <label class="block text-xs font-medium text-neutral-400">Description</label>
                <UInput
                  v-model="newDescription"
                  placeholder="Optional description"
                  size="lg"
                />
              </div>
            </div>

            <!-- Permission Grid -->
            <div class="space-y-3">
              <div class="flex items-center justify-between">
                <p class="text-sm font-medium text-neutral-300">Permissions</p>
                <span class="text-xs text-neutral-500">
                  {{ newPermissions.length }} selected
                </span>
              </div>
              <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <div
                  v-for="category in permissionCategories"
                  :key="category.label"
                  :class="[
                    'perm-category rounded-xl border overflow-hidden transition-all duration-200',
                    categorySelectedCount(newPermissions, category) > 0
                      ? category.borderColor
                      : 'border-neutral-800/60'
                  ]"
                >
                  <!-- Category header -->
                  <div :class="['px-4 py-3 flex items-center justify-between', category.headerBg]">
                    <div class="flex items-center gap-2.5">
                      <div :class="['w-7 h-7 rounded-lg flex items-center justify-center', category.bgColor]">
                        <UIcon :name="category.icon" :class="['text-sm', category.iconColor]" />
                      </div>
                      <span class="text-sm font-medium text-neutral-200">{{ category.label }}</span>
                      <span
                        v-if="categorySelectedCount(newPermissions, category) > 0"
                        :class="['px-1.5 py-0.5 rounded-full text-[10px] font-bold', category.pillActive]"
                      >
                        {{ categorySelectedCount(newPermissions, category) }}/{{ category.permissions.length }}
                      </span>
                    </div>
                    <div class="flex items-center gap-1">
                      <button
                        type="button"
                        class="text-[11px] text-neutral-500 hover:text-neutral-300 px-1.5 py-0.5 rounded transition-colors"
                        @click="selectAllNewCategory(category)"
                      >
                        All
                      </button>
                      <span class="text-neutral-700">|</span>
                      <button
                        type="button"
                        class="text-[11px] text-neutral-500 hover:text-neutral-300 px-1.5 py-0.5 rounded transition-colors"
                        @click="clearNewCategory(category)"
                      >
                        Clear
                      </button>
                    </div>
                  </div>
                  <!-- Permission pills -->
                  <div class="px-4 py-3 flex flex-wrap gap-2 bg-neutral-900/30">
                    <button
                      v-for="perm in category.permissions"
                      :key="perm.value"
                      type="button"
                      :class="[
                        'perm-pill inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg border text-xs font-medium transition-all duration-150 cursor-pointer',
                        newPermissions.includes(perm.value)
                          ? category.pillActive
                          : 'bg-neutral-900/40 border-neutral-700/50 text-neutral-400 hover:text-neutral-300 hover:border-neutral-600'
                      ]"
                      @click="toggleNewPermission(perm.value)"
                    >
                      <span
                        :class="[
                          'w-1.5 h-1.5 rounded-full transition-colors',
                          newPermissions.includes(perm.value) ? category.dotColor : 'bg-neutral-600'
                        ]"
                      />
                      {{ perm.label }}
                    </button>
                  </div>
                </div>
              </div>
            </div>

            <!-- Create button -->
            <div class="flex justify-end pt-2 border-t border-neutral-800/50">
              <UButton
                icon="i-lucide-shield-plus"
                label="Create Role"
                color="primary"
                size="lg"
                :loading="isCreating"
                :disabled="!newName || newPermissions.length === 0"
                @click="handleCreate"
              />
            </div>
          </div>
        </div>

        <!-- Roles List -->
        <div class="space-y-4">
          <h2 class="text-base font-semibold text-neutral-200 flex items-center gap-3">
            <div class="w-8 h-8 rounded-lg bg-neutral-800 flex items-center justify-center">
              <UIcon name="i-lucide-shield" class="text-primary-400" />
            </div>
            All Roles
            <span v-if="roles.length > 0" class="ml-1 px-2 py-0.5 rounded-full bg-neutral-800 text-neutral-400 text-xs font-medium">
              {{ roles.length }}
            </span>
          </h2>

          <div v-if="isLoading" class="py-12 text-center">
            <UIcon
              name="i-lucide-loader-2"
              class="animate-spin text-3xl text-primary-400"
            />
          </div>

          <div
            v-else-if="roles.length === 0"
            class="py-12 text-center rounded-2xl border border-dashed border-neutral-800 bg-neutral-900/20"
          >
            <UIcon name="i-lucide-shield" class="text-4xl text-neutral-700 mb-3" />
            <p class="text-neutral-500">No roles found</p>
            <p class="text-sm text-neutral-600 mt-1">Create one above to get started</p>
          </div>

          <div v-else class="space-y-3">
            <div
              v-for="role in roles"
              :key="role.id"
              :class="[
                'role-card rounded-xl border overflow-hidden transition-all duration-200',
                role.isSystem
                  ? 'border-amber-800/30 bg-amber-950/10'
                  : 'border-neutral-800 hover:border-neutral-700'
              ]"
            >
              <div class="p-5">
                <div class="flex items-start justify-between gap-4">
                  <div class="flex items-start gap-3 min-w-0 flex-1">
                    <!-- Role icon -->
                    <div :class="[
                      'w-10 h-10 rounded-xl flex items-center justify-center shrink-0',
                      role.isSystem ? 'bg-amber-500/10 border border-amber-500/20' : 'bg-primary-500/10 border border-primary-500/20'
                    ]">
                      <UIcon
                        :name="role.isSystem ? 'i-lucide-lock' : 'i-lucide-shield'"
                        :class="role.isSystem ? 'text-amber-400' : 'text-primary-400'"
                      />
                    </div>
                    <div class="min-w-0 flex-1 space-y-1.5">
                      <!-- Header -->
                      <div class="flex items-center gap-2.5 flex-wrap">
                        <span class="font-semibold text-neutral-100">{{ role.name }}</span>
                        <span
                          v-if="role.isSystem"
                          class="flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-semibold bg-amber-900/40 text-amber-300 border border-amber-500/20"
                        >
                          <UIcon name="i-lucide-lock" class="text-[10px]" />
                          System
                        </span>
                        <span
                          v-else
                          class="px-2 py-0.5 rounded-full text-[11px] font-medium bg-neutral-800 text-neutral-400"
                        >
                          Custom
                        </span>
                        <span class="px-2 py-0.5 rounded-full text-[11px] font-semibold bg-primary-900/40 text-primary-300">
                          {{ role.permissions.length }} permission{{ role.permissions.length !== 1 ? 's' : '' }}
                        </span>
                      </div>
                      <!-- Description -->
                      <p v-if="role.description" class="text-xs text-neutral-500">
                        {{ role.description }}
                      </p>
                    </div>
                  </div>
                </div>

                <!-- Expandable permissions -->
                <Transition name="expand">
                  <div v-if="isRoleExpanded(role.id)" class="mt-4 pt-3 border-t border-neutral-800/50 space-y-2">
                    <div
                      v-for="category in permissionCategories"
                      :key="category.label"
                    >
                      <div
                        v-if="category.permissions.some(p => role.permissions.includes(p.value))"
                        class="flex items-center gap-2 flex-wrap"
                      >
                        <div :class="['inline-flex items-center gap-1.5 px-2 py-1 rounded-lg text-[11px] font-medium', category.bgColor, category.iconColor]">
                          <UIcon :name="category.icon" class="text-[11px]" />
                          {{ category.label }}
                        </div>
                        <span
                          v-for="perm in category.permissions.filter(p => role.permissions.includes(p.value))"
                          :key="perm.value"
                          :class="[
                            'inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium',
                            category.pillActive
                          ]"
                        >
                          <span :class="['w-1.5 h-1.5 rounded-full', category.dotColor]" />
                          {{ perm.label }}
                        </span>
                      </div>
                    </div>
                  </div>
                </Transition>

                <!-- Card toolbar -->
                <div class="flex items-center justify-between mt-4 pt-3 border-t border-neutral-800/50">
                  <button
                    type="button"
                    class="flex items-center gap-1.5 text-xs text-neutral-500 hover:text-neutral-300 transition-colors"
                    @click="toggleRoleExpand(role.id)"
                  >
                    <UIcon
                      :name="isRoleExpanded(role.id) ? 'i-lucide-chevron-up' : 'i-lucide-chevron-down'"
                      class="text-sm"
                    />
                    {{ isRoleExpanded(role.id) ? 'Collapse' : 'View permissions' }}
                  </button>
                  <div class="flex items-center gap-1">
                    <UButton
                      icon="i-lucide-pencil"
                      color="neutral"
                      variant="ghost"
                      size="xs"
                      label="Edit"
                      :disabled="role.name === 'admin'"
                      @click="openEdit(role)"
                    />
                    <UButton
                      icon="i-lucide-trash-2"
                      color="error"
                      variant="ghost"
                      size="xs"
                      :disabled="role.isSystem"
                      @click="confirmDelete(role)"
                    />
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Edit Role Modal -->
    <UModal v-model:open="isEditModalOpen" class="sm:max-w-2xl">
      <template #content>
        <div class="w-full">
          <!-- Header -->
          <div class="flex items-center gap-4 px-6 py-5 border-b border-neutral-800/60">
            <div class="w-11 h-11 rounded-xl bg-primary-500/15 flex items-center justify-center shrink-0">
              <UIcon name="i-lucide-shield" class="text-xl text-primary-400" />
            </div>
            <div>
              <h3 class="text-lg font-semibold text-neutral-100">
                Edit Role
              </h3>
              <p class="text-sm text-neutral-500">
                Update name, description, and permissions
              </p>
            </div>
          </div>

          <!-- Body -->
          <div class="px-6 py-5 space-y-5 max-h-[80vh] overflow-y-auto">
            <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div class="space-y-1.5">
                <label class="block text-xs font-medium text-neutral-400">
                  Role Name <span class="text-red-400">*</span>
                </label>
                <UInput
                  v-model="editName"
                  placeholder="Role name"
                  size="lg"
                  :readonly="editingRole?.isSystem"
                />
              </div>
              <div class="space-y-1.5">
                <label class="block text-xs font-medium text-neutral-400">Description</label>
                <UInput
                  v-model="editDescription"
                  placeholder="Optional description"
                  size="lg"
                />
              </div>
            </div>

            <!-- Permission Grid -->
            <div class="space-y-3">
              <div class="flex items-center justify-between">
                <p class="text-sm font-medium text-neutral-300">Permissions</p>
                <span class="text-xs text-neutral-500">
                  {{ editPermissions.length }} selected
                </span>
              </div>
              <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <div
                  v-for="category in permissionCategories"
                  :key="category.label"
                  :class="[
                    'perm-category rounded-xl border overflow-hidden transition-all duration-200',
                    categorySelectedCount(editPermissions, category) > 0
                      ? category.borderColor
                      : 'border-neutral-800/60'
                  ]"
                >
                  <!-- Category header -->
                  <div :class="['px-4 py-3 flex items-center justify-between', category.headerBg]">
                    <div class="flex items-center gap-2.5">
                      <div :class="['w-7 h-7 rounded-lg flex items-center justify-center', category.bgColor]">
                        <UIcon :name="category.icon" :class="['text-sm', category.iconColor]" />
                      </div>
                      <span class="text-sm font-medium text-neutral-200">{{ category.label }}</span>
                      <span
                        v-if="categorySelectedCount(editPermissions, category) > 0"
                        :class="['px-1.5 py-0.5 rounded-full text-[10px] font-bold', category.pillActive]"
                      >
                        {{ categorySelectedCount(editPermissions, category) }}/{{ category.permissions.length }}
                      </span>
                    </div>
                    <div class="flex items-center gap-1">
                      <button
                        type="button"
                        class="text-[11px] text-neutral-500 hover:text-neutral-300 px-1.5 py-0.5 rounded transition-colors"
                        @click="selectAllEditCategory(category)"
                      >
                        All
                      </button>
                      <span class="text-neutral-700">|</span>
                      <button
                        type="button"
                        class="text-[11px] text-neutral-500 hover:text-neutral-300 px-1.5 py-0.5 rounded transition-colors"
                        @click="clearEditCategory(category)"
                      >
                        Clear
                      </button>
                    </div>
                  </div>
                  <!-- Permission pills -->
                  <div class="px-4 py-3 flex flex-wrap gap-2 bg-neutral-900/30">
                    <button
                      v-for="perm in category.permissions"
                      :key="perm.value"
                      type="button"
                      :class="[
                        'perm-pill inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg border text-xs font-medium transition-all duration-150 cursor-pointer',
                        editPermissions.includes(perm.value)
                          ? category.pillActive
                          : 'bg-neutral-900/40 border-neutral-700/50 text-neutral-400 hover:text-neutral-300 hover:border-neutral-600'
                      ]"
                      @click="toggleEditPermission(perm.value)"
                    >
                      <span
                        :class="[
                          'w-1.5 h-1.5 rounded-full transition-colors',
                          editPermissions.includes(perm.value) ? category.dotColor : 'bg-neutral-600'
                        ]"
                      />
                      {{ perm.label }}
                    </button>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- Footer -->
          <div class="flex justify-end gap-3 px-6 py-4 border-t border-neutral-800/60">
            <UButton
              color="neutral"
              variant="ghost"
              label="Cancel"
              @click="isEditModalOpen = false"
            />
            <UButton
              color="primary"
              label="Save Changes"
              icon="i-lucide-check"
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
        <div class="w-full">
          <!-- Header -->
          <div class="flex items-center gap-4 px-6 py-5 border-b border-neutral-800/60">
            <div class="w-12 h-12 rounded-xl bg-red-500/15 flex items-center justify-center shrink-0">
              <UIcon name="i-lucide-triangle-alert" class="text-2xl text-red-400" />
            </div>
            <div>
              <h3 class="text-lg font-semibold text-neutral-100">
                Delete Role
              </h3>
              <p class="text-sm text-neutral-500">
                This action is permanent and cannot be undone
              </p>
            </div>
          </div>

          <!-- Body -->
          <div class="px-6 py-5">
            <p class="text-sm text-neutral-300 leading-relaxed">
              Are you sure you want to delete the role
              <code class="px-1.5 py-0.5 rounded bg-red-500/10 text-red-300 font-semibold text-sm">{{ roleToDelete?.name }}</code>?
              All users currently assigned this role will lose its permissions immediately.
            </p>
          </div>

          <!-- Footer -->
          <div class="flex justify-end gap-3 px-6 py-4 border-t border-neutral-800/60">
            <UButton
              color="neutral"
              variant="ghost"
              label="Cancel"
              @click="isDeleteModalOpen = false"
            />
            <UButton
              color="error"
              label="Delete Role"
              icon="i-lucide-trash-2"
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

.role-card {
  background: linear-gradient(145deg, rgba(23, 23, 23, 0.6), rgba(15, 15, 15, 0.8));
  backdrop-filter: blur(8px);
}

.role-card:hover {
  background: linear-gradient(145deg, rgba(28, 28, 28, 0.7), rgba(18, 18, 18, 0.9));
}

.perm-category {
  background: rgba(15, 15, 15, 0.6);
}

.perm-pill:active {
  transform: scale(0.96);
}

.expand-enter-active,
.expand-leave-active {
  transition: all 0.25s ease;
  overflow: hidden;
}

.expand-enter-from,
.expand-leave-to {
  opacity: 0;
  max-height: 0;
}

.expand-enter-to,
.expand-leave-from {
  opacity: 1;
  max-height: 300px;
}
</style>
