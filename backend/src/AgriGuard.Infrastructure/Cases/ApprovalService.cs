using System.Data;
using System.Globalization;
using System.Text.Json;
using AgriGuard.Application.Agent;
using AgriGuard.Application.Cases;
using AgriGuard.Application.Common.Exceptions;
using AgriGuard.Application.Common.Interfaces;
using AgriGuard.Domain.Cases;
using AgriGuard.Domain.Inventory;
using AgriGuard.Domain.Registry;
using AgriGuard.Domain.Validation;
using AgriGuard.Infrastructure.Agent;
using AgriGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AgriGuard.Infrastructure.Cases;

/// <summary>
/// The human gate (§9.5): the only code that turns an agent's proposal into a prescription, and the
/// only code that commits dealer stock. The agent has no tool that reaches here.
///
/// **Approve** runs as ONE serializable transaction. Either all of these happen, or none do:
///   1. the proposal is re-validated against the rules as they stand now (V1–V11);
///   2. the dealer's batch rows are locked (SELECT … FOR UPDATE) and drawn first-expiry-first-out;
///   3. a committed StockReservation records exactly which batches were drawn;
///   4. the Prescription is issued, with the earliest safe harvest date;
///   5. the InputOrder is confirmed for the dealer;
///   6. a ChemicalApplication is scheduled, so the next proposal's V6/V7 count this spray;
///   7. the case becomes Prescribed, the run Completed, and the decision and events are appended.
///
/// **Reject** ends the run. **Revise** sends the same run back to the agent with the agronomist's
/// reason as guidance, at most <see cref="MaxHumanRevisions"/> times.
///
/// Double submission is handled three ways, from cheapest to last resort: the Idempotency-Key
/// replays the stored result; the run's status check refuses a second decision; and the run's xmin
/// plus serializable isolation make two racing approvals conflict, so at most one ever commits.
/// </summary>
public sealed class ApprovalService(
    AgriGuardDbContext db,
    ICurrentUserAccessor currentUser,
    AgentPrescriptionGate prescriptionGate,
    IAgentDispatcher dispatcher,
    TimeProvider timeProvider,
    ILogger<ApprovalService> logger) : IApprovalService
{
    private const int MaxHumanRevisions = ApprovalLimits.MaxHumanRevisions;
    private const int MaxReviewerNoteLength = ApprovalLimits.MaxReviewerNoteLength;
    private const int MaxAttempts = 3;

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<DecisionResultDto> DecideAsync(Guid runId, DecideRequest request, string idempotencyKey, CancellationToken ct = default)
    {
        // A retry of a request that already succeeded gets the original answer, and nothing runs twice.
        if (await ReplayAsync(runId, idempotencyKey, ct) is { } replay)
            return replay;

        // Agronomists decide only for their own district (the policy already limited the role).
        if (!await db.AgentRuns.AsNoTracking().ScopedTo(currentUser).AnyAsync(r => r.Id == runId, ct))
            CaseScope.EnsureVisible<object>(null, await db.AgentRuns.AnyAsync(r => r.Id == runId, ct), "Agent run", runId);

        var userId = currentUser.UserId ?? throw new ForbiddenAccessException();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (await DecideOnceAsync(runId, request, idempotencyKey, userId, ct) is not { } decisionId)
                {
                    // The same key committed between our first check and this transaction.
                    return await ReplayAsync(runId, idempotencyKey, ct)
                        ?? throw new ConflictException("This decision was submitted twice at once. Reload to see the result.");
                }

                if (request.Decision == ApprovalDecisionType.Revise)
                    await DispatchRevisionAsync(runId, request.Reason!, ct);

                return await BuildResultAsync(decisionId, replayed: false, ct);
            }
            catch (Exception ex) when (IsRaceLost(ex) && attempt < MaxAttempts)
            {
                // Another request changed the same rows first. Start again from fresh data: the
                // status check will then see the run already decided and answer 409.
                db.ChangeTracker.Clear();
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex, "idempotency_key"))
            {
                // The same key, submitted twice at the same instant: the other request won.
                db.ChangeTracker.Clear();
                return await ReplayAsync(runId, idempotencyKey, ct)
                    ?? throw new ConflictException("This decision was submitted twice at once. Reload to see the result.");
            }
            catch (Exception ex) when (IsRaceLost(ex) || IsUniqueViolation(ex, "agent_run_id"))
            {
                throw new ConflictException("This run was decided by someone else at the same moment. Reload to see the result.");
            }
        }
    }

    /// <summary>
    /// One attempt, in one serializable transaction. Returns the new decision's id, or null when a
    /// decision with this key already exists (so the caller replays it).
    /// </summary>
    private async Task<Guid?> DecideOnceAsync(Guid runId, DecideRequest request, string idempotencyKey, Guid userId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        if (await db.ApprovalDecisions.AnyAsync(d => d.IdempotencyKey == idempotencyKey, ct))
            return null;

        var run = await db.AgentRuns
            .Include(r => r.Case)
            .Include(r => r.Steps)
            .FirstAsync(r => r.Id == runId, ct);

        if (run.Status != AgentRunStatus.PendingApproval)
            throw new ConflictException($"This run is {run.Status}. Only a run awaiting approval can be decided.");

        var now = UtcNow;
        var from = run.Status;
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();

        var decision = new ApprovalDecision
        {
            AgentRunId = run.Id,
            DecidedByUserId = userId,
            Decision = request.Decision,
            Reason = reason,
            DecidedAt = now,
            IdempotencyKey = idempotencyKey
        };
        db.ApprovalDecisions.Add(decision);

        string? prescriptionNo = null;
        switch (request.Decision)
        {
            case ApprovalDecisionType.Approve:
                prescriptionNo = await IssueAsync(run, userId, now, ct);
                run.Status = AgentRunStatus.Completed;
                run.CompletedAt = now;
                if (run.Case.Status == CaseStatus.PendingApproval)
                    run.Case.Status = CaseStatus.Prescribed;
                break;

            case ApprovalDecisionType.Reject:
                run.Status = AgentRunStatus.Rejected;
                run.CompletedAt = now;
                run.FailureReason = AgentRunLifecycle.Truncate($"Rejected by the agronomist: {reason}", 1000);
                if (run.Case.Status == CaseStatus.PendingApproval)
                    run.Case.Status = CaseStatus.Rejected;
                break;

            case ApprovalDecisionType.Revise:
                var revisionsSoFar = await db.ApprovalDecisions
                    .CountAsync(d => d.AgentRunId == run.Id && d.Decision == ApprovalDecisionType.Revise, ct);
                if (revisionsSoFar >= MaxHumanRevisions)
                    throw new BusinessRuleException("REVISION_LIMIT_REACHED",
                        $"This run has already been sent back {revisionsSoFar} times. Reject it and prescribe manually.");

                run.Status = AgentRunStatus.RevisionRequested;
                // The run's clock restarts: the timeout sweeper measures from here.
                run.StartedAt = now;
                run.CompletedAt = null;
                if (run.Case.Status == CaseStatus.PendingApproval)
                    run.Case.Status = CaseStatus.AgentProcessing;
                break;

            default:
                throw new RequestValidationException(nameof(request.Decision), "Decision must be Approve, Reject or Revise.");
        }

        db.AgentRunEvents.Add(new AgentRunEvent
        {
            AgentRunId = run.Id,
            EventType = AgentEventType.ApprovalDecided,
            PayloadJson = AgentPayloads.ToStorable(new
            {
                decision = request.Decision.ToString(),
                reason,
                decidedByRole = currentUser.Role?.ToString(),
                prescriptionNo
            }),
            OccurredAt = now
        });
        db.AgentRunEvents.Add(AgentRunLifecycle.StatusChanged(run.Id, from, run.Status, now));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation("Run {RunId} {Decision} by {UserId}{Prescription}",
            run.Id, request.Decision, userId, prescriptionNo is null ? "" : $"; issued {prescriptionNo}");
        return decision.Id;
    }

    /// <summary>Steps 1–6 of the approval. Throws, rolling everything back, if anything no longer holds.</summary>
    private async Task<string> IssueAsync(AgentRun run, Guid userId, DateTime now, CancellationToken ct)
    {
        if (AgentPayloads.ToElement(run.ProposalJson) is not { ValueKind: JsonValueKind.Object } stored)
            throw new BusinessRuleException("NO_PROPOSAL", "This run has no proposal to approve.");

        // 1. Re-validate. Time has passed since the agent proposed: stock may have gone, another
        //    spray may have been scheduled, the spray date may now be in the past.
        var input = AgentPrescriptionGate.FromStoredProposal(stored, run.Id, run.Case.CropCycleId);
        var verdict = await prescriptionGate.CheckAsync(input, ct);
        run.VerdictJson = AgentPayloads.ToStorable(verdict);
        if (verdict.Outcome != ValidationOutcome.Approved)
            throw new BusinessRuleException("PROPOSAL_NO_LONGER_VALID",
                $"The proposal no longer passes the safety rules ({verdict.Summary}). Request a revision instead.");

        // An approved verdict means V1 passed, so every field is present and well-formed.
        var productId = Guid.Parse(input.ProductId!);
        var dose = input.DosePerHectare!.Value;
        var total = input.TotalQuantity!.Value;
        var sprayDate = DateOnly.ParseExact(input.SprayDate!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        Guid? namedDealer = Guid.TryParse(input.DealerId, out var dealer) ? dealer : null;

        var facts = await db.CropCycles.AsNoTracking()
            .Where(c => c.Id == run.Case.CropCycleId)
            .Select(c => new { c.CropId, c.Plot.AreaHectares })
            .FirstAsync(ct);
        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new { p.Name, p.PackSize, p.UnitPrice, p.Unit })
            .FirstAsync(ct);
        var rule = await db.ProductCropApprovals.AsNoTracking()
            .Where(a => a.ProductId == productId && a.CropId == facts.CropId)
            .Select(a => new { a.PreHarvestIntervalDays, a.ReEntryIntervalHours })
            .FirstAsync(ct);

        // 2. Lock and draw the stock. The farmer buys whole packs, so that is what leaves the shelf.
        var packs = StockAllocation.PacksFor(total, product.PackSize);
        var drawn = packs * product.PackSize;
        var batches = await LockBatchesAsync(productId, namedDealer, run.Case.DistrictId, sprayDate, ct);

        // The same dealer V9 judged: the named one, or the best-stocked in the district.
        var source = batches
            .GroupBy(b => b.DealerId)
            .Where(g => namedDealer is null || g.Key == namedDealer)
            .OrderByDescending(g => g.Sum(b => b.QuantityOnHand - b.QuantityReserved))
            .FirstOrDefault();

        if (source is null
            || StockAllocation.PlanFefo(
                source.Select(b => new BatchStock(b.Id, b.BatchNo, b.ExpiryDate, b.QuantityOnHand - b.QuantityReserved)),
                drawn) is not { } plan)
            throw new BusinessRuleException("INSUFFICIENT_STOCK",
                $"{packs} pack(s) of {product.Name} ({Num(drawn)} {product.Unit}) are no longer in stock at one dealer. Request a revision instead.");

        var byId = source.ToDictionary(b => b.Id);
        foreach (var draw in plan)
            byId[draw.BatchId].QuantityOnHand -= draw.Quantity;
        var packPrice = plan.Max(d => byId[d.BatchId].UnitPrice);

        // 3. The reservation, created already committed: it records which batches were drawn.
        var reservation = new StockReservation
        {
            AgentRunId = run.Id,
            DealerId = source.Key,
            ProductId = productId,
            TotalQuantity = drawn,
            Status = ReservationStatus.Committed,
            ExpiresAt = now,
            ResolvedAt = now
        };
        db.StockReservations.Add(reservation);
        foreach (var draw in plan)
            db.StockReservationLines.Add(new StockReservationLine { Reservation = reservation, BatchId = draw.BatchId, Quantity = draw.Quantity });

        // 4. The prescription. Instructions are written here from the rules table, never taken from
        //    the model's text: this is what the farmer acts on.
        var safeHarvest = sprayDate.AddDays(rule.PreHarvestIntervalDays);
        var unit = product.Unit == ProductUnit.Litre ? "L" : "kg";
        var prescriptionNo = await NextNumberAsync(AgriGuardDbContext.PrescriptionSequence, "RX", ct);
        var prescription = new Prescription
        {
            PrescriptionNo = prescriptionNo,
            CaseId = run.CaseId,
            AgentRunId = run.Id,
            CropCycleId = run.Case.CropCycleId,
            ProductId = productId,
            DiagnosedPathogenId = await DiagnosedPathogenAsync(run.FinalOutcomeJson, ct),
            DosePerHectare = dose,
            TotalQuantity = total,
            SprayDate = sprayDate,
            EarliestSafeHarvestDate = safeHarvest,
            Instructions =
                $"Spray {Num(dose)} {unit}/ha of {product.Name} ({Num(total)} {unit} over {Num(facts.AreaHectares)} ha) on {sprayDate:yyyy-MM-dd}. " +
                $"Do not harvest before {safeHarvest:yyyy-MM-dd} ({rule.PreHarvestIntervalDays}-day pre-harvest interval). " +
                $"Keep people out of the field for {rule.ReEntryIntervalHours} hours after spraying.",
            Status = PrescriptionStatus.Issued,
            IssuedByUserId = userId,
            IssuedAt = now
        };
        db.Prescriptions.Add(prescription);

        // 5. The dealer's order, ready to collect.
        var order = new InputOrder
        {
            OrderNo = await NextNumberAsync(AgriGuardDbContext.InputOrderSequence, "ORD", ct),
            FarmerId = run.Case.FarmerId,
            DealerId = source.Key,
            Prescription = prescription,
            Status = OrderStatus.Confirmed,
            TotalAmount = packs * packPrice,
            ConfirmedAt = now
        };
        db.InputOrders.Add(order);
        db.InputOrderLines.Add(new InputOrderLine
        {
            Order = order,
            ProductId = productId,
            Packs = packs,
            Quantity = drawn,
            UnitPrice = packPrice,
            LineTotal = packs * packPrice
        });

        // 6. The spray goes on the crop's record, so V6/V7 count it for the next proposal.
        db.ChemicalApplications.Add(new ChemicalApplication
        {
            CropCycleId = run.Case.CropCycleId,
            ProductId = productId,
            Prescription = prescription,
            ApplicationDate = sprayDate,
            DosePerHectare = dose,
            TotalQuantity = total,
            Status = ApplicationStatus.Scheduled
        });

        return prescriptionNo;
    }

    /// <summary>
    /// Locks the candidate batch rows until the transaction ends, so no other approval can draw the
    /// same stock in between. Raw SQL because LINQ has no FOR UPDATE; xmin is selected because EF
    /// maps it as the row version and needs it to track the rows.
    /// </summary>
    private Task<List<InventoryBatch>> LockBatchesAsync(Guid productId, Guid? dealerId, Guid districtId, DateOnly sprayDate, CancellationToken ct) =>
        dealerId is { } id
            ? db.InventoryBatches.FromSql($"""
                SELECT b.*, b.xmin FROM inventory_batches b
                WHERE b.product_id = {productId} AND b.dealer_id = {id}
                  AND b.expiry_date > {sprayDate} AND b.quantity_on_hand - b.quantity_reserved > 0
                FOR UPDATE OF b
                """).ToListAsync(ct)
            : db.InventoryBatches.FromSql($"""
                SELECT b.*, b.xmin FROM inventory_batches b
                JOIN dealers d ON d.id = b.dealer_id
                WHERE b.product_id = {productId} AND d.district_id = {districtId}
                  AND b.expiry_date > {sprayDate} AND b.quantity_on_hand - b.quantity_reserved > 0
                FOR UPDATE OF b
                """).ToListAsync(ct);

    /// <summary>Revise, after the decision is committed: the same run goes back to the agent.</summary>
    private async Task DispatchRevisionAsync(Guid runId, string reason, CancellationToken ct)
    {
        var run = await db.AgentRuns.Include(r => r.Case).Include(r => r.Steps).FirstAsync(r => r.Id == runId, ct);
        try
        {
            // The request's token is ignored from here: the decision is committed and the run must
            // either reach the agent or be recorded as failed, whatever the client does.
            await dispatcher.DispatchAsync(
                new AgentDispatchRequest(run.Id, run.CaseId, run.Objective, AgentRunLifecycle.Truncate(reason, MaxReviewerNoteLength)),
                CancellationToken.None);
        }
        catch (AgentDispatchException ex)
        {
            logger.LogWarning(ex, "Revision of run {RunId} could not be dispatched", run.Id);
            var now = UtcNow;
            AgentRunLifecycle.End(run, AgentRunStatus.Failed, ex.Message, now);
            db.AgentRunEvents.Add(AgentRunLifecycle.StatusChanged(run.Id, AgentRunStatus.RevisionRequested, AgentRunStatus.Failed, now));
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task<DecisionResultDto?> ReplayAsync(Guid runId, string idempotencyKey, CancellationToken ct)
    {
        var earlier = await db.ApprovalDecisions.AsNoTracking()
            .Where(d => d.IdempotencyKey == idempotencyKey)
            .Select(d => new { d.Id, d.AgentRunId, d.DecidedByUserId })
            .FirstOrDefaultAsync(ct);
        if (earlier is null)
            return null;

        // A key belongs to one request. Reusing it for another run, or by another person, is a
        // client bug — answering with someone else's result would be worse than refusing.
        if (earlier.AgentRunId != runId || earlier.DecidedByUserId != currentUser.UserId)
            throw new BusinessRuleException("IDEMPOTENCY_KEY_REUSED",
                "This Idempotency-Key was already used for a different request. Generate a new key for each decision.");

        return await BuildResultAsync(earlier.Id, replayed: true, ct);
    }

    private async Task<DecisionResultDto> BuildResultAsync(Guid decisionId, bool replayed, CancellationToken ct)
    {
        var d = await db.ApprovalDecisions.AsNoTracking()
            .Where(x => x.Id == decisionId)
            .Select(x => new
            {
                x.Id,
                x.AgentRunId,
                x.AgentRun.CaseId,
                x.Decision,
                x.Reason,
                x.DecidedAt,
                RunStatus = x.AgentRun.Status,
                CaseStatus = x.AgentRun.Case.Status
            })
            .FirstAsync(ct);

        var prescription = d.Decision == ApprovalDecisionType.Approve
            ? await IssuedPrescriptions.ForRunAsync(db, d.AgentRunId, ct)
            : null;

        return new DecisionResultDto(d.Id, d.AgentRunId, d.CaseId, d.Decision, d.Reason, d.DecidedAt,
            d.RunStatus, d.CaseStatus, prescription, replayed);
    }

    /// <summary>The pathogen the Diagnosis agent settled on, if it names one in the catalogue.</summary>
    private async Task<Guid?> DiagnosedPathogenAsync(string? finalOutcomeJson, CancellationToken ct)
    {
        if (AgentPayloads.ToElement(finalOutcomeJson) is not { ValueKind: JsonValueKind.Object } outcome
            || !outcome.TryGetProperty("diagnosis", out var diagnosis) || diagnosis.ValueKind != JsonValueKind.Object
            || !diagnosis.TryGetProperty("primary_pathogen_code", out var code) || code.ValueKind != JsonValueKind.String)
            return null;

        var value = code.GetString()!.Trim().ToUpperInvariant();
        return await db.Pathogens.Where(p => p.Code == value).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
    }

    /// <summary>"RX-2026-000001". From a PostgreSQL sequence, so concurrent approvals never collide.</summary>
    private async Task<string> NextNumberAsync(string sequence, string prefix, CancellationToken ct)
    {
        // The sequence name goes in as a parameter, cast to regclass, not spliced into the SQL.
        var next = await db.Database.SqlQuery<long>($"SELECT nextval({sequence}::regclass) AS \"Value\"").SingleAsync(ct);
        return $"{prefix}-{UtcNow.Year}-{next:D6}";
    }

    /// <summary>Lost a race: a serialization failure (40001) or a stale row version.</summary>
    private static bool IsRaceLost(Exception ex) =>
        ex is DbUpdateConcurrencyException || FindPostgres(ex)?.SqlState == PostgresErrorCodes.SerializationFailure;

    private static bool IsUniqueViolation(Exception ex, string column) =>
        FindPostgres(ex) is { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && (pg.ConstraintName?.Contains(column, StringComparison.Ordinal) ?? false);

    private static PostgresException? FindPostgres(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
            if (ex is PostgresException pg) return pg;
        return null;
    }

    private static string Num(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
