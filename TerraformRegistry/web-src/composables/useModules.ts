import { useAuth } from './useAuth';

export interface Module {
  id: string;
  owner: string;
  namespace: string;
  name: string;
  version: string;
  provider: string;
  description: string;
  published_at: string;
  publishedAt?: string;
  versions: string[];
  download_url: string;
  downloadUrl?: string;
}

export interface ModulesResponse {
  modules: Module[];
  meta?: {
    limit: string;
    current_offset: string;
    total?: string;
  };
}

export interface ModuleFacets {
  namespaces: string[];
  providers: string[];
}

export type ModuleSortField = 'name' | 'published' | 'versions';
export type ModuleSortOrder = 'asc' | 'desc';

export interface ModuleListQuery {
  q?: string;
  namespace?: string;
  provider?: string;
  sort?: ModuleSortField;
  order?: ModuleSortOrder;
  offset?: number;
  limit?: number;
}

export function useModules() {
  const { getAuthHeaders } = useAuth();

  const deleteModuleVersion = async (
    namespace: string,
    name: string,
    provider: string,
    version: string
  ): Promise<boolean> => {
    try {
      await $fetch(`/v1/modules/${namespace}/${name}/${provider}/${version}`, {
        method: 'DELETE',
        headers: getAuthHeaders(),
      });
      return true;
    } catch (err) {
      console.error('Error deleting module version:', err);
      return false;
    }
  };

  const restoreModuleVersion = async (
    namespace: string,
    name: string,
    provider: string,
    version: string
  ): Promise<boolean> => {
    try {
      await $fetch(`/v1/modules/${namespace}/${name}/${provider}/${version}/restore`, {
        method: 'POST',
        headers: getAuthHeaders(),
      });
      return true;
    } catch (err) {
      console.error('Error restoring module version:', err);
      return false;
    }
  };

  const purgeModuleVersion = async (
    namespace: string,
    name: string,
    provider: string,
    version: string
  ): Promise<boolean> => {
    try {
      await $fetch(`/v1/modules/${namespace}/${name}/${provider}/${version}/purge`, {
        method: 'DELETE',
        headers: getAuthHeaders(),
      });
      return true;
    } catch (err) {
      console.error('Error purging module version:', err);
      return false;
    }
  };

  const listDeletedModules = async (
    offset = 0,
    limit = 10
  ): Promise<ModulesResponse> => {
    try {
      return await $fetch<ModulesResponse>(
        `/v1/modules/trash?offset=${offset}&limit=${limit}`,
        {
          headers: getAuthHeaders(),
        }
      );
    } catch (err) {
      console.error('Error fetching deleted modules:', err);
      return { modules: [] };
    }
  };

  const listModules = async (
    query: ModuleListQuery = {}
  ): Promise<ModulesResponse> => {
    const { offset = 0, limit = 25, q, namespace, provider, sort, order } = query;

    const params = new URLSearchParams({
      offset: String(offset),
      limit: String(limit),
    });

    if (q) params.set('q', q);
    if (namespace) params.set('namespace', namespace);
    if (provider) params.set('provider', provider);
    if (sort) params.set('sort', sort);
    if (order) params.set('order', order);

    return await $fetch<ModulesResponse>(`/v1/modules?${params.toString()}`, {
      headers: getAuthHeaders(),
    });
  };

  const getModuleFacets = async (): Promise<ModuleFacets> => {
    try {
      return await $fetch<ModuleFacets>('/v1/modules/facets', {
        headers: getAuthHeaders(),
      });
    } catch (err) {
      // Filters degrade to "All" rather than blocking the listing.
      console.error('Error fetching module facets:', err);
      return { namespaces: [], providers: [] };
    }
  };

  const getModuleVersions = async (
    namespace: string,
    name: string,
    provider: string
  ): Promise<{ modules: { versions: { version: string }[] }[] } | null> => {
    try {
      return await $fetch(`/v1/modules/${namespace}/${name}/${provider}/versions`, {
        headers: getAuthHeaders(),
      });
    } catch (err) {
      console.error('Error fetching module versions:', err);
      return null;
    }
  };

  const updateModuleDescription = async (
    namespace: string,
    name: string,
    provider: string,
    description: string
  ): Promise<boolean> => {
    try {
      await $fetch(`/v1/modules/${namespace}/${name}/${provider}/description`, {
        method: 'PATCH',
        headers: {
          ...getAuthHeaders(),
          'Content-Type': 'application/json',
        },
        body: { description },
      });
      return true;
    } catch (err) {
      console.error('Error updating module description:', err);
      return false;
    }
  };

  return {
    deleteModuleVersion,
    restoreModuleVersion,
    purgeModuleVersion,
    listDeletedModules,
    listModules,
    getModuleFacets,
    getModuleVersions,
    updateModuleDescription,
  };
}
