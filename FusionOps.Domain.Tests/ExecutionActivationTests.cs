using FusionOps.Domain.Execution;

namespace FusionOps.Domain.Tests;

public sealed class ExecutionActivationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-13T09:00:00Z");
    private static ExecutionPlan Plan()
    {
        var (plan, _) = ExecutionPlan.Propose(ExecutionPlanTests.Baseline());
        plan.Decide(ExecutionPlanMutation.Approve, plan.Baseline.Digest, "approver", null, Now); return plan;
    }
    private static ExecutionActivation Activation()
    { var plan = Plan(); return ExecutionActivation.Request(plan, plan.Baseline.Digest, "operator", Now); }
    private static void ConfirmResources(ExecutionActivation a, string? failure = null) => a.CompleteStep(ActivationState.ReservingResources, new(ActivationStepOutcome.Confirmed, Resources: []), Now, 5, failure);
    [Test]
    public void AdmissionRequiresExactValidApprovalBeforeAcquiringEffects()
    {
        var plan = Plan();
        Assert.Throws<ExecutionPlanRuleException>(() => ExecutionActivation.Request(plan, "wrong", "operator", Now));
        Assert.Throws<ExecutionPlanRuleException>(() => ExecutionActivation.Request(plan, plan.Baseline.Digest, "operator", plan.ApprovalExpiresAt!.Value));
        plan.Decide(ExecutionPlanMutation.Withdraw, plan.Baseline.Digest, "planner", "Revised", Now);
        Assert.Throws<ExecutionPlanRuleException>(() => ExecutionActivation.Request(plan, plan.Baseline.Digest, "operator", Now));
    }
    [Test]
    public void CancellationDuringUnknownWorkforceResultReconcilesBeforeRelease()
    {
        var a = Activation(); a.RequestCancellation("operator", "cancel_requested", Now);
        a.CompleteStep(a.State, new(ActivationStepOutcome.Unresolved, "IOException"), Now, 1);
        Assert.That(a.State, Is.EqualTo(ActivationState.NeedsAttention)); Assert.That(a.PausedStep, Is.EqualTo(ActivationState.ReservingResources));
        a.Resume("operator", Now); ConfirmResources(a); Assert.That(a.State, Is.EqualTo(ActivationState.ReleasingResources));
        a.CompleteStep(a.State, new(ActivationStepOutcome.Confirmed), Now, 5); Assert.That(a.State, Is.EqualTo(ActivationState.Compensated));
    }
    [Test]
    public void CancellationDuringUnknownLicenseResultReleasesBothOwners()
    {
        var a = Activation(); ConfirmResources(a); a.RequestCancellation("operator", "cancel_requested", Now);
        a.CompleteStep(a.State, new(ActivationStepOutcome.Unresolved, "TimeoutException"), Now, 1); a.Resume("operator", Now);
        a.CompleteStep(a.State, new(ActivationStepOutcome.Confirmed), Now, 5); Assert.That(a.State, Is.EqualTo(ActivationState.ReleasingLicenses));
        a.CompleteStep(a.State, new(ActivationStepOutcome.Unresolved, "IOException"), Now, 1); Assert.That(ExecutionActivation.HoldsProject(a.State), Is.True);
        a.Resume("operator", Now); a.CompleteStep(a.State, new(ActivationStepOutcome.Confirmed), Now, 5);
        Assert.That(a.State, Is.EqualTo(ActivationState.ReleasingResources)); a.CompleteStep(a.State, new(ActivationStepOutcome.Confirmed), Now, 5);
        Assert.That(ExecutionActivation.HoldsProject(a.State), Is.False);
    }
    [TestCase(true)]
    [TestCase(false)]
    public void ConfirmedRejectionNeverBecomesActiveAndOnlyReleasesEarlierEffects(bool workforce)
    {
        var a = Activation(); if (!workforce) ConfirmResources(a);
        a.CompleteStep(a.State, new(ActivationStepOutcome.Rejected, "capacity"), Now, 5);
        Assert.That(a.State, Is.EqualTo(workforce ? ActivationState.Rejected : ActivationState.ReleasingResources));
        Assert.That(a.FailureCode, Is.EqualTo("capacity"));
    }
    [TestCase(true)]
    [TestCase(false)]
    public void LostAuthorityAfterConfirmedCommitCausesOwnershipCompensation(bool workforce)
    {
        var a = Activation(); if (!workforce) ConfirmResources(a);
        a.CompleteStep(a.State, new(ActivationStepOutcome.Confirmed, Resources: workforce ? [] : null), Now, 5, "execution_authority_revoked");
        Assert.That(a.State, Is.EqualTo(workforce ? ActivationState.ReleasingResources : ActivationState.ReleasingLicenses));
        Assert.That(a.CancellationRequested, Is.True); Assert.That(a.FailureCode, Is.EqualTo("execution_authority_revoked"));
    }
    [Test]
    public void ActiveRequiresConfirmedOwnershipAndInvalidResourceResultCannotAdvance()
    {
        var a = Activation();
        Assert.Throws<ExecutionPlanRuleException>(() => a.CompleteStep(a.State, new(ActivationStepOutcome.Confirmed, Resources: [new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())]), Now, 5));
        Assert.That(a.State, Is.EqualTo(ActivationState.ReservingResources)); ConfirmResources(a);
        a.CompleteStep(a.State, new(ActivationStepOutcome.Confirmed), Now, 5); Assert.That(a.State, Is.EqualTo(ActivationState.Active));
        Assert.That(ExecutionActivation.IsExecutable(a.State), Is.False);
        a.RequestCancellation("operator", "cancel_requested", Now); Assert.That(a.State, Is.EqualTo(ActivationState.ReleasingLicenses));
        var decision = ExecutionActivationDecision.Capture(a, 17); Assert.That(decision.LeaseEpoch, Is.EqualTo(17)); Assert.That(decision.Version, Is.EqualTo(a.Version));
    }
}
