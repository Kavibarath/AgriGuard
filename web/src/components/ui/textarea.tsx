import { useId, type ReactNode, type TextareaHTMLAttributes } from 'react'
import { cn } from '@/lib/utils'

export interface TextareaFieldProps extends Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, 'id'> {
  label: string
  error?: string
  hint?: ReactNode
}

/** Multi-line counterpart of Field: same label, hint and error wiring for screen readers. */
export function TextareaField({ label, error, hint, className, ...props }: TextareaFieldProps) {
  const id = useId()
  const errorId = `${id}-error`
  const hintId = `${id}-hint`

  return (
    <div className="space-y-1.5">
      <label htmlFor={id} className="block text-sm font-medium text-stone-800">
        {label}
      </label>
      <textarea
        id={id}
        rows={4}
        aria-invalid={error ? true : undefined}
        aria-describedby={cn(error && errorId, hint && hintId) || undefined}
        className={cn(
          'block w-full rounded-md border bg-white px-3 py-2 text-sm shadow-xs',
          'placeholder:text-stone-400 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-1',
          error ? 'border-red-500 focus-visible:ring-red-500' : 'border-stone-300 focus-visible:ring-brand-500',
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
