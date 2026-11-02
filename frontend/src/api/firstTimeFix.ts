import { query, request } from './client'

export interface FirstTimeFixRow {
  name: string
  jobsAttended: number
  fixedFirstTime: number
  ratePercent: number
}

export interface FirstTimeFixReport {
  from: string
  to: string
  overall: FirstTimeFixRow
  byRegion: FirstTimeFixRow[]
  byJobType: FirstTimeFixRow[]
}

export function getFirstTimeFix(from: string, to: string): Promise<FirstTimeFixReport> {
  return request<FirstTimeFixReport>(`/api/reports/first-time-fix${query({ from, to })}`)
}
