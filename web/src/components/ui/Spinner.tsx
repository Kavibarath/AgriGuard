import { cn } from '@/lib/utils'

export function Spinner({ className, label = 'Loading' }: { className?: string; label?: string }) {
  return (
    <span
      role="status"
      aria-label={label}
      className={cn('inline-block size-5 animate-spin rounded-full border-2 border-stone-300 border-t-brand-600', className)}
    />
  )
}
