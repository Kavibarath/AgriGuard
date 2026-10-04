import { StatusBadge } from '@/components/ui/StatusBadge'
import type { Triage } from './types'

/**
 * The Coordinator's triage: whether a product can treat the diagnosis, or a person must look first.
 * "Decided by a safety rule" means a hard stop in code (no approved product, an uncertain
 * diagnosis) made the call, whatever the model thought.
 */
export function TriageCard({ triage }: { triage: Triage }) {
  const treat = triage.route === 'TREAT'
  return (
    <section aria-labelledby="triage-heading" className="space-y-3 rounded-lg border border-stone-200 bg-white p-4">
      <header className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <h2 id="triage-heading" className="font-medium text-stone-900">
            Triage
          </h2>
          <p className="text-xs text-stone-500">Decided by {triage.decided_by === 'rules' ? 'a safety rule, not the model' : 'the Coordinator agent'}.</p>
        </div>
        <StatusBadge label={treat ? 'Treat with a product' : 'Hand to an agronomist'} tone={treat ? 'done' : 'warning'} />
      </header>
      <p className="text-sm text-stone-700">{triage.reason}</p>
      {triage.farmer_advice.length > 0 && (
        <div className="space-y-1">
          <h3 className="text-sm font-medium text-stone-900">Advice sent to the farmer</h3>
          <ul className="list-disc space-y-0.5 pl-5 text-sm text-stone-700">
            {triage.farmer_advice.map((tip) => (
              <li key={tip}>{tip}</li>
            ))}
          </ul>
          <p className="text-xs text-stone-500">Non-chemical steps only: any tip naming a pesticide or a dose is removed before it is sent.</p>
        </div>
      )}
    </section>
  )
}
