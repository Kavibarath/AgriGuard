import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Field } from '@/components/ui/field'
import { Modal } from '@/components/ui/Modal'
import { SelectField } from '@/components/ui/select'
import { ApiError, userMessage } from '@/lib/api'
import { amount } from '@/lib/form'
import { isoToday } from '@/features/inventory/format'
import { useCreateSlot } from './queries'
import type { CollectionCentre } from './types'

// Mirrors CreateCollectionSlotRequestValidator. The server re-checks, and alone knows the day's
// remaining centre capacity (CENTRE_CAPACITY_EXCEEDED) and whether the slot number is taken (409).
const schema = z
  .object({
    centreId: z.string().min(1, 'Choose the centre.'),
    slotDate: z.string().min(1, 'Choose the day.'),
    slotIndex: amount('Enter the slot number.', (n) => n.int('Whole numbers only.').min(1, 'From 1 to 24.').max(24, 'From 1 to 24.')),
    startTime: z.string().min(1, 'Enter the start time.'),
    endTime: z.string().min(1, 'Enter the end time.'),
    capacityKg: amount("Enter the slot's capacity in kg.", (n) => n.gt(0, 'Capacity must be more than 0 kg.').max(100_000)),
  })
  .refine((v) => v.endTime > v.startTime, { path: ['endTime'], message: 'The slot must end after it starts.' })
  .refine((v) => v.slotDate >= isoToday(), { path: ['slotDate'], message: 'A slot cannot be added in the past.' })

type FormValues = z.input<typeof schema>

/** Opens a collection slot at a centre (Co-op Administrator only; the API enforces it). */
export function SlotFormModal({ open, onClose, centres, defaults }: { open: boolean; onClose: () => void; centres: CollectionCentre[]; defaults: { centreId?: string; slotDate: string } }) {
  const create = useCreateSlot()
  const { register, handleSubmit, reset, setError, formState: { errors } } = useForm<FormValues, unknown, z.output<typeof schema>>({
    resolver: zodResolver(schema),
    values: { centreId: defaults.centreId ?? '', slotDate: defaults.slotDate, slotIndex: '4', startTime: '13:00', endTime: '15:00', capacityKg: '' },
  })

  const close = () => {
    create.reset()
    reset()
    onClose()
  }

  const onSubmit = handleSubmit(async (values) => {
    try {
      await create.mutateAsync(values)
      close()
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) setError('slotIndex', { message: error.message })
      else if (error instanceof ApiError && error.problem.code === 'CENTRE_CAPACITY_EXCEEDED') setError('capacityKg', { message: error.message })
    }
  })

  const handled = create.error instanceof ApiError && (create.error.status === 409 || create.error.problem.code === 'CENTRE_CAPACITY_EXCEEDED')

  return (
    <Modal open={open} onClose={close} title="Open a collection slot" description="A centre's slots on one day may not add up to more than it handles a day.">
      <form className="space-y-4" onSubmit={onSubmit} noValidate>
        {create.error && !handled && <Alert tone="error">{userMessage(create.error)}</Alert>}
        <SelectField label="Centre" error={errors.centreId?.message} {...register('centreId')}>
          <option value="">Choose a centre…</option>
          {centres.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name} ({c.dailyCapacityKg.toLocaleString('en-US')} kg a day)
            </option>
          ))}
        </SelectField>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Day" type="date" min={isoToday()} error={errors.slotDate?.message} {...register('slotDate')} />
          <Field label="Slot number" type="number" min={1} max={24} error={errors.slotIndex?.message} {...register('slotIndex')} />
          <Field label="Starts" type="time" error={errors.startTime?.message} {...register('startTime')} />
          <Field label="Ends" type="time" error={errors.endTime?.message} {...register('endTime')} />
        </div>
        <Field label="Capacity (kg)" type="number" step="any" min={0} error={errors.capacityKg?.message} {...register('capacityKg')} />
        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" loading={create.isPending}>
            Open slot
          </Button>
        </div>
      </form>
    </Modal>
  )
}
