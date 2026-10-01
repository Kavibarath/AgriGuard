import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

/**
 * One headline number with its label and a line of context. A number, not a chart. Raised, and
 * lifts on hover when it is a link (`interactive`). The figure is set in the display serif.
 */
export function StatTile({
  label,
  value,
  detail,
  icon,
  interactive = false,
  className,
}: {
  label: string
  value: ReactNode
  detail?: ReactNode
  icon?: ReactNode
  interactive?: boolean
  className?: string
}) {
  return (
    <div className={cn('card-raised h-full rounded-xl border border-border-subtle bg-surface-card px-4 py-3.5', interactive && 'is-interactive', className)}>
      <div className="flex items-start justify-between gap-2">
        <p className="text-sm font-medium text-stone-600">{label}</p>
        {icon && <span className="text-brand-600">{icon}</span>}
      </div>
      <div className="font-display mt-1 text-[28px] leading-tight font-semibold text-stone-900">{value}</div>
      {detail && <div className="mt-1 text-sm text-stone-600">{detail}</div>}
    </div>
  )
}
