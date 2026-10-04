import { useId, type ReactNode, type SelectHTMLAttributes } from 'react'
import { cn } from '@/lib/utils'
import { controlClass } from './control'
import { FieldError } from './field'

export interface SelectFieldProps extends Omit<SelectHTMLAttributes<HTMLSelectElement>, 'id'> {
  label: string
  error?: string
  children: ReactNode
  ref?: React.Ref<HTMLSelectElement>
}

/** Labelled <select> matching Field's markup, so forms mix the two without drifting. */
export function SelectField({ label, error, className, children, ref, ...props }: SelectFieldProps) {
  const id = useId()
  const errorId = `${id}-error`

  return (
    <div className="space-y-1.5">
      <label htmlFor={id} className="block text-sm font-medium text-stone-800">
        {label}
      </label>
      <select
        id={id}
        ref={ref}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? errorId : undefined}
        className={cn(controlClass, 'pr-8', error ? 'border-danger' : 'border-border-strong', className)}
        {...props}
      >
        {children}
      </select>
      {error && <FieldError id={errorId}>{error}</FieldError>}
    </div>
  )
}
