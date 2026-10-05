import { useState, type FormEvent } from 'react'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Field } from '@/components/ui/field'
import { Modal } from '@/components/ui/Modal'
import { ApiError, userMessage } from '@/lib/api'
import { formatLkr, formatPaidBy } from './format'
import { useFulfilOrder, useRecordCashPayment } from './queries'
import type { Order } from './types'

/**
 * Hands a packed order over. Two checks, in the order they happen at the counter:
 * 1. it must be paid: by card in the farmer's app, or in cash, which can be recorded right here;
 * 2. the dealer types the six-digit pickup code the farmer shows on their phone; the API refuses
 *    any other code, so the packs go to the person the prescription is for.
 * Open it with a key per order, so a new order starts fresh.
 */
export function HandOverModal({ order, onClose }: { order: Order | null; onClose: () => void }) {
  const fulfil = useFulfilOrder()
  const recordCash = useRecordCashPayment()
  const [code, setCode] = useState('')
  const [fieldError, setFieldError] = useState<string | null>(null)
  // The order as it is after cash is recorded here, without waiting for the list to reload.
  const [updated, setUpdated] = useState<Order | null>(null)
  const current = updated ?? order
  const paid = current?.paymentStatus === 'Paid'

  const close = () => {
    fulfil.reset()
    recordCash.reset()
    setCode('')
    setFieldError(null)
    setUpdated(null)
    onClose()
  }

  const takeCash = async () => {
    if (!current) return
    try {
      setUpdated(await recordCash.mutateAsync(current.id))
    } catch {
      // Shown in the dialog.
    }
  }

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (!current || !paid) return
    const digits = code.replace(/[\s-]/g, '')
    if (!/^\d{6}$/.test(digits)) {
      setFieldError('Enter the six digits shown on the farmer’s phone.')
      return
    }
    setFieldError(null)
    try {
      await fulfil.mutateAsync({ id: current.id, status: 'Collected', pickupCode: digits })
      close()
    } catch (error) {
      if (error instanceof ApiError && (error.problem.code === 'WRONG_PICKUP_CODE' || error.problem.code === 'PICKUP_CODE_REQUIRED')) {
        setFieldError(error.message)
      }
    }
  }

  const handled = fulfil.error instanceof ApiError && fieldError !== null
  const paidBy = current ? formatPaidBy(current) : null
  return (
    <Modal
      open={current !== null}
      onClose={close}
      title={current ? `Hand over ${current.orderNo}` : 'Hand over'}
      description={current ? `${current.farmerName} · ${current.lines.map((l) => `${l.packs} × ${l.productName}`).join(', ')}` : undefined}
    >
      <form className="space-y-4" onSubmit={submit} noValidate>
        {fulfil.error && !handled && <Alert tone="error">{userMessage(fulfil.error)}</Alert>}
        {recordCash.error && <Alert tone="error">{userMessage(recordCash.error)}</Alert>}

        {current &&
          (paid ? (
            <Alert tone="success" title="Paid">
              {paidBy ?? 'Paid'} · {formatLkr(current.totalAmount)}
            </Alert>
          ) : (
            <Alert tone="warning" title="Not paid yet">
              <p>
                Take {formatLkr(current.totalAmount)} in cash before handing over, or ask the farmer to pay by card in the AgriGuard app and
                reload.
              </p>
              <Button type="button" className="mt-2.5" loading={recordCash.isPending} onClick={takeCash}>
                Cash received: {formatLkr(current.totalAmount)}
              </Button>
            </Alert>
          ))}

        <Field
          label="Pickup code"
          inputMode="numeric"
          autoComplete="off"
          maxLength={7}
          placeholder="e.g. 482913"
          hint="Ask the farmer to open My orders on their phone. Nothing is handed over with a wrong code."
          value={code}
          onChange={(event) => setCode(event.target.value)}
          error={fieldError ?? undefined}
          disabled={!paid}
        />
        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" loading={fulfil.isPending} disabled={!paid}>
            Hand over
          </Button>
        </div>
      </form>
    </Modal>
  )
}
