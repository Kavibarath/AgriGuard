import { Link, useParams } from 'react-router'
import { Alert } from '@/components/ui/alert'
import { AsyncBoundary } from '@/components/ui/AsyncBoundary'
import { Spinner } from '@/components/ui/Spinner'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { caseStatusTone, runStatusTone, severityTone } from '@/components/ui/status-tones'
import { useCurrentUser } from '@/features/auth/auth-store'
import { can } from '@/features/auth/policies'
import { DecisionPanel } from './DecisionPanel'
import { PrescriptionCard, ProposalCard } from './ProposalCard'
import { useCase, useRun, useRunEvents } from './queries'
import { RunSteps } from './RunSteps'
import { RunTimeline } from './RunTimeline'
import { VerdictCard } from './VerdictCard'
import { caseStatusLabels, runStatusLabels, workingStatuses, type CaseDetail } from './types'

/**
 * The agent-run console (§7, /agent-runs/:runId): everything the agronomist needs to trust or
 * refuse a proposal — the case as reported, what each agent did, the safety verdict rule by rule,
 * and the timeline — next to the decision itself. It updates live while the agent works.
 */
export function AgentRunPage() {
  const { runId = '' } = useParams()
  const user = useCurrentUser()
  const run = useRun(runId)
  const cropCase = useCase(run.data?.caseId)
  const events = useRunEvents(runId, run.data?.status)

  const working = run.data !== undefined && workingStatuses.includes(run.data.status)
  const ended = run.data?.status === 'Failed' || run.data?.status === 'Rejected' || run.data?.status === 'TimedOut'

  return (
    <main className="mx-auto max-w-6xl space-y-6 p-6">
      <Link to="/agent-runs" className="text-sm text-stone-500 hover:text-stone-800">
        ← Agent runs
      </Link>

      <AsyncBoundary isPending={run.isPending} error={run.error} onRetry={run.refetch} label="Loading the run">
        {run.data && (
          <>
            <header className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h1 className="text-2xl font-semibold text-stone-900">Case {run.data.caseReferenceNo}</h1>
                <p className="text-sm text-stone-600">{run.data.objective}</p>
              </div>
              <div className="flex items-center gap-3">
                {working && (
                  <span className="flex items-center gap-2 text-sm text-stone-600">
                    <Spinner label="Agent working" /> Updating live
                  </span>
                )}
                <StatusBadge label={runStatusLabels[run.data.status]} tone={runStatusTone[run.data.status]} className="text-sm" />
              </div>
            </header>

            {ended && run.data.failureReason && (
              <Alert tone={run.data.status === 'Rejected' ? 'warning' : 'error'} title={`Run ${runStatusLabels[run.data.status].toLowerCase()}`}>
                {run.data.failureReason} The case has gone to manual review.
              </Alert>
            )}

            <div className="grid gap-6 lg:grid-cols-[3fr_2fr]">
              <div className="space-y-6">
                {cropCase.data && <CaseContext detail={cropCase.data} />}
                {run.data.prescription && <PrescriptionCard prescription={run.data.prescription} />}
                {run.data.status === 'PendingApproval' && (
                  <DecisionPanel run={run.data} canDecide={can(user?.role, 'CanApprovePrescriptions')} />
                )}
                {run.data.proposal && <ProposalCard proposal={run.data.proposal} productName={run.data.proposedProductName} />}
                {run.data.verdict && <VerdictCard verdict={run.data.verdict} />}
              </div>
              <div className="space-y-6">
                <RunSteps run={run.data} />
                <RunTimeline events={events.data?.items ?? []} loading={events.isPending} />
              </div>
            </div>
          </>
        )}
      </AsyncBoundary>
    </main>
  )
}

/** The case as the farmer reported it. The note is untrusted input and is shown as plain text. */
function CaseContext({ detail }: { detail: CaseDetail }) {
  return (
    <section aria-labelledby="case-heading" className="space-y-3 rounded-lg border border-stone-200 bg-white p-4">
      <header className="flex flex-wrap items-start justify-between gap-2">
        <h2 id="case-heading" className="font-medium text-stone-900">
          The case
        </h2>
        <div className="flex gap-2">
          <StatusBadge label={detail.severity} tone={severityTone[detail.severity]} />
          <StatusBadge label={caseStatusLabels[detail.status]} tone={caseStatusTone[detail.status]} />
        </div>
      </header>

      <dl className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-4">
        <div>
          <dt className="text-stone-500">Farmer</dt>
          <dd className="font-medium text-stone-900">{detail.farmerName}</dd>
        </div>
        <div>
          <dt className="text-stone-500">Plot</dt>
          <dd className="font-medium text-stone-900">
            {detail.plotCode} · {detail.plotAreaHectares} ha
          </dd>
        </div>
        <div>
          <dt className="text-stone-500">Crop</dt>
          <dd className="font-medium text-stone-900">
            {detail.cropName} ({detail.stage})
          </dd>
        </div>
        <div>
          <dt className="text-stone-500">Reported</dt>
          <dd className="font-medium text-stone-900">{detail.createdAt.slice(0, 10)}</dd>
        </div>
      </dl>

      <div>
        <h3 className="text-xs font-medium uppercase tracking-wide text-stone-500">Symptoms</h3>
        <ul className="mt-1.5 flex flex-wrap gap-1.5">
          {detail.symptoms.map((symptom) => (
            <li key={symptom.code} className="rounded-full bg-stone-100 px-2 py-0.5 text-xs text-stone-700">
              {symptom.label}
            </li>
          ))}
        </ul>
      </div>

      {detail.farmerNote && (
        <figure>
          <figcaption className="text-xs font-medium uppercase tracking-wide text-stone-500">
            Farmer's note <span className="normal-case tracking-normal text-stone-400">(as typed — not verified)</span>
          </figcaption>
          <blockquote className="mt-1 whitespace-pre-wrap break-words border-l-2 border-stone-300 pl-3 text-sm text-stone-700">
            {detail.farmerNote}
          </blockquote>
        </figure>
      )}
    </section>
  )
}
