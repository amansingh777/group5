import { query, request } from './client'
import type { Customer, Paged } from './types'

export function listCustomers(params: { search?: string; page?: number }): Promise<Paged<Customer>> {
  return request<Paged<Customer>>(`/api/customers${query({ search: params.search, page: params.page })}`)
}
