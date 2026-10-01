import { Panel, PanelHeader } from '@/components/layout/PageHeader'
import { Leaf } from '@/components/icons'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { AgentGlyph } from './agent-identity'
import type { Triage } from './types'

/**
 * The Coordinator's triage: whether a product can treat the diagnosis, or a person must look first.
 * "Decided by a safety rule" means a hard stop in code (no approved product, an uncertain
 * diagnosis) made the call, whatever the model thought.
 */
export function TriageCard({ triage }: { triage: Triage }) {
  const treat = triage.route === 'TREAT'
  return (
    <Panel aria-labelledby="triage-heading">
      <PanelHeader
        id="triage-heading"
        title="Triage"
        icon={<AgentGlyph role="Coordinator" />}
        description={`Decided by ${triage.decided_by === 'rules' ? 'a safety rule, not the model' : 'the Coordinator agent'}.`}
        aside={<StatusBadge label={treat ? 'Treat with a product' : 'Hand to an agronomist'} tone={treat ? 'done' : 'warning'} />}
      />
      <p className="text-sm text-stone-800">{triage.reason}</p>
      {triage.farmer_advice.length > 0 && (
        <div className="mt-3 rounded-lg bg-brand-50 p-3">
          <h3 className="text-sm font-semibold text-brand-800">Advice sent to the farmer</h3>
          <ul className="mt-1.5 space-y-1 text-sm text-stone-800">
            {triage.farmer_advice.map((tip) => (
              <li key={tip} className="flex items-start gap-1.5">
                <Leaf size={16} className="mt-0.5 shrink-0 text-brand-600" />
                <span>{tip}</span>
              </li>
            ))}
          </ul>
          <p className="mt-2 text-xs text-stone-600">Non-chemical steps only: any tip naming a pesticide or a dose is removed before it is sent.</p>
        </div>
      )}
    </Panel>
  )
}
