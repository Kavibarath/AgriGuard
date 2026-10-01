import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

/** The filters above a list: one sunken band, fields side by side, so the table starts close under them. */
export function FilterBar({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div role="group" aria-label="Filters" className={cn('grid items-end gap-3 rounded-xl border border-border-subtle bg-surface-sunken/70 p-3', className)}>
      {children}
    </div>
  )
}
