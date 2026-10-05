import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/Modal'
import { userMessage } from '@/lib/api'
import { formatLkr } from './format'
import { useRecordCashPayment } from './queries'
import type { Order } from './types'

/**
 * Records cash taken at the counter. The dealer confirms the exact amount, because recording it is
 * what lets the order be handed over. Any card checkout the farmer has open in the app is closed.
 */
export function CashPaymentModal({ order, onClose }: { order: Order | null; onClose: () => void }) {
  const record = useRecordCashPayment()

  const close = () => {
    record.reset()
    onClose()
  }

  const confirm = async () => {
    if (!order) return
    try {
      await record.mutateAsync(order.id)
      close()
    } catch {
      // Shown in the dialog.
    }
  }

  return (
    <Modal
      open={order !== null}
      onClose={close}
      title={order ? `Record cash for ${order.orderNo}` : 'Record cash'}
      description={order ? `${order.farmerName} · ${order.lines.map((l) => `${l.packs} × ${l.productName}`).join(', ')}` : undefined}
    >
      {order && (
        <div className="space-y-4">
          {record.error && <Alert tone="error">{userMessage(record.error)}</Alert>}
          <p className="text-[15px] text-stone-700">
            Take <strong className="text-stone-900">{formatLkr(order.totalAmount)}</strong> in cash from {order.farmerName}, then confirm. If they
            had started paying by card in the app, that payment is closed.
          </p>
          <div className="flex justify-end gap-2 pt-2">
            <Button type="button" variant="secondary" onClick={close}>
              Cancel
            </Button>
            <Button type="button" loading={record.isPending} onClick={confirm}>
              Cash received: {formatLkr(order.totalAmount)}
            </Button>
          </div>
        </div>
      )}
    </Modal>
  )
}
