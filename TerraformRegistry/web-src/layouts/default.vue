<script setup lang="ts">
import { useDashboard } from "~/composables/useDashboard";
import { usePermissions } from "~/composables/usePermissions";
import { useImpersonation } from "~/composables/useImpersonation";

const { isAuthenticated } = useAuth();
const { isSidebarOpen, isSidebarCollapsed } = useDashboard();
const { isAdmin, hasAdminSection, hasPermission } = usePermissions();
const { impersonatedUser, isImpersonating, stopImpersonation } = useImpersonation();
const route = useRoute();

// Settings sub-menu expansion state
const isSettingsExpanded = ref(true);
const isAdminExpanded = ref(true);

// Navigation sections
const mainLinks = [
  {
    label: "Modules",
    icon: "i-lucide-package",
    to: "/",
  },
  {
    label: "Providers",
    icon: "i-lucide-plug",
    to: "/providers",
    permission: "providers.read",
  },
  {
    label: "Analytics",
    icon: "i-lucide-bar-chart-3",
    to: "/analytics",
  },
  {
    label: "Trash",
    icon: "i-lucide-trash-2",
    to: "/settings/trash",
  }
];

const settingsLinks = [
  {
    label: "API Keys",
    icon: "i-lucide-key-round",
    to: "/settings/api-keys",
  },
];

const adminLinks = [
  { label: 'Roles', icon: 'i-lucide-shield', to: '/admin/roles', permission: 'admin.roles' },
  { label: 'Users', icon: 'i-lucide-users', to: '/admin/users', permission: 'admin.users' },
  { label: 'Webhooks', icon: 'i-lucide-webhook', to: '/admin/webhooks', permission: 'webhooks.manage' },
  { label: 'VCS Connections', icon: 'i-lucide-git-branch', to: '/admin/vcs-connections', permission: 'vcs.manage' },
  { label: 'Module Docs', icon: 'i-lucide-file-search', to: '/admin/module-docs', permission: 'module_docs.read' },
  { label: 'Audit Log', icon: 'i-lucide-scroll-text', to: '/admin/audit', permission: 'admin.audit' },
];

const visibleAdminLinks = computed(() =>
  adminLinks.filter(link => hasPermission(link.permission))
);

const visibleMainLinks = computed(() =>
  mainLinks.filter(link => !link.permission || hasPermission(link.permission))
);

const isActive = (path: string) => {
  if (path === '/') return route.path === '/';
  return route.path.startsWith(path);
};

const isSettingsActive = computed(() =>
  route.path.startsWith('/settings')
);

const isAdminActive = computed(() =>
  route.path.startsWith('/admin')
);

const toggleSidebar = () => {
  isSidebarCollapsed.value = !isSidebarCollapsed.value;
};

const toggleSettings = () => {
  isSettingsExpanded.value = !isSettingsExpanded.value;
};

const toggleAdmin = () => {
  isAdminExpanded.value = !isAdminExpanded.value;
};
</script>

<template>
  <div class="fixed inset-0 flex overflow-hidden">
    <!-- Desktop Sidebar -->
    <aside
      :class="[
        'hidden lg:flex lg:flex-col border-r border-neutral-800 bg-neutral-900/95 backdrop-blur-xl transition-all duration-300',
        isSidebarCollapsed ? 'w-16' : 'w-64'
      ]"
    >
      <!-- Collapse Toggle -->
      <div class="px-2 py-3">
        <button
          @click="toggleSidebar"
          class="flex items-center justify-center w-full h-8 rounded-lg text-neutral-400 hover:text-white hover:bg-neutral-800 transition-all"
          :title="isSidebarCollapsed ? 'Expand sidebar' : 'Collapse sidebar'"
        >
          <UIcon 
            :name="isSidebarCollapsed ? 'i-lucide-panel-left-open' : 'i-lucide-panel-left-close'" 
            class="text-lg" 
          />
        </button>
      </div>

      <!-- Navigation -->
      <nav class="flex-1 overflow-y-auto px-2 py-2 space-y-4">
        <!-- Registry Section -->
        <div>
          <p v-if="!isSidebarCollapsed" class="px-2 mb-2 text-[10px] font-medium text-neutral-500 uppercase tracking-wider">
            Registry
          </p>
          <div class="space-y-0.5">
            <NuxtLink
              v-for="link in visibleMainLinks"
              :key="link.to"
              :to="link.to"
              :class="[
                'flex items-center gap-3 px-3 py-2 rounded-lg text-sm font-medium transition-all',
                isActive(link.to) 
                  ? 'bg-white/10 text-white' 
                  : 'text-neutral-400 hover:text-white hover:bg-neutral-800',
                isSidebarCollapsed ? 'justify-center' : ''
              ]"
              :title="isSidebarCollapsed ? link.label : ''"
            >
              <UIcon :name="link.icon" class="text-lg flex-shrink-0" />
              <span v-if="!isSidebarCollapsed">{{ link.label }}</span>
            </NuxtLink>
          </div>
        </div>

        <!-- Settings Section (collapsible) -->
        <div class="!mt-auto pt-4 border-t border-neutral-800">
          <!-- Settings header / toggle -->
          <button
            v-if="!isSidebarCollapsed"
            @click="toggleSettings"
            :class="[
              'flex items-center justify-between w-full px-2 mb-2 group',
              isSettingsActive ? 'text-white' : 'text-neutral-500 hover:text-neutral-300'
            ]"
          >
            <span class="text-[10px] font-medium uppercase tracking-wider">Settings</span>
            <UIcon 
              :name="isSettingsExpanded ? 'i-lucide-chevron-down' : 'i-lucide-chevron-right'" 
              class="text-xs transition-transform" 
            />
          </button>
          <p v-else class="px-2 mb-2 text-[10px] font-medium text-neutral-500 uppercase tracking-wider text-center">
            ⚙
          </p>

          <!-- Settings sub-links -->
          <div 
            v-if="isSettingsExpanded || isSidebarCollapsed"
            class="space-y-0.5"
          >
            <NuxtLink
              v-for="link in settingsLinks"
              :key="link.to"
              :to="link.to"
              :class="[
                'flex items-center gap-3 px-3 py-2 rounded-lg text-sm font-medium transition-all',
                isActive(link.to) 
                  ? 'bg-white/10 text-white' 
                  : 'text-neutral-400 hover:text-white hover:bg-neutral-800',
                isSidebarCollapsed ? 'justify-center' : 'pl-5'
              ]"
              :title="isSidebarCollapsed ? link.label : ''"
            >
              <UIcon :name="link.icon" class="text-lg flex-shrink-0" />
              <span v-if="!isSidebarCollapsed">{{ link.label }}</span>
            </NuxtLink>
          </div>
        </div>

        <!-- Admin Section -->
        <div v-if="hasAdminSection" class="pt-4 border-t border-neutral-800">
          <button
            v-if="!isSidebarCollapsed"
            @click="toggleAdmin"
            :class="[
              'flex items-center justify-between w-full px-2 mb-2 group',
              isAdminActive ? 'text-white' : 'text-neutral-500 hover:text-neutral-300'
            ]"
          >
            <span class="text-[10px] font-medium uppercase tracking-wider">Administration</span>
            <UIcon
              :name="isAdminExpanded ? 'i-lucide-chevron-down' : 'i-lucide-chevron-right'"
              class="text-xs transition-transform"
            />
          </button>
          <p v-else class="px-2 mb-2 text-[10px] font-medium text-neutral-500 uppercase tracking-wider text-center">
            A
          </p>

          <div
            v-if="isAdminExpanded || isSidebarCollapsed"
            class="space-y-0.5"
          >
            <NuxtLink
              v-for="link in visibleAdminLinks"
              :key="link.to"
              :to="link.to"
              :class="[
                'flex items-center gap-3 px-3 py-2 rounded-lg text-sm font-medium transition-all',
                isActive(link.to)
                  ? 'bg-white/10 text-white'
                  : 'text-neutral-400 hover:text-white hover:bg-neutral-800',
                isSidebarCollapsed ? 'justify-center' : 'pl-5'
              ]"
              :title="isSidebarCollapsed ? link.label : ''"
            >
              <UIcon :name="link.icon" class="text-lg flex-shrink-0" />
              <span v-if="!isSidebarCollapsed">{{ link.label }}</span>
            </NuxtLink>
          </div>
        </div>
      </nav>

      <!-- User Menu -->
      <div
        :class="[
          'p-2 border-t border-neutral-800',
          isSidebarCollapsed ? 'flex justify-center' : ''
        ]"
      >
        <UserMenu v-if="isAuthenticated" :collapsed="isSidebarCollapsed" />
      </div>
    </aside>

    <!-- Mobile Sidebar -->
    <USlideover
      v-model:open="isSidebarOpen"
      side="left"
      class="lg:hidden"
      :ui="{ content: 'max-w-xs' }"
    >
      <template #content>
        <div class="flex flex-col h-full bg-neutral-900">
          <!-- Close Button -->
          <div class="flex justify-end px-3 py-3">
            <button
              @click="isSidebarOpen = false"
              class="flex items-center justify-center w-8 h-8 rounded-lg text-neutral-400 hover:text-white hover:bg-neutral-800 transition-all"
            >
              <UIcon name="i-lucide-x" class="text-lg" />
            </button>
          </div>

          <!-- Navigation -->
          <nav class="flex-1 overflow-y-auto px-3 py-2 space-y-4">
            <!-- Registry Section -->
            <div>
              <p class="px-2 mb-2 text-[10px] font-medium text-neutral-500 uppercase tracking-wider">
                Registry
              </p>
              <div class="space-y-0.5">
                <NuxtLink
                  v-for="link in visibleMainLinks"
                  :key="link.to"
                  :to="link.to"
                  :class="[
                    'flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition-all',
                    isActive(link.to) 
                      ? 'bg-white/10 text-white' 
                      : 'text-neutral-400 hover:text-white hover:bg-neutral-800'
                  ]"
                  @click="isSidebarOpen = false"
                >
                  <UIcon :name="link.icon" class="text-lg" />
                  <span>{{ link.label }}</span>
                </NuxtLink>
              </div>
            </div>

            <!-- Settings Section -->
            <div class="!mt-auto pt-4 border-t border-neutral-800">
              <button
                @click="toggleSettings"
                :class="[
                  'flex items-center justify-between w-full px-2 mb-2',
                  isSettingsActive ? 'text-white' : 'text-neutral-500'
                ]"
              >
                <span class="text-[10px] font-medium uppercase tracking-wider">Settings</span>
                <UIcon 
                  :name="isSettingsExpanded ? 'i-lucide-chevron-down' : 'i-lucide-chevron-right'" 
                  class="text-xs" 
                />
              </button>
              <div v-if="isSettingsExpanded" class="space-y-0.5">
                <NuxtLink
                  v-for="link in settingsLinks"
                  :key="link.to"
                  :to="link.to"
                  :class="[
                    'flex items-center gap-3 px-3 py-2.5 pl-5 rounded-lg text-sm font-medium transition-all',
                    isActive(link.to) 
                      ? 'bg-white/10 text-white' 
                      : 'text-neutral-400 hover:text-white hover:bg-neutral-800'
                  ]"
                  @click="isSidebarOpen = false"
                >
                  <UIcon :name="link.icon" class="text-lg" />
                  <span>{{ link.label }}</span>
                </NuxtLink>
              </div>
            </div>

            <!-- Admin Section (mobile) -->
            <div v-if="hasAdminSection" class="pt-4 border-t border-neutral-800">
              <button
                @click="toggleAdmin"
                :class="[
                  'flex items-center justify-between w-full px-2 mb-2',
                  isAdminActive ? 'text-white' : 'text-neutral-500'
                ]"
              >
                <span class="text-[10px] font-medium uppercase tracking-wider">Administration</span>
                <UIcon
                  :name="isAdminExpanded ? 'i-lucide-chevron-down' : 'i-lucide-chevron-right'"
                  class="text-xs"
                />
              </button>
              <div v-if="isAdminExpanded" class="space-y-0.5">
                <NuxtLink
                  v-for="link in visibleAdminLinks"
                  :key="link.to"
                  :to="link.to"
                  :class="[
                    'flex items-center gap-3 px-3 py-2.5 pl-5 rounded-lg text-sm font-medium transition-all',
                    isActive(link.to)
                      ? 'bg-white/10 text-white'
                      : 'text-neutral-400 hover:text-white hover:bg-neutral-800'
                  ]"
                  @click="isSidebarOpen = false"
                >
                  <UIcon :name="link.icon" class="text-lg" />
                  <span>{{ link.label }}</span>
                </NuxtLink>
              </div>
            </div>
          </nav>

          <!-- User Menu -->
          <div class="p-3 border-t border-neutral-800">
            <UserMenu v-if="isAuthenticated" />
          </div>
        </div>
      </template>
    </USlideover>

    <!-- Main Content -->
    <main class="flex-1 flex flex-col min-w-0 overflow-hidden bg-neutral-950">
      <!-- Impersonation Banner -->
      <div
        v-if="isImpersonating"
        class="flex items-center justify-between gap-3 px-4 py-2.5 bg-amber-500/15 border-b border-amber-500/30"
      >
        <div class="flex items-center gap-2.5 min-w-0">
          <UIcon name="i-lucide-eye" class="text-amber-400 text-lg shrink-0" />
          <span class="text-sm text-amber-200 truncate">
            Viewing as <strong class="text-amber-100">{{ impersonatedUser?.email }}</strong>
            <span class="text-amber-400/70 ml-1 text-xs">
              ({{ impersonatedUser?.permissions?.length || 0 }} permissions)
            </span>
          </span>
        </div>
        <UButton
          icon="i-lucide-x"
          label="Exit"
          color="warning"
          variant="soft"
          size="xs"
          @click="stopImpersonation"
        />
      </div>
      <div class="flex-1 overflow-y-auto">
        <slot />
      </div>
    </main>
  </div>
</template>
