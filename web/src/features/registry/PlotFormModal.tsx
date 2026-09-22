import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Field } from '@/components/ui/field'
import { Modal } from '@/components/ui/Modal'
import { SelectField } from '@/components/ui/select'
import { ApiError, userMessage } from '@/lib/api'
import { useCreatePlot, useUpdatePlot } from './queries'
import { soilLabels, type Plot, type SoilType } from './types'

// Bounds match the API validators and the database CHECK constraints.
const schema = z.object({
  plotCode: z.string().trim().min(1, 'Give the plot a short code, e.g. P-07.').max(20),
  name: z.string().trim().max(100).optional(),
  areaHectares: z.coerce
    .number({ message: 'Enter the area in hectares.' })
    .positive('Area must be greater than 0 hectares.')
    .max(10_000, 'That area looks wrong — enter hectares, not square metres.'),
  latitude: z.coerce.number({ message: 'Enter a latitude.' }).min(-90).max(90),
  longitude: z.coerce.number({ message: 'Enter a longitude.' }).min(-180).max(180),
  soilType: z.string().min(1, 'Choose a soil type.'),
  status: z.string().optional(),
})

type FormValues = z.input<typeof schema>

export function PlotFormModal({
  open,
  onClose,
  farmId,
  plot,
}: {
  open: boolean
  onClose: () => void
  farmId: string
  plot?: Plot
}) {
  const create = useCreatePlot()
  const update = useUpdatePlot()
  const mutation = plot ? update : create

  const { register, handleSubmit, setError, reset, formState: { errors } } = useForm<FormValues>({
    resolver: zodResolver(schema),
    values: {
      plotCode: plot?.plotCode ?? '',
      name: plot?.name ?? '',
      areaHectares: plot?.areaHectares ?? ('' as unknown as number),
      latitude: plot?.latitude ?? ('' as unknown as number),
      longitude: plot?.longitude ?? ('' as unknown as number),
      soilType: plot?.soilType ?? 'Loam',
      status: plot?.status ?? 'Active',
    },
  })

  const close = () => {
    mutation.reset()
    reset()
    onClose()
  }

  const onSubmit = handleSubmit(async (raw) => {
    const values = schema.parse(raw)
    const input = { ...values, name: values.name || null }
    try {
      if (plot) await update.mutateAsync({ id: plot.id, ...input, status: values.status ?? 'Active' })
      else await create.mutateAsync({ farmId, ...input })
      close()
    } catch (error) {
      if (error instanceof ApiError && error.status === 400) {
        for (const [field, messages] of Object.entries(error.fieldErrors)) {
          if (field in schema.shape) setError(field as keyof FormValues, { message: messages[0] })
        }
      } else if (error instanceof ApiError && error.status === 409) {
        setError('plotCode', { message: error.message })
      }
    }
  })

  // 422 means a business rule refused the change (area locked mid-cycle, plot not retirable):
  // it is not a field error, and the message explains what to do first.
  const banner =
    mutation.error && !(mutation.error instanceof ApiError && [400, 409].includes(mutation.error.status))
      ? userMessage(mutation.error)
      : null

  return (
    <Modal open={open} onClose={close} title={plot ? `Edit plot ${plot.plotCode}` : 'Add a plot'}>
      <form className="space-y-4" onSubmit={onSubmit} noValidate>
        {banner && <Alert tone="error">{banner}</Alert>}

        <div className="grid grid-cols-2 gap-3">
          <Field label="Plot code" autoFocus error={errors.plotCode?.message} {...register('plotCode')} />
          <Field label="Name" hint="Optional" error={errors.name?.message} {...register('name')} />
        </div>

        <Field
          label="Area (hectares)"
          type="number"
          step="0.001"
          inputMode="decimal"
          error={errors.areaHectares?.message}
          hint={plot?.activeCycle ? 'Locked while a crop cycle is active' : undefined}
          disabled={Boolean(plot?.activeCycle)}
          {...register('areaHectares')}
        />

        <div className="grid grid-cols-2 gap-3">
          <Field label="Latitude" type="number" step="0.000001" inputMode="decimal" error={errors.latitude?.message} {...register('latitude')} />
          <Field label="Longitude" type="number" step="0.000001" inputMode="decimal" error={errors.longitude?.message} {...register('longitude')} />
        </div>

        <SelectField label="Soil type" error={errors.soilType?.message} {...register('soilType')}>
          {Object.entries(soilLabels).map(([value, label]) => (
            <option key={value} value={value as SoilType}>
              {label}
            </option>
          ))}
        </SelectField>

        {plot && (
          <SelectField label="Status" error={errors.status?.message} {...register('status')}>
            <option value="Active">Active</option>
            <option value="Fallow">Fallow</option>
            <option value="Retired">Retired</option>
          </SelectField>
        )}

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" loading={mutation.isPending}>
            {plot ? 'Save changes' : 'Add plot'}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
