import { useAuth } from './useAuth'

export interface Webhook {
  id: string
  userId: string
  url: string
  events: string[]
  format: string
  template: string | null
  isActive: boolean
  createdAt: string
  updatedAt: string
}

export const WEBHOOK_EVENTS = [
  'module.published',
  'module.deleted',
  'module.restored',
  'module.purged',
] as const

export const WEBHOOK_FORMATS = [
  { value: 'generic', label: 'Generic (JSON)' },
  { value: 'discord', label: 'Discord' },
  { value: 'slack', label: 'Slack' },
  { value: 'teams', label: 'Microsoft Teams' },
  { value: 'custom', label: 'Custom Template' },
] as const

export const TEMPLATE_VARIABLES = [
  '{{id}}', '{{event}}', '{{action}}', '{{timestamp}}',
  '{{module.namespace}}', '{{module.name}}', '{{module.provider}}',
  '{{module.version}}', '{{module.description}}', '{{module.source}}',
  '{{module.download_url}}',
] as const

export function useWebhooks() {
  const { getAuthHeaders } = useAuth()

  async function listWebhooks(): Promise<Webhook[]> {
    return await $fetch('/api/webhooks', { headers: getAuthHeaders() })
  }

  async function createWebhook(url: string, events: string[], secret?: string, format = 'generic', template?: string): Promise<Webhook> {
    return await $fetch('/api/webhooks', {
      method: 'POST',
      headers: getAuthHeaders(),
      body: { url, events, secret, format, template },
    })
  }

  async function updateWebhook(id: string, data: { url?: string, events?: string[], secret?: string, isActive?: boolean, format?: string, template?: string }): Promise<Webhook> {
    return await $fetch(`/api/webhooks/${id}`, {
      method: 'PUT',
      headers: getAuthHeaders(),
      body: data,
    })
  }

  async function deleteWebhook(id: string): Promise<void> {
    await $fetch(`/api/webhooks/${id}`, {
      method: 'DELETE',
      headers: getAuthHeaders(),
    })
  }

  return { listWebhooks, createWebhook, updateWebhook, deleteWebhook }
}
