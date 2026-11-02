import type { FirstTimeFixReport, FirstTimeFixRow } from '../../api/firstTimeFix'

export type SortMode = 'rate' | 'name'

const MONTH_PATTERN = /^\d{4}-\d{2}$/

/** Month key (yyyy-MM) for the month `monthsAgo` months before today, in local time. */
export function monthKey(monthsAgo = 0): string {
  const now = new Date()
  const d = new Date(now.getFullYear(), now.getMonth() - monthsAgo, 1)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
}

/** Returns a message when the range is not usable, or null when it is. */
export function validateRange(from: string, to: string): string | null {
  if (!MONTH_PATTERN.test(from) || !MONTH_PATTERN.test(to)) return 'Choose both a from month and a to month.'
  if (to < from) return 'The to month must be the same as or later than the from month.'
  return null
}

export function hasJobs(row: FirstTimeFixRow): boolean {
  return row.jobsAttended > 0
}

/** Rate with one decimal place, or an en dash when there were no jobs. */
export function formatRate(row: FirstTimeFixRow): string {
  return hasJobs(row) ? `${row.ratePercent.toFixed(1)}%` : '–'
}

/** Sorts rows by rate ascending (no-job rows last) or by name. Ties fall back to name. */
export function sortRows(rows: FirstTimeFixRow[], mode: SortMode): FirstTimeFixRow[] {
  return [...rows].sort((a, b) => {
    if (mode === 'name') return a.name.localeCompare(b.name)
    const ra = hasJobs(a) ? a.ratePercent : Number.POSITIVE_INFINITY
    const rb = hasJobs(b) ? b.ratePercent : Number.POSITIVE_INFINITY
    if (ra !== rb) return ra < rb ? -1 : 1
    return a.name.localeCompare(b.name)
  })
}

function csvCell(value: string): string {
  return /[",\r\n]/.test(value) ? `"${value.replace(/"/g, '""')}"` : value
}

export function buildCsv(report: FirstTimeFixReport, mode: SortMode): string {
  const lines: string[][] = [['Table', 'Name', 'Jobs attended', 'Fixed first time', 'Rate %']]
  const add = (table: string, rows: FirstTimeFixRow[]) => {
    for (const r of rows) {
      lines.push([
        table,
        r.name,
        String(r.jobsAttended),
        String(r.fixedFirstTime),
        hasJobs(r) ? r.ratePercent.toFixed(1) : '',
      ])
    }
  }
  add('Overall', [{ ...report.overall, name: 'All work orders' }])
  add('By region', sortRows(report.byRegion, mode))
  add('By job type', sortRows(report.byJobType, mode))
  return lines.map((cols) => cols.map(csvCell).join(',')).join('\r\n')
}

/** Downloads text as a CSV file using a temporary object URL. No dependencies. */
export function downloadCsv(filename: string, text: string): void {
  const blob = new Blob(['\uFEFF' + text], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = filename
  document.body.appendChild(link)
  link.click()
  link.remove()
  setTimeout(() => URL.revokeObjectURL(url), 0)
}
