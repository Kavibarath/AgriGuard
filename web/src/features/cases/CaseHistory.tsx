import type { ReactNode } from 'react'
import { Panel, PanelHeader } from '@/components/layout/PageHeader'
import { AlertTriangle, CheckCircle, Clock, DashedCircle, XCircle } from '@/components/icons'
import type { Tone } from '@/components/ui/StatusBadge'
import { caseStatusTone, runStatusTone } from '@/components/ui/status-tones'
import { formatDateTime } from '@/lib/dates'
import { cn } from '@/lib/utils'
import { caseStatusLabels, runStatusLabels, type CaseDetail } from '@/features/agent-runs/types'

interface Moment {
  key: string
  at: string | null
  title: string
  detail?: string
  tone: Tone
  current?: boolean
}

const marks: Record<Tone, { Icon: (p: { size?: number; className?: string }) => ReactNode; ink: string }> = {
  done: { Icon: CheckCircle, ink: 'text-success' },
  active: { Icon: Clock, ink: 'text-info' },
  warning: { Icon: AlertTriangle, ink: 'text-warning' },
  danger: { Icon: XCircle, ink: 'text-danger' },
  neutral: { Icon: DashedCircle, ink: 'text-stone-600' },
}

/** The case's story from the data it carries: reported, received, each run and how it ended, and where it stands now. */
function momentsOf(detail: CaseDetail): Moment[] {
  const moments: Moment[] = []
  if (detail.capturedAt) moments.push({ key: 'captured', at: detail.capturedAt, title: 'Reported on the phone', detail: 'Saved offline, sent later', tone: 'done' })
  moments.push({ key: 'received', at: detail.createdAt, title: detail.capturedAt ? 'Reached AgriGuard' : 'Reported from the field', tone: 'done' })

  for (const run of [...detail.agentRuns].reverse()) {
    moments.push({ key: `${run.id}-start`, at: run.createdAt, title: 'Agents asked for advice', tone: 'done' })
    if (run.completedAt) {
      moments.push({ key: `${run.id}-end`, at: run.completedAt, title: `Run ${runStatusLabels[run.status].toLowerCase()}`, detail: run.failureReason ?? undefined, tone: runStatusTone[run.status] ?? 'neutral' })
    }
  }

  moments.push({
    key: 'now',
    at: null,
    title: `Now: ${caseStatusLabels[detail.status]}`,
    detail:
      detail.status === 'PendingApproval'
        ? 'An agronomist is reviewing the proposal. Nothing is issued until they approve.'
        : detail.status === 'AwaitingManualReview'
          ? 'A person will look at this case before anything else happens.'
          : undefined,
    tone: caseStatusTone[detail.status] ?? 'neutral',
    current: true,
  })
  return moments
}

/** A vertical timeline of the case, oldest at the top; each step's shape and word carry its outcome. */
export function CaseHistory({ detail }: { detail: CaseDetail }) {
  const moments = momentsOf(detail)
  return (
    <Panel aria-labelledby="history-heading">
      <PanelHeader id="history-heading" title="Case history" />
      <ol className="relative">
        {moments.map((m, index) => {
          const { Icon, ink } = marks[m.tone]
          return (
            <li key={m.key} className="relative pb-3 pl-8 last:pb-0">
              {index < moments.length - 1 && <span aria-hidden="true" className="absolute top-6 bottom-0 left-[9px] w-px bg-border-strong" />}
              <span className={cn('absolute top-0.5 left-0 flex size-5 items-center justify-center rounded-full bg-surface-card', ink)}>
                <Icon size={20} />
              </span>
              <p className={cn('text-sm', m.current ? 'font-semibold text-stone-900' : 'font-medium text-stone-800')}>{m.title}</p>
              {m.at && <p className="text-xs text-stone-600 tabular-nums">{formatDateTime(m.at)}</p>}
              {m.detail && <p className={cn('mt-0.5 text-sm', m.current ? 'text-stone-800' : 'text-stone-600')}>{m.detail}</p>}
            </li>
          )
        })}
      </ol>
    </Panel>
  )
}
