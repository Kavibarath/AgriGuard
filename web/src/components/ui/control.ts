import { cn } from '@/lib/utils'

/** Shared look of every text-like control: warm white, strong hairline, brand border on focus. */
export const controlClass = cn(
  'block h-9 w-full rounded-md border bg-surface-card px-3 text-sm text-stone-900',
  'placeholder:text-stone-500 disabled:cursor-not-allowed disabled:bg-surface-sunken disabled:text-stone-600',
  'focus-visible:border-brand-600',
)
