import type { ReactNode } from 'react'
import { userMessage } from '@/lib/api'
import { Alert } from './alert'
import { Button } from './button'
import { Spinner } from './Spinner'

/**
 * The loading / error / success states of a query in one place (§7 "every data view implements
 * loading / empty / success / error states"), so no screen forgets one and every screen fails
 * the same way. Errors go through userMessage, which keeps server internals off the page.
 */
export function AsyncBoundary({
  isPending,
  error,
  onRetry,
  children,
  label = 'Loading',
}: {
  isPending: boolean
  error: unknown
  onRetry?: () => void
  children: ReactNode
  label?: string
}) {
  if (isPending) {
    return (
      <div className="flex items-center justify-center gap-3 py-12 text-sm text-stone-600">
        <Spinner label={label} />
        {label}…
      </div>
    )
  }

  if (error) {
    return (
      <Alert tone="error" title="Could not load this">
        <p>{userMessage(error)}</p>
        {onRetry && (
          <Button variant="secondary" className="mt-2 h-8" onClick={onRetry}>
            Try again
          </Button>
        )}
      </Alert>
    )
  }

  return <>{children}</>
}
