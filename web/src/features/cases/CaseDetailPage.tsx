import { useState } from 'react'
import { Link, useParams } from 'react-router'
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
    <main className="mx-auto max-w-6xl space-y-6 p-6">
      <Link to="/cases" className="text-sm text-stone-500 hover:text-stone-800">
        ← All cases
      </Link>
      <AsyncBoundary isPending={cropCase.isPending} error={cropCase.error} onRetry={cropCase.refetch} label="Loading the case">
        {cropCase.data && <CaseBody detail={cropCase.data} />}
      </AsyncBoundary>
    </main>
  )
}

function CaseBody({ detail }: { detail: CaseDetail }) {
  const reported = { lat: detail.reportedLatitude, lng: detail.reportedLongitude }
  const plot = { lat: detail.plotLatitude, lng: detail.plotLongitude }
  const gap = distanceMetres(reported, plot)
  const latestRun = detail.agentRuns[0]

  return (
    <>
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold text-stone-900">Case {detail.referenceNo}</h1>
          <p className="text-sm text-stone-600">
            {detail.farmerName} ·{' '}
            <Link to={`/plots/${detail.plotId}`} className="text-brand-700 hover:underline">
              {detail.plotCode}
            </Link>{' '}
            · {detail.cropName} ({detail.stage}) · {detail.districtName}
          </p>
        </div>
        <div className="flex gap-2">
          <StatusBadge label={detail.severity} tone={severityTone[detail.severity]} className="text-sm" />
          <StatusBadge label={caseStatusLabels[detail.status]} tone={caseStatusTone[detail.status]} className="text-sm" />
        </div>
      </header>

      {detail.capturedAt && (
        <Alert tone="info" title="Sent from the phone's offline queue">
          Reported on the phone at {formatDateTime(detail.capturedAt)} and received at {formatDateTime(detail.createdAt)}, after waiting{' '}
          {formatWait(detail.capturedAt, detail.createdAt)} for a connection.
        </Alert>
      )}

      <div className="grid gap-6 lg:grid-cols-[3fr_2fr]">
        <div className="space-y-6">
          <section aria-labelledby="where-heading" className="space-y-2">
            <h2 id="where-heading" className="text-sm font-medium uppercase tracking-wide text-stone-500">
              Where it was reported
            </h2>
            <TileMap
              label={`Map: where ${detail.referenceNo} was reported, and plot ${detail.plotCode}`}
              pins={[
                { id: 'plot', position: plot, label: `Plot ${detail.plotCode}, registered centre`, tone: 'neutral', variant: 'ring' },
                { id: 'report', position: reported, label: `Where the phone was when ${detail.referenceNo} was reported`, tone: severityPinTone[detail.severity] },
              ]}
            />
            <p className="text-xs text-stone-500">
              Filled dot: the phone ({detail.reportedLatitude.toFixed(5)}, {detail.reportedLongitude.toFixed(5)}). Ring: the plot's registered centre.
            </p>
            {gap > FAR_FROM_PLOT_METRES ? (
              <Alert tone="warning" title={`Reported ${formatDistance(gap)} from the plot`}>
                The phone was far from {detail.plotCode} when this was reported. Check with the farmer that it is the right plot before approving a treatment for it.
              </Alert>
            ) : (
              <p className="text-sm text-stone-700">
                {gap < 10 ? "Reported at the plot's registered centre." : `Reported ${formatDistance(gap)} from the plot's registered centre.`}
              </p>
            )}
          </section>

          <section aria-labelledby="seen-heading" className="space-y-3 rounded-lg border border-stone-200 bg-white p-4">
            <h2 id="seen-heading" className="font-medium text-stone-900">
              What the farmer saw
            </h2>
            <ul className="flex flex-wrap gap-2">
              {detail.symptoms.map((s) => (
                <li key={s.code} className="rounded-md bg-stone-50 px-2 py-1 text-sm ring-1 ring-inset ring-stone-200">
                  {s.label}
                </li>
              ))}
            </ul>
            {detail.farmerNote && (
              <div>
                <h3 className="text-xs font-medium uppercase tracking-wide text-stone-500">Farmer's note (as typed)</h3>
                {/* Untrusted input: rendered as text by React, never as markup. */}
                <p className="mt-1 whitespace-pre-wrap text-sm text-stone-800">{detail.farmerNote}</p>
              </div>
            )}
            <CasePhotos caseId={detail.id} photos={detail.photos} />
          </section>
        </div>

        <div className="space-y-6">
          <CaseActions detail={detail} />

          <section aria-labelledby="runs-heading" className="space-y-2">
            <h2 id="runs-heading" className="text-sm font-medium uppercase tracking-wide text-stone-500">
              Agent runs
            </h2>
            {detail.agentRuns.length === 0 ? (
              <p className="text-sm text-stone-600">No advice requested yet. The farmer starts a run from the phone with "Get AI advice".</p>
            ) : (
              <ol className="space-y-2">
                {detail.agentRuns.map((run) => (
                  <li key={run.id} className="rounded-lg border border-stone-200 bg-white p-3 text-sm">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <Link to={`/agent-runs/${run.id}`} className="font-medium text-brand-700 hover:underline">
                        Run of {formatDateTime(run.createdAt)}
                      </Link>
                      <StatusBadge label={runStatusLabels[run.status]} tone={runStatusTone[run.status]} />
                    </div>
                    {run.revisionCount > 0 && <p className="mt-1 text-xs text-stone-500">Revised {run.revisionCount}×</p>}
                    {run.failureReason && <p className="mt-1 text-stone-700">{run.failureReason}</p>}
                  </li>
                ))}
              </ol>
            )}
          </section>

          {latestRun?.farmerAdvice && (
            <section aria-labelledby="advice-heading" className="rounded-lg border border-brand-100 bg-brand-50 p-4">
              <h2 id="advice-heading" className="font-medium text-brand-700">
                Advice the farmer was given
              </h2>
              <ul className="mt-2 list-disc space-y-1 pl-5 text-sm text-stone-800">
                {latestRun.farmerAdvice
                  .split('\n')
                  .filter((line) => line.trim())
                  .map((line) => (
                    <li key={line}>{line}</li>
                  ))}
              </ul>
            </section>
          )}

          <dl className="grid grid-cols-2 gap-3 text-sm">
            <div>
              <dt className="text-stone-500">Received</dt>
              <dd className="text-stone-900">{formatDateTime(detail.createdAt)}</dd>
            </div>
            <div>
              <dt className="text-stone-500">Last change</dt>
              <dd className="text-stone-900">{formatDateTime(detail.updatedAt)}</dd>
            </div>
            {detail.assignedAgronomistName && (
              <div>
                <dt className="text-stone-500">Agronomist</dt>
                <dd className="text-stone-900">{detail.assignedAgronomistName}</dd>
              </div>
            )}
            {detail.confirmedPathogenCode && (
              <div>
                <dt className="text-stone-500">Confirmed</dt>
                <dd className="text-stone-900">{detail.confirmedPathogenCode}</dd>
              </div>
            )}
          </dl>
        </div>
      </div>
    </>
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
