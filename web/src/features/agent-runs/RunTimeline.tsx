import { Panel, PanelHeader } from '@/components/layout/PageHeader'
import { AlertTriangle, StopOctagon } from '@/components/icons'
import { cn } from '@/lib/utils'
import { AgentGlyph } from './agent-identity'
import { agentIdentity } from './agents'
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
  StockHeld: 'Stock held',
  StockReleased: 'Stock released',
  SafetyReviewed: 'Safety review',
  TriageDecided: 'Triage',
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
    case 'StockHeld':
      return [
        `${text(p.packs)} pack(s) off sale until ${text(p.expiresAt).slice(0, 16).replace('T', ' ')} UTC`,
        Array.isArray(p.batches) ? `from ${p.batches.join(', ')}` : '',
      ]
        .filter(Boolean)
        .join(' · ')
    case 'StockReleased':
      return `Back on sale: ${text(p.reason)}`
    case 'TriageDecided': {
      const route = p.route === 'TREAT' ? 'Treat with a product' : 'Hand to an agronomist'
      const by = p.decided_by === 'rules' ? 'a safety rule' : 'the Coordinator'
      return [`${route}, decided by ${by}: ${text(p.reason)}`, p.overridden === true ? 'the model had said treat' : '']
        .filter(Boolean)
        .join(' · ')
    }
    case 'SafetyReviewed': {
      if (p.accepted === true && p.review && typeof p.review === 'object') return text((p.review as { explanation?: unknown }).explanation)
      return `Set aside: ${text(p.reason)}`
    }
    default:
      return ''
  }
}

/**
 * The auditable timeline (§9.6): every status change, tool call, retry and verdict, in order, as a
 * ledger — time, agent, what happened, how long it took. Rows that need attention carry an icon
 * and a spoken "Attention" as well as their tint.
 */
export function RunTimeline({ events, loading = false }: { events: AgentRunEvent[]; loading?: boolean }) {
  return (
    <Panel aria-labelledby="timeline-heading">
      <PanelHeader
        id="timeline-heading"
        title="Timeline"
        description="Every status change, tool call, retry and verdict, oldest first."
        aside={<span className="text-xs text-stone-600">Times in UTC</span>}
      />

      {loading ? (
        <p className="text-sm text-stone-600">Loading the timeline…</p>
      ) : events.length === 0 ? (
        <p className="text-sm text-stone-600">No events yet.</p>
      ) : (
        <ol className="divide-y divide-border-subtle text-sm">
          {events.map((event) => {
            const flag = attention[event.eventType]
            const line = detail(event)
            return (
              <li
                key={event.id}
                className={cn(
                  'grid grid-cols-[4.25rem_1fr] gap-x-3 px-2 py-2 sm:grid-cols-[4.25rem_8.5rem_1fr_4.5rem]',
                  flag === 'warning' && 'bg-warning-50',
                  flag === 'error' && 'bg-danger-50',
                )}
              >
                <time dateTime={event.occurredAt} className="text-xs leading-5 text-stone-600 tabular-nums">
                  {event.occurredAt.slice(11, 19)}
                </time>
                <span className="hidden sm:block">
                  {event.agentRole && (
                    <span className={cn('inline-flex items-center gap-1.5 text-xs font-semibold', agentIdentity[event.agentRole].ink)}>
                      <AgentGlyph role={event.agentRole} size="sm" />
                      {event.agentRole}
                    </span>
                  )}
                </span>
                <div className="min-w-0">
                  <span
                    className={cn(
                      'inline-flex items-center gap-1 font-medium',
                      flag === 'error' ? 'text-danger-800' : flag ? 'text-warning-800' : 'text-stone-900',
                    )}
                  >
                    {flag === 'error' && <StopOctagon size={16} className="shrink-0 text-danger" />}
                    {flag === 'warning' && <AlertTriangle size={16} className="shrink-0 text-warning" />}
                    {flag && <span className="sr-only">Attention: </span>}
                    {labels[event.eventType]}
                  </span>
                  {event.agentRole && <span className="text-stone-600 sm:hidden"> · {event.agentRole}</span>}
                  {line && <p className="text-xs break-words text-stone-600">{line}</p>}
                </div>
                <span className="hidden text-right text-xs leading-5 text-stone-600 tabular-nums sm:block">
                  {event.durationMs !== null ? formatDuration(event.durationMs) : ''}
                </span>
              </li>
            )
          })}
        </ol>
      )}
    </Panel>
  )
}
