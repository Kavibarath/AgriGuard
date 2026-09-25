import { api } from '@/lib/api'
import type { Crop, CropCycle, District, Farm, PagedResult, Plot } from './types'

export interface FarmQuery {
  page?: number
  pageSize?: number
  sortBy?: string
  desc?: boolean
  search?: string
  districtId?: string
}

export interface PlotQuery {
  page?: number
  pageSize?: number
  sortBy?: string
  desc?: boolean
  farmId?: string
  cropId?: string
  status?: string
  search?: string
}

/** Drops empty values so the URL carries only what was actually chosen. */
function queryString(query: object): string {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') continue
    params.set(key, String(value))
  }
  const serialised = params.toString()
  return serialised ? `?${serialised}` : ''
}

export const listFarms = (query: FarmQuery) => api<PagedResult<Farm>>(`/api/farms${queryString(query)}`)
export const getFarm = (id: string) => api<Farm>(`/api/farms/${id}`)

export interface FarmInput {
  name: string
  village?: string | null
  districtId: string
}

export const createFarm = (input: FarmInput) => api<Farm>('/api/farms', { method: 'POST', json: input })
export const updateFarm = (id: string, input: FarmInput) => api<Farm>(`/api/farms/${id}`, { method: 'PUT', json: input })
export const deleteFarm = (id: string) => api<void>(`/api/farms/${id}`, { method: 'DELETE' })

export const listPlots = (query: PlotQuery) => api<PagedResult<Plot>>(`/api/plots${queryString(query)}`)
export const getPlot = (id: string) => api<Plot>(`/api/plots/${id}`)

export interface PlotInput {
  plotCode: string
  name?: string | null
  areaHectares: number
  latitude: number
  longitude: number
  soilType: string
}

export const createPlot = (input: PlotInput & { farmId: string }) =>
  api<Plot>('/api/plots', { method: 'POST', json: input })

export const updatePlot = (id: string, input: PlotInput & { status: string }) =>
  api<Plot>(`/api/plots/${id}`, { method: 'PUT', json: input })

export const getCropCycle = (id: string) => api<CropCycle>(`/api/crop-cycles/${id}`)

export const createCropCycle = (input: {
  plotId: string
  cropId: string
  sownDate: string
  plannedHarvestDate?: string | null
}) => api<CropCycle>('/api/crop-cycles', { method: 'POST', json: input })

export const advanceStage = (id: string, input: { toStage: string; reachedOn?: string | null; note?: string | null }) =>
  api<CropCycle>(`/api/crop-cycles/${id}/advance-stage`, { method: 'POST', json: input })

export const listDistricts = () => api<District[]>('/api/districts')
export const listCrops = () => api<Crop[]>('/api/crops')
