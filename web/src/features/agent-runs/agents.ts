import type { ReactNode } from 'react'
import { Compass, LensLeaf, ShieldCheck, Sprayer } from '@/components/icons'
import type { AgentRole, AgentRunStatus } from './types'

export interface AgentIdentity {
  /** What the agent is for, in one line — the "four distinct agents" story, shown next to its work. */
  description: string
  glyph: (props: { size?: number; className?: string }) => ReactNode
  /** Text and icon colour (4.5:1 or better on white). */
  ink: string
  /** The glyph's tile. */
  tile: string
  /** The tree's connector dot and rule. */
  rail: string
}

/**
 * Each agent keeps one hue and one glyph wherever it appears — the plan, the steps, the timeline
 * and the reviews — so a reader can follow who did what at a glance. The name is always written
 * beside the glyph: the colour is a reinforcement, never the only cue.
 */
export const agentIdentity: Record<AgentRole, AgentIdentity> = {
  Coordinator: {
    description: 'Reads the case and plans the run',
    glyph: Compass,
    ink: 'text-agent-coordinator',
    tile: 'bg-brand-50 ring-brand-200',
    rail: 'bg-agent-coordinator',
  },
  Diagnosis: {
    description: 'Ranks the likely pest or disease',
    glyph: LensLeaf,
    ink: 'text-agent-diagnosis',
    tile: 'bg-info-50 ring-info-200',
    rail: 'bg-agent-diagnosis',
  },
  Action: {
    description: 'Proposes a product, dose and spray date',
    glyph: Sprayer,
    ink: 'text-agent-action',
    tile: 'bg-earth-50 ring-earth-200',
    rail: 'bg-agent-action',
  },
  Validation: {
    description: 'Submits the proposal to the deterministic safety rules',
    glyph: ShieldCheck,
    ink: 'text-agent-validation',
    tile: 'bg-slate-50 ring-slate-200',
    rail: 'bg-agent-validation',
  },
}


/** Which agent holds a run in each working status, so a list can show who is busy with it. */
export const workingAgent: Partial<Record<AgentRunStatus, AgentRole>> = {
  Planning: 'Coordinator',
  Triaging: 'Coordinator',
  Diagnosing: 'Diagnosis',
  Drafting: 'Action',
  RevisionRequested: 'Action',
  Validating: 'Validation',
}
