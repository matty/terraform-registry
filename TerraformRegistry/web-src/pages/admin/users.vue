<script setup lang="ts">
import { useDashboard } from '~/composables/useDashboard'
import { useAdmin } from '~/composables/useAdmin'
import type { AdminRole, AdminUser } from '~/composables/useAdmin'
import { extractErrorMessage } from "~/composables/useErrorMessage"

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

// Search filter
const searchQuery = ref('')

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
    errorMessage.value = extractErrorMessage(e, 'Failed to load users')
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
    errorMessage.value = extractErrorMessage(e, 'Failed to assign role')
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
    errorMessage.value = extractErrorMessage(e, 'Failed to remove role')
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

// Display helpers
const filteredUsers = computed(() => {
  if (!searchQuery.value) return users.value
  const q = searchQuery.value.toLowerCase()
  return users.value.filter(u => u.email.toLowerCase().includes(q))
})

const getInitial = (email: string): string => {
  return email.charAt(0).toUpperCase()
}

const avatarGradient = (email: string): string => {
  const gradients = [
    'from-violet-500 to-purple-600',
    'from-blue-500 to-cyan-500',
    'from-emerald-500 to-teal-500',
    'from-rose-500 to-pink-500',
    'from-amber-500 to-orange-500',
    'from-indigo-500 to-blue-500',
    'from-fuchsia-500 to-pink-500',
    'from-sky-500 to-indigo-500',
  ]
  let hash = 0
  for (let i = 0; i < email.length; i++) {
    hash = email.charCodeAt(i) + ((hash << 5) - hash)
  }
  return gradients[Math.abs(hash) % gradients.length]
}

const relativeTime = (dateStr: string): string => {
  const date = new Date(dateStr)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffSec = Math.floor(diffMs / 1000)
  const diffMin = Math.floor(diffSec / 60)
  const diffHour = Math.floor(diffMin / 60)
  const diffDay = Math.floor(diffHour / 24)
  const diffMonth = Math.floor(diffDay / 30)
  const diffYear = Math.floor(diffDay / 365)

  if (diffYear > 0) return `${diffYear}y ago`
  if (diffMonth > 0) return `${diffMonth}mo ago`
  if (diffDay > 0) return `${diffDay}d ago`
  if (diffHour > 0) return `${diffHour}h ago`
  if (diffMin > 0) return `${diffMin}m ago`
  return 'just now'
}

const providerIcon = (provider: string): string => {
  const icons: Record<string, string> = {
    github: 'i-lucide-github',
    google: 'i-lucide-chrome',
    microsoft: 'i-lucide-monitor',
    dev: 'i-lucide-code',
  }
  return icons[provider.toLowerCase()] ?? 'i-lucide-key'
}

const roleBadgeClass = (roleName: string): string => {
  const lower = roleName.toLowerCase()
  if (lower === 'admin') return 'bg-rose-500/15 text-rose-300 border border-rose-500/25'
  if (lower === 'user') return 'bg-blue-500/15 text-blue-300 border border-blue-500/25'
  return 'bg-purple-500/15 text-purple-300 border border-purple-500/25'
}

const roleBadgeIcon = (roleName: string): string => {
  const lower = roleName.toLowerCase()
  if (lower === 'admin') return 'i-lucide-shield'
  if (lower === 'user') return 'i-lucide-user'
  return 'i-lucide-tag'
}

const highestRoleAccent = (userId: string): string => {
  const roles = userRolesMap.value[userId] || []
  if (roles.some(r => r.name.toLowerCase() === 'admin')) return 'border-l-rose-500/70'
  if (roles.some(r => r.name.toLowerCase() === 'user')) return 'border-l-blue-500/70'
  if (roles.length > 0) return 'border-l-purple-500/70'
  return 'border-l-neutral-700/50'
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
        <div class="flex items-center gap-3">
          <div>
            <h1 class="page-header-title flex items-center gap-2.5">
              Users
              <span
                v-if="!isLoading && users.length > 0"
                class="px-2.5 py-0.5 rounded-full bg-neutral-800 text-neutral-400 text-xs font-medium"
              >
                {{ users.length }}
              </span>
            </h1>
            <p class="page-header-subtitle">
              Manage user role assignments
            </p>
          </div>
        </div>
      </div>
    </div>
    <div class="page-divider" />

    <!-- Body -->
    <div class="flex-1 overflow-y-auto px-6 py-8">
      <div class="max-w-5xl space-y-6">
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

        <!-- Search Bar -->
        <div v-if="!isLoading && users.length > 0" class="relative">
          <UInput
            v-model="searchQuery"
            icon="i-lucide-search"
            placeholder="Filter users by email..."
            size="lg"
            class="w-full max-w-sm"
          />
        </div>

        <!-- Loading Skeleton -->
        <div v-if="isLoading" class="grid grid-cols-1 lg:grid-cols-2 gap-4">
          <div
            v-for="i in 4"
            :key="i"
            class="skeleton-card rounded-2xl border border-neutral-800/60 p-5"
          >
            <div class="flex items-start gap-4">
              <div class="w-12 h-12 rounded-full bg-neutral-800 animate-pulse shrink-0" />
              <div class="flex-1 space-y-3">
                <div class="h-4 w-48 bg-neutral-800 rounded-lg animate-pulse" />
                <div class="flex gap-2">
                  <div class="h-5 w-16 bg-neutral-800 rounded-full animate-pulse" />
                  <div class="h-5 w-12 bg-neutral-800 rounded-full animate-pulse" />
                </div>
                <div class="h-3 w-24 bg-neutral-800/60 rounded animate-pulse" />
              </div>
            </div>
          </div>
        </div>

        <!-- Empty State -->
        <div
          v-else-if="users.length === 0"
          class="py-16 text-center rounded-2xl border border-dashed border-neutral-800 bg-neutral-900/20"
        >
          <div class="w-16 h-16 rounded-2xl bg-neutral-800/50 flex items-center justify-center mx-auto mb-4">
            <UIcon name="i-lucide-users" class="text-3xl text-neutral-600" />
          </div>
          <p class="text-neutral-400 font-medium">
            No users found
          </p>
          <p class="text-sm text-neutral-600 mt-1">
            Users will appear here once they sign in
          </p>
        </div>

        <!-- No search results -->
        <div
          v-else-if="filteredUsers.length === 0 && searchQuery"
          class="py-12 text-center rounded-2xl border border-dashed border-neutral-800 bg-neutral-900/20"
        >
          <UIcon name="i-lucide-search-x" class="text-3xl text-neutral-600 mb-3" />
          <p class="text-neutral-400">
            No users matching "{{ searchQuery }}"
          </p>
        </div>

        <!-- User Cards Grid -->
        <div v-else class="grid grid-cols-1 lg:grid-cols-2 gap-4">
          <div
            v-for="u in filteredUsers"
            :key="u.id"
            :class="[
              'user-card rounded-2xl border border-neutral-800/60 border-l-[3px] transition-all duration-200 hover:border-neutral-700/80 cursor-pointer overflow-hidden',
              highestRoleAccent(u.id),
            ]"
            @click="toggleExpand(u.id)"
          >
            <!-- Card body -->
            <div class="p-5">
              <div class="flex items-start gap-4">
                <!-- Avatar -->
                <div
                  :class="[
                    'w-12 h-12 rounded-full bg-gradient-to-br flex items-center justify-center shrink-0 text-white font-bold text-lg shadow-lg',
                    avatarGradient(u.email),
                  ]"
                >
                  {{ getInitial(u.email) }}
                </div>

                <!-- User info -->
                <div class="min-w-0 flex-1">
                  <p class="font-semibold text-neutral-100 text-sm truncate">
                    {{ u.email }}
                  </p>
                  <div class="flex items-center gap-2 mt-1.5 flex-wrap">
                    <!-- Provider badge -->
                    <span
                      :class="[
                        'inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium',
                        providerColor(u.provider),
                      ]"
                    >
                      <UIcon :name="providerIcon(u.provider)" class="text-[11px]" />
                      {{ u.provider }}
                    </span>
                    <!-- Join date -->
                    <span class="text-[11px] text-neutral-500 flex items-center gap-1">
                      <UIcon name="i-lucide-clock" class="text-[11px]" />
                      {{ relativeTime(u.createdAt) }}
                    </span>
                  </div>

                  <!-- Role badges -->
                  <div class="flex flex-wrap items-center gap-1.5 mt-2.5">
                    <span
                      v-for="role in (userRolesMap[u.id] || [])"
                      :key="role.id"
                      :class="[
                        'inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium',
                        roleBadgeClass(role.name),
                      ]"
                    >
                      <UIcon :name="roleBadgeIcon(role.name)" class="text-[10px]" />
                      {{ role.name }}
                    </span>
                    <span
                      v-if="(userRolesMap[u.id] || []).length === 0 && expandedUserId !== u.id"
                      class="text-[11px] text-neutral-600 italic"
                    >
                      No roles
                    </span>
                  </div>
                </div>

                <!-- Expand indicator -->
                <UIcon
                  :name="expandedUserId === u.id ? 'i-lucide-chevron-up' : 'i-lucide-chevron-down'"
                  class="text-neutral-500 text-lg shrink-0 mt-1"
                />
              </div>
            </div>

            <!-- Expanded role management -->
            <Transition name="expand">
              <div
                v-if="expandedUserId === u.id"
                class="px-5 pb-5 border-t border-neutral-800/40"
                @click.stop
              >
                <div class="pt-4 space-y-4">
                  <!-- Current roles -->
                  <div>
                    <p class="text-xs font-medium text-neutral-400 mb-2 flex items-center gap-1.5">
                      <UIcon name="i-lucide-shield-check" class="text-sm text-neutral-500" />
                      Current Roles
                    </p>
                    <div v-if="(userRolesMap[u.id] || []).length === 0" class="text-xs text-neutral-600 italic pl-0.5">
                      No roles assigned yet.
                    </div>
                    <div v-else class="flex flex-wrap gap-2">
                      <span
                        v-for="role in userRolesMap[u.id]"
                        :key="role.id"
                        :class="[
                          'role-removable inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition-all',
                          roleBadgeClass(role.name),
                        ]"
                      >
                        <UIcon :name="roleBadgeIcon(role.name)" class="text-[12px]" />
                        {{ role.name }}
                        <button
                          class="ml-0.5 opacity-60 hover:opacity-100 hover:text-red-400 transition-all"
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
                    <p class="text-xs font-medium text-neutral-400 mb-2 flex items-center gap-1.5">
                      <UIcon name="i-lucide-plus-circle" class="text-sm text-neutral-500" />
                      Assign Role
                    </p>
                    <div class="flex items-center gap-2">
                      <USelect
                        v-model="assigningRoleId"
                        :items="availableRoles.map(r => ({ label: r.name, value: r.id }))"
                        placeholder="Select a role"
                        class="flex-1 min-w-0"
                      />
                      <UButton
                        icon="i-lucide-plus"
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
            </Transition>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.user-card {
  background: linear-gradient(145deg, rgba(23, 23, 23, 0.6), rgba(12, 12, 12, 0.8));
  backdrop-filter: blur(12px);
}

.user-card:hover {
  background: linear-gradient(145deg, rgba(28, 28, 28, 0.7), rgba(16, 16, 16, 0.9));
  transform: translateY(-1px);
  box-shadow: 0 8px 24px -8px rgba(0, 0, 0, 0.4);
}

.skeleton-card {
  background: linear-gradient(145deg, rgba(23, 23, 23, 0.4), rgba(12, 12, 12, 0.6));
}

.role-removable {
  backdrop-filter: blur(4px);
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
