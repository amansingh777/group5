import { query, request } from './client'
import type { Job, Paged } from './types'

export function listJobs(params: { status?: string; page?: number }): Promise<Paged<Job>> {
  return request<Paged<Job>>(`/api/jobs${query({ status: params.status, page: params.page })}`)
}
