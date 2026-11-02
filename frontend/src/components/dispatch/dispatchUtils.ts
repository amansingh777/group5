import type { DispatchJob } from '../../api/dispatch'

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']
const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']
const DASH = '–'

function pad(n: number): string {
  return n < 10 ? `0${n}` : String(n)
}

/** Local calendar date as yyyy-mm-dd (avoids the UTC shift from toISOString). */
export function toIsoDate(d: Date): string {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

export function parseIsoDate(iso: string): Date {
  const [y, m, d] = iso.split('-').map(Number)
  return new Date(y, m - 1, d)
}

export function startOfToday(): Date {
  const d = new Date()
  return new Date(d.getFullYear(), d.getMonth(), d.getDate())
}

export function addDays(d: Date, days: number): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate() + days)
}

/** "Tue 27 Oct" */
export function dayHeading(iso: string): string {
  const d = parseIsoDate(iso)
  return `${WEEKDAYS[d.getDay()]} ${pad(d.getDate())} ${MONTHS[d.getMonth()]}`
}

/** HH:mm from an API date-time, or null when missing. */
export function formatClock(iso: string | null): string | null {
  if (!iso) return null
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return null
  return `${pad(d.getHours())}:${pad(d.getMinutes())}`
}

export function formatTimeRange(start: string | null, end: string | null): string {
  const s = formatClock(start)
  const e = formatClock(end)
  if (s && e) return `${s}${DASH}${e}`
  if (s) return `from ${s}`
  return 'Time to be confirmed'
}

/** "09:00:00" to "09:00" */
export function formatShiftTime(t: string): string {
  return t.slice(0, 5)
}

export function formatShiftRange(start: string, end: string): string {
  return `${formatShiftTime(start)}${DASH}${formatShiftTime(end)}`
}

function startValue(job: DispatchJob): number {
  if (!job.start) return Number.POSITIVE_INFINITY
  const t = new Date(job.start).getTime()
  return Number.isNaN(t) ? Number.POSITIVE_INFINITY : t
}

export function sortByStart(jobs: DispatchJob[]): DispatchJob[] {
  return [...jobs].sort((a, b) => startValue(a) - startValue(b))
}
