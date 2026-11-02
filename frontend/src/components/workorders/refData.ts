import { request } from '../../api/client'

// Reference data used by the work-order pages. Parts have no client in api/reference.ts yet.
export interface PartRef {
  id: number
  sku: string
  name: string
  stockQty: number
  unitCost: number
  reorderLevel: number
}

export function listParts(): Promise<PartRef[]> {
  return request<PartRef[]>('/api/parts')
}
