import type { SlaState, VisitOutcome, WorkOrderStatus } from '../../api/workOrders'

export const STATUS_LABELS: Record<WorkOrderStatus, string> = {
  Requested: 'Raised',
  Triaged: 'Triaged',
  Allocated: 'Allocated',
  Scheduled: 'Scheduled',
  OnSite: 'On site',
  Completed: 'Completed',
  NotCompleted: 'Not completed',
  Cancelled: 'Cancelled',
}

export const ALL_STATUSES: WorkOrderStatus[] = [
  'Requested',
  'Triaged',
  'Allocated',
  'Scheduled',
  'OnSite',
  'Completed',
  'NotCompleted',
  'Cancelled',
]

export const OPEN_STATUSES: WorkOrderStatus[] = ['Requested', 'Triaged', 'Allocated', 'Scheduled', 'OnSite']

export const FINAL_STATUSES: WorkOrderStatus[] = ['Completed', 'NotCompleted', 'Cancelled']

export const PRIORITIES = ['P1', 'P2', 'P3']

export const OUTCOME_OPTIONS: { value: VisitOutcome; label: string }[] = [
  { value: 'Fixed', label: 'Fixed' },
  { value: 'PartsNeeded', label: 'Parts needed' },
  { value: 'NoAccess', label: 'No access' },
  { value: 'FollowUpRequired', label: 'Follow-up required' },
]

export const SLA_LABELS: Record<SlaState, string> = {
  ok: 'SLA ok',
  amber: 'SLA near',
  breached: 'SLA breached',
  none: 'No SLA',
}

export function statusLabel(status: string): string {
  return STATUS_LABELS[status as WorkOrderStatus] ?? status
}

export function outcomeLabel(outcome: string | null): string {
  if (!outcome) return '-'
  return OUTCOME_OPTIONS.find((o) => o.value === outcome)?.label ?? outcome
}

export function formatDateTime(value: string | null | undefined): string {
  return value ? new Date(value).toLocaleString() : '-'
}

/** Local wall-clock value for a datetime-local input (yyyy-MM-ddTHH:mm). */
export function toLocalInput(iso: string): string {
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/** Sends the local wall-clock time without a zone, as the API expects for booking starts. */
export function toApiLocal(value: string): string {
  return value.length === 16 ? `${value}:00` : value
}

export function errorMessage(e: unknown): string {
  return e instanceof Error ? e.message : 'Request failed.'
}
