import { StatusBadge } from '@/components/ui/StatusBadge'
import { stepStatusTone } from '@/components/ui/status-tones'
import { formatDuration } from './format'
import type { AgentRole, AgentRun, AgentRunStep, Diagnosis } from './types'

/** What each of the four agents is for — the "distinct agent" story, shown next to its work. */
const roleDescriptions: Record<AgentRole, string> = {
  Coordinator: 'Reads the case and plans the run',
  Diagnosis: 'Ranks the likely pest or disease',
  Action: 'Proposes a product, dose and spray date',
  Validation: 'Submits the proposal to the deterministic safety rules',
}

/** The plan the Coordinator wrote, then each agent's step as it actually ran. */
export function RunSteps({ run }: { run: AgentRun }) {
  const diagnosis = run.steps.find((s) => s.agentRole === 'Diagnosis' && s.status === 'Succeeded')?.output as Diagnosis | undefined

  return (
    <section aria-labelledby="steps-heading" className="space-y-4 rounded-lg border border-stone-200 bg-white p-4">
      <h2 id="steps-heading" className="font-medium text-stone-900">
        Plan and steps
      </h2>

      {run.plan && (
        <div>
          <h3 className="text-xs font-medium uppercase tracking-wide text-stone-500">Coordinator's plan</h3>
          <ol className="mt-2 space-y-1 text-sm text-stone-700">
            {run.plan.steps.map((step) => (
              <li key={step.seq}>
                <span className="font-medium text-stone-900">{step.seq}. {step.agent}</span> — {step.goal}
              </li>
            ))}
          </ol>
        </div>
      )}

      {run.steps.length === 0 ? (
        <p className="text-sm text-stone-600">The agent has not started yet.</p>
      ) : (
        <ol className="space-y-3">
          {run.steps.map((step) => (
            <li key={step.sequenceNo}>
              <Step step={step} />
              {step.agentRole === 'Diagnosis' && diagnosis && <DiagnosisDetail diagnosis={diagnosis} />}
            </li>
          ))}
        </ol>
      )}
    </section>
  )
}

function Step({ step }: { step: AgentRunStep }) {
  return (
    <div className="rounded-md border border-stone-100 bg-stone-50 p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm">
          <span className="font-medium text-stone-900">{step.agentRole}</span>{' '}
          <span className="text-stone-500">· {roleDescriptions[step.agentRole]}</span>
        </p>
        <div className="flex items-center gap-2">
          {step.durationMs !== null && <span className="text-xs tabular-nums text-stone-500">{formatDuration(step.durationMs)}</span>}
          <StatusBadge label={step.status} tone={stepStatusTone[step.status]} />
        </div>
      </div>
      {step.retryCount > 0 && (
        <p className="mt-1 text-xs text-amber-800">
          {step.retryCount} tool call {step.retryCount === 1 ? 'attempt' : 'attempts'} failed and {step.retryCount === 1 ? 'was' : 'were'} retried.
        </p>
      )}
      {step.errorMessage && <p className="mt-1 text-xs text-red-700">{step.errorMessage}</p>}
    </div>
  )
}

function DiagnosisDetail({ diagnosis }: { diagnosis: Diagnosis }) {
  return (
    <div className="mt-2 space-y-2 pl-3 text-sm">
      <ul className="space-y-1.5" aria-label="Candidate diagnoses">
        {diagnosis.candidates.map((candidate) => {
          const percent = Math.round(candidate.confidence * 100)
          const primary = candidate.pathogen_code === diagnosis.primary_pathogen_code
          return (
            <li key={candidate.pathogen_code}>
              <div className="flex items-center gap-2">
                <span className={primary ? 'font-medium text-stone-900' : 'text-stone-700'}>
                  {candidate.pathogen_code}
                  {primary && <span className="ml-1 text-xs text-brand-700">(primary)</span>}
                </span>
                <span className="text-xs tabular-nums text-stone-500">{percent}%</span>
              </div>
              <div className="mt-0.5 h-1.5 w-full max-w-xs rounded bg-stone-200" aria-hidden="true">
                <div className="h-1.5 rounded bg-brand-500" style={{ width: `${percent}%` }} />
              </div>
              <p className="mt-0.5 text-xs text-stone-500">{candidate.evidence.join('; ')}</p>
            </li>
          )
        })}
      </ul>
      <p className="text-xs text-stone-600">
        <span className="font-medium">Agent's reasoning:</span> {diagnosis.reasoning}
      </p>
    </div>
  )
}
