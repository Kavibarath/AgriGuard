import { StatusBadge } from '@/components/ui/StatusBadge'
import { ruleStatusTone, verdictTone } from '@/components/ui/status-tones'
import type { RuleStatus, Verdict } from './types'

const ruleStatusLabels: Record<RuleStatus, string> = {
  Passed: 'Passed',
  Failed: 'Failed',
  NotEvaluated: 'Not checked',
}

/**
 * Verdicts are stored as the validator wrote them, so an older run can carry a status this build
 * does not name (runs validated before the rules engine was consolidated say "Skipped"). Show it
 * as it is, flagged, rather than as an empty badge.
 */
const statusLabel = (status: string) => ruleStatusLabels[status as RuleStatus] ?? status
const statusTone = (status: string) => ruleStatusTone[status] ?? 'warning'

/**
 * Every rule, not just the failures (§7): the agronomist sees what was checked, what passed and
 * what could not be checked — "not checked" is shown as a caution, never as a pass.
 */
export function VerdictCard({ verdict }: { verdict: Verdict }) {
  return (
    <section aria-labelledby="verdict-heading" className="space-y-3 rounded-lg border border-stone-200 bg-white p-4">
      <header className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <h2 id="verdict-heading" className="font-medium text-stone-900">
            Safety rules
          </h2>
          <p className="text-xs text-stone-500">
            Checked by the deterministic validator (rules V1–V11), not by the model — and checked again when you approve.
          </p>
        </div>
        <StatusBadge label={verdict.outcome} tone={verdictTone[verdict.outcome]} />
      </header>

      <p className="text-sm text-stone-700">{verdict.summary}</p>

      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <caption className="sr-only">Validation rule results</caption>
          <thead className="border-b border-stone-200 text-left text-xs uppercase tracking-wide text-stone-500">
            <tr>
              <th scope="col" className="py-1.5 pr-2 font-medium">Rule</th>
              <th scope="col" className="py-1.5 pr-2 font-medium">Result</th>
              <th scope="col" className="py-1.5 font-medium">Detail</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-stone-100">
            {verdict.results.map((rule) => (
              <tr key={rule.code} className="align-top">
                <td className="py-2 pr-2">
                  <span className="font-medium text-stone-900">{rule.code}</span>
                  <span className="block text-xs text-stone-500">{rule.name}</span>
                </td>
                <td className="py-2 pr-2">
                  <StatusBadge label={statusLabel(rule.status)} tone={statusTone(rule.status)} />
                  {rule.status === 'Failed' && (
                    <span className="mt-1 block text-xs text-stone-500">{rule.severity === 'Reject' ? 'Ends the run' : 'Can be revised'}</span>
                  )}
                </td>
                <td className="py-2 text-stone-700">
                  {rule.message}
                  {rule.evidence && <code className="mt-0.5 block text-xs text-stone-500">{rule.evidence}</code>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}
