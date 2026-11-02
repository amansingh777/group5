import { request } from './client'

export interface Region {
  id: number
  code: string
  name: string
}

export interface Skill {
  id: number
  code: string
  name: string
}

export interface JobType {
  id: number
  code: string
  name: string
  defaultMinutes: number
  requiredSkillId: number
}

export function getRegions(): Promise<Region[]> {
  return request<Region[]>('/api/regions')
}

export function getSkills(): Promise<Skill[]> {
  return request<Skill[]>('/api/skills')
}

export function getJobTypes(): Promise<JobType[]> {
  return request<JobType[]>('/api/job-types')
}
