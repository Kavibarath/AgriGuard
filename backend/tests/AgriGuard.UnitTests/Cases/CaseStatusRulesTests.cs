using AgriGuard.Domain.Cases;
using AgriGuard.Domain.Identity;

namespace AgriGuard.UnitTests.Cases;

public sealed class CaseStatusRulesTests
{
    [Theory]
    [InlineData(CaseStatus.Submitted, UserRole.Farmer)]
    [InlineData(CaseStatus.AwaitingManualReview, UserRole.FieldAgronomist)]
    [InlineData(CaseStatus.Prescribed, UserRole.Farmer)]
    [InlineData(CaseStatus.Rejected, UserRole.CoopAdministrator)]
    public void A_case_with_nothing_in_flight_can_be_closed(CaseStatus from, UserRole role) =>
        Assert.Null(CaseStatusRules.ExplainManualChange(from, CaseStatus.Closed, role));

    [Theory]
    [InlineData(CaseStatus.Submitted)]
    [InlineData(CaseStatus.Rejected)]
    public void An_agronomist_can_send_a_case_to_manual_review(CaseStatus from) =>
        Assert.Null(CaseStatusRules.ExplainManualChange(from, CaseStatus.AwaitingManualReview, UserRole.FieldAgronomist));

    [Fact]
    public void A_farmer_cannot_send_a_case_to_manual_review() =>
        Assert.Contains("Only an agronomist",
            CaseStatusRules.ExplainManualChange(CaseStatus.Submitted, CaseStatus.AwaitingManualReview, UserRole.Farmer));

    [Theory]
    [InlineData(CaseStatus.AgentProcessing)]
    [InlineData(CaseStatus.PendingApproval)]
    [InlineData(CaseStatus.Prescribed)]
    [InlineData(CaseStatus.Rejected)]
    public void Workflow_statuses_cannot_be_set_by_hand(CaseStatus to) =>
        Assert.Contains("agent workflow",
            CaseStatusRules.ExplainManualChange(CaseStatus.Submitted, to, UserRole.CoopAdministrator));

    [Theory]
    [InlineData(CaseStatus.AgentProcessing)]
    [InlineData(CaseStatus.PendingApproval)]
    public void A_case_with_a_run_in_flight_cannot_be_closed(CaseStatus from) =>
        Assert.NotNull(CaseStatusRules.ExplainManualChange(from, CaseStatus.Closed, UserRole.CoopAdministrator));

    [Fact]
    public void A_closed_case_cannot_be_reopened() =>
        Assert.Contains("cannot be reopened",
            CaseStatusRules.ExplainManualChange(CaseStatus.Closed, CaseStatus.AwaitingManualReview, UserRole.FieldAgronomist));

    [Theory]
    [InlineData(CaseStatus.Submitted, true)]
    [InlineData(CaseStatus.AwaitingManualReview, true)]
    [InlineData(CaseStatus.AgentProcessing, false)]
    [InlineData(CaseStatus.PendingApproval, false)]
    [InlineData(CaseStatus.Prescribed, false)]
    [InlineData(CaseStatus.Closed, false)]
    public void A_run_starts_only_on_a_new_case_or_one_back_from_review(CaseStatus status, bool allowed) =>
        Assert.Equal(allowed, CaseStatusRules.CanStartAgentRun(status));
}
