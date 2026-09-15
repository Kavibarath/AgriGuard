using AgriGuard.Domain.Cases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgriGuard.Infrastructure.Persistence.Configurations;

internal sealed class CropCaseConfiguration : IEntityTypeConfiguration<CropCase>
{
    public void Configure(EntityTypeBuilder<CropCase> b)
    {
        b.Property(x => x.ReferenceNo).HasMaxLength(20);
        b.HasIndex(x => x.ReferenceNo).IsUnique();

        // Length cap is the first line of prompt-injection defence on the untrusted field.
        b.Property(x => x.FarmerNote).HasMaxLength(1000);

        b.Property(x => x.ReportedLatitude).HasPrecision(9, 6);
        b.Property(x => x.ReportedLongitude).HasPrecision(9, 6);
        b.Property(x => x.Version).IsRowVersion();

        b.HasOne(x => x.Farmer).WithMany().HasForeignKey(x => x.FarmerId);
        b.HasOne(x => x.AssignedAgronomist).WithMany().HasForeignKey(x => x.AssignedAgronomistId);
        b.HasOne(x => x.Plot).WithMany().HasForeignKey(x => x.PlotId);
        b.HasOne(x => x.CropCycle).WithMany().HasForeignKey(x => x.CropCycleId);
        b.HasOne(x => x.District).WithMany().HasForeignKey(x => x.DistrictId);
        b.HasOne(x => x.ConfirmedPathogen).WithMany().HasForeignKey(x => x.ConfirmedPathogenId);

        // The agronomist queue: filter by status + district, newest first.
        b.HasIndex(x => new { x.Status, x.DistrictId, x.CreatedAt }).IsDescending(false, false, true);
        b.HasIndex(x => x.FarmerId);

        // Outbreak signal: confirmed pathogen per district over a time window.
        b.HasIndex(x => new { x.DistrictId, x.ConfirmedPathogenId, x.CreatedAt });

        // GIN index supports "cases containing symptom X" queries on the text[] column.
        b.HasIndex(x => x.SymptomCodes).HasMethod("gin");
    }
}

internal sealed class CaseAttachmentConfiguration : IEntityTypeConfiguration<CaseAttachment>
{
    public void Configure(EntityTypeBuilder<CaseAttachment> b)
    {
        b.Property(x => x.FileName).HasMaxLength(255);
        b.Property(x => x.ContentType).HasMaxLength(100);
        b.Property(x => x.Sha256).HasMaxLength(64);

        b.HasOne(x => x.Case).WithMany(c => c.Attachments)
            .HasForeignKey(x => x.CaseId)
            .OnDelete(DeleteBehavior.Cascade);

        b.ToTable(t => t.HasCheckConstraint("ck_case_attachments_size", "size_bytes > 0 AND size_bytes <= 2097152"));
    }
}

internal sealed class AgentRunConfiguration : IEntityTypeConfiguration<AgentRun>
{
    public void Configure(EntityTypeBuilder<AgentRun> b)
    {
        b.Property(x => x.Objective).HasMaxLength(1000);
        b.Property(x => x.FailureReason).HasMaxLength(1000);
        b.Property(x => x.PlanJson).HasColumnType("jsonb");
        b.Property(x => x.ProposalJson).HasColumnType("jsonb");
        b.Property(x => x.VerdictJson).HasColumnType("jsonb");
        b.Property(x => x.FinalOutcomeJson).HasColumnType("jsonb");
        b.Property(x => x.Version).IsRowVersion();

        b.HasOne(x => x.Case).WithMany(c => c.AgentRuns).HasForeignKey(x => x.CaseId);

        b.HasIndex(x => new { x.CaseId, x.CreatedAt });
        b.HasIndex(x => x.Status);

        b.ToTable(t => t.HasCheckConstraint("ck_agent_runs_revision_count", "revision_count BETWEEN 0 AND 2"));
    }
}

internal sealed class AgentRunStepConfiguration : IEntityTypeConfiguration<AgentRunStep>
{
    public void Configure(EntityTypeBuilder<AgentRunStep> b)
    {
        b.Property(x => x.Goal).HasMaxLength(500);
        b.Property(x => x.ErrorMessage).HasMaxLength(2000);
        b.Property(x => x.InputJson).HasColumnType("jsonb");
        b.Property(x => x.OutputJson).HasColumnType("jsonb");

        b.HasOne(x => x.AgentRun).WithMany(r => r.Steps)
            .HasForeignKey(x => x.AgentRunId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.AgentRunId, x.SequenceNo }).IsUnique();
        b.ToTable(t => t.HasCheckConstraint("ck_agent_run_steps_retry_count", "retry_count >= 0"));
    }
}

internal sealed class AgentRunEventConfiguration : IEntityTypeConfiguration<AgentRunEvent>
{
    public void Configure(EntityTypeBuilder<AgentRunEvent> b)
    {
        b.Property(x => x.ToolName).HasMaxLength(100);
        b.Property(x => x.CorrelationId).HasMaxLength(64);
        b.Property(x => x.PayloadJson).HasColumnType("jsonb");

        b.HasOne(x => x.AgentRun).WithMany(r => r.Events)
            .HasForeignKey(x => x.AgentRunId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.AgentRunId, x.OccurredAt });
    }
}

internal sealed class ApprovalDecisionConfiguration : IEntityTypeConfiguration<ApprovalDecision>
{
    public void Configure(EntityTypeBuilder<ApprovalDecision> b)
    {
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.Property(x => x.IdempotencyKey).HasMaxLength(100);

        // Decisions are audit records: Restrict (the global default) — never cascade-deleted.
        b.HasOne(x => x.AgentRun).WithMany(r => r.Decisions).HasForeignKey(x => x.AgentRunId);
        b.HasOne(x => x.DecidedBy).WithMany().HasForeignKey(x => x.DecidedByUserId);

        b.HasIndex(x => x.IdempotencyKey).IsUnique();
        b.HasIndex(x => new { x.AgentRunId, x.DecidedAt });
    }
}
