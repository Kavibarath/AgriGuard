import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

/**
 * The band at the top of every console page: the title in the display serif, one line of
 * context, and the page's actions on the right — about 96px, never a hero.
 */
export function PageHeader({
  title,
  description,
  actions,
  meta,
  className,
}: {
  title: ReactNode
  description?: ReactNode
  /** Buttons or links for the page as a whole. */
  actions?: ReactNode
  /** Status badges and the like, shown beside the title. */
  meta?: ReactNode
  className?: string
}) {
  return (
    <header className={cn('flex min-h-24 flex-wrap items-center justify-between gap-x-6 gap-y-3 py-5', className)}>
      <div className="min-w-0">
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
          <h1 className="font-display text-[28px] leading-tight font-semibold text-stone-900 sm:text-3xl">{title}</h1>
          {meta}
        </div>
        {description && <p className="mt-1 max-w-3xl text-sm text-stone-600">{description}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </header>
  )
}

/** A panel: white card, subtle hairline, 16–20px padding. `raised` for the cards that carry a verdict or a figure. */
export function Panel({
  children,
  className,
  raised = false,
  as: Tag = 'section',
  ...props
}: {
  children: ReactNode
  className?: string
  raised?: boolean
  as?: 'section' | 'div' | 'aside'
} & React.AriaAttributes) {
  return (
    <Tag className={cn('rounded-xl border border-border-subtle bg-surface-card p-4 sm:p-5', raised && 'card-raised', className)} {...props}>
      {children}
    </Tag>
  )
}

/** A panel's heading row: serif title, optional context line, and whatever sits on the right. */
export function PanelHeader({
  id,
  title,
  description,
  aside,
  icon,
}: {
  id?: string
  title: ReactNode
  description?: ReactNode
  aside?: ReactNode
  icon?: ReactNode
}) {
  return (
    <header className="mb-3 flex flex-wrap items-start justify-between gap-x-4 gap-y-2">
      <div className="flex min-w-[14rem] flex-1 items-start gap-2">
        {icon && <span className="mt-0.5 text-brand-600">{icon}</span>}
        <div className="min-w-0">
          <h2 id={id} className="font-display text-xl leading-tight font-semibold text-stone-900">
            {title}
          </h2>
          {description && <p className="mt-0.5 text-sm text-stone-600">{description}</p>}
        </div>
      </div>
      {aside && <div className="shrink-0">{aside}</div>}
    </header>
  )
}
