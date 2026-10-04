import { api } from '@/lib/api'
import { queryString } from '@/lib/query-string'
import type { PagedResult } from '@/features/registry/types'
import type { AgentRun, AgentRunEvent, CaseDetail, CaseSeverity, CaseStatus, CaseSummary, DecisionResult, DecisionType } from './types'

export interface CaseQuery {
  page?: number
  pageSize?: number
  sortBy?: string
  desc?: boolean
  status?: CaseStatus
  severity?: CaseSeverity
  cropId?: string
  search?: string
}

export const listCases = (query: CaseQuery) => api<PagedResult<CaseSummary>>(`/api/cases${queryString(query)}`)
export const getCase = (id: string) => api<CaseDetail>(`/api/cases/${id}`)

/** Only Closed and AwaitingManualReview may be set by hand; the API explains any refusal (422). */
export const updateCaseStatus = (id: string, status: 'Closed' | 'AwaitingManualReview') =>
  api<CaseDetail>(`/api/cases/${id}/status`, { method: 'PATCH', json: { status } })

/** Through the authenticated client: an <img src> could not carry the bearer token. */
export const getCasePhoto = (caseId: string, photoId: string) =>
  api<Blob>(`/api/cases/${caseId}/photos/${photoId}`, { responseType: 'blob', headers: { Accept: 'image/*' } })

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
