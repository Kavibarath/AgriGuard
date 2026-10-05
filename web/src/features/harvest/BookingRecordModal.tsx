import { useState, type FormEvent } from 'react'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Field } from '@/components/ui/field'
import { Modal } from '@/components/ui/Modal'
import { userMessage } from '@/lib/api'
import { formatKg } from './format'
import { useRecordBooking } from './queries'
import type { CollectionBooking } from './types'

export type BookingStep = 'weigh' | 'missed'

/**
 * The two steps at the centre that deserve a second look, because neither can be undone:
 * - **weigh:** the weight actually delivered completes the booking;
 * - **missed:** the farmer did not come, so the booking ends and they must book again.
 * Checking in is a single click on the list; it is only ever followed by one of these.
 */
export function BookingRecordModal({ booking, step, onClose }: { booking: CollectionBooking | null; step: BookingStep; onClose: () => void }) {
  const record = useRecordBooking()
  const [weight, setWeight] = useState('')
  const [weightError, setWeightError] = useState<string | null>(null)

  const close = () => {
    record.reset()
    setWeight('')
    setWeightError(null)
    onClose()
  }

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (!booking) return
    let actualQuantityKg: number | undefined
    if (step === 'weigh') {
      const kg = Number(weight)
      if (weight.trim() === '' || !Number.isFinite(kg) || kg < 0) {
        setWeightError('Enter the weight delivered, in kg.')
        return
      }
      if (kg > 100_000) {
        setWeightError('That is more than any centre takes in a day. Check the weight.')
        return
      }
      actualQuantityKg = kg
    }
    setWeightError(null)
    try {
      await record.mutateAsync({ id: booking.id, status: step === 'weigh' ? 'Completed' : 'NoShow', actualQuantityKg })
      close()
    } catch {
      // Shown in the dialog.
    }
  }

  const who = booking ? `${booking.farmerName} · ${booking.cropName} · ${formatKg(booking.quantityKg)} booked` : undefined
  return (
    <Modal
      open={booking !== null}
      onClose={close}
      title={booking ? (step === 'weigh' ? `Record the weight for ${booking.bookingNo}` : `Mark ${booking.bookingNo} missed`) : ''}
      description={who}
    >
      <form className="space-y-4" onSubmit={submit} noValidate>
        {record.error && <Alert tone="error">{userMessage(record.error)}</Alert>}
        {step === 'weigh' ? (
          <Field
            label="Weight delivered (kg)"
            type="number"
            step="any"
            min={0}
            inputMode="decimal"
            hint="From the centre's scale. Zero if the farmer brought nothing to sell."
            value={weight}
            onChange={(event) => setWeight(event.target.value)}
            error={weightError ?? undefined}
          />
        ) : (
          <p className="text-[15px] text-stone-700">
            The farmer did not come for this slot. The booking ends, and they will need to book again from the phone.
          </p>
        )}
        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" variant={step === 'missed' ? 'danger' : 'primary'} loading={record.isPending}>
            {step === 'weigh' ? 'Complete delivery' : 'Mark missed'}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
