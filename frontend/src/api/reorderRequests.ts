import { query, request } from './client'
import type { Paged } from './types'

export type ReorderStatus = 'Open' | 'Ordered' | 'Received'

export interface ReorderRequest {
  id: number
  partId: number
  partSku: string
  partName: string
  stockQty: number
  reorderLevel: number
  belowReorderLevel: boolean
  status: ReorderStatus
  /** yyyy-MM-dd */
  expectedOn: string | null
  orderedBy: string | null
  receivedBy: string | null
  createdAt: string
  updatedAt: string
}

export interface ReorderListParams {
  page?: number
  pageSize?: number
  status?: ReorderStatus
  partId?: number
}

export interface MarkOrderedRequest {
  /** yyyy-MM-dd, today or later */
  expectedOn: string
  userName: string
}

export interface MarkReceivedRequest {
  quantity: number
  userName: string
}

function postJson<T>(path: string, body: unknown): Promise<T> {
  return request<T>(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
}

export function listReorderRequests(params: ReorderListParams = {}): Promise<Paged<ReorderRequest>> {
  return request<Paged<ReorderRequest>>(`/api/reorder-requests${query({ ...params })}`)
}

export function markOrdered(id: number, body: MarkOrderedRequest): Promise<ReorderRequest> {
  return postJson<ReorderRequest>(`/api/reorder-requests/${id}/ordered`, body)
}

export function markReceived(id: number, body: MarkReceivedRequest): Promise<ReorderRequest> {
  return postJson<ReorderRequest>(`/api/reorder-requests/${id}/received`, body)
}
