// Component B — Crop Health Case Management & Agentic Workflow
using AgriGuard.Domain.Common;
using AgriGuard.Domain.Identity;
using AgriGuard.Domain.Reference;
using AgriGuard.Domain.Registry;

namespace AgriGuard.Domain.Cases;

public enum CaseStatus
{
    Submitted,
    AgentProcessing,
    PendingApproval,
    Prescribed,
    Rejected,
    AwaitingManualReview,
    Closed
}

public enum CaseSeverity { Low, Medium, High, Critical }

/// <summary>Named CropCase because "case" is a C# keyword.</summary>
public class CropCase : AuditableEntity
{
    /// <summary>Human-friendly reference shown in both apps, e.g. "AG-2026-000142".</summary>
    public string ReferenceNo { get; set; } = string.Empty;

    public Guid FarmerId { get; set; }
    public User Farmer { get; set; } = null!;

    public Guid PlotId { get; set; }
    public Plot Plot { get; set; } = null!;

    public Guid CropCycleId { get; set; }
    public CropCycle CropCycle { get; set; } = null!;

    /// <summary>Denormalised from the farm for fast district-scoped queue queries.</summary>
    public Guid DistrictId { get; set; }
    public District District { get; set; } = null!;

    public CaseStatus Status { get; set; } = CaseStatus.Submitted;
    public CaseSeverity Severity { get; set; }

    public List<string> SymptomCodes { get; set; } = new();

    /// <summary>
    /// UNTRUSTED free text from the farmer. Treat as data, never as instructions —
    /// this is the deliberate prompt-injection surface (golden case G7).
    /// </summary>
    public string? FarmerNote { get; set; }

    /// <summary>Where the phone was when the report was made (may differ from plot centroid).</summary>
    public decimal ReportedLatitude { get; set; }
    public decimal ReportedLongitude { get; set; }

    /// <summary>Set once an agronomist confirms a diagnosis; feeds the outbreak signal.</summary>
    public Guid? ConfirmedPathogenId { get; set; }
    public Pathogen? ConfirmedPathogen { get; set; }

    public Guid? AssignedAgronomistId { get; set; }
    public User? AssignedAgronomist { get; set; }

    public uint Version { get; set; }

    public ICollection<CaseAttachment> Attachments { get; set; } = new List<CaseAttachment>();
    public ICollection<AgentRun> AgentRuns { get; set; } = new List<AgentRun>();
}

public class CaseAttachment : Entity
{
    public Guid CaseId { get; set; }
    public CropCase Case { get; set; } = null!;

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    /// <summary>Integrity check and de-duplication.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Stored as bytea, capped at 2 MB (resized on the phone before upload).</summary>
    public byte[] Content { get; set; } = Array.Empty<byte>();

    public DateTime UploadedAt { get; set; }
}
