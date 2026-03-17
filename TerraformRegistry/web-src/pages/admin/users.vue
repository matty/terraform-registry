<script setup lang="ts">
import { useDashboard } from '~/composables/useDashboard'
import { useAdmin } from '~/composables/useAdmin'
import type { AdminRole, AdminUser } from '~/composables/useAdmin'

definePageMeta({
  middleware: 'auth',
})

const { isSidebarOpen } = useDashboard()
const { listUsers, listRoles, getUserRoles, assignRole, removeRole } = useAdmin()

// State
const users = ref<AdminUser[]>([])
const allRoles = ref<AdminRole[]>([])
const userRolesMap = ref<Record<string, AdminRole[]>>({})
const isLoading = ref(false)
const errorMessage = ref<string | null>(null)

// Expanded user for role management
const expandedUserId = ref<string | null>(null)
const assigningRoleId = ref<string>('')
const isAssigning = ref(false)

const fetchData = async () => {
  isLoading.value = true
  errorMessage.value = null
  try {
    const [usersResult, rolesResult] = await Promise.all([listUsers(), listRoles()])
    users.value = usersResult
    allRoles.value = rolesResult
  }
  catch (e) {
    console.error('Failed to fetch data', e)
    errorMessage.value = 'Failed to load users.'
  }
  finally {
    isLoading.value = false
  }
}

const toggleExpand = async (userId: string) => {
  if (expandedUserId.value === userId) {
    expandedUserId.value = null
    return
  }
  expandedUserId.value = userId
  assigningRoleId.value = ''
  if (!userRolesMap.value[userId]) {
    try {
      userRolesMap.value[userId] = await getUserRoles(userId)
    }
    catch (e) {
      console.error('Failed to fetch user roles', e)
      userRolesMap.value[userId] = []
    }
  }
}

const availableRoles = computed(() => {
  if (!expandedUserId.value) return []
  const currentRoleIds = (userRolesMap.value[expandedUserId.value] || []).map(r => r.id)
  return allRoles.value.filter(r => !currentRoleIds.includes(r.id))
})

const handleAssignRole = async () => {
  if (!expandedUserId.value || !assigningRoleId.value) return
  isAssigning.value = true
  errorMessage.value = null
  try {
    await assignRole(expandedUserId.value, assigningRoleId.value)
    userRolesMap.value[expandedUserId.value] = await getUserRoles(expandedUserId.value)
    assigningRoleId.value = ''
  }
  catch (e: any) {
    console.error('Failed to assign role', e)
    errorMessage.value = e?.data?.detail || 'Failed to assign role.'
  }
  finally {
    isAssigning.value = false
  }
}

const handleRemoveRole = async (userId: string, roleId: string) => {
  errorMessage.value = null
  try {
    await removeRole(userId, roleId)
    userRolesMap.value[userId] = await getUserRoles(userId)
  }
  catch (e: any) {
    console.error('Failed to remove role', e)
    errorMessage.value = e?.data?.detail || 'Failed to remove role.'
  }
}

const providerColor = (provider: string): string => {
  const colors: Record<string, string> = {
    github: 'bg-neutral-800 text-neutral-300',
    google: 'bg-blue-900/40 text-blue-300',
    microsoft: 'bg-indigo-900/40 text-indigo-300',
    dev: 'bg-amber-900/40 text-amber-300',
  }
  return colors[provider.toLowerCase()] ?? 'bg-neutral-800 text-neutral-400'
}

onMounted(() => {
  fetchData()
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
            Users
          </h1>
          <p class="page-header-subtitle">
            Manage user role assignments
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

        <!-- Users List -->
        <div>
          <h2 class="text-base font-semibold text-neutral-200 mb-3 flex items-center gap-2">
            <UIcon name="i-lucide-users" class="text-primary-400" />
            All Users
          </h2>

          <div v-if="isLoading" class="py-8 text-center">
            <UIcon
              name="i-lucide-loader-2"
              class="animate-spin text-2xl text-primary-400"
            />
          </div>

          <div
            v-else-if="users.length === 0"
            class="py-8 text-center text-neutral-500"
          >
            <p>No users found.</p>
          </div>

          <div v-else class="space-y-2">
            <div
              v-for="u in users"
              :key="u.id"
              class="rounded-xl bg-neutral-900/40 border border-neutral-800 hover:border-neutral-700 transition-colors"
            >
              <!-- User row -->
              <div
                class="flex items-center justify-between p-4 cursor-pointer"
                @click="toggleExpand(u.id)"
              >
                <div class="min-w-0 flex-1">
                  <div class="flex items-center gap-2">
                    <span class="font-medium text-neutral-100 text-sm">{{ u.email }}</span>
                    <span
                      :class="[
                        'px-2 py-0.5 rounded-full text-[11px] font-medium',
                        providerColor(u.provider),
                      ]"
                    >
                      {{ u.provider }}
                    </span>
                  </div>
                  <div class="flex flex-wrap items-center gap-1.5 mt-2">
                    <span
                      v-for="role in (userRolesMap[u.id] || [])"
                      :key="role.id"
                      class="px-2 py-0.5 rounded-full text-[11px] font-medium bg-primary-900/40 text-primary-300"
                    >
                      {{ role.name }}
                    </span>
                  </div>
                </div>
                <UIcon
                  :name="expandedUserId === u.id ? 'i-lucide-chevron-up' : 'i-lucide-chevron-down'"
                  class="text-neutral-400 text-lg flex-shrink-0"
                />
              </div>

              <!-- Expanded role management -->
              <div
                v-if="expandedUserId === u.id"
                class="px-4 pb-4 border-t border-neutral-800/50"
              >
                <div class="pt-3 space-y-3">
                  <!-- Current roles -->
                  <div>
                    <p class="text-xs text-neutral-500 mb-2">
                      Current Roles
                    </p>
                    <div v-if="(userRolesMap[u.id] || []).length === 0" class="text-xs text-neutral-600">
                      No roles assigned.
                    </div>
                    <div v-else class="flex flex-wrap gap-2">
                      <span
                        v-for="role in userRolesMap[u.id]"
                        :key="role.id"
                        class="flex items-center gap-1 px-2.5 py-1 rounded-full text-xs font-medium bg-primary-900/40 text-primary-300"
                      >
                        {{ role.name }}
                        <button
                          class="ml-0.5 text-primary-400 hover:text-red-400 transition-colors"
                          title="Remove role"
                          @click.stop="handleRemoveRole(u.id, role.id)"
                        >
                          <UIcon name="i-lucide-x" class="text-[12px]" />
                        </button>
                      </span>
                    </div>
                  </div>

                  <!-- Assign role -->
                  <div>
                    <p class="text-xs text-neutral-500 mb-2">
                      Assign Role
                    </p>
                    <div class="flex items-center gap-2">
                      <USelect
                        v-model="assigningRoleId"
                        :items="availableRoles.map(r => ({ label: r.name, value: r.id }))"
                        placeholder="Select a role"
                        class="flex-1 min-w-[200px]"
                      />
                      <UButton
                        label="Assign"
                        color="primary"
                        size="sm"
                        :loading="isAssigning"
                        :disabled="!assigningRoleId"
                        @click.stop="handleAssignRole"
                      />
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
