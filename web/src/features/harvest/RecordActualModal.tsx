import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Field } from '@/components/ui/field'
import { Modal } from '@/components/ui/Modal'
import { userMessage } from '@/lib/api'
import { amount } from '@/lib/form'
import { useRecordActual } from './queries'
import type { HarvestForecast } from './types'

// Mirrors RecordActualYieldRequestValidator. The server re-checks.
const schema = z.object({
  actualYieldKg: amount('Enter what was harvested, in kg.', (n) => n.min(0, 'A harvest cannot be negative.').max(1_000_000)),
})

type FormValues = z.input<typeof schema>

/** Records what a forecast crop actually yielded, so the forecast-vs-actual report can judge it. */
export function RecordActualModal({ forecast, onClose }: { forecast: HarvestForecast | null; onClose: () => void }) {
  const record = useRecordActual()
  const { register, handleSubmit, reset, formState: { errors } } = useForm<FormValues, unknown, z.output<typeof schema>>({
    resolver: zodResolver(schema),
    values: { actualYieldKg: forecast?.actualYieldKg != null ? String(forecast.actualYieldKg) : '' },
  })

  const close = () => {
    record.reset()
    reset()
    onClose()
  }

  const onSubmit = handleSubmit(async ({ actualYieldKg }) => {
    if (!forecast) return
    try {
      await record.mutateAsync({ id: forecast.id, actualYieldKg })
      close()
    } catch {
      // Shown in the banner below.
    }
  })

  return (
    <Modal
      open={forecast !== null}
      onClose={close}
      title="Record the actual harvest"
      description={forecast ? `${forecast.cropName} on ${forecast.plotCode} (${forecast.farmerName}): ${forecast.estimatedYieldKg.toLocaleString('en-US')} kg forecast.` : undefined}
    >
      <form className="space-y-4" onSubmit={onSubmit} noValidate>
        {record.error && <Alert tone="error">{userMessage(record.error)}</Alert>}
        <Field label="Harvested (kg)" type="number" step="any" min={0} error={errors.actualYieldKg?.message} {...register('actualYieldKg')} />
        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" loading={record.isPending}>
            Save
          </Button>
        </div>
      </form>
    </Modal>
  )
}
