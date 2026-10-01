import { useState } from 'react'
import { AlertTriangle, CheckCircle, StopOctagon } from '@/components/icons'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/Modal'
import { TextareaField } from '@/components/ui/textarea'
import { userMessage } from '@/lib/api'
import { formatDose } from './format'
import { useDecide } from './queries'
import type { AgentRun, DecisionType } from './types'

/** The API requires a reason of at least this length to reject or revise (DecideRequestValidator). */
export const MIN_REASON_LENGTH = 10
/** A revision's reason is passed to the agent, which accepts at most this many characters. */
export const MAX_REVISION_LENGTH = 300

const copy: Record<DecisionType, { title: string; button: string; variant: 'primary' | 'secondary' | 'danger' }> = {
  Approve: { title: 'Approve and issue the prescription?', button: 'Approve and issue', variant: 'primary' },
  Revise: { title: 'Send back to the agent', button: 'Request revision', variant: 'secondary' },
  Reject: { title: 'Reject this proposal', button: 'Reject', variant: 'danger' },
}

/**
 * The human gate, as the agronomist sees it (§9.5).
 *
 * One Idempotency-Key is made when a dialog opens and reused for every retry from that dialog. A
 * double-click, or a retry after a dropped connection, therefore reaches the API as the same
 * decision and gets the original result back — it can never approve twice. Closing the dialog
 * discards the key: opening it again is a new decision.
 */
export function DecisionPanel({ run, canDecide }: { run: AgentRun; canDecide: boolean }) {
  const decide = useDecide(run.id)
  const [open, setOpen] = useState<DecisionType | null>(null)
  const [idempotencyKey, setIdempotencyKey] = useState('')
  const [reason, setReason] = useState('')
  const [touched, setTouched] = useState(false)

  if (!canDecide) {
    return (
      <Alert tone="info" title="Awaiting an agronomist's decision">
        Only a field agronomist for this district can approve, reject or send back a proposal.
      </Alert>
    )
  }

  const start = (decision: DecisionType) => {
    decide.reset()
    setReason('')
    setTouched(false)
    setIdempotencyKey(crypto.randomUUID())
    setOpen(decision)
  }

  const close = () => setOpen(null)

  const trimmed = reason.trim()
  const reasonError =
    open === 'Approve'
      ? undefined
      : trimmed.length < MIN_REASON_LENGTH
        ? 'Say what is wrong in a sentence, so the reason is useful later.'
        : open === 'Revise' && trimmed.length > MAX_REVISION_LENGTH
          ? `Keep it under ${MAX_REVISION_LENGTH} characters — it is passed to the agent.`
          : undefined

  const submit = async () => {
    setTouched(true)
    if (!open || reasonError) return
    try {
      await decide.mutateAsync({ decision: open, reason: open === 'Approve' ? undefined : trimmed, idempotencyKey })
      close()
    } catch {
      // Shown in the dialog; the same key is reused if the agronomist tries again.
    }
  }

  const proposal = run.proposal

  return (
    <section
      aria-labelledby="decision-heading"
      className="card-raised overflow-hidden rounded-xl border-2 border-brand-600 bg-surface-card"
    >
      <header className="border-b border-brand-100 bg-brand-50 px-4 py-3">
        <h2 id="decision-heading" className="font-display text-xl leading-tight font-semibold text-brand-800">
          Your decision
        </h2>
        <p className="mt-0.5 text-sm text-brand-800">
          The proposal passed the safety rules. Nothing is issued until you approve.
        </p>
      </header>
      <div className="space-y-3 p-4">
        <Button size="lg" className="w-full" onClick={() => start('Approve')}>
          <CheckCircle />
          Approve
        </Button>
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" className="flex-1" onClick={() => start('Revise')}>
            <AlertTriangle size={16} className="text-warning" />
            Request revision
          </Button>
          {/* Set apart from the routine actions: ending a run is not a slip of the finger. */}
          <Button variant="secondary" className="flex-1 border-danger-200 text-danger-800 hover:border-danger hover:bg-danger-50" onClick={() => start('Reject')}>
            <StopOctagon size={16} className="text-danger" />
            Reject
          </Button>
        </div>
        <p className="text-xs text-stone-600">Revising and rejecting ask for a reason; it is recorded in the audit trail.</p>
      </div>

      <Modal
        open={open !== null}
        onClose={close}
        title={open ? copy[open].title : ''}
        description={
          open === 'Approve'
            ? `This issues the prescription${proposal ? ` for ${run.proposedProductName ?? 'the proposed product'} (${formatDose(proposal.dose_per_hectare, run.proposedProductUnit)}, spraying ${proposal.spray_date})` : ''}, confirms the dealer order and takes the stock off the shelf. The rules are checked once more first.`
            : open === 'Revise'
              ? 'The agent drafts a new proposal with your reason as guidance. The case stays open.'
              : 'The run ends and nothing is issued. The case moves to Rejected for manual follow-up.'
        }
      >
        <form
          className="space-y-4"
          onSubmit={(event) => {
            event.preventDefault()
            void submit()
          }}
        >
          {decide.error && <Alert tone="error">{userMessage(decide.error)}</Alert>}

          {open !== 'Approve' && (
            <TextareaField
              label={open === 'Revise' ? 'What should the agent change?' : 'Reason for rejecting'}
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              error={touched ? reasonError : undefined}
              hint={open === 'Revise' ? `${trimmed.length}/${MAX_REVISION_LENGTH} characters` : 'Recorded in the audit trail.'}
            />
          )}

          <div className="flex justify-end gap-2 pt-2">
            <Button type="button" variant="secondary" onClick={close}>
              Cancel
            </Button>
            <Button type="submit" variant={open ? copy[open].variant : 'primary'} loading={decide.isPending}>
              {open ? copy[open].button : ''}
            </Button>
          </div>
        </form>
      </Modal>
    </section>
  )
}
