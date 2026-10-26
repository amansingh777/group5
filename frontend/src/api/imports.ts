import { request } from './client'
import type { ImportResult } from './types'

export function importJobRequests(file: File): Promise<ImportResult> {
  const form = new FormData()
  form.append('file', file)
  return request<ImportResult>('/api/imports/job-requests', { method: 'POST', body: form })
}
