import { useState, type FormEvent } from 'react'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Field } from '@/components/ui/field'
import { Modal } from '@/components/ui/Modal'
import { ApiError, userMessage } from '@/lib/api'
import { useFulfilOrder } from './queries'
import type { Order } from './types'

/**
 * Hands a packed order over. The dealer types the six-digit pickup code the farmer shows on their
 * phone; the API refuses any other code, so the packs go to the person the prescription is for.
 */
export function HandOverModal({ order, onClose }: { order: Order | null; onClose: () => void }) {
  const fulfil = useFulfilOrder()
  const [code, setCode] = useState('')
  const [fieldError, setFieldError] = useState<string | null>(null)

  const close = () => {
    fulfil.reset()
    setCode('')
    setFieldError(null)
    onClose()
  }

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (!order) return
    const digits = code.replace(/[\s-]/g, '')
    if (!/^\d{6}$/.test(digits)) {
      setFieldError('Enter the six digits shown on the farmer’s phone.')
      return
    }
    setFieldError(null)
    try {
      await fulfil.mutateAsync({ id: order.id, status: 'Collected', pickupCode: digits })
      close()
    } catch (error) {
      if (error instanceof ApiError && (error.problem.code === 'WRONG_PICKUP_CODE' || error.problem.code === 'PICKUP_CODE_REQUIRED')) {
        setFieldError(error.message)
      }
    }
  }

  const handled = fulfil.error instanceof ApiError && fieldError !== null
  return (
    <Modal
      open={order !== null}
      onClose={close}
      title={order ? `Hand over ${order.orderNo}` : 'Hand over'}
      description={order ? `${order.farmerName} · ${order.lines.map((l) => `${l.packs} × ${l.productName}`).join(', ')}` : undefined}
    >
      <form className="space-y-4" onSubmit={submit} noValidate>
        {fulfil.error && !handled && <Alert tone="error">{userMessage(fulfil.error)}</Alert>}
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
        />
        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" loading={fulfil.isPending}>
            Hand over
          </Button>
        </div>
      </form>
    </Modal>
  )
}
