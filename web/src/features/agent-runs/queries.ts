import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as agentRuns from './api'
import { workingStatuses, type AgentRun, type DecisionType } from './types'

/** How often a run is re-read while the agent is still working. A whole run takes about a minute. */
export const POLL_INTERVAL_MS = 2500

export const agentRunKeys = {
  all: ['agent-runs'] as const,
  cases: (query?: agentRuns.CaseQuery) => ['agent-runs', 'cases', query ?? {}] as const,
  case: (id: string) => ['agent-runs', 'case', id] as const,
  run: (id: string) => ['agent-runs', 'run', id] as const,
  events: (id: string, status?: string) => ['agent-runs', 'events', id, status ?? ''] as const,
}

const isWorking = (run: AgentRun | undefined) => run !== undefined && workingStatuses.includes(run.status)

export const useCaseQueue = (query: agentRuns.CaseQuery) =>
  useQuery({ queryKey: agentRunKeys.cases(query), queryFn: () => agentRuns.listCases(query) })

export const useCase = (id: string | undefined) =>
  useQuery({ queryKey: agentRunKeys.case(id ?? 'none'), queryFn: () => agentRuns.getCase(id!), enabled: id !== undefined })

/**
 * Polls while the agent is working and stops once the run waits for a human or has ended — there
 * is nothing left to change by itself, so polling on would only cost requests.
 */
export const useRun = (id: string) =>
  useQuery({
    queryKey: agentRunKeys.run(id),
    queryFn: () => agentRuns.getRun(id),
    refetchInterval: (query) => (isWorking(query.state.data) ? POLL_INTERVAL_MS : false),
  })

/**
 * Keyed on the run's status as well as its id: when the run leaves the working states and polling
 * stops, the new key fetches once more, so the timeline's last events are never missed.
 */
export const useRunEvents = (id: string, status: AgentRun['status'] | undefined) =>
  useQuery({
    queryKey: agentRunKeys.events(id, status),
    queryFn: () => agentRuns.listRunEvents(id),
    enabled: status !== undefined,
    placeholderData: (previous) => previous,
    refetchInterval: status !== undefined && workingStatuses.includes(status) ? POLL_INTERVAL_MS : false,
  })

export const useDecide = (runId: string) => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ decision, reason, idempotencyKey }: { decision: DecisionType; reason?: string; idempotencyKey: string }) =>
      agentRuns.decide(runId, { decision, reason }, idempotencyKey),
    // A decision changes the run, its timeline, its case and the queue.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: agentRunKeys.all }),
  })
}
