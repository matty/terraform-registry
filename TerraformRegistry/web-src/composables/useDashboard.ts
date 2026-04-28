import { createSharedComposable } from "@vueuse/core";

const _useDashboard = () => {
  const route = useRoute();

  // Sidebar open state for mobile
  const isSidebarOpen = ref(false);

  // Sidebar collapsed state for desktop
  const isSidebarCollapsed = ref(false);

  // Close sidebar on route change
  watch(
    () => route.fullPath,
    () => {
      isSidebarOpen.value = false;
    }
  );

  return {
    isSidebarOpen,
    isSidebarCollapsed,
  };
};

export const useDashboard = createSharedComposable(_useDashboard);
