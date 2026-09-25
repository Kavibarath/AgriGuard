import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Field } from '@/components/ui/field'
import { Modal } from '@/components/ui/Modal'
import { SelectField } from '@/components/ui/select'
import { ApiError, userMessage } from '@/lib/api'
import { useCreateFarm, useDistricts, useUpdateFarm } from './queries'
import type { Farm } from './types'

// Mirrors CreateFarmRequestValidator on the server. The server still re-checks: this is for speed
// of feedback, not for trust.
const schema = z.object({
  name: z.string().trim().min(1, 'Give the farm a name.').max(150),
  village: z.string().trim().max(150).optional(),
  districtId: z.string().min(1, 'Choose a district.'),
})

type FormValues = z.infer<typeof schema>

export function FarmFormModal({ open, onClose, farm }: { open: boolean; onClose: () => void; farm?: Farm }) {
  const districts = useDistricts()
  const create = useCreateFarm()
  const update = useUpdateFarm()
  const mutation = farm ? update : create

  const { register, handleSubmit, setError, reset, formState: { errors } } = useForm<FormValues>({
    resolver: zodResolver(schema),
    values: {
      name: farm?.name ?? '',
      village: farm?.village ?? '',
      districtId: farm?.districtId ?? '',
    },
  })

  const close = () => {
    mutation.reset()
    reset()
    onClose()
  }

  const onSubmit = handleSubmit(async (values) => {
    const input = { ...values, village: values.village || null }
    try {
      if (farm) await update.mutateAsync({ id: farm.id, ...input })
      else await create.mutateAsync(input)
      close()
    } catch (error) {
      // 400 carries per-field messages; put them on the fields. 409 (duplicate name) has no
      // field attached, so it belongs on the name input where the fix is.
      if (error instanceof ApiError && error.status === 400) {
        for (const [field, messages] of Object.entries(error.fieldErrors)) {
          if (field in schema.shape) setError(field as keyof FormValues, { message: messages[0] })
        }
      } else if (error instanceof ApiError && error.status === 409) {
        setError('name', { message: error.message })
      }
    }
  })

  const banner =
    mutation.error && !(mutation.error instanceof ApiError && [400, 409].includes(mutation.error.status))
      ? userMessage(mutation.error)
      : null

  return (
    <Modal open={open} onClose={close} title={farm ? 'Edit farm' : 'Register a farm'}>
      <form className="space-y-4" onSubmit={onSubmit} noValidate>
        {banner && <Alert tone="error">{banner}</Alert>}

        <Field label="Farm name" autoFocus error={errors.name?.message} {...register('name')} />
        <Field label="Village" hint="Optional" error={errors.village?.message} {...register('village')} />

        <SelectField label="District" error={errors.districtId?.message} {...register('districtId')}>
          <option value="">Choose a district…</option>
          {districts.data?.map((district) => (
            <option key={district.id} value={district.id}>
              {district.name} ({district.province})
            </option>
          ))}
        </SelectField>

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button type="submit" loading={mutation.isPending}>
            {farm ? 'Save changes' : 'Register farm'}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
