import { api } from '@/lib/api'
import { queryString } from '@/lib/query-string'
import type { PagedResult } from '@/features/registry/types'
import type { CollectionBooking, CollectionCentre, CollectionSlot, ForecastVsActualReport, HarvestForecast, SprayWindow } from './types'

interface PageQuery {
  page?: number
  pageSize?: number
  sortBy?: string
  desc?: boolean
}

export interface ForecastQuery extends PageQuery {
  districtId?: string
  cropId?: string
  from?: string
  to?: string
}

export interface ReportQuery {
  districtId?: string
  cropId?: string
  from?: string
  to?: string
}

export interface SlotQuery extends PageQuery {
  from?: string
  to?: string
  centreId?: string
  districtId?: string
}

export interface BookingQuery extends PageQuery {
  from?: string
  centreId?: string
  status?: string
}

export const listForecasts = (query: ForecastQuery) =>
  api<PagedResult<HarvestForecast>>(`/api/harvest-forecasts${queryString(query)}`)

export const recordActualYield = (id: string, actualYieldKg: number) =>
  api<HarvestForecast>(`/api/harvest-forecasts/${id}/actual`, { method: 'PUT', json: { actualYieldKg } })

export const getForecastVsActual = (query: ReportQuery) =>
  api<ForecastVsActualReport>(`/api/reports/harvest-forecast-vs-actual${queryString(query)}`)

export const listCentres = (districtId?: string) =>
  api<CollectionCentre[]>(`/api/collection-centres${queryString({ districtId })}`)

export const listSlots = (query: SlotQuery) => api<PagedResult<CollectionSlot>>(`/api/collection-slots${queryString(query)}`)

export interface NewSlot {
  centreId: string
  slotDate: string
  slotIndex: number
  startTime: string
  endTime: string
  capacityKg: number
}

export const createSlot = (input: NewSlot) => api<CollectionSlot>('/api/collection-slots', { method: 'POST', json: input })

export const listBookings = (query: BookingQuery) =>
  api<PagedResult<CollectionBooking>>(`/api/collection-bookings${queryString(query)}`)

/** Co-op staff at the centre: CheckedIn, Completed (with the weight) or NoShow. Repeating it is harmless. */
export const recordBooking = (id: string, status: 'CheckedIn' | 'Completed' | 'NoShow', actualQuantityKg?: number) =>
  api<CollectionBooking>(`/api/collection-bookings/${id}/record`, {
    method: 'POST',
    json: actualQuantityKg === undefined ? { status } : { status, actualQuantityKg },
  })

/** The coming days' spray suitability at a plot, from the same weather the V8 rule uses. */
export const getSprayWindow = (plotId: string, days = 7) =>
  api<SprayWindow>(`/api/weather/spray-window${queryString({ plotId, days })}`)
