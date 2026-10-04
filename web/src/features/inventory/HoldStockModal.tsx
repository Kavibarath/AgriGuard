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
import { isoToday } from './format'
import { useCreateReservation, useProducts } from './queries'
import { unitLabels } from './types'

// Mirrors CreateReservationRequestValidator.
const schema = z.object({
  productId: z.string().min(1, 'Choose the product.'),
  quantity: amount('Enter how much to hold.', (n) => n.positive('Enter how much to hold.').max(100_000)),
  usableOn: z.string().optional(),
  note: z.string().trim().max(300).optional(),
})

type FormValues = z.input<typeof schema>

/**
 * Puts stock aside for 24 hours — a phone order, a farmer coming back tomorrow. The server rounds
 * up to whole packs, draws the batch closest to expiry first, and refuses if the shop cannot cover it.
 */
export function HoldStockModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const products = useProducts({ pageSize: 100 })
  const hold = useCreateReservation()

  const { register, handleSubmit, setError, reset, control, formState: { errors } } = useForm<FormValues, unknown, z.output<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { productId: '', quantity: '', usableOn: '', note: '' },
  })
  const productId = useWatch({ control, name: 'productId' })
  const product = products.data?.items.find((p) => p.id === productId)

  const close = () => {
    hold.reset()
    reset()
    onClose()
  }

  const onSubmit = handleSubmit(async (values) => {
    try {
      await hold.mutateAsync({
        productId: values.productId,
        quantity: values.quantity,
        usableOn: values.usableOn || null,
        note: values.note || null,
      })
      close()
    } catch (error) {
      if (error instanceof ApiError && error.status === 400) {
        for (const [field, messages] of Object.entries(error.fieldErrors)) {
          if (field in schema.shape) setError(field as keyof FormValues, { message: messages[0] })
        }
      }
    }
  })

  // "Not enough in stock" is the answer to the whole form, so it stays a banner with the figures.
  const banner = hold.error && !(hold.error instanceof ApiError && hold.error.status === 400) ? userMessage(hold.error) : null

  return (
    <Modal open={open} onClose={close} title="Hold stock" description="Keeps stock off sale for 24 hours. Commit it when sold, or release it.">
      <form className="space-y-4" onSubmit={onSubmit} noValidate>
        {banner && <Alert tone="error">{banner}</Alert>}

        <SelectField label="Product" error={errors.productId?.message} {...register('productId')}>
          <option value="">Choose a product…</option>
          {products.data?.items.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}
            </option>
          ))}
        </SelectField>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field
            label={`Quantity${product ? ` (${unitLabels[product.unit]})` : ''}`}
            type="number"
            step="any"
            min={0}
            hint={product ? `Rounded up to whole ${product.packSize} ${unitLabels[product.unit]} packs.` : undefined}
            error={errors.quantity?.message}
            {...register('quantity')}
          />
          <Field label="Needed on" type="date" min={isoToday()} hint="Optional. Stock expiring before then is skipped." {...register('usableOn')} />
        </div>
        <Field label="Note" hint="Optional, e.g. who it is for." error={errors.note?.message} {...register('note')} />

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" loading={hold.isPending}>
            Hold stock
          </Button>
        </div>
      </form>
    </Modal>
  )
}
