// Mirrors the API's case and agent-run DTOs. Enums arrive as names.
//
// Three payloads are stored as JSON by the backend and passed through unchanged, so they keep the
// shape their writer gave them: the plan and the proposal are the agent's (snake_case, from
// agent/app/contracts.py); the verdict is Component A's validator's (camelCase).

export type CaseStatus =
  | 'Submitted'
  | 'AgentProcessing'
  | 'PendingApproval'
  | 'Prescribed'
  | 'Rejected'
  | 'AwaitingManualReview'
  | 'Closed'

export type CaseSeverity = 'Low' | 'Medium' | 'High' | 'Critical'

export type AgentRunStatus =
  | 'Planning'
  | 'Diagnosing'
  | 'Drafting'
  | 'Validating'
  | 'RevisionRequested'
  | 'PendingApproval'
  | 'Approved'
  | 'Executing'
  | 'Completed'
  | 'Rejected'
  | 'Failed'
  | 'TimedOut'

export type AgentRole = 'Coordinator' | 'Diagnosis' | 'Action' | 'Validation'

export type StepStatus = 'Pending' | 'Running' | 'Succeeded' | 'Failed' | 'Skipped'

export type DecisionType = 'Approve' | 'Reject' | 'Revise'

/** The agent is still working: the console keeps polling while a run is in one of these. */
export const workingStatuses: readonly AgentRunStatus[] = ['Planning', 'Diagnosing', 'Drafting', 'Validating', 'RevisionRequested']

export const runStatusLabels: Record<AgentRunStatus, string> = {
  Planning: 'Planning',
  Diagnosing: 'Diagnosing',
  Drafting: 'Drafting proposal',
  Validating: 'Validating',
  RevisionRequested: 'Revising',
  PendingApproval: 'Awaiting approval',
  Approved: 'Approved',
  Executing: 'Executing',
  Completed: 'Prescribed',
  Rejected: 'Rejected',
  Failed: 'Failed',
  TimedOut: 'Timed out',
}

export const caseStatusLabels: Record<CaseStatus, string> = {
  Submitted: 'Submitted',
  AgentProcessing: 'Agent working',
  PendingApproval: 'Awaiting approval',
  Prescribed: 'Prescribed',
  Rejected: 'Rejected',
  AwaitingManualReview: 'Manual review',
  Closed: 'Closed',
}

export interface CaseSummary {
  id: string
  referenceNo: string
  status: CaseStatus
  severity: CaseSeverity
  farmerId: string
  farmerName: string
  plotId: string
  plotCode: string
  cropCycleId: string
  cropId: string
  cropName: string
  stage: string
  districtId: string
  districtName: string
  symptomCodes: string[]
  assignedAgronomistId: string | null
  createdAt: string
  updatedAt: string
  latestRunId: string | null
  latestRunStatus: AgentRunStatus | null
}

export interface CaseDetail {
  id: string
  referenceNo: string
  status: CaseStatus
  severity: CaseSeverity
  farmerName: string
  plotCode: string
  plotAreaHectares: number
  cropName: string
  stage: string
  districtName: string
  symptoms: { code: string; label: string }[]
  /** Untrusted text exactly as the farmer typed it. Render as text, never as markup. */
  farmerNote: string | null
  reportedLatitude: number
  reportedLongitude: number
  createdAt: string
}

// ── Stored agent payloads ────────────────────────────────────────────────────

export interface PlanStep {
  seq: number
  agent: AgentRole
  goal: string
  success_criteria: string
}

export interface Plan {
  steps: PlanStep[]
}

export interface Diagnosis {
  candidates: { pathogen_code: string; confidence: number; evidence: string[] }[]
  primary_pathogen_code: string
  reasoning: string
}

export interface Proposal {
  product_id: string
  dose_per_hectare: number
  total_quantity: number
  spray_date: string
  dealer_id: string | null
  /** The model's own words. Shown as its explanation, never as instructions. */
  justification: string
}

export type RuleStatus = 'Passed' | 'Failed' | 'NotEvaluated'

export interface RuleResult {
  code: string
  name: string
  status: RuleStatus
  severity: 'Reject' | 'Revise'
  message: string
  evidence: string | null
}

export interface Verdict {
  outcome: 'Approved' | 'Revise' | 'Rejected'
  summary: string
  results: RuleResult[]
}

// ── Runs ─────────────────────────────────────────────────────────────────────

export interface AgentRunStep {
  sequenceNo: number
  agentRole: AgentRole
  goal: string
  status: StepStatus
  output: unknown
  errorMessage: string | null
  retryCount: number
  startedAt: string | null
  completedAt: string | null
  durationMs: number | null
}

export interface IssuedPrescription {
  id: string
  prescriptionNo: string
  productName: string
  dosePerHectare: number
  totalQuantity: number
  sprayDate: string
  earliestSafeHarvestDate: string
  instructions: string | null
  orderId: string
  orderNo: string
  dealerName: string
  packs: number
  orderTotal: number
}

export interface AgentRun {
  id: string
  caseId: string
  caseReferenceNo: string
  objective: string
  status: AgentRunStatus
  plan: Plan | null
  proposal: Proposal | null
  verdict: Verdict | null
  finalOutcome: unknown
  failureReason: string | null
  revisionCount: number
  createdAt: string
  startedAt: string | null
  completedAt: string | null
  steps: AgentRunStep[]
  proposedProductName: string | null
  prescription: IssuedPrescription | null
}

export type AgentEventType =
  | 'StatusChanged'
  | 'PlanCreated'
  | 'StepStarted'
  | 'StepCompleted'
  | 'ToolCalled'
  | 'ToolFailed'
  | 'LlmCalled'
  | 'ValidationResult'
  | 'RetryAttempted'
  | 'InjectionFlagged'
  | 'ApprovalRequested'
  | 'ApprovalDecided'
  | 'RunFailed'
  | 'RunCompleted'

export interface AgentRunEvent {
  id: string
  eventType: AgentEventType
  agentRole: AgentRole | null
  toolName: string | null
  payload: Record<string, unknown> | null
  durationMs: number | null
  occurredAt: string
  correlationId: string | null
}

export interface DecisionResult {
  decisionId: string
  runId: string
  caseId: string
  decision: DecisionType
  reason: string | null
  decidedAt: string
  runStatus: AgentRunStatus
  caseStatus: CaseStatus
  prescription: IssuedPrescription | null
  replayed: boolean
}
