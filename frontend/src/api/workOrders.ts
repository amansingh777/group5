import { query, request } from './client'
import type { Paged } from './types'

export type WorkOrderStatus =
  | 'Requested'
  | 'Triaged'
  | 'Allocated'
  | 'Scheduled'
  | 'OnSite'
  | 'Completed'
  | 'NotCompleted'
  | 'Cancelled'

export type SlaState = 'ok' | 'amber' | 'breached' | 'none'

export type VisitOutcome = 'Fixed' | 'PartsNeeded' | 'NoAccess' | 'FollowUpRequired'

export interface WorkOrderNote {
  id: number
  jobId: number
  author: string
  note: string
  createdAt: string
}

export interface WorkOrderPart {
  id: number
  jobId: number
  partId: number
  quantity: number
}

export interface WorkOrder {
  id: number
  jobRef: string
  regionId: number
  customerName: string
  siteName: string
  sitePostcode: string
  assetSummary: string | null
  jobType: string
  requiredSkill: string
  priority: string
  status: WorkOrderStatus
  engineerId: number | null
  engineerName: string | null
  scheduledStart: string | null
  scheduledEnd: string | null
  slaDueAt: string | null
  slaState: SlaState
  description: string | null
  outcome: string | null
  createdAt: string
  notes: WorkOrderNote[]
  parts: WorkOrderPart[]
}

export interface WorkOrderListParams {
  page?: number
  pageSize?: number
  status?: string
  regionId?: number
  engineerId?: number
  priority?: string
}

export interface StatusChangeRequest {
  status: WorkOrderStatus
  userName: string
}

export interface AllocateRequest {
  engineerId: number
  userName: string
  /** yyyy-mm-dd */
  date?: string
}

export interface AllocationResult {
  jobId: number
  status: WorkOrderStatus
  engineerId: number
  warnings: string[]
}

export interface WorkOrderCandidate {
  rank: number
  engineerId: number
  engineerName: string
  regionId: number
  rankReason: string
  warnings: string[]
}

export interface ScheduleRequest {
  engineerId?: number
  /** ISO date-time */
  start: string
  userName: string
}

export interface DisplacedJob {
  jobId: number
  jobRef: string
  status: WorkOrderStatus
  engineerId: number | null
}

export interface ScheduleResult {
  jobId: number
  status: WorkOrderStatus
  engineerId: number
  start: string
  end: string
  displaced: DisplacedJob[]
}

export interface NextSlot {
  engineerId: number
  engineerName: string
  start: string
  end: string
}

export interface Visit {
  id: number
  jobId: number
  engineerId: number | null
  visitNo: number
  scheduledStart: string | null
  scheduledEnd: string | null
  actualStart: string | null
  actualEnd: string | null
  outcome: VisitOutcome | null
  createdAt: string
}

export interface CompleteVisitRequest {
  outcome: VisitOutcome
  userName: string
  notes?: string
  parts?: { partId: number; quantity: number }[]
}

function postJson<T>(path: string, body: unknown): Promise<T> {
  return request<T>(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
}

export function listWorkOrders(params: WorkOrderListParams = {}): Promise<Paged<WorkOrder>> {
  return request<Paged<WorkOrder>>(`/api/work-orders${query({ ...params })}`)
}

export function getWorkOrder(id: number): Promise<WorkOrder> {
  return request<WorkOrder>(`/api/work-orders/${id}`)
}

export function changeStatus(id: number, body: StatusChangeRequest): Promise<WorkOrder> {
  return postJson<WorkOrder>(`/api/work-orders/${id}/status`, body)
}

export function getCandidates(id: number, date?: string): Promise<WorkOrderCandidate[]> {
  return request<WorkOrderCandidate[]>(`/api/work-orders/${id}/candidates${query({ date })}`)
}

export function allocate(id: number, body: AllocateRequest): Promise<AllocationResult> {
  return postJson<AllocationResult>(`/api/work-orders/${id}/allocate`, body)
}

export function schedule(id: number, body: ScheduleRequest): Promise<ScheduleResult> {
  return postJson<ScheduleResult>(`/api/work-orders/${id}/schedule`, body)
}

export function nextSlot(id: number, from?: string): Promise<NextSlot> {
  return request<NextSlot>(`/api/work-orders/${id}/next-slot${query({ from })}`)
}

export function startVisit(id: number): Promise<Visit> {
  return request<Visit>(`/api/work-orders/${id}/visits/start`, { method: 'POST' })
}

export function completeVisit(id: number, body: CompleteVisitRequest): Promise<Visit> {
  return postJson<Visit>(`/api/work-orders/${id}/visits/complete`, body)
}

export function listVisits(id: number): Promise<Visit[]> {
  return request<Visit[]>(`/api/work-orders/${id}/visits`)
}
