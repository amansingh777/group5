import { query, request } from './client'
import type { SlaState, WorkOrderStatus } from './workOrders'

export interface DispatchJob {
  id: number
  jobRef: string
  priority: string
  status: WorkOrderStatus
  start: string | null
  end: string | null
  slaState: SlaState
  needsRebook: boolean
}

export interface DispatchCell {
  /** yyyy-mm-dd */
  date: string
  /** HH:mm:ss or null when no shift */
  shiftStart: string | null
  shiftEnd: string | null
  breakMinutes: number
  jobs: DispatchJob[]
}

export interface DispatchEngineerRow {
  engineerId: number
  fullName: string
  regionId: number
  skillIds: number[]
  cells: DispatchCell[]
}

export interface DispatchBoard {
  /** yyyy-mm-dd */
  from: string
  days: number
  engineers: DispatchEngineerRow[]
  /** At most 200 rows, rebooks first. */
  unscheduled: DispatchJob[]
  /** Uncapped count of unscheduled work orders. */
  unscheduledTotal: number
}

export function getBoard(from: string, days: number, regionId?: number): Promise<DispatchBoard> {
  return request<DispatchBoard>(`/api/dispatch/board${query({ from, days, regionId })}`)
}
