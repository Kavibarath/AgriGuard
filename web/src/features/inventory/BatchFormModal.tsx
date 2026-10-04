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
import { formatQuantity, isoToday } from './format'
import { useCreateBatch, useProducts, useUpdateBatch } from './queries'
import { unitLabels, type InventoryBatch } from './types'

// Mirrors CreateBatchRequestValidator / UpdateBatchRequestValidator. The server re-checks.
const schema = z.object({
  productId: z.string().min(1, 'Choose the product.'),
  batchNo: z.string().trim().min(1, 'Enter the batch number printed on the pack.').max(50),
  expiryDate: z.string().min(1, 'Enter the expiry date on the pack.'),
  quantityOnHand: amount('Enter the quantity on hand.', (n) => n.min(0, 'Stock cannot be negative.').max(100_000)),
  unitPrice: amount('Enter the price per pack.', (n) => n.min(0, 'Price cannot be negative.').max(10_000_000)),
})

type FormValues = z.input<typeof schema>

/**
 * Receives a delivery (new batch) or corrects one: a recount, a price change, a misread expiry.
 * The product and batch number of an existing batch are fixed — a different number is a
 * different delivery.
 */
export function BatchFormModal({ open, onClose, batch }: { open: boolean; onClose: () => void; batch?: InventoryBatch }) {
  const products = useProducts({ pageSize: 100 })
  const create = useCreateBatch()
  const update = useUpdateBatch()
  const mutation = batch ? update : create

  const { register, handleSubmit, setError, reset, control, formState: { errors } } = useForm<FormValues, unknown, z.output<typeof schema>>({
    resolver: zodResolver(schema),
    values: {
      productId: batch?.productId ?? '',
      batchNo: batch?.batchNo ?? '',
      expiryDate: batch?.expiryDate ?? '',
      quantityOnHand: batch ? String(batch.quantityOnHand) : '',
      unitPrice: batch ? String(batch.unitPrice) : '',
    },
  })
  const productId = useWatch({ control, name: 'productId' })

  const product = products.data?.items.find((p) => p.id === productId)

  const close = () => {
    mutation.reset()
    reset()
    onClose()
  }

  const onSubmit = handleSubmit(async (values) => {
    if (!batch && values.expiryDate <= isoToday()) {
      setError('expiryDate', { message: 'This batch has already expired. Do not put it on the shelf.' })
      return
    }
    try {
      if (batch) {
        await update.mutateAsync({ id: batch.id, expiryDate: values.expiryDate, quantityOnHand: values.quantityOnHand, unitPrice: values.unitPrice })
      } else {
        await create.mutateAsync(values)
      }
      close()
    } catch (error) {
      if (error instanceof ApiError && error.status === 400) {
        for (const [field, messages] of Object.entries(error.fieldErrors)) {
          if (field in schema.shape) setError(field as keyof FormValues, { message: messages[0] })
        }
      } else if (error instanceof ApiError && error.status === 409 && !batch) {
        setError('batchNo', { message: error.message })
      }
    }
  })

  const handled = mutation.error instanceof ApiError && (mutation.error.status === 400 || (mutation.error.status === 409 && !batch))
  const banner = mutation.error && !handled ? userMessage(mutation.error) : null

  return (
    <Modal
      open={open}
      onClose={close}
      title={batch ? `Correct batch ${batch.batchNo}` : 'Receive a delivery'}
      description={batch ? batch.productName : 'Add a batch to your shelf exactly as printed on the pack.'}
    >
      <form className="space-y-4" onSubmit={onSubmit} noValidate>
        {banner && <Alert tone="error">{banner}</Alert>}

        {!batch && (
          <>
            <SelectField label="Product" error={errors.productId?.message} {...register('productId')}>
              <option value="">Choose a product…</option>
              {products.data?.items.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name} ({p.packSize} {unitLabels[p.unit]} pack)
                </option>
              ))}
            </SelectField>
            <Field label="Batch number" error={errors.batchNo?.message} {...register('batchNo')} />
          </>
        )}

        <Field label="Expiry date" type="date" error={errors.expiryDate?.message} {...register('expiryDate')} />
        <div className="grid gap-4 sm:grid-cols-2">
          <Field
            label={`Quantity on hand${product || batch ? ` (${unitLabels[(product ?? batch)!.unit]})` : ''}`}
            type="number"
            step="any"
            min={0}
            hint={batch && batch.quantityReserved > 0 ? `${formatQuantity(batch.quantityReserved, batch.unit)} is held and cannot be counted away.` : undefined}
            error={errors.quantityOnHand?.message}
            {...register('quantityOnHand')}
          />
          <Field label="Price per pack (LKR)" type="number" step="0.01" min={0} error={errors.unitPrice?.message} {...register('unitPrice')} />
        </div>

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" loading={mutation.isPending}>
            {batch ? 'Save correction' : 'Add to shelf'}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
