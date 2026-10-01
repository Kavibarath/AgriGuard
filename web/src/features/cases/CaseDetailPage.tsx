import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { Panel, PageHeader, PanelHeader } from '@/components/layout/PageHeader'
import { Camera, Leaf, MapPin, Workflow } from '@/components/icons'
import { Alert } from '@/components/ui/alert'
import { AsyncBoundary } from '@/components/ui/AsyncBoundary'
import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/Modal'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { caseStatusTone, runStatusTone, severityTone } from '@/components/ui/status-tones'
import { TileMap } from '@/components/map/TileMap'
import { userMessage } from '@/lib/api'
import { formatDateTime } from '@/lib/dates'
import { distanceMetres, formatDistance } from '@/lib/geo'
import { useCurrentUser } from '@/features/auth/auth-store'
import { CasePhotos } from '@/features/agent-runs/CasePhotos'
import { useCase, useUpdateCaseStatus } from '@/features/agent-runs/queries'
import { caseStatusLabels, runStatusLabels, type CaseDetail } from '@/features/agent-runs/types'
import { CaseHistory } from './CaseHistory'
import { formatWait, severityPinTone } from './format'

/** Beyond this, the report was probably made from home, or against the wrong plot. */
const FAR_FROM_PLOT_METRES = 1000

/**
 * One case (§7 /cases/:id): what the farmer saw, where they stood, and what the agents and the
 * agronomist have done since. The map puts the phone's position beside the plot's registered
 * centre; a large gap is worth a question before anyone approves a spray for that plot.
 */
export function CaseDetailPage() {
  const { caseId } = useParams()
  const cropCase = useCase(caseId)

  return (
    <div className="space-y-4 pb-4">
      <AsyncBoundary isPending={cropCase.isPending} error={cropCase.error} onRetry={cropCase.refetch} label="Loading the case">
        {cropCase.data && <CaseBody detail={cropCase.data} />}
      </AsyncBoundary>
    </div>
  )
}

function CaseBody({ detail }: { detail: CaseDetail }) {
  const reported = { lat: detail.reportedLatitude, lng: detail.reportedLongitude }
  const plot = { lat: detail.plotLatitude, lng: detail.plotLongitude }
  const gap = distanceMetres(reported, plot)
  const latestRun = detail.agentRuns[0]

  return (
    <>
      <PageHeader
        title={`Case ${detail.referenceNo}`}
        meta={
          <>
            <StatusBadge label={detail.severity} tone={severityTone[detail.severity]} className="text-sm" />
            <StatusBadge label={caseStatusLabels[detail.status]} tone={caseStatusTone[detail.status]} className="text-sm" />
          </>
        }
        description={
          <>
            {detail.farmerName} ·{' '}
            <Link to={`/plots/${detail.plotId}`} className="font-medium text-brand-700 underline decoration-brand-200 underline-offset-2 hover:decoration-brand-600">
              {detail.plotCode}
            </Link>{' '}
            · {detail.cropName} ({detail.stage}) · {detail.districtName}
          </>
        }
        className="pb-2"
      />

      {detail.capturedAt && (
        <Alert tone="info" title="Sent from the phone's offline queue">
          Reported on the phone at {formatDateTime(detail.capturedAt)} and received at {formatDateTime(detail.createdAt)}, after waiting{' '}
          {formatWait(detail.capturedAt, detail.createdAt)} for a connection.
        </Alert>
      )}

      <div className="grid items-start gap-4 xl:grid-cols-12">
        <div className="space-y-4 xl:col-span-7">
          <Panel aria-labelledby="seen-heading">
            <PanelHeader id="seen-heading" title="What the farmer saw" description="Symptoms from the closed checklist, the note as typed, and the leaf photos." />
            <div className="grid gap-4 md:grid-cols-[1fr_16rem]">
              <div className="space-y-3">
                <ul className="flex flex-wrap gap-1.5">
                  {detail.symptoms.map((s) => (
                    <li key={s.code} className="inline-flex items-center gap-1 rounded-full bg-earth-50 px-2.5 py-1 text-sm font-medium text-earth-700 ring-1 ring-earth-200 ring-inset">
                      <Leaf size={14} className="text-earth-500" />
                      {s.label}
                    </li>
                  ))}
                </ul>
                {detail.farmerNote && (
                  <figure>
                    <figcaption className="text-sm font-semibold text-stone-800">
                      Farmer's note <span className="font-normal text-stone-600">(as typed)</span>
                    </figcaption>
                    {/* Untrusted input: rendered as text by React, never as markup. */}
                    <p className="mt-1 border-l-2 border-earth-300 pl-3 text-sm whitespace-pre-wrap text-stone-800">{detail.farmerNote}</p>
                  </figure>
                )}
              </div>
              {detail.photos.length > 0 ? (
                <CasePhotos caseId={detail.id} photos={detail.photos} feature />
              ) : (
                <p className="flex items-center gap-2 self-start rounded-lg bg-surface-sunken p-3 text-sm text-stone-600">
                  <Camera size={18} className="shrink-0 text-stone-600" /> No leaf photo was sent with this report.
                </p>
              )}
            </div>
          </Panel>

          <Panel aria-labelledby="where-heading">
            <PanelHeader id="where-heading" icon={<MapPin />} title="Where it was reported" description="The phone's position beside the plot's registered centre." />
            <TileMap
              label={`Map: where ${detail.referenceNo} was reported, and plot ${detail.plotCode}`}
              height={300}
              pins={[
                { id: 'plot', position: plot, label: `Plot ${detail.plotCode}, registered centre`, tone: 'neutral', variant: 'ring' },
                { id: 'report', position: reported, label: `Where the phone was when ${detail.referenceNo} was reported`, tone: severityPinTone[detail.severity] },
              ]}
            />
            <p className="mt-2 text-xs text-stone-600">
              Filled dot: the phone ({detail.reportedLatitude.toFixed(5)}, {detail.reportedLongitude.toFixed(5)}). Ring: the plot's registered centre.
            </p>
            {gap > FAR_FROM_PLOT_METRES ? (
              <Alert tone="warning" title={`Reported ${formatDistance(gap)} from the plot`} className="mt-2">
                The phone was far from {detail.plotCode} when this was reported. Check with the farmer that it is the right plot before approving a treatment for it.
              </Alert>
            ) : (
              <p className="mt-1 text-sm text-stone-800">
                {gap < 10 ? "Reported at the plot's registered centre." : `Reported ${formatDistance(gap)} from the plot's registered centre.`}
              </p>
            )}
          </Panel>
        </div>

        <div className="space-y-4 xl:col-span-5">
          <CaseActions detail={detail} />

          <CaseHistory detail={detail} />

          <Panel aria-labelledby="runs-heading">
            <PanelHeader id="runs-heading" title="Agent runs" icon={<Workflow />} />
            {detail.agentRuns.length === 0 ? (
              <p className="text-sm text-stone-600">No advice requested yet. The farmer starts a run from the phone with "Get AI advice".</p>
            ) : (
              <ol className="divide-y divide-border-subtle">
                {detail.agentRuns.map((run) => (
                  <li key={run.id} className="py-2 text-sm first:pt-0 last:pb-0">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <Link to={`/agent-runs/${run.id}`} className="font-semibold text-brand-700 hover:underline">
                        Run of {formatDateTime(run.createdAt)}
                      </Link>
                      <StatusBadge label={runStatusLabels[run.status]} tone={runStatusTone[run.status]} />
                    </div>
                    {run.revisionCount > 0 && <p className="mt-1 text-xs text-stone-600">Revised {run.revisionCount}×</p>}
                    {run.failureReason && <p className="mt-1 text-stone-800">{run.failureReason}</p>}
                  </li>
                ))}
              </ol>
            )}
          </Panel>

          {latestRun?.farmerAdvice && (
            <section aria-labelledby="advice-heading" className="rounded-xl border border-brand-200 bg-brand-50 p-4">
              <h2 id="advice-heading" className="font-display text-lg font-semibold text-brand-800">
                Advice the farmer was given
              </h2>
              <ul className="mt-2 space-y-1 text-sm text-stone-800">
                {latestRun.farmerAdvice
                  .split('\n')
                  .filter((line) => line.trim())
                  .map((line) => (
                    <li key={line} className="flex items-start gap-1.5">
                      <Leaf size={16} className="mt-0.5 shrink-0 text-brand-600" />
                      <span>{line}</span>
                    </li>
                  ))}
              </ul>
            </section>
          )}

          <dl className="grid grid-cols-2 gap-px overflow-hidden rounded-xl border border-border-subtle bg-border-subtle text-sm">
            <Fact label="Received" value={formatDateTime(detail.createdAt)} />
            <Fact label="Last change" value={formatDateTime(detail.updatedAt)} />
            {detail.assignedAgronomistName && <Fact label="Agronomist" value={detail.assignedAgronomistName} />}
            {detail.confirmedPathogenCode && <Fact label="Confirmed" value={detail.confirmedPathogenCode} />}
          </dl>
        </div>
      </div>
    </>
  )
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div className="bg-surface-card px-3 py-2">
      <dt className="text-xs text-stone-600">{label}</dt>
      <dd className="mt-0.5 font-medium text-stone-900">{value}</dd>
    </div>
  )
}

/**
 * The two changes a person may make by hand (CaseStatusRules on the server). The buttons appear
 * only when the rule would allow them, but the server decides: a refusal (422) is shown as sent.
 */
function CaseActions({ detail }: { detail: CaseDetail }) {
  const user = useCurrentUser()
  const update = useUpdateCaseStatus(detail.id)
  const [confirmingClose, setConfirmingClose] = useState(false)

  const reviewer = user?.role === 'FieldAgronomist' || user?.role === 'CoopAdministrator'
  const busy = detail.status === 'AgentProcessing' || detail.status === 'PendingApproval'
  const canReview = reviewer && (detail.status === 'Submitted' || detail.status === 'Rejected')
  const canClose = detail.status !== 'Closed' && !busy

  if (!canReview && !canClose) return null

  return (
    <section aria-label="Case actions" className="space-y-2">
      {update.error && <Alert tone="error">{userMessage(update.error)}</Alert>}
      <div className="flex flex-wrap gap-2">
        {canReview && (
          <Button variant="secondary" loading={update.isPending && update.variables === 'AwaitingManualReview'} onClick={() => update.mutate('AwaitingManualReview')}>
            Take into manual review
          </Button>
        )}
        {canClose && (
          <Button variant="secondary" onClick={() => setConfirmingClose(true)}>
            Close case
          </Button>
        )}
      </div>
      <Modal
        open={confirmingClose}
        onClose={() => setConfirmingClose(false)}
        title={`Close ${detail.referenceNo}?`}
        description="A closed case cannot be reopened. If the problem comes back, the farmer reports a new case."
      >
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={() => setConfirmingClose(false)}>
            Keep open
          </Button>
          <Button
            loading={update.isPending}
            onClick={() => update.mutate('Closed', { onSettled: () => setConfirmingClose(false) })}
          >
            Close case
          </Button>
        </div>
      </Modal>
    </section>
  )
}
