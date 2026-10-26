export interface Paged<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

export interface Job {
  id: number
  jobRef: string
  customerName: string
  siteName: string
  engineerName: string | null
  priority: string
  status: string
  scheduledStart: string | null
  outcome: string | null
}

export interface Engineer {
  id: number
  employeeNo: string
  fullName: string
  regionId: number
  grade: string
  active: boolean
  skillIds: number[]
}

export interface Customer {
  id: number
  accountRef: string
  name: string
  customerType: string
  city: string
  postcode: string
  regionId: number
}

export interface UtilisationRow {
  engineerId: number
  engineerName: string
  regionId: number
  shiftsRostered: number
  hoursWorked: number
  hoursAvailable: number
  utilisationPct: number
}

export interface ImportResult {
  batchId: number
  totalRows: number
  createdRows: number
  rejectedRows: number
  messages: string[]
}
