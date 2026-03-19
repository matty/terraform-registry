<script setup lang="ts">
defineProps<{
  collapsed?: boolean;
}>();

const { user, logout } = useAuth();

const isOpen = ref(false);

const initials = computed(() => {
  const name = user.value?.name || user.value?.email || "U";
  return name
    .split(" ")
    .map((w: string) => w[0])
    .slice(0, 2)
    .join("")
    .toUpperCase();
});

const handleLogout = () => {
  isOpen.value = false;
  logout();
};
</script>

<template>
  <UPopover v-model:open="isOpen" :content="{ align: 'center', side: 'top', sideOffset: 8 }">
    <!-- Trigger Button -->
    <button
      :class="[
        'w-full flex items-center gap-3 px-2 py-2 rounded-xl transition-all',
        'hover:bg-white/5',
        isOpen ? 'bg-white/10' : '',
        collapsed ? 'justify-center' : ''
      ]"
    >
      <!-- Avatar -->
      <div class="relative flex-shrink-0">
        <img
          v-if="user?.avatarUrl"
          :src="user.avatarUrl"
          :alt="user?.name"
          class="w-8 h-8 rounded-lg object-cover ring-1 ring-white/10"
        />
        <div
          v-else
          class="w-8 h-8 rounded-lg bg-gradient-to-br from-neutral-600 to-neutral-700 flex items-center justify-center ring-1 ring-white/10"
        >
          <span class="text-xs font-semibold text-white">{{ initials }}</span>
        </div>
        <!-- Online dot -->
        <div class="absolute -bottom-0.5 -right-0.5 w-2.5 h-2.5 bg-emerald-500 rounded-full ring-2 ring-neutral-900"></div>
      </div>

      <!-- Name + email (when expanded) -->
      <div v-if="!collapsed" class="flex-1 min-w-0 text-left">
        <p class="text-sm font-medium text-white truncate leading-tight">
          {{ user?.name || 'User' }}
        </p>
        <p v-if="user?.email && user?.name" class="text-[11px] text-neutral-500 truncate leading-tight">
          {{ user.email }}
        </p>
      </div>

      <UIcon
        v-if="!collapsed"
        name="i-lucide-chevrons-up-down"
        class="text-neutral-500 text-sm flex-shrink-0"
      />
    </button>

    <!-- Popup Content -->
    <template #content>
      <div class="w-64 bg-neutral-900 border border-neutral-800 rounded-xl shadow-2xl shadow-black/50 overflow-hidden">
        <!-- User info header -->
        <div class="px-4 py-4 bg-gradient-to-b from-white/[0.03] to-transparent">
          <div class="flex items-center gap-3">
            <div class="relative">
              <img
                v-if="user?.avatarUrl"
                :src="user.avatarUrl"
                :alt="user?.name"
                class="w-10 h-10 rounded-xl object-cover ring-1 ring-white/10"
              />
              <div
                v-else
                class="w-10 h-10 rounded-xl bg-gradient-to-br from-neutral-600 to-neutral-700 flex items-center justify-center ring-1 ring-white/10"
              >
                <span class="text-sm font-semibold text-white">{{ initials }}</span>
              </div>
              <div class="absolute -bottom-0.5 -right-0.5 w-3 h-3 bg-emerald-500 rounded-full ring-2 ring-neutral-900"></div>
            </div>
            <div class="min-w-0">
              <p class="text-sm font-semibold text-white truncate">
                {{ user?.name || 'User' }}
              </p>
              <p class="text-xs text-neutral-400 truncate">
                {{ user?.email || 'Authenticated' }}
              </p>
            </div>
          </div>
        </div>

        <!-- Divider -->
        <div class="h-px bg-neutral-800 mx-3"></div>

        <!-- Actions -->
        <div class="p-2 space-y-0.5">
          <NuxtLink
            to="/settings/account"
            @click="isOpen = false"
            class="w-full flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium text-neutral-400 hover:text-white hover:bg-white/5 transition-all group"
          >
            <UIcon name="i-lucide-user-cog" class="text-lg group-hover:text-primary-400 transition-colors" />
            <span>Account</span>
          </NuxtLink>
          <button
            @click="handleLogout"
            class="w-full flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium text-neutral-400 hover:text-white hover:bg-white/5 transition-all group"
          >
            <UIcon name="i-lucide-log-out" class="text-lg group-hover:text-red-400 transition-colors" />
            <span>Sign out</span>
          </button>
        </div>
      </div>
    </template>
  </UPopover>
</template>
