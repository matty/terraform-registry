import { useAuth } from './useAuth'

export interface ModuleExtractionRuntimeConfig {
  enabled: boolean
  startupEnabled: boolean
  persistedEnabled: boolean | null
  hasRuntimeOverride: boolean
  updatedAt: string | null
  updatedBy: string | null
}

export interface ModuleExtractionAdminSummary {
  succeeded: number
  failed: number
  pending: number
  processing: number
  neverExtracted: number
  total: number
}

export interface ModuleDocumentationSummary {
  primaryReadmePath: string | null
  inputCount: number
  outputCount: number
  exampleCount: number
  hasSubmoduleDocs: boolean
}

export interface ModuleExtractionAdminListItem {
  namespace: string
  name: string
  provider: string
  version: string
  description: string | null
  status: string
  lastAttemptedAt: string | null
  lastSucceededAt: string | null
  error: string | null
  documentation: ModuleDocumentationSummary | null
}

export interface ModuleReadmeDocument {
  path: string
  title: string | null
  markdown: string
}

export interface ModuleInputDefinition {
  name: string
  description: string | null
  required: boolean
  type: string | null
  defaultJson: string | null
  sensitive: boolean
}

export interface ModuleOutputDefinition {
  name: string
  description: string | null
  sensitive: boolean
}

export interface ModuleProviderRequirement {
  name: string
  namespace: string | null
  source: string | null
  versionConstraint: string | null
}

export interface ModuleResourceDefinition {
  type: string
  name: string
  provider: string | null
  mode: string | null
}

export interface ModuleSubmodule {
  path: string
  providers: Record<string, string>
}

export interface ModuleExampleDefinition {
  name: string
  path: string
  description: string | null
  readmePath: string | null
}

export interface ModuleExtractionDocument {
  schemaVersion: string
  generatedAt: string
  extractor: string
  readme: ModuleReadmeDocument | null
  inputs: ModuleInputDefinition[]
  outputs: ModuleOutputDefinition[]
  providerRequirements: ModuleProviderRequirement[]
  managedResources: ModuleResourceDefinition[]
  dataResources: ModuleResourceDefinition[]
  submodules: ModuleSubmodule[]
  examples: ModuleExampleDefinition[]
  warnings: string[]
}

export interface ModuleExtractionAdminDetail extends ModuleExtractionAdminListItem {
  document: ModuleExtractionDocument | null
}

export interface ModuleExtractionAdminPage {
  items: ModuleExtractionAdminListItem[]
  total: number
}

export interface ModuleDocsSummaryResponse {
  config: ModuleExtractionRuntimeConfig
  summary: ModuleExtractionAdminSummary
}

export function useModuleDocsAdmin() {
  const { getAuthHeaders } = useAuth()

  const pathFor = (module: Pick<ModuleExtractionAdminListItem, 'namespace' | 'name' | 'provider' | 'version'>) =>
    [
      module.namespace,
      module.name,
      module.provider,
      module.version,
    ].map(encodeURIComponent).join('/')

  async function getSummary(): Promise<ModuleDocsSummaryResponse> {
    return await $fetch('/api/admin/module-docs/summary', { headers: getAuthHeaders() })
  }

  async function listModules(params?: {
    status?: string
    q?: string
    limit?: number
    offset?: number
  }): Promise<ModuleExtractionAdminPage> {
    const query = new URLSearchParams()
    if (params?.status) query.set('status', params.status)
    if (params?.q) query.set('q', params.q)
    if (params?.limit !== undefined) query.set('limit', String(params.limit))
    if (params?.offset !== undefined) query.set('offset', String(params.offset))
    const qs = query.toString()
    return await $fetch(`/api/admin/module-docs/modules${qs ? `?${qs}` : ''}`, {
      headers: getAuthHeaders(),
    })
  }

  async function getModuleDetail(module: Pick<ModuleExtractionAdminListItem, 'namespace' | 'name' | 'provider' | 'version'>): Promise<ModuleExtractionAdminDetail> {
    return await $fetch(`/api/admin/module-docs/modules/${pathFor(module)}`, {
      headers: getAuthHeaders(),
    })
  }

  async function requeueModule(module: Pick<ModuleExtractionAdminListItem, 'namespace' | 'name' | 'provider' | 'version'>): Promise<{ queued: boolean }> {
    return await $fetch(`/api/admin/module-docs/modules/${pathFor(module)}/requeue`, {
      method: 'POST',
      headers: getAuthHeaders(),
    })
  }

  async function queueBackfill(limit = 25): Promise<{ queued: number, modules: ModuleExtractionAdminListItem[] }> {
    return await $fetch('/api/admin/module-docs/backfill', {
      method: 'POST',
      headers: getAuthHeaders(),
      body: { limit },
    })
  }

  async function getConfig(): Promise<ModuleExtractionRuntimeConfig> {
    return await $fetch('/api/admin/module-docs/config', {
      headers: getAuthHeaders(),
    })
  }

  async function updateConfig(enabled: boolean): Promise<ModuleExtractionRuntimeConfig> {
    return await $fetch('/api/admin/module-docs/config', {
      method: 'PUT',
      headers: getAuthHeaders(),
      body: { enabled },
    })
  }

  return {
    getSummary,
    listModules,
    getModuleDetail,
    requeueModule,
    queueBackfill,
    getConfig,
    updateConfig,
  }
}
