import { createSharedComposable } from "@vueuse/core";

const _useDashboard = () => {
  const route = useRoute();

  // Sidebar open state for mobile
  const isSidebarOpen = ref(false);

  // Close sidebar on route change
  watch(
    () => route.fullPath,
    () => {
      isSidebarOpen.value = false;
    }
  );

  return {
    isSidebarOpen,
  };
};

export const useDashboard = createSharedComposable(_useDashboard);
