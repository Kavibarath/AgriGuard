import type { Tone } from './StatusBadge'

/** Crop stages read as a progression: growing → nearly ready → finished. */
export const stageTone: Record<string, Tone> = {
  Sown: 'neutral',
  Vegetative: 'active',
  Flowering: 'active',
  FruitSet: 'active',
  PreHarvest: 'warning',
  Harvested: 'done',
}

export const plotStatusTone: Record<string, Tone> = {
  Active: 'active',
  Fallow: 'neutral',
  Retired: 'danger',
}

/** Agent runs: working → waiting on a person → finished well or badly. */
export const runStatusTone: Record<string, Tone> = {
  Planning: 'active',
  Diagnosing: 'active',
  Drafting: 'active',
  Validating: 'active',
  RevisionRequested: 'active',
  PendingApproval: 'warning',
  Approved: 'done',
  Executing: 'done',
  Completed: 'done',
  Rejected: 'danger',
  Failed: 'danger',
  TimedOut: 'danger',
}

export const caseStatusTone: Record<string, Tone> = {
  Submitted: 'neutral',
  AgentProcessing: 'active',
  PendingApproval: 'warning',
  Prescribed: 'done',
  Rejected: 'danger',
  AwaitingManualReview: 'warning',
  Closed: 'neutral',
}

export const severityTone: Record<string, Tone> = {
  Low: 'neutral',
  Medium: 'neutral',
  High: 'warning',
  Critical: 'danger',
}

export const stepStatusTone: Record<string, Tone> = {
  Pending: 'neutral',
  Running: 'active',
  Succeeded: 'done',
  Failed: 'danger',
  Skipped: 'neutral',
}

/** Validation rules. "Not evaluated" is not a pass, so it is flagged rather than greyed out. */
export const ruleStatusTone: Record<string, Tone> = {
  Passed: 'done',
  Failed: 'danger',
  NotEvaluated: 'warning',
}

export const verdictTone: Record<string, Tone> = {
  Approved: 'done',
  Revise: 'warning',
  Rejected: 'danger',
}
