import type { ReactNode } from 'react'
import { Panel, PanelHeader } from '@/components/layout/PageHeader'
import { AlertTriangle, CheckCircle, DashedCircle, StopOctagon } from '@/components/icons'
import { cn } from '@/lib/utils'
import { AgentGlyph } from './agent-identity'
import type { RuleResult, SafetyReview, Verdict } from './types'

type IconType = (p: { size?: number; className?: string }) => ReactNode

/** How each result reads: a word, an icon shape and a colour — never the colour alone. */
function reading(rule: RuleResult): { label: string; Icon: IconType; ink: string; cell: string } {
  if (rule.status === 'Passed') return { label: 'Passed', Icon: CheckCircle, ink: 'text-success', cell: 'bg-success-50 ring-success-200' }
  if (rule.status === 'Failed')
    return rule.severity === 'Reject'
      ? { label: 'Failed', Icon: StopOctagon, ink: 'text-danger', cell: 'bg-danger-50 ring-danger-200' }
      : { label: 'Failed', Icon: AlertTriangle, ink: 'text-warning', cell: 'bg-warning-50 ring-warning-200' }
  // "Not checked" is a caution, never a pass. Verdicts are stored as the validator wrote them, so
  // an older run can carry a status this build does not name ("Skipped"): shown as it is.
  return {
    label: rule.status === 'NotEvaluated' ? 'Not checked' : rule.status,
    Icon: DashedCircle,
    ink: 'text-warning',
    cell: 'bg-warning-50 ring-warning-200',
  }
}

const outcomes: Record<Verdict['outcome'], { label: string; Icon: IconType; stamp: string; note: string }> = {
  Approved: { label: 'Approved', Icon: CheckCircle, stamp: 'border-success-200 bg-success-50 text-success-800', note: 'Safe to put before an agronomist.' },
  Revise: { label: 'Revise', Icon: AlertTriangle, stamp: 'border-warning-200 bg-warning-50 text-warning-800', note: 'A rule failed that the agent can fix.' },
  Rejected: { label: 'Rejected', Icon: StopOctagon, stamp: 'border-danger-200 bg-danger-50 text-danger-800', note: 'A hard stop failed; nothing can be issued.' },
}

/**
 * The safety report: every rule V1–V11, not just the failures (§7). A strip gives the eleven
 * results at a glance; the table beneath gives each rule's result, what a failure would mean, and
 * the evidence. A failed hard stop (a rule whose failure ends the run, such as V2 unapproved
 * product or V5 pre-harvest interval) is marked with an octagon, a red rule and the words
 * "Hard stop", so it cannot be mistaken for a revisable failure.
 */
export function VerdictCard({ verdict, review }: { verdict: Verdict; review?: SafetyReview | null }) {
  const outcome = outcomes[verdict.outcome] ?? {
    label: verdict.outcome,
    Icon: DashedCircle,
    stamp: 'border-warning-200 bg-warning-50 text-warning-800',
    note: 'An outcome this console does not recognise.',
  }
  const passed = verdict.results.filter((r) => r.status === 'Passed').length
  const failed = verdict.results.filter((r) => r.status === 'Failed').length
  const unchecked = verdict.results.length - passed - failed

  return (
    <Panel raised aria-labelledby="verdict-heading">
      <PanelHeader
        id="verdict-heading"
        title="Safety rules"
        description="Checked by the deterministic validator (rules V1–V11), not by the model — and checked again when you approve."
        aside={
          <div className={cn('flex items-center gap-2 rounded-lg border px-3 py-1.5', outcome.stamp)}>
            <outcome.Icon size={20} />
            <div className="leading-tight">
              <p className="text-sm font-bold">{outcome.label}</p>
              <p className="text-xs">{outcome.note}</p>
            </div>
          </div>
        }
      />

      {/* The eleven results at a glance; the table below says the same in words. */}
      <ol aria-hidden="true" className="grid grid-cols-6 gap-1.5 sm:grid-cols-11">
        {verdict.results.map((rule) => {
          const r = reading(rule)
          return (
            <li key={rule.code} className={cn('flex flex-col items-center gap-0.5 rounded-md py-1.5 ring-1 ring-inset', r.cell)}>
              <r.Icon size={16} className={r.ink} />
              <span className="text-xs font-semibold text-stone-800 tabular-nums">{rule.code}</span>
            </li>
          )
        })}
      </ol>

      <p className="mt-3 text-sm text-stone-800">{verdict.summary}</p>
      <p className="mt-0.5 text-xs text-stone-600">
        {passed} passed, {failed} failed, {unchecked} not checked.
      </p>

      <div className="mt-3 overflow-x-auto rounded-lg border border-border-subtle">
        <table className="w-full border-separate border-spacing-0 text-sm">
          <caption className="sr-only">Validation rule results</caption>
          <thead className="bg-surface-sunken text-left text-xs text-stone-600">
            <tr>
              <th scope="col" className="h-9 border-b border-border-subtle px-3 font-semibold">Rule</th>
              <th scope="col" className="h-9 border-b border-border-subtle px-3 font-semibold">Result</th>
              <th scope="col" className="hidden h-9 border-b border-border-subtle px-3 font-semibold md:table-cell">On failure</th>
              <th scope="col" className="h-9 border-b border-border-subtle px-3 font-semibold">Detail</th>
            </tr>
          </thead>
          <tbody className="[&>tr:last-child>td]:border-b-0">
            {verdict.results.map((rule) => {
              const r = reading(rule)
              const hardStop = rule.status === 'Failed' && rule.severity === 'Reject'
              return (
                <tr key={rule.code} className={cn('align-top', hardStop ? 'bg-danger-50' : rule.status === 'Failed' ? 'bg-warning-50' : 'even:bg-surface-sunken/60')}>
                  <td className={cn('border-b border-border-subtle px-3 py-2.5', hardStop && 'shadow-[inset_4px_0_0_var(--color-danger)]')}>
                    <span className="inline-block min-w-8 rounded bg-surface-inset px-1.5 py-0.5 text-center text-xs font-bold text-stone-900 tabular-nums">
                      {rule.code}
                    </span>
                    <span className="mt-1 block text-xs text-stone-600">{rule.name}</span>
                  </td>
                  <td className="border-b border-border-subtle px-3 py-2.5 whitespace-nowrap">
                    <span className={cn('inline-flex items-center gap-1 text-sm font-semibold', hardStop ? 'text-danger-800' : 'text-stone-800')}>
                      <r.Icon size={16} className={cn('shrink-0', r.ink)} />
                      {r.label}
                    </span>
                    {hardStop && <span className="mt-0.5 block text-xs font-bold text-danger-800">Hard stop: ends the run</span>}
                    {rule.status === 'Failed' && !hardStop && <span className="mt-0.5 block text-xs text-warning-800">The agent can revise this</span>}
                  </td>
                  <td className="hidden border-b border-border-subtle px-3 py-2.5 text-xs whitespace-nowrap text-stone-700 md:table-cell">
                    {/* What a failure of this rule would mean; red only when it has actually failed. */}
                    {rule.severity === 'Reject' ? (
                      <span className="inline-flex items-center gap-1 font-semibold">
                        <StopOctagon size={14} className={hardStop ? 'text-danger' : 'text-stone-600'} /> Hard stop
                      </span>
                    ) : (
                      <span className="inline-flex items-center gap-1">
                        <AlertTriangle size={14} className={rule.status === 'Failed' ? 'text-warning' : 'text-stone-600'} /> Revisable
                      </span>
                    )}
                  </td>
                  <td className="border-b border-border-subtle px-3 py-2.5 text-stone-800">
                    {rule.message}
                    {rule.evidence && <code className="mt-1 block font-mono text-xs break-all text-stone-600">{rule.evidence}</code>}
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>

      {review && (
        <div className="mt-4 space-y-2 rounded-lg border border-slate-200 bg-slate-50 p-3" aria-labelledby="review-heading">
          <h3 id="review-heading" className="flex items-center gap-1.5 text-sm font-semibold text-stone-900">
            <AgentGlyph role="Validation" size="sm" />
            Validation agent's reading
          </h3>
          <p className="text-sm text-stone-800">{review.explanation}</p>
          {review.fixes.length > 0 && (
            <ul className="space-y-1 text-sm text-stone-800">
              {review.fixes.map((fix) => (
                <li key={fix.rule_code}>
                  <span className="font-semibold text-stone-900">{fix.rule_code}</span>: {fix.fix}
                  {fix.suggested_value && <span className="text-stone-600"> (suggested {fix.suggested_value})</span>}
                </li>
              ))}
            </ul>
          )}
          <p className="text-xs text-stone-600">Written by the model from the rule results above. It explains them; it cannot change them.</p>
        </div>
      )}
    </Panel>
  )
}
