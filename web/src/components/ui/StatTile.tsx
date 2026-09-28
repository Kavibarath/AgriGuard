import type { ReactNode } from 'react'

/** One headline number with its label and a line of context. A number, not a chart. */
export function StatTile({ label, value, detail }: { label: string; value: ReactNode; detail?: ReactNode }) {
  return (
    <div className="rounded-lg border border-stone-200 bg-white p-4">
      <p className="text-xs font-medium uppercase tracking-wide text-stone-500">{label}</p>
      <div className="mt-1 text-2xl font-semibold text-stone-900">{value}</div>
      {detail && <div className="mt-1 text-sm text-stone-600">{detail}</div>}
    </div>
  )
}
