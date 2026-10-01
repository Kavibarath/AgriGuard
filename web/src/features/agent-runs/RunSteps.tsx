import { Panel, PanelHeader } from '@/components/layout/PageHeader'
import { AlertTriangle } from '@/components/icons'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { stepStatusTone } from '@/components/ui/status-tones'
import { cn } from '@/lib/utils'
import { AgentGlyph } from './agent-identity'
import { agentIdentity } from './agents'
import { formatDuration } from './format'
import type { AgentRun, AgentRunStep, Diagnosis } from './types'

/**
 * The plan tree: the Coordinator's plan, then each agent's step as it actually ran, joined by a
 * rail so the order reads top to bottom. Each node carries its agent's glyph, name, duration and
 * status; retries and errors sit on the node they belong to.
 */
export function RunSteps({ run }: { run: AgentRun }) {
  const diagnosis = run.steps.find((s) => s.agentRole === 'Diagnosis' && s.status === 'Succeeded')?.output as Diagnosis | undefined

  return (
    <Panel aria-labelledby="steps-heading">
      <PanelHeader id="steps-heading" title="Plan and steps" description="What the Coordinator planned, and what each agent did." />

      {run.plan && (
        <div className="mb-4 rounded-lg bg-surface-sunken p-3">
          <h3 className="flex items-center gap-1.5 text-sm font-semibold text-stone-800">
            <AgentGlyph role="Coordinator" size="sm" />
            Coordinator's plan
          </h3>
          <ol className="mt-2 space-y-1.5 text-sm">
            {run.plan.steps.map((step) => (
              <li key={step.seq} className="grid grid-cols-[1.25rem_1fr] gap-x-1.5">
                <span className="text-right font-semibold text-stone-600 tabular-nums">{step.seq}.</span>
                <span className="text-stone-700">
                  <span className={cn('font-semibold', agentIdentity[step.agent]?.ink ?? 'text-stone-900')}>{step.agent}</span> — {step.goal}
                </span>
              </li>
            ))}
          </ol>
        </div>
      )}

      {run.steps.length === 0 ? (
        <p className="text-sm text-stone-600">The agent has not started yet.</p>
      ) : (
        <ol className="relative">
          {run.steps.map((step, index) => (
            <li key={step.sequenceNo} className="relative pb-4 pl-10 last:pb-0">
              {/* The rail between this node and the next. */}
              {index < run.steps.length - 1 && <span aria-hidden="true" className="absolute top-8 bottom-0 left-[13px] w-px bg-border-strong" />}
              <AgentGlyph role={step.agentRole} className="absolute top-0 left-0" />
              <Step step={step} />
              {step.agentRole === 'Diagnosis' && diagnosis && <DiagnosisDetail diagnosis={diagnosis} />}
            </li>
          ))}
        </ol>
      )}
    </Panel>
  )
}

function Step({ step }: { step: AgentRunStep }) {
  return (
    <div>
      <div className="flex flex-wrap items-start justify-between gap-x-2 gap-y-1">
        <div className="min-w-0">
          <p className={cn('font-semibold', agentIdentity[step.agentRole].ink)}>{step.agentRole}</p>
          <p className="text-xs text-stone-600">{agentIdentity[step.agentRole].description}</p>
        </div>
        <div className="flex items-center gap-2">
          {step.durationMs !== null && <span className="text-xs text-stone-600 tabular-nums">{formatDuration(step.durationMs)}</span>}
          <StatusBadge label={step.status} tone={stepStatusTone[step.status]} />
        </div>
      </div>
      {step.retryCount > 0 && (
        <p className="mt-1.5 flex items-start gap-1 text-xs font-medium text-warning-800">
          <AlertTriangle size={14} className="mt-px shrink-0 text-warning" />
          {step.retryCount} tool call {step.retryCount === 1 ? 'attempt' : 'attempts'} failed and {step.retryCount === 1 ? 'was' : 'were'} retried.
        </p>
      )}
      {step.errorMessage && <p className="mt-1 text-xs font-medium text-danger-800">{step.errorMessage}</p>}
    </div>
  )
}

function DiagnosisDetail({ diagnosis }: { diagnosis: Diagnosis }) {
  return (
    <div className="mt-2 space-y-2.5 rounded-lg border border-border-subtle bg-surface-sunken p-3 text-sm">
      <ul className="space-y-2" aria-label="Candidate diagnoses">
        {diagnosis.candidates.map((candidate) => {
          const percent = Math.round(candidate.confidence * 100)
          const primary = candidate.pathogen_code === diagnosis.primary_pathogen_code
          return (
            <li key={candidate.pathogen_code}>
              <div className="flex items-baseline justify-between gap-2">
                <span className={primary ? 'font-semibold text-stone-900' : 'text-stone-700'}>
                  {candidate.pathogen_code}
                  {primary && <span className="ml-1 text-xs font-medium text-agent-diagnosis">(primary)</span>}
                </span>
                <span className="text-xs font-semibold text-stone-700 tabular-nums">{percent}%</span>
              </div>
              <div className="mt-1 h-1.5 w-full rounded-full bg-surface-inset" aria-hidden="true">
                <div className={cn('h-1.5 rounded-full', primary ? 'bg-agent-diagnosis' : 'bg-stone-400')} style={{ width: `${percent}%` }} />
              </div>
              <p className="mt-1 text-xs text-stone-600">{candidate.evidence.join('; ')}</p>
            </li>
          )
        })}
      </ul>
      <p className="text-xs text-stone-700">
        <span className="font-semibold">Agent's reasoning:</span> {diagnosis.reasoning}
      </p>
    </div>
  )
}
