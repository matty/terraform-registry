import { useAuth } from './useAuth';

export interface TerraformProvider {
  id: string;
  namespace: string;
  type: string;
  display_name?: string | null;
  description?: string | null;
  source_repository_url?: string | null;
  created_by?: string | null;
  created_at?: string;
  updated_at?: string;
  deleted_at?: string | null;
}

export interface ProviderPlatform {
  id?: string;
  os: string;
  arch: string;
  filename: string;
  shasum: string;
  has_package?: boolean;
  size_bytes?: number;
  uploaded_at?: string | null;
  package_storage_path?: string | null;
}

export interface ProviderVersionEntry {
  id?: string;
  version: string;
  protocols: string[];
  platforms: ProviderPlatform[];
  key_id?: string;
  has_shasums?: boolean;
  has_shasums_signature?: boolean;
  published_at?: string;
}

export interface ProviderVersion {
  id: string;
  provider_id: string;
  version: string;
  protocols: string[];
  key_id: string;
  shasums_storage_path?: string | null;
  shasums_signature_storage_path?: string | null;
  published_at: string;
  deleted_at?: string | null;
}

export interface ProviderGpgKey {
  id: string;
  namespace: string;
  key_id: string;
  ascii_armor: string;
  trust_signature?: string | null;
  source?: string | null;
  source_url?: string | null;
  created_at: string;
  revoked_at?: string | null;
}

export interface ProvidersResponse {
  providers: TerraformProvider[];
  offset: number;
  limit: number;
  total: number;
}

export interface ProviderVersionsResponse {
  versions: ProviderVersionEntry[];
}

export interface ProviderPlatformsResponse {
  platforms: ProviderPlatform[];
}

export interface ProviderGpgKeysResponse {
  gpg_keys: ProviderGpgKey[];
}

export interface CreateProviderRequest {
  namespace: string;
  type: string;
  display_name?: string;
  description?: string;
  source_repository_url?: string;
}

export interface UpdateProviderRequest {
  display_name: string | null;
  description: string | null;
  source_repository_url: string | null;
}

export interface CreateProviderVersionRequest {
  version: string;
  protocols: string[];
  key_id: string;
}

export interface CreateProviderPlatformRequest {
  os: string;
  arch: string;
  filename: string;
  shasum: string;
}

export interface CreateProviderGpgKeyRequest {
  key_id: string;
  ascii_armor: string;
  trust_signature?: string;
  source?: string;
  source_url?: string;
}

const encoded = (value: string): string => encodeURIComponent(value);

export function useProviders() {
  const { getAuthHeaders } = useAuth();

  const providerPath = (namespace: string, type: string): string =>
    `/api/providers/${encoded(namespace)}/${encoded(type)}`;

  const versionPath = (namespace: string, type: string, version: string): string =>
    `${providerPath(namespace, type)}/versions/${encoded(version)}`;

  const platformPath = (
    namespace: string,
    type: string,
    version: string,
    os: string,
    arch: string
  ): string => `${versionPath(namespace, type, version)}/platforms/${encoded(os)}/${encoded(arch)}`;

  const listProviders = async (
    q = '',
    offset = 0,
    limit = 20
  ): Promise<ProvidersResponse> => {
    const params = new URLSearchParams({
      offset: String(offset),
      limit: String(limit),
    });

    if (q.trim()) {
      params.set('q', q);
    }

    return await $fetch<ProvidersResponse>(`/api/providers?${params.toString()}`, {
      headers: getAuthHeaders(),
    });
  };

  const createProvider = async (
    request: CreateProviderRequest
  ): Promise<TerraformProvider> => {
    return await $fetch<TerraformProvider>('/api/providers', {
      method: 'POST',
      headers: getAuthHeaders(),
      body: request,
    });
  };

  const getProvider = async (
    namespace: string,
    type: string
  ): Promise<TerraformProvider> => {
    return await $fetch<TerraformProvider>(providerPath(namespace, type), {
      headers: getAuthHeaders(),
    });
  };

  const updateProvider = async (
    namespace: string,
    type: string,
    request: UpdateProviderRequest
  ): Promise<TerraformProvider> => {
    return await $fetch<TerraformProvider>(providerPath(namespace, type), {
      method: 'PATCH',
      headers: getAuthHeaders(),
      body: request,
    });
  };

  const deleteProvider = async (namespace: string, type: string): Promise<void> => {
    await $fetch<void>(providerPath(namespace, type), {
      method: 'DELETE',
      headers: getAuthHeaders(),
    });
  };

  const listGpgKeys = async (
    namespace: string,
    type: string
  ): Promise<ProviderGpgKeysResponse> => {
    return await $fetch<ProviderGpgKeysResponse>(`${providerPath(namespace, type)}/gpg-keys`, {
      headers: getAuthHeaders(),
    });
  };

  const addGpgKey = async (
    namespace: string,
    type: string,
    request: CreateProviderGpgKeyRequest
  ): Promise<ProviderGpgKey> => {
    return await $fetch<ProviderGpgKey>(`${providerPath(namespace, type)}/gpg-keys`, {
      method: 'POST',
      headers: getAuthHeaders(),
      body: request,
    });
  };

  const revokeGpgKey = async (
    namespace: string,
    type: string,
    keyId: string
  ): Promise<void> => {
    await $fetch<void>(`${providerPath(namespace, type)}/gpg-keys/${encoded(keyId)}`, {
      method: 'DELETE',
      headers: getAuthHeaders(),
    });
  };

  const listVersions = async (
    namespace: string,
    type: string
  ): Promise<ProviderVersionsResponse> => {
    return await $fetch<ProviderVersionsResponse>(`${providerPath(namespace, type)}/versions`, {
      headers: getAuthHeaders(),
    });
  };

  const createVersion = async (
    namespace: string,
    type: string,
    request: CreateProviderVersionRequest
  ): Promise<ProviderVersion> => {
    return await $fetch<ProviderVersion>(`${providerPath(namespace, type)}/versions`, {
      method: 'POST',
      headers: getAuthHeaders(),
      body: request,
    });
  };

  const deleteVersion = async (
    namespace: string,
    type: string,
    version: string
  ): Promise<void> => {
    await $fetch<void>(versionPath(namespace, type, version), {
      method: 'DELETE',
      headers: getAuthHeaders(),
    });
  };

  const uploadShasums = async (
    namespace: string,
    type: string,
    version: string,
    file: File | Blob
  ): Promise<void> => {
    await $fetch<void>(`${versionPath(namespace, type, version)}/shasums`, {
      method: 'PUT',
      headers: getAuthHeaders(),
      body: file,
    });
  };

  const uploadShasumsSignature = async (
    namespace: string,
    type: string,
    version: string,
    file: File | Blob
  ): Promise<void> => {
    await $fetch<void>(`${versionPath(namespace, type, version)}/shasums.sig`, {
      method: 'PUT',
      headers: getAuthHeaders(),
      body: file,
    });
  };

  const listPlatforms = async (
    namespace: string,
    type: string,
    version: string
  ): Promise<ProviderPlatformsResponse> => {
    return await $fetch<ProviderPlatformsResponse>(
      `${versionPath(namespace, type, version)}/platforms`,
      {
        headers: getAuthHeaders(),
      }
    );
  };

  const createPlatform = async (
    namespace: string,
    type: string,
    version: string,
    request: CreateProviderPlatformRequest
  ): Promise<ProviderPlatform> => {
    return await $fetch<ProviderPlatform>(`${versionPath(namespace, type, version)}/platforms`, {
      method: 'POST',
      headers: getAuthHeaders(),
      body: request,
    });
  };

  const deletePlatform = async (
    namespace: string,
    type: string,
    version: string,
    os: string,
    arch: string
  ): Promise<void> => {
    await $fetch<void>(platformPath(namespace, type, version, os, arch), {
      method: 'DELETE',
      headers: getAuthHeaders(),
    });
  };

  const uploadPlatformPackage = async (
    namespace: string,
    type: string,
    version: string,
    os: string,
    arch: string,
    file: File | Blob
  ): Promise<void> => {
    await $fetch<void>(`${platformPath(namespace, type, version, os, arch)}/package`, {
      method: 'PUT',
      headers: getAuthHeaders(),
      body: file,
    });
  };

  return {
    listProviders,
    createProvider,
    getProvider,
    updateProvider,
    deleteProvider,
    listGpgKeys,
    addGpgKey,
    revokeGpgKey,
    listVersions,
    createVersion,
    deleteVersion,
    uploadShasums,
    uploadShasumsSignature,
    listPlatforms,
    createPlatform,
    deletePlatform,
    uploadPlatformPackage,
  };
}
