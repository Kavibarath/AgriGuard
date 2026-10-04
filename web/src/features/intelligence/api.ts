import { api } from '@/lib/api'
import { queryString } from '@/lib/query-string'
import type { OutbreakSignal } from './types'

export interface OutbreakQuery {
  cropId?: string
  districtId?: string
  days?: number
}

export const getOutbreakSignal = (query: OutbreakQuery) =>
  api<OutbreakSignal>(`/api/intelligence/outbreak-signal${queryString(query)}`)
