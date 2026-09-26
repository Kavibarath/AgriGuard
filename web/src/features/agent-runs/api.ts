import { api } from '@/lib/api'
import type { PagedResult } from '@/features/registry/types'
import type { AgentRun, AgentRunEvent, CaseDetail, CaseStatus, CaseSummary, DecisionResult, DecisionType } from './types'

export interface CaseQuery {
  page?: number
  pageSize?: number
  sortBy?: string
  desc?: boolean
  status?: CaseStatus
  search?: string
}

function queryString(query: object): string {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') continue
    params.set(key, String(value))
  }
  const serialised = params.toString()
  return serialised ? `?${serialised}` : ''
}

export const listCases = (query: CaseQuery) => api<PagedResult<CaseSummary>>(`/api/cases${queryString(query)}`)
export const getCase = (id: string) => api<CaseDetail>(`/api/cases/${id}`)

export const getRun = (id: string) => api<AgentRun>(`/api/agent-runs/${id}`)

/** A run's whole timeline fits one page of 100 (a typical run writes about 20 events). */
export const listRunEvents = (id: string) => api<PagedResult<AgentRunEvent>>(`/api/agent-runs/${id}/events?pageSize=100`)

/**
 * The human gate. `idempotencyKey` must be the same for every retry of one decision: the API then
 * returns the original result instead of deciding twice.
 */
export const decide = (runId: string, body: { decision: DecisionType; reason?: string }, idempotencyKey: string) =>
  api<DecisionResult>(`/api/agent-runs/${runId}/decision`, {
    method: 'POST',
    json: body,
    headers: { 'Idempotency-Key': idempotencyKey },
  })
