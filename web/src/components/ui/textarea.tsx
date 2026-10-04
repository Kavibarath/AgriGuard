import { useId, type ReactNode, type TextareaHTMLAttributes } from 'react'
import { cn } from '@/lib/utils'
import { controlClass } from './control'
import { FieldError } from './field'

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
        className={cn(controlClass, 'h-auto py-2 leading-6', error ? 'border-danger' : 'border-border-strong', className)}
        {...props}
      />
      {hint && !error && (
        <p id={hintId} className="text-xs text-stone-600">
          {hint}
        </p>
      )}
      {error && <FieldError id={errorId}>{error}</FieldError>}
    </div>
  )
}
