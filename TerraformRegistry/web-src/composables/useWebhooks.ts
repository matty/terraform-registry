import { useAuth } from './useAuth'

export interface Webhook {
  id: string
  userId: string
  url: string
  events: string[]
  isActive: boolean
  createdAt: string
  updatedAt: string
}

export const WEBHOOK_EVENTS = [
  'module.uploaded',
  'module.deleted',
  'module.restored',
  'module.purged',
] as const

export function useWebhooks() {
  const { getAuthHeaders } = useAuth()

  async function listWebhooks(): Promise<Webhook[]> {
    return await $fetch('/api/webhooks', { headers: getAuthHeaders() })
  }

  async function createWebhook(url: string, events: string[], secret?: string): Promise<Webhook> {
    return await $fetch('/api/webhooks', {
      method: 'POST',
      headers: getAuthHeaders(),
      body: { url, events, secret },
    })
  }

  async function updateWebhook(id: string, data: { url?: string, events?: string[], secret?: string, isActive?: boolean }): Promise<Webhook> {
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
