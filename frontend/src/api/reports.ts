import { query, request } from './client'
import type { UtilisationRow } from './types'

export function getUtilisation(from: string, to: string): Promise<UtilisationRow[]> {
  return request<UtilisationRow[]>(`/api/reports/engineer-utilisation${query({ from, to })}`)
}
