import { Link, useParams } from 'react-router'
import { Panel, PageHeader, PanelHeader } from '@/components/layout/PageHeader'
import { Leaf } from '@/components/icons'
import { Alert } from '@/components/ui/alert'
import { AsyncBoundary } from '@/components/ui/AsyncBoundary'
import { Spinner } from '@/components/ui/Spinner'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { caseStatusTone, runStatusTone, severityTone } from '@/components/ui/status-tones'
import { useCurrentUser } from '@/features/auth/auth-store'
import { can } from '@/features/auth/policies'
import { formatDateTime } from '@/lib/dates'
import { CasePhotos } from './CasePhotos'
import { DecisionPanel } from './DecisionPanel'
import { localDate } from './format'
import { PrescriptionCard, ProposalCard } from './ProposalCard'
import { useCase, useRun, useRunEvents } from './queries'
import { RunSteps } from './RunSteps'
import { RunTimeline } from './RunTimeline'
import { TriageCard } from './TriageCard'
import { VerdictCard } from './VerdictCard'
import { caseStatusLabels, runStatusLabels, workingStatuses, type AgentRunEvent, type CaseDetail } from './types'

/** The agronomist's latest "send back to the agent", if there was one: when, and what they asked for. */
function latestRevisionRequest(events: AgentRunEvent[]): { at: string; guidance: string | null } | null {
  const revise = [...events]
    .filter((e) => e.eventType === 'ApprovalDecided' && e.payload?.decision === 'Revise')
    .sort((a, b) => a.occurredAt.localeCompare(b.occurredAt))
    .at(-1)
  if (!revise) return null
  return { at: revise.occurredAt, guidance: typeof revise.payload?.reason === 'string' ? revise.payload.reason : null }
}

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
  // After "Request revision" the old proposal and its verdict stay on the run until the agents
  // replace them; while they work, they are out of date and are not shown.
  const revision = latestRevisionRequest(events.data?.items ?? [])
  const revising = working && revision !== null
  const ended = run.data?.status === 'Failed' || run.data?.status === 'Rejected' || run.data?.status === 'TimedOut'

  return (
    <div className="space-y-4 pb-4">
      <AsyncBoundary isPending={run.isPending} error={run.error} onRetry={run.refetch} label="Loading the run">
        {run.data && (
          <>
            <PageHeader
              title={
                <>
                  Case{' '}
                  <Link to={`/cases/${run.data.caseId}`} className="rounded-sm text-brand-700 underline decoration-brand-200 underline-offset-4 hover:decoration-brand-600">
                    {run.data.caseReferenceNo}
                  </Link>
                </>
              }
              meta={<StatusBadge label={runStatusLabels[run.data.status]} tone={runStatusTone[run.data.status]} className="text-sm" />}
              description={run.data.objective}
              actions={
                working ? (
                  <span className="flex items-center gap-2 rounded-full bg-info-50 px-3 py-1.5 text-sm font-medium text-info-800 ring-1 ring-info-200">
                    <Spinner label="Agent working" className="size-4" /> Updating live
                  </span>
                ) : undefined
              }
              className="pb-2"
            />

            {revising && (
              <Alert tone="info" title="Sent back to the agents">
                {revision.guidance ? <>Your guidance: “{revision.guidance}”. </> : null}
                The four agents are drafting a new proposal, usually in about a minute, and the safety rules will check it again. This page updates by itself.
              </Alert>
            )}

            {run.data.status === 'PendingApproval' && revision && (
              <Alert tone="success" title="Revised proposal">
                Drafted again after your revision request of {formatDateTime(revision.at)}
                {revision.guidance ? <>: “{revision.guidance}”</> : null}. Check the new proposal and the safety rules before deciding.
              </Alert>
            )}

            {run.data.status === 'Escalated' && (
              <Alert tone="warning" title="Handed to an agronomist">
                {run.data.failureReason} The case is in the manual review queue; no treatment was drafted.
              </Alert>
            )}

            {ended && run.data.failureReason && (
              <Alert tone={run.data.status === 'Rejected' ? 'warning' : 'error'} title={`Run ${runStatusLabels[run.data.status].toLowerCase()}`}>
                {run.data.failureReason} The case has gone to manual review.
              </Alert>
            )}

            {/*
              Evidence on the left (the case, the triage, the safety report); on the right what is
              being asked for and the decision, then how the agents got there.
            */}
            <div className="grid items-start gap-4 xl:grid-cols-12">
              <div className="space-y-4 xl:col-span-8">
                {cropCase.data && <CaseContext detail={cropCase.data} />}
                {run.data.triage && <TriageCard triage={run.data.triage} />}
                {run.data.verdict && !revising && <VerdictCard verdict={run.data.verdict} review={run.data.safetyReview} />}
              </div>
              <div className="space-y-4 xl:col-span-4">
                {run.data.prescription && <PrescriptionCard prescription={run.data.prescription} />}
                {run.data.proposal && !revising && <ProposalCard proposal={run.data.proposal} productName={run.data.proposedProductName} productUnit={run.data.proposedProductUnit} />}
                {run.data.status === 'PendingApproval' && (
                  <DecisionPanel run={run.data} canDecide={can(user?.role, 'CanApprovePrescriptions')} />
                )}
                <RunSteps run={run.data} />
              </div>
            </div>

            <RunTimeline events={events.data?.items ?? []} loading={events.isPending} />
          </>
        )}
      </AsyncBoundary>
    </div>
  )
}

/** The case as the farmer reported it. The note is untrusted input and is shown as plain text. */
function CaseContext({ detail }: { detail: CaseDetail }) {
  return (
    <Panel aria-labelledby="case-heading">
      <PanelHeader
        id="case-heading"
        title="The case"
        description="As the farmer reported it from the field."
        aside={
          <div className="flex gap-2">
            <StatusBadge label={detail.severity} tone={severityTone[detail.severity]} />
            <StatusBadge label={caseStatusLabels[detail.status]} tone={caseStatusTone[detail.status]} />
          </div>
        }
      />

      <dl className="grid grid-cols-2 gap-px overflow-hidden rounded-lg border border-border-subtle bg-border-subtle text-sm sm:grid-cols-4">
        <Fact label="Farmer" value={detail.farmerName} />
        <Fact label="Plot" value={`${detail.plotCode} · ${detail.plotAreaHectares} ha`} />
        <Fact label="Crop" value={`${detail.cropName} (${detail.stage})`} />
        <Fact label="Reported" value={localDate(detail.createdAt)} />
      </dl>

      <div className="mt-3 grid gap-4 md:grid-cols-[1fr_auto]">
        <div className="space-y-3">
          <div>
            <h3 className="text-sm font-semibold text-stone-800">Symptoms</h3>
            <ul className="mt-1.5 flex flex-wrap gap-1.5">
              {detail.symptoms.map((symptom) => (
                <li key={symptom.code} className="inline-flex items-center gap-1 rounded-full bg-earth-50 px-2.5 py-1 text-xs font-medium text-earth-700 ring-1 ring-earth-200 ring-inset">
                  <Leaf size={14} className="text-earth-500" />
                  {symptom.label}
                </li>
              ))}
            </ul>
          </div>

          {detail.farmerNote && (
            <figure>
              <figcaption className="text-sm font-semibold text-stone-800">
                Farmer's note <span className="font-normal text-stone-600">(as typed — not verified)</span>
              </figcaption>
              <blockquote className="mt-1 border-l-2 border-earth-300 pl-3 text-sm break-words whitespace-pre-wrap text-stone-800">
                {detail.farmerNote}
              </blockquote>
            </figure>
          )}
        </div>
        <CasePhotos caseId={detail.id} photos={detail.photos ?? []} />
      </div>
    </Panel>
  )
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div className="bg-surface-card px-3 py-2">
      <dt className="text-xs text-stone-600">{label}</dt>
      <dd className="mt-0.5 font-semibold text-stone-900">{value}</dd>
    </div>
  )
}
