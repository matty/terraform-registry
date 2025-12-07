<script setup lang="ts">
import type { DropdownMenuItem } from "@nuxt/ui";

defineProps<{
  collapsed?: boolean;
}>();

const { user, logout } = useAuth();

const items = computed<DropdownMenuItem[][]>(() => [
  [
    {
      type: "label",
      label: user.value?.name || user.value?.email || "User",
      avatar: user.value?.avatarUrl
        ? { src: user.value.avatarUrl, alt: user.value.name }
        : undefined,
    },
  ],
  [
    {
      label: "Log out",
      icon: "i-lucide-log-out",
      onSelect: () => logout(),
    },
  ],
]);
</script>

<template>
  <UDropdownMenu
    :items="items"
    :content="{ align: 'center', collisionPadding: 12 }"
    :ui="{
      content: collapsed ? 'w-48' : 'w-(--reka-dropdown-menu-trigger-width)',
    }"
  >
    <UButton
      v-bind="{
        label: collapsed ? undefined : user?.name || user?.email || 'User',
        avatar: user?.avatarUrl
          ? { src: user.avatarUrl, alt: user?.name }
          : undefined,
        trailingIcon: collapsed ? undefined : 'i-lucide-chevrons-up-down',
      }"
      color="neutral"
      variant="ghost"
      block
      :square="collapsed"
      class="data-[state=open]:bg-slate-800"
      :ui="{
        trailingIcon: 'text-slate-400',
      }"
    />
  </UDropdownMenu>
</template>
