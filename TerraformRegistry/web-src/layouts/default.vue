<script setup lang="ts">
import type { NavigationMenuItem } from "@nuxt/ui";
import { useDashboard } from "~/composables/useDashboard";

const { isAuthenticated } = useAuth();
const { isSidebarOpen } = useDashboard();

const links: NavigationMenuItem[][] = [
  [
    {
      label: "Modules",
      icon: "i-lucide-package",
      to: "/",
      onSelect: () => {
        isSidebarOpen.value = false;
      },
    },
  ],
  [
    {
      label: "Settings",
      icon: "i-lucide-settings",
      to: "/settings",
      onSelect: () => {
        isSidebarOpen.value = false;
      },
    },
  ],
];
</script>

<template>
  <div class="fixed inset-0 flex overflow-hidden">
    <!-- Desktop Sidebar -->
    <aside
      class="hidden lg:flex lg:flex-col w-64 border-r border-neutral-200 dark:border-neutral-800 bg-neutral-50/50 dark:bg-neutral-900/50 backdrop-blur"
    >
      <div class="flex items-center gap-2.5 px-4 h-16 border-b border-neutral-200 dark:border-neutral-800">
        <div
          class="flex items-center justify-center w-8 h-8 rounded-lg bg-blue-600"
        >
          <UIcon name="i-lucide-box" class="text-white text-lg" />
        </div>
        <span class="font-semibold text-slate-900 dark:text-slate-100 truncate">
          Terraform Registry
        </span>
      </div>

      <div class="flex-1 overflow-y-auto p-4 flex flex-col gap-2">
        <UNavigationMenu :items="links[0]" orientation="vertical" />
        <div class="mt-auto">
          <UNavigationMenu :items="links[1]" orientation="vertical" />
        </div>
      </div>

      <div class="p-4 border-t border-neutral-200 dark:border-neutral-800">
        <UserMenu v-if="isAuthenticated" />
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
        <div class="flex flex-col h-full bg-white dark:bg-neutral-900">
          <div class="flex items-center gap-2.5 px-4 h-16 border-b border-neutral-200 dark:border-neutral-800">
            <div
              class="flex items-center justify-center w-8 h-8 rounded-lg bg-blue-600"
            >
              <UIcon name="i-lucide-box" class="text-white text-lg" />
            </div>
            <span class="font-semibold text-slate-900 dark:text-slate-100 truncate">
              Terraform Registry
            </span>
          </div>

          <div class="flex-1 overflow-y-auto p-4 flex flex-col gap-2">
            <UNavigationMenu :items="links[0]" orientation="vertical" />
             <div class="mt-auto">
              <UNavigationMenu :items="links[1]" orientation="vertical" />
            </div>
          </div>

          <div class="p-4 border-t border-neutral-200 dark:border-neutral-800">
            <UserMenu v-if="isAuthenticated" />
          </div>
        </div>
      </template>
    </USlideover>

    <!-- Main Content -->
    <main class="flex-1 flex flex-col min-w-0 overflow-hidden bg-white dark:bg-neutral-950">
      <div class="flex-1 overflow-y-auto">
        <slot />
      </div>
    </main>
  </div>
</template>
