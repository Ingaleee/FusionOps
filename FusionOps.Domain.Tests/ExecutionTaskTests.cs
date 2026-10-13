using FusionOps.Domain.Execution;
namespace FusionOps.Domain.Tests;
public sealed class ExecutionTaskTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-13T09:00:00Z");
    private static (ExecutionActivation Activation, ExecutionTask Task) Fixture()
    {
        var baseline = ExecutionPlanTests.Baseline(); var (plan, _) = ExecutionPlan.Propose(baseline);
        plan.Decide(ExecutionPlanMutation.Approve, baseline.Digest, "approver", null, Now);
        var a = ExecutionActivation.Request(plan, baseline.Digest, "operator", Now);
        a.CompleteStep(a.State, new(ActivationStepOutcome.Confirmed, Resources: []), Now, 5); a.CompleteStep(a.State, new(ActivationStepOutcome.Confirmed), Now, 5);
        return (a, ExecutionTask.Planned(baseline, baseline.ScenarioEvidence.Result.Tasks.Single().TaskId));
    }
    [Test]
    public void TaskRequiresActiveBaselineAndOwnedTimeWindow()
    {
        var (a, t) = Fixture(); var start = a.Baseline.ScenarioEvidence.Result.Tasks.Single().Period.Start;
        Assert.That(t.StartRejection(a, [t], start.AddTicks(-10)), Is.EqualTo("execution_task_start_window"));
        Assert.That(t.StartRejection(a, [t], start), Is.Null);
        a.RequestCancellation("operator", "cancel_requested", Now); Assert.That(t.StartRejection(a, [t], start), Is.EqualTo("execution_task_start_state"));
    }
    [Test]
    public void TrueCompletionSurvivesOverrunAndCannotBecomePlannedAgain()
    {
        var (a, t) = Fixture(); var interval = a.Baseline.ScenarioEvidence.Result.Tasks.Single().Period; t.Start(interval.Start);
        Assert.That(t.CompletionRejection(interval.Start.AddTicks(-10), interval.End), Is.EqualTo("execution_task_completion_time"));
        Assert.That(t.CompletionRejection(interval.End.AddSeconds(1), interval.End), Is.EqualTo("execution_task_completion_time"));
        t.Complete(interval.End.AddMinutes(2), interval.End.AddMinutes(3), interval.End);
        Assert.That(t.CompletedOutsideReservation, Is.True); Assert.That(t.State, Is.EqualTo(ExecutionTaskState.Completed));
        Assert.That(t.CompletionRejection(interval.End.AddMinutes(2), interval.End.AddMinutes(3)), Is.EqualTo("execution_task_completion_state"));
        Assert.Throws<ExecutionPlanRuleException>(() => t.Start(interval.Start));
    }
    [Test]
    public void ReportedCompletionClosesOwnershipWithoutErasingTaskFacts()
    {
        var (a, t) = Fixture(); var interval = a.Baseline.ScenarioEvidence.Result.Tasks.Single().Period;
        Assert.Throws<ExecutionPlanRuleException>(() => a.FinishReportedWork("operator", [t], interval.Start));
        t.Start(interval.Start); t.Complete(interval.Start.AddMinutes(5), interval.Start.AddMinutes(6), interval.End);
        var fact = ExecutionTaskFact.Capture(t, Guid.NewGuid(), new string('A', 64), "operator", ExecutionTaskMutation.Complete, null, interval.Start.AddMinutes(6));
        a.FinishReportedWork("operator", [t], interval.Start.AddMinutes(6));
        a.CompleteStep(a.State, new(ActivationStepOutcome.Confirmed), interval.Start.AddMinutes(7), 5); a.CompleteStep(a.State, new(ActivationStepOutcome.Confirmed), interval.Start.AddMinutes(8), 5);
        Assert.That(a.State, Is.EqualTo(ActivationState.Completed)); Assert.That(ExecutionActivation.HoldsProject(a.State), Is.False);
        Assert.That(fact.EffectiveAt, Is.EqualTo(t.CompletedAt)); Assert.That(fact.TaskVersion, Is.EqualTo(3)); Assert.That(fact.Accepted, Is.True);
    }
    [Test]
    public void OriginalRejectedCommandDoesNotInventEffectiveWork()
    {
        var (_, t) = Fixture(); var fact = ExecutionTaskFact.Capture(t, Guid.NewGuid(), new string('A', 64), "operator", ExecutionTaskMutation.Start, "execution_task_start_window", Now);
        Assert.That(fact.Accepted, Is.False); Assert.That(fact.EffectiveAt, Is.Null); Assert.That(fact.TaskVersion, Is.EqualTo(1)); Assert.That(t.State, Is.EqualTo(ExecutionTaskState.Planned));
    }
}
