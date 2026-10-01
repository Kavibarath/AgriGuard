import type { ReactNode } from 'react'
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

/** The limits, grouped by what they protect. Each group says which validator rule reads it. */
const limitGroups: {
  title: string
  rules: string
  explain: string
  fields: { name: keyof Limits; label: string; hint: string; unit: string }[]
}[] = [
  {
    title: 'Harvest safety',
    rules: 'V5',
    explain: 'Residues must fall below the legal limit before the crop is picked.',
    fields: [{ name: 'preHarvestIntervalDays', label: 'Pre-harvest interval (days)', hint: 'No harvest sooner than this after spraying.', unit: 'days' }],
  },
  {
    title: 'Resistance management',
    rules: 'V6, V7',
    explain: 'Limits how often one active ingredient is used, so pests do not adapt to it.',
    fields: [
      { name: 'maxApplicationsPerCycle', label: 'Applications per crop cycle', hint: 'Seasonal limit for this active ingredient.', unit: 'times' },
      { name: 'minDaysBetweenApplications', label: 'Days between applications', hint: 'Minimum gap between two sprays of it.', unit: 'days' },
    ],
  },
  {
    title: 'Weather and workers',
    rules: 'V8',
    explain: 'The spray must dry before rain, and people stay out while it is fresh.',
    fields: [
      { name: 'rainfastHours', label: 'Rainfast time (hours)', hint: 'Dry hours needed after spraying (V8).', unit: 'hours' },
      { name: 'reEntryIntervalHours', label: 'Re-entry interval (hours)', hint: 'Printed on the prescription.', unit: 'hours' },
    ],
  },
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
      size="lg"
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

        <RuleGroup title="Dose" rules="V3" explain="The proposal's dose per hectare must sit inside this range.">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Minimum dose" unit={unit ? `${unit}/ha` : 'per ha'} type="number" step="any" min={0} error={errors.minDosePerHectare?.message} {...register('minDosePerHectare')} />
            <Field label="Maximum dose" unit={unit ? `${unit}/ha` : 'per ha'} type="number" step="any" min={0} error={errors.maxDosePerHectare?.message} {...register('maxDosePerHectare')} />
          </div>
        </RuleGroup>

        {limitGroups.map((group) => (
          <RuleGroup key={group.title} title={group.title} rules={group.rules} explain={group.explain}>
            <div className="grid gap-4 sm:grid-cols-2">
              {group.fields.map((f) => (
                <Field key={f.name} label={f.label} hint={f.hint} unit={f.unit} type="number" step="1" min={0} error={errors[f.name]?.message} {...register(f.name)} />
              ))}
            </div>
          </RuleGroup>
        ))}

        <RuleGroup title="Status" rules="V2, V10" explain="Whether the product may be proposed for this crop at all.">
          <div className="space-y-2 text-sm text-stone-800">
            <label className="flex items-start gap-2">
              <input type="checkbox" className="mt-0.5 size-4 accent-brand-600" {...register('isActive')} />
              <span>
                <span className="font-medium">Approved (active)</span>
                <span className="block text-xs text-stone-600">Untick to withdraw the approval: proposals fail V2 until it is ticked again.</span>
              </span>
            </label>
            <label className="flex items-start gap-2">
              <input type="checkbox" className="mt-0.5 size-4 accent-brand-600" {...register('isRestricted')} />
              <span>
                <span className="font-medium">Restricted use</span>
                <span className="block text-xs text-stone-600">V10 — needs a permit the system cannot yet verify, so proposals are rejected.</span>
              </span>
            </label>
          </div>
        </RuleGroup>

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

/** One group of limits: its name, the rules that read it, and what it protects. */
function RuleGroup({ title, rules, explain, children }: { title: string; rules: string; explain: string; children: ReactNode }) {
  return (
    <fieldset className="rounded-lg border border-border-subtle bg-surface-sunken/50 p-3">
      <legend className="px-1 text-sm font-semibold text-stone-900">
        {title} <span className="ml-1 rounded bg-surface-inset px-1.5 py-0.5 text-xs font-bold text-stone-700 tabular-nums">{rules}</span>
      </legend>
      <p className="mb-2 text-xs text-stone-600">{explain}</p>
      {children}
    </fieldset>
  )
}
