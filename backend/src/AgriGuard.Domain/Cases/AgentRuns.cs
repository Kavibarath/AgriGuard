// Component B — durable Agentic AI workflow state (§9 "Shared state").
// These EF-owned tables are the system of record. The Python agent service has no
// database access; it reports progress through ASP.NET Core internal callbacks.
using AgriGuard.Domain.Common;
using AgriGuard.Domain.Identity;

namespace AgriGuard.Domain.Cases;

public enum AgentRunStatus
{
    Planning,
    Diagnosing,
    Drafting,
    Validating,
    RevisionRequested,
    PendingApproval,
    Approved,
    Executing,
    Completed,
    Rejected,
    Failed,
    TimedOut
}

public enum AgentRole { Coordinator, Diagnosis, Action, Validation }

public enum AgentStepStatus { Pending, Running, Succeeded, Failed, Skipped }

public enum AgentEventType
{
    StatusChanged,
    PlanCreated,
    StepStarted,
    StepCompleted,
    ToolCalled,
    ToolFailed,
    LlmCalled,
    ValidationResult,
    RetryAttempted,
    InjectionFlagged,
    ApprovalRequested,
    ApprovalDecided,
    RunFailed,
    RunCompleted
}

public enum ApprovalDecisionType { Approve, Reject, Revise }

public class AgentRun : AuditableEntity
{
    public Guid CaseId { get; set; }
    public CropCase Case { get; set; } = null!;

    public string Objective { get; set; } = string.Empty;
    public AgentRunStatus Status { get; set; } = AgentRunStatus.Planning;

    /// <summary>Structured plan from the Coordinator (jsonb).</summary>
    public string? PlanJson { get; set; }

    /// <summary>Latest validated prescription proposal awaiting approval (jsonb).</summary>
    public string? ProposalJson { get; set; }

    /// <summary>Latest deterministic validation verdict (jsonb).</summary>
    public string? VerdictJson { get; set; }

    /// <summary>Final outcome or safe-failure record (jsonb).</summary>
    public string? FinalOutcomeJson { get; set; }

    public string? FailureReason { get; set; }

    /// <summary>Action → Validation loops performed; capped at 2 before safe failure.</summary>
    public int RevisionCount { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Guards the approve/reject/revise decision against double submission.</summary>
    public uint Version { get; set; }

    public ICollection<AgentRunStep> Steps { get; set; } = new List<AgentRunStep>();
    public ICollection<AgentRunEvent> Events { get; set; } = new List<AgentRunEvent>();
    public ICollection<ApprovalDecision> Decisions { get; set; } = new List<ApprovalDecision>();

    public bool IsTerminal => Status is AgentRunStatus.Completed or AgentRunStatus.Rejected
        or AgentRunStatus.Failed or AgentRunStatus.TimedOut;
}

public class AgentRunStep : Entity
{
    public Guid AgentRunId { get; set; }
    public AgentRun AgentRun { get; set; } = null!;

    public int SequenceNo { get; set; }
    public AgentRole AgentRole { get; set; }
    public string Goal { get; set; } = string.Empty;
    public AgentStepStatus Status { get; set; } = AgentStepStatus.Pending;

    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public string? ErrorMessage { get; set; }

    public int RetryCount { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? DurationMs { get; set; }
}

/// <summary>Append-only execution timeline. Stores redacted payloads, never raw prompts or hidden reasoning (§6).</summary>
public class AgentRunEvent : Entity
{
    public Guid AgentRunId { get; set; }
    public AgentRun AgentRun { get; set; } = null!;

    public AgentEventType EventType { get; set; }
    public AgentRole? AgentRole { get; set; }
    public string? ToolName { get; set; }
    public string? PayloadJson { get; set; }
    public int? DurationMs { get; set; }
    public DateTime OccurredAt { get; set; }

    /// <summary>Links events to API log lines for tracing.</summary>
    public string? CorrelationId { get; set; }
}

public class ApprovalDecision : Entity
{
    public Guid AgentRunId { get; set; }
    public AgentRun AgentRun { get; set; } = null!;

    public Guid DecidedByUserId { get; set; }
    public User DecidedBy { get; set; } = null!;

    public ApprovalDecisionType Decision { get; set; }
    public string? Reason { get; set; }
    public DateTime DecidedAt { get; set; }

    /// <summary>Client-supplied key; a replayed decision returns the original result (golden case G12).</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}
