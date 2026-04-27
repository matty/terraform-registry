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
    total_count?: string;
    has_more?: string;
    next_offset?: string;
  };
}

export interface ListModulesInput {
  q?: string;
  namespace?: string;
  provider?: string;
  requiredProvider?: string;
  offset?: number;
  limit?: number;
}

export interface UploadModuleInput {
  namespace: string;
  name: string;
  provider: string;
  version: string;
  moduleFile: File;
  description?: string;
  replace?: boolean;
}

export interface UploadModuleResponse {
  filename: string;
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

  const listModules = async ({
    q,
    namespace,
    provider,
    requiredProvider,
    offset = 0,
    limit = 10,
  }: ListModulesInput = {}): Promise<ModulesResponse> => {
    try {
      const params = new URLSearchParams({
        offset: String(offset),
        limit: String(limit),
      })

      if (q?.trim()) {
        params.set('q', q.trim())
      }

      if (namespace?.trim()) {
        params.set('namespace', namespace.trim())
      }

      if (provider?.trim()) {
        params.set('provider', provider.trim())
      }

      if (requiredProvider?.trim()) {
        params.set('required_provider', requiredProvider.trim())
      }

      return await $fetch<ModulesResponse>(
        `/v1/modules?${params.toString()}`,
        {
          headers: getAuthHeaders(),
        }
      );
    } catch (err) {
      console.error('Error fetching modules:', err);
      return { modules: [] };
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

  const uploadModule = async ({
    namespace,
    name,
    provider,
    version,
    moduleFile,
    description,
    replace = false,
  }: UploadModuleInput): Promise<UploadModuleResponse> => {
    const formData = new FormData()
    formData.append('moduleFile', moduleFile)

    if (description?.trim()) {
      formData.append('description', description.trim())
    }

    if (replace) {
      formData.append('replace', 'true')
    }

    try {
      return await $fetch<UploadModuleResponse>(`/v1/modules/${namespace}/${name}/${provider}/${version}`, {
        method: 'POST',
        headers: getAuthHeaders(),
        body: formData,
      })
    } catch (err) {
      console.error('Error uploading module:', err)
      throw err
    }
  }

  return {
    deleteModuleVersion,
    restoreModuleVersion,
    purgeModuleVersion,
    listDeletedModules,
    listModules,
    getModuleVersions,
    updateModuleDescription,
    uploadModule,
  };
}
