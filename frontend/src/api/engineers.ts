import { request } from './client'
import type { Engineer } from './types'

export function listEngineers(): Promise<Engineer[]> {
  return request<Engineer[]>('/api/engineers')
}
