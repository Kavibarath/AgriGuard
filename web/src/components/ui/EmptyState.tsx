import type { ReactNode } from 'react'

/**
 * Shown when a list has no rows. Says what the screen is for and offers the action that fills
 * it — an empty table with no explanation looks broken, especially to a farmer on their first
 * login. Distinguish "nothing yet" from "nothing matched your filter" at the call site.
 */
export function EmptyState({ title, description, action }: { title: string; description?: string; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center gap-2 rounded-lg border border-dashed border-stone-300 bg-white px-6 py-12 text-center">
      <p className="font-medium text-stone-900">{title}</p>
      {description && <p className="max-w-sm text-sm text-stone-600">{description}</p>}
      {action && <div className="mt-2">{action}</div>}
    </div>
  )
}
