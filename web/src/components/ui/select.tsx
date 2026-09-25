import { useId, type ReactNode, type SelectHTMLAttributes } from 'react'
import { cn } from '@/lib/utils'

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
        className={cn(
          'block h-10 w-full rounded-md border bg-white px-3 text-sm shadow-xs',
          'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-1',
          error ? 'border-red-500 focus-visible:ring-red-500' : 'border-stone-300 focus-visible:ring-brand-500',
          className,
        )}
        {...props}
      >
        {children}
      </select>
      {error && (
        <p id={errorId} role="alert" className="text-xs text-red-600">
          {error}
        </p>
      )}
    </div>
  )
}
