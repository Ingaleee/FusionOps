using FusionOps.Domain.Execution;
using FusionOps.Domain.Projects;
using FusionOps.Domain.Scheduling;
using FusionOps.Domain.ValueObjects;

namespace FusionOps.Domain.Tests;

public sealed class ExecutionPlanTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-13T09:00:00Z");
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly Guid Site = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static TemporalScenario Scenario(int duration = 60, long version = 1, string currency = "USD")
    {
        var start = Now.AddDays(2);
        var definition = new ProjectDefinition("Installation", new(start, start.AddHours(4)), 1000, currency,
            [new(Site, "Customer", [new(start, start.AddHours(4))])],
            [new(TaskId, "Install", Site, duration, start, start.AddHours(4), [], [], [], [])], []);
        var input = new TemporalPlanningInput(Project, definition, [], new Dictionary<string, int>(), new Dictionary<string, int>(), 60, 10000);
        var assumptions = ScenarioAssumptions.None;
        var result = new TemporalPlanner().Plan(input);
        return TemporalScenario.Create(Guid.NewGuid(), Tenant, "planner", new string('A', 64),
            new(new(1, version, version, ProjectDefinitionCodec.Digest(Tenant, Project, version, ProjectDefinitionCodec.Canonicalize(definition)),
                Now, Now, [], [], []), input, assumptions, input, result, new TemporalPlanValidator().Validate(input, result.Tasks)), Now);
    }
    private static ExecutionBaseline Baseline(TemporalScenario? scenario = null, ExecutionBaseline? previous = null,
        ExecutionApprovalPolicy? policy = null) => ExecutionBaseline.Capture(Guid.NewGuid(), scenario ?? Scenario(), "planner",
            new string('B', 64), policy ?? ExecutionApprovalPolicy.Standard(), previous, Now);

    [Test]
    public void BaselineRetainsSourceProofAndReturnsDetachedEvidence()
    {
        var scenario = Scenario(); var baseline = Baseline(scenario); var digest = baseline.Digest;
        Assert.That(baseline.Evidence.Scenario.Digest, Is.EqualTo(scenario.Digest));
        Assert.That(baseline.DefinitionDigest, Is.EqualTo(scenario.Evidence.Source.DefinitionDigest));
        var decoded = baseline.ScenarioEvidence; ((IList<ScheduledProjectTask>)decoded.Result.Tasks)[0] = decoded.Result.Tasks[0] with { Cost = 999 };
        baseline.VerifyEvidence(); Assert.That(baseline.Digest, Is.EqualTo(digest));
        Assert.That(baseline.ScenarioEvidence.Result.Tasks[0].Cost, Is.Zero);
        typeof(ExecutionBaseline).GetProperty(nameof(ExecutionBaseline.EvidenceJson))!.SetValue(baseline, baseline.EvidenceJson + " ");
        Assert.Throws<ExecutionPlanRuleException>(baseline.VerifyEvidence);
    }
    [Test]
    public void ApprovalBindsDigestPolicyActorAndExpiryWithoutClaimingOwnership()
    {
        var baseline = Baseline(); var (plan, proposed) = ExecutionPlan.Propose(baseline);
        Assert.That(plan.Version, Is.EqualTo(1)); Assert.That(proposed.Action, Is.EqualTo("proposed"));
        Assert.Throws<ExecutionPlanRuleException>(() => plan.Decide(ExecutionPlanMutation.Approve, baseline.Digest, "planner", null, Now));
        Assert.Throws<ExecutionPlanRuleException>(() => plan.Decide(ExecutionPlanMutation.Approve, new string('0', 64), "approver", null, Now));
        Assert.That(plan.State, Is.EqualTo(ExecutionPlanState.AwaitingApproval)); Assert.That(plan.Version, Is.EqualTo(1));
        var decision = plan.Decide(ExecutionPlanMutation.Approve, baseline.Digest, "approver", null, Now.AddMinutes(1));
        Assert.That(decision.ReviewedDigest, Is.EqualTo(baseline.Digest)); Assert.That(decision.PolicyVersion, Is.EqualTo("execution-approval/v1"));
        Assert.That(plan.ApprovalExpiresAt, Is.EqualTo(Now.AddMinutes(1).AddDays(1))); Assert.That(plan.Version, Is.EqualTo(2));
        plan.RequireValidApproval(Now.AddHours(1));
        Assert.Throws<ExecutionPlanRuleException>(() => plan.RequireValidApproval(plan.ApprovalExpiresAt!.Value));
        Assert.Throws<ExecutionPlanRuleException>(() => plan.Decide(ExecutionPlanMutation.Approve, baseline.Digest, "another", null, Now.AddMinutes(2)));
    }
    [Test]
    public void ExpiredReviewOrBackdatedDecisionCannotApprove()
    {
        var baseline = Baseline(policy: new("execution-approval/v1", true, 30, 60)); var (plan, _) = ExecutionPlan.Propose(baseline);
        Assert.Throws<ExecutionPlanRuleException>(() => plan.Decide(ExecutionPlanMutation.Approve, baseline.Digest, "reviewer", null, Now.AddSeconds(30)));
        Assert.Throws<ExecutionPlanRuleException>(() => plan.Decide(ExecutionPlanMutation.Approve, baseline.Digest, "reviewer", null, Now.AddTicks(-100)));
        Assert.That(plan.Version, Is.EqualTo(1));
    }
    [Test]
    public void FailedRejectionOrWithdrawalDoesNotMutateStateAndApprovedWithdrawalKeepsHistory()
    {
        var baseline = Baseline(); var (plan, _) = ExecutionPlan.Propose(baseline);
        Assert.Throws<ExecutionPlanRuleException>(() => plan.Decide(ExecutionPlanMutation.Reject, baseline.Digest, "reviewer", "", Now));
        Assert.Throws<ExecutionPlanRuleException>(() => plan.Decide(ExecutionPlanMutation.Withdraw, baseline.Digest, "planner", null, Now));
        Assert.That(plan.State, Is.EqualTo(ExecutionPlanState.AwaitingApproval));
        plan.Decide(ExecutionPlanMutation.Approve, baseline.Digest, "reviewer", null, Now);
        var withdrawal = plan.Decide(ExecutionPlanMutation.Withdraw, baseline.Digest, "planner", "New equipment required", Now.AddMinutes(2));
        Assert.That(withdrawal.State, Is.EqualTo(ExecutionPlanState.Withdrawn)); Assert.That(plan.ApprovedBy, Is.EqualTo("reviewer"));
        Assert.That(plan.Version, Is.EqualTo(3)); Assert.Throws<ExecutionPlanRuleException>(() => plan.RequireValidApproval(Now.AddMinutes(3)));
    }
    [Test]
    public void ChangedTaskDefinitionAndCurrencyAreVisibleInVersionedDiff()
    {
        var original = Baseline(); var revised = Baseline(Scenario(duration: 120, version: 2), original);
        var diff = revised.Evidence.Diff;
        Assert.That(diff.PreviousDigest, Is.EqualTo(original.Digest)); Assert.That(diff.Tasks, Has.Count.EqualTo(1));
        Assert.That(diff.Tasks[0].DefinitionChanged, Is.True); Assert.That(diff.FinishDeltaMinutes, Is.EqualTo(60));
        var currency = Baseline(Scenario(currency: "EUR", version: 2), original).Evidence.Diff;
        Assert.That(currency.CurrencyChanged, Is.True); Assert.That(currency.CostDelta, Is.Null, "Unlike currencies cannot be subtracted");
    }
    [Test]
    public void InvalidPolicyAndPartialScenarioCannotBecomeReviewedBaseline()
    {
        Assert.Throws<ExecutionPlanRuleException>(() => Baseline(policy: new("unrecognized", true, 86400, 86400)));
        var complete = Scenario(); var e = complete.Evidence;
        var partial = TemporalScenario.Create(Guid.NewGuid(), Tenant, "planner", new string('C', 64), e with
        {
            Result = e.Result with { IsComplete = false, Tasks = [], TotalCost = 0, FinishAt = null },
            Validation = new TemporalPlanValidator().Validate(e.Input, [], requireComplete: false)
        }, Now);
        Assert.Throws<ExecutionPlanRuleException>(() => Baseline(partial));
    }
}
