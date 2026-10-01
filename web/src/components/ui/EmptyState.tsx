import type { ReactNode } from 'react'

/**
 * Shown when a list has no rows. Says what the screen is for and offers the action that fills
 * it — an empty table with no explanation looks broken, especially to a farmer on their first
 * login. Distinguish "nothing yet" from "nothing matched your filter" at the call site.
 */
export function EmptyState({ title, description, action }: { title: string; description?: string; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center gap-2 rounded-xl border border-dashed border-border-strong bg-surface-card px-6 py-10 text-center">
      <EmptyField />
      <p className="font-display mt-1 text-lg font-semibold text-stone-900">{title}</p>
      {description && <p className="max-w-md text-sm text-stone-600">{description}</p>}
      {action && <div className="mt-2">{action}</div>}
    </div>
  )
}

/** A line drawing of an empty, furrowed plot with one seedling: nothing here yet. */
function EmptyField() {
  return (
    <svg width="112" height="56" viewBox="0 0 112 56" fill="none" aria-hidden="true" className="text-brand-300">
      <g stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
        <path d="M6 50h100" />
        <path d="M14 44c12-3 26-3 40 0s28 3 44 0M20 38c10-2.5 22-2.5 34 0s24 2.5 36 0M28 32c8-2 18-2 26 0s18 2 26 0" />
        <path d="M56 30V16" />
        <path d="M56 22c0-5-4-9-9-9 0 5 4 9 9 9ZM56 18c0-4.4 3.6-8 8-8 0 4.4-3.6 8-8 8Z" />
      </g>
    </svg>
  )
}
