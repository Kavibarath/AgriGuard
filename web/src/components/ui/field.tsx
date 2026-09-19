import { useId, type InputHTMLAttributes, type ReactNode } from 'react'
import { cn } from '@/lib/utils'

export interface FieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'id'> {
  label: string
  /** Validation message; when present the input is marked invalid and the message is announced. */
  error?: string
  hint?: ReactNode
  /** Passed through so react-hook-form's `register` can attach. */
  ref?: React.Ref<HTMLInputElement>
}

/** Labelled input with hint and error wired for screen readers. */
export function Field({ label, error, hint, className, ref, ...props }: FieldProps) {
  const id = useId()
  const errorId = `${id}-error`
  const hintId = `${id}-hint`

  return (
    <div className="space-y-1.5">
      <label htmlFor={id} className="block text-sm font-medium text-stone-800">
        {label}
      </label>
      <input
        id={id}
        ref={ref}
        aria-invalid={error ? true : undefined}
        aria-describedby={cn(error && errorId, hint && hintId) || undefined}
        className={cn(
          'block h-10 w-full rounded-md border bg-white px-3 text-sm shadow-xs',
          'placeholder:text-stone-400 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-1',
          error
            ? 'border-red-500 focus-visible:ring-red-500'
            : 'border-stone-300 focus-visible:ring-brand-500',
          className,
        )}
        {...props}
      />
      {hint && !error && (
        <p id={hintId} className="text-xs text-stone-500">
          {hint}
        </p>
      )}
      {error && (
        <p id={errorId} role="alert" className="text-xs text-red-600">
          {error}
        </p>
      )}
    </div>
  )
}
