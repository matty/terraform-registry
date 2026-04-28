import { useAuth } from './useAuth'

export interface ManualModuleUploadRequest {
  namespace: string
  name: string
  provider: string
  version: string
  description: string
  file: File
  replace: boolean
}

export function useModulePublishing() {
  const { getAuthHeaders } = useAuth()

  async function uploadModule(request: ManualModuleUploadRequest) {
    const form = new FormData()
    form.append('moduleFile', request.file)
    form.append('description', request.description)

    if (request.replace) {
      form.append('replace', 'true')
    }

    return await $fetch(
      `/v1/modules/${request.namespace}/${request.name}/${request.provider}/${request.version}`,
      {
        method: 'POST',
        headers: getAuthHeaders(),
        body: form,
      }
    )
  }

  return { uploadModule }
}
