import { useId, type InputHTMLAttributes, type ReactNode } from 'react'
import { AlertTriangle } from '@/components/icons'
import { cn } from '@/lib/utils'
import { controlClass } from './control'

export interface FieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'id'> {
  label: string
  /** Validation message; when present the input is marked invalid and the message is announced. */
  error?: string
  hint?: ReactNode
  /** The unit the value is in ("kg/ha", "days"), shown inside the box and read with the field. */
  unit?: string
  /** Passed through so react-hook-form's `register` can attach. */
  ref?: React.Ref<HTMLInputElement>
}

/** Labelled input with hint, unit and error wired for screen readers. */
export function Field({ label, error, hint, unit, className, ref, ...props }: FieldProps) {
  const id = useId()
  const errorId = `${id}-error`
  const hintId = `${id}-hint`
  const unitId = `${id}-unit`

  return (
    <div className="space-y-1.5">
      <label htmlFor={id} className="block text-sm font-medium text-stone-800">
        {label}
      </label>
      <div className="relative">
        <input
          id={id}
          ref={ref}
          aria-invalid={error ? true : undefined}
          aria-describedby={cn(unit && unitId, error && errorId, hint && hintId) || undefined}
          className={cn(controlClass, error ? 'border-danger' : 'border-border-strong', unit && 'pr-16', className)}
          {...props}
        />
        {unit && (
          <span id={unitId} className="pointer-events-none absolute inset-y-0 right-3 flex items-center text-xs font-medium text-stone-600">
            {unit}
          </span>
        )}
      </div>
      {hint && !error && (
        <p id={hintId} className="text-xs text-stone-600">
          {hint}
        </p>
      )}
      {error && <FieldError id={errorId}>{error}</FieldError>}
    </div>
  )
}

/** A field's validation message: announced, and marked with an icon as well as colour. */
export function FieldError({ id, children }: { id: string; children: ReactNode }) {
  return (
    <p id={id} role="alert" className="flex items-start gap-1 text-xs font-medium text-danger">
      <AlertTriangle size={14} className="mt-px shrink-0" />
      <span>{children}</span>
    </p>
  )
}
