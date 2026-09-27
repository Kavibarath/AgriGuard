import { zodResolver } from '@hookform/resolvers/zod'
import { useForm, useWatch } from 'react-hook-form'
import { z } from 'zod'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Field } from '@/components/ui/field'
import { Modal } from '@/components/ui/Modal'
import { SelectField } from '@/components/ui/select'
import { ApiError, userMessage } from '@/lib/api'
import { amount } from '@/lib/form'
import { useCrops } from '@/features/registry/queries'
import { useCreateRule, useProducts, useUpdateRule } from '@/features/inventory/queries'
import { unitLabels, type CropRule } from '@/features/inventory/types'

// Mirrors ApprovalLimitsValidator on the server, which mirrors the table's CHECK constraints.
const schema = z
  .object({
    productId: z.string().min(1, 'Choose the product.'),
    cropId: z.string().min(1, 'Choose the crop.'),
    minDosePerHectare: amount('Enter the minimum dose.', (n) => n.positive('The minimum dose must be more than zero.')),
    maxDosePerHectare: amount('Enter the maximum dose.', (n) => n.positive().max(1000)),
    preHarvestIntervalDays: amount('Enter the pre-harvest interval.', (n) => n.int().min(0).max(365, 'At most 365 days.')),
    reEntryIntervalHours: amount('Enter the re-entry interval.', (n) => n.int().min(0).max(720, 'At most 720 hours.')),
    maxApplicationsPerCycle: amount('Enter the seasonal limit.', (n) => n.int().min(1, 'Allow at least one application.').max(20)),
    minDaysBetweenApplications: amount('Enter the minimum gap.', (n) => n.int().min(0).max(180)),
    rainfastHours: amount('Enter the rainfast time.', (n) => n.int().min(0).max(72)),
    isRestricted: z.boolean(),
    isActive: z.boolean(),
  })
  .refine((v) => v.maxDosePerHectare >= v.minDosePerHectare, {
    path: ['maxDosePerHectare'],
    message: 'The maximum dose cannot be below the minimum.',
  })

type FormValues = z.input<typeof schema>
type Limits = Omit<z.output<typeof schema>, 'productId' | 'cropId'>

const limitFields: { name: keyof Limits; label: string; hint: string; step: string }[] = [
  { name: 'preHarvestIntervalDays', label: 'Pre-harvest interval (days)', hint: 'V5 — no harvest sooner than this after spraying.', step: '1' },
  { name: 'maxApplicationsPerCycle', label: 'Applications per crop cycle', hint: 'V6 — seasonal limit for this active ingredient.', step: '1' },
  { name: 'minDaysBetweenApplications', label: 'Days between applications', hint: 'V7 — resistance management.', step: '1' },
  { name: 'rainfastHours', label: 'Rainfast time (hours)', hint: 'V8 — dry hours needed after spraying.', step: '1' },
  { name: 'reEntryIntervalHours', label: 'Re-entry interval (hours)', hint: 'Printed on the prescription.', step: '1' },
]

/**
 * Edits one rule's limits, or approves a product for a crop. What is saved here is what the
 * deterministic validator judges the next proposal against — no code change, no restart.
 */
export function RuleFormModal({ open, onClose, rule }: { open: boolean; onClose: () => void; rule?: CropRule }) {
  const products = useProducts({ pageSize: 100, includeInactive: true })
  const crops = useCrops()
  const create = useCreateRule()
  const update = useUpdateRule()
  const mutation = rule ? update : create

  const { register, handleSubmit, setError, reset, control, formState: { errors } } = useForm<FormValues, unknown, z.output<typeof schema>>({
    resolver: zodResolver(schema),
    values: {
      productId: rule?.productId ?? '',
      cropId: rule?.cropId ?? '',
      minDosePerHectare: rule ? String(rule.minDosePerHectare) : '',
      maxDosePerHectare: rule ? String(rule.maxDosePerHectare) : '',
      preHarvestIntervalDays: rule ? String(rule.preHarvestIntervalDays) : '',
      reEntryIntervalHours: rule ? String(rule.reEntryIntervalHours) : '24',
      maxApplicationsPerCycle: rule ? String(rule.maxApplicationsPerCycle) : '',
      minDaysBetweenApplications: rule ? String(rule.minDaysBetweenApplications) : '',
      rainfastHours: rule ? String(rule.rainfastHours) : '4',
      isRestricted: rule?.isRestricted ?? false,
      isActive: rule?.isActive ?? true,
    },
  })
  const productId = useWatch({ control, name: 'productId' })

  const product = products.data?.items.find((p) => p.id === productId)
  const unit = rule ? unitLabels[rule.unit] : product ? unitLabels[product.unit] : ''

  const close = () => {
    mutation.reset()
    reset()
    onClose()
  }

  const onSubmit = handleSubmit(async ({ productId, cropId, ...limits }) => {
    try {
      if (rule) await update.mutateAsync({ id: rule.id, ...limits })
      else await create.mutateAsync({ productId, cropId, ...limits })
      close()
    } catch (error) {
      if (error instanceof ApiError && error.status === 400) {
        for (const [field, messages] of Object.entries(error.fieldErrors)) {
          if (field in schema.shape) setError(field as keyof FormValues, { message: messages[0] })
        }
      }
    }
  })

  const banner = mutation.error && !(mutation.error instanceof ApiError && mutation.error.status === 400) ? userMessage(mutation.error) : null

  return (
    <Modal
      open={open}
      onClose={close}
      title={rule ? `${rule.productName} on ${rule.cropName}` : 'Approve a product for a crop'}
      description="Saved limits apply to the very next proposal the validator checks."
    >
      <form className="space-y-4" onSubmit={onSubmit} noValidate>
        {banner && <Alert tone="error">{banner}</Alert>}

        {!rule && (
          <div className="grid gap-4 sm:grid-cols-2">
            <SelectField label="Product" error={errors.productId?.message} {...register('productId')}>
              <option value="">Choose a product…</option>
              {products.data?.items.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </SelectField>
            <SelectField label="Crop" error={errors.cropId?.message} {...register('cropId')}>
              <option value="">Choose a crop…</option>
              {crops.data?.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </SelectField>
          </div>
        )}

        <fieldset className="space-y-2">
          <legend className="text-sm font-medium text-stone-800">Dose per hectare{unit && ` (${unit}/ha)`} — V3</legend>
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Minimum dose" type="number" step="any" min={0} error={errors.minDosePerHectare?.message} {...register('minDosePerHectare')} />
            <Field label="Maximum dose" type="number" step="any" min={0} error={errors.maxDosePerHectare?.message} {...register('maxDosePerHectare')} />
          </div>
        </fieldset>

        <div className="grid gap-4 sm:grid-cols-2">
          {limitFields.map((f) => (
            <Field key={f.name} label={f.label} hint={f.hint} type="number" step={f.step} min={0} error={errors[f.name]?.message} {...register(f.name)} />
          ))}
        </div>

        <div className="space-y-2 text-sm text-stone-800">
          <label className="flex items-start gap-2">
            <input type="checkbox" className="mt-0.5 size-4 accent-brand-600" {...register('isActive')} />
            <span>
              <span className="font-medium">Approved (active)</span>
              <span className="block text-xs text-stone-500">Untick to withdraw the approval: proposals fail V2 until it is ticked again.</span>
            </span>
          </label>
          <label className="flex items-start gap-2">
            <input type="checkbox" className="mt-0.5 size-4 accent-brand-600" {...register('isRestricted')} />
            <span>
              <span className="font-medium">Restricted use</span>
              <span className="block text-xs text-stone-500">V10 — needs a permit the system cannot yet verify, so proposals are rejected.</span>
            </span>
          </label>
        </div>

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" loading={mutation.isPending}>
            {rule ? 'Save rule' : 'Approve'}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
