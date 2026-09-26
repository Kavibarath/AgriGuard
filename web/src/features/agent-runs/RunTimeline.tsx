import { cn } from '@/lib/utils'
import { formatDuration } from './format'
import type { AgentEventType, AgentRunEvent } from './types'

const labels: Record<AgentEventType, string> = {
  StatusChanged: 'Status changed',
  PlanCreated: 'Plan created',
  StepStarted: 'Step started',
  StepCompleted: 'Step completed',
  ToolCalled: 'Tool called',
  ToolFailed: 'Tool failed',
  LlmCalled: 'Model called',
  ValidationResult: 'Validation result',
  RetryAttempted: 'Retry',
  InjectionFlagged: 'Suspicious farmer note',
  ApprovalRequested: 'Approval requested',
  ApprovalDecided: 'Decision recorded',
  RunFailed: 'Run failed',
  RunCompleted: 'Run finished',
}

/** Events that need the reader's attention are marked, and never by colour alone. */
const attention: Partial<Record<AgentEventType, 'warning' | 'error'>> = {
  ToolFailed: 'warning',
  InjectionFlagged: 'warning',
  RunFailed: 'error',
}

const text = (value: unknown) => (typeof value === 'string' || typeof value === 'number' ? String(value) : '')

/** One line of human-readable detail per event type; the raw payload stays in the API. */
function detail(event: AgentRunEvent): string {
  const p = event.payload ?? {}
  switch (event.eventType) {
    case 'StatusChanged':
      return `${text(p.from) || 'start'} → ${text(p.to)}`
    case 'StepStarted':
      return text(p.goal)
    case 'PlanCreated':
      return Array.isArray(p.steps) ? `${p.steps.length} steps` : ''
    case 'ToolCalled':
      return event.toolName ?? ''
    case 'ToolFailed':
      return `${event.toolName ?? ''} — attempt ${text(p.attempt)}: ${text(p.error)}`
    case 'ValidationResult':
    case 'ApprovalRequested':
      return text(p.summary)
    case 'InjectionFlagged':
      return Array.isArray(p.patterns) ? `Instruction-like text in the note: ${p.patterns.join(', ')}` : ''
    case 'ApprovalDecided':
      return [text(p.decision), text(p.prescriptionNo), text(p.reason)].filter(Boolean).join(' · ')
    case 'RunFailed':
      return text(p.reason)
    case 'RunCompleted':
      return text(p.outcome)
    default:
      return ''
  }
}

/** The auditable timeline (§9.6): every status change, tool call, retry and verdict, in order. */
export function RunTimeline({ events, loading = false }: { events: AgentRunEvent[]; loading?: boolean }) {
  return (
    <section aria-labelledby="timeline-heading" className="space-y-3 rounded-lg border border-stone-200 bg-white p-4">
      <header className="flex items-baseline justify-between gap-2">
        <h2 id="timeline-heading" className="font-medium text-stone-900">
          Timeline
        </h2>
        <span className="text-xs text-stone-500">Times in UTC</span>
      </header>

      {loading ? (
        <p className="text-sm text-stone-600">Loading the timeline…</p>
      ) : events.length === 0 ? (
        <p className="text-sm text-stone-600">No events yet.</p>
      ) : (
        <ol className="space-y-1.5 text-sm">
          {events.map((event) => {
            const flag = attention[event.eventType]
            return (
              <li
                key={event.id}
                className={cn(
                  'grid grid-cols-[4.5rem_1fr] gap-x-2 rounded px-1 py-0.5',
                  flag === 'warning' && 'bg-amber-50',
                  flag === 'error' && 'bg-red-50',
                )}
              >
                <time dateTime={event.occurredAt} className="tabular-nums text-xs leading-5 text-stone-500">
                  {event.occurredAt.slice(11, 19)}
                </time>
                <div>
                  <span className={cn('font-medium', flag === 'error' ? 'text-red-800' : flag ? 'text-amber-900' : 'text-stone-900')}>
                    {flag && <span className="sr-only">Attention: </span>}
                    {labels[event.eventType]}
                  </span>
                  {event.agentRole && <span className="text-stone-500"> · {event.agentRole}</span>}
                  {event.durationMs !== null && (
                    <span className="text-xs tabular-nums text-stone-500"> · {formatDuration(event.durationMs)}</span>
                  )}
                  {detail(event) && <p className="text-xs text-stone-600">{detail(event)}</p>}
                </div>
              </li>
            )
          })}
        </ol>
      )}
    </section>
  )
}
