using FusionOps.Domain.Entities;
using FusionOps.Domain.Events;
using FusionOps.Domain.Shared;
using FusionOps.Domain.ValueObjects;
using FusionOps.Domain.Workflows;
using FusionOps.Infrastructure.Outbox;

namespace FusionOps.Domain.Tests;

public sealed class ReservationWorkflowTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-13T08:00:00Z");
    private static ReservationSpecification Specification() => new(Guid.NewGuid(), [Guid.NewGuid(), Guid.NewGuid()],
        new TimeRange(Now.AddDays(1), Now.AddDays(1).AddHours(2)), [new("compiler", 2)], 30);
    private static ReservationWorkflow Approved()
    {
        var workflow = ReservationWorkflow.Propose(Guid.NewGuid(), Specification(), "proposer", Now);
        workflow.Approve("approver", true, Now);
        return workflow;
    }
    private static Guid[] Receipt(ReservationWorkflow workflow) => workflow.ResourceIds.Select(_ => Guid.NewGuid()).ToArray();

    [Test]
    public void ProposalCanonicalizationPreservesMeaningAndCopiesCallerCollections()
    {
        var resourceIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var spec = Specification() with { ResourceIds = resourceIds };
        var first = ReservationWorkflow.Propose(Guid.NewGuid(), spec, "proposer", Now);
        var reordered = spec with { ResourceIds = resourceIds.Reverse().ToArray(), Period = new(spec.Period.Start.ToOffset(TimeSpan.FromHours(3)), spec.Period.End) };
        var second = ReservationWorkflow.Propose(Guid.NewGuid(), reordered, "proposer", Now.AddDays(1));
        Assert.That(second.SpecificationHash, Is.EqualTo(first.SpecificationHash));
        resourceIds[0] = Guid.NewGuid();
        Assert.That(first.ResourceIds, Is.Not.EquivalentTo(resourceIds));
        var changed = ReservationWorkflow.Propose(Guid.NewGuid(), spec with { ProjectId = Guid.NewGuid() }, "proposer", Now);
        Assert.That(changed.SpecificationHash, Is.Not.EqualTo(first.SpecificationHash));
    }

    [Test]
    public void SeparateApproverAndStateRulesAreEnforced()
    {
        var workflow = ReservationWorkflow.Propose(Guid.NewGuid(), Specification(), "proposer", Now);
        Assert.Throws<WorkflowApprovalException>(() => workflow.Approve("proposer", true, Now));
        Assert.That(workflow.Version, Is.EqualTo(1));
        workflow.Approve("reviewer", true, Now);
        Assert.That(workflow.DeadlineAt, Is.EqualTo(Now.AddSeconds(30)));
        Assert.Throws<DomainException>(() => workflow.Approve("another", true, Now));
        Assert.Throws<DomainException>(() => workflow.Resume("operator", Now));
    }

    [Test]
    public void SuccessAndCancellationRecordContinuousVersionedHistory()
    {
        var workflow = Approved();
        var allocations = Receipt(workflow);
        workflow.CompleteStep(WorkflowState.ReservingResources, WorkflowStepResult.Success(allocations), Now, 3);
        workflow.CompleteStep(WorkflowState.ReservingLicenses, WorkflowStepResult.Success(), Now, 3);
        Assert.That(workflow.Outcome, Is.EqualTo(WorkflowOutcome.Successful));
        Assert.That(workflow.State, Is.EqualTo(WorkflowState.Succeeded));
        workflow.RequestCancellation("operator", Now);
        workflow.CompleteStep(WorkflowState.ReleasingLicenses, WorkflowStepResult.Success(), Now, 3);
        workflow.CompleteStep(WorkflowState.ReleasingResources, WorkflowStepResult.Success(), Now, 3);
        Assert.That(workflow.State, Is.EqualTo(WorkflowState.Compensated));
        Assert.That(workflow.Outcome, Is.EqualTo(WorkflowOutcome.UserCancelled));
        Assert.That(workflow.AllocationIds, Is.EqualTo(allocations));
        Assert.That(workflow.Transitions.Select(t => t.Version), Is.EqualTo(Enumerable.Range(1, 7).Select(i => (long)i)));
        Assert.That(workflow.DomainEvents.Count, Is.EqualTo(workflow.Version));
    }

    [Test]
    public void ConfirmedLicenseRejectionCompensatesResources()
    {
        var workflow = Approved();
        workflow.CompleteStep(workflow.State, WorkflowStepResult.Success(Receipt(workflow)), Now, 3);
        workflow.CompleteStep(workflow.State, WorkflowStepResult.Rejected("license_capacity"), Now, 3);
        Assert.That(workflow.State, Is.EqualTo(WorkflowState.ReleasingResources));
        Assert.That(workflow.Outcome, Is.EqualTo(WorkflowOutcome.Rejected));
        Assert.That(workflow.FailureCode, Is.EqualTo("license_capacity"));
        workflow.CompleteStep(workflow.State, WorkflowStepResult.Success(), Now, 3);
        Assert.That(workflow.State, Is.EqualTo(WorkflowState.Compensated));
    }

    [Test]
    public void AmbiguousFailuresPauseTheOriginalStepWithoutInventingRollback()
    {
        var workflow = Approved();
        workflow.CompleteStep(workflow.State, WorkflowStepResult.Retry("timeout"), Now, 2);
        Assert.That(workflow.NextAttemptAt, Is.EqualTo(Now.AddSeconds(2)));
        workflow.CompleteStep(workflow.State, WorkflowStepResult.Retry("timeout"), Now.AddSeconds(2), 2);
        Assert.That(workflow.State, Is.EqualTo(WorkflowState.NeedsAttention));
        Assert.That(workflow.PausedStep, Is.EqualTo(WorkflowState.ReservingResources));
        Assert.That(workflow.Outcome, Is.EqualTo(WorkflowOutcome.None));
        Assert.That(workflow.AllocationIds, Is.Empty);
        var hash = workflow.SpecificationHash;
        workflow.Resume("operator", Now.AddSeconds(3));
        Assert.That(workflow.State, Is.EqualTo(WorkflowState.ReservingResources));
        Assert.That(workflow.StepAttempts, Is.Zero);
        Assert.That(workflow.SpecificationHash, Is.EqualTo(hash));
    }

    [TestCase(WorkflowState.ReservingResources)]
    [TestCase(WorkflowState.ReservingLicenses)]
    public void InFlightCancellationWaitsForTheParticipantOutcome(WorkflowState step)
    {
        var workflow = Approved();
        if (step == WorkflowState.ReservingLicenses)
            workflow.CompleteStep(workflow.State, WorkflowStepResult.Success(Receipt(workflow)), Now, 3);
        workflow.RequestCancellation("operator", Now);
        var version = workflow.Version;
        workflow.RequestCancellation("operator", Now);
        Assert.That(workflow.Version, Is.EqualTo(version));
        workflow.CompleteStep(step, WorkflowStepResult.Success(step == WorkflowState.ReservingResources ? Receipt(workflow) : null), Now, 3);
        Assert.That(workflow.State, Is.EqualTo(step == WorkflowState.ReservingResources ? WorkflowState.ReleasingResources : WorkflowState.ReleasingLicenses));
    }

    [Test]
    public void DeadlineDoesNotDiscardACommittedResourceReceipt()
    {
        var workflow = Approved();
        var ids = Receipt(workflow);
        workflow.CompleteStep(workflow.State, WorkflowStepResult.Success(ids), Now.AddSeconds(31), 3);
        Assert.That(workflow.State, Is.EqualTo(WorkflowState.ReleasingResources));
        Assert.That(workflow.Outcome, Is.EqualTo(WorkflowOutcome.DeadlineExceeded));
        Assert.That(workflow.AllocationIds, Is.EqualTo(ids));
        Assert.That(workflow.Transitions.Any(t => t.Reason == "deadline_exceeded"), Is.True);
    }

    [Test]
    public void CompensationRejectionRequiresAttentionInsteadOfClaimingCompletion()
    {
        var workflow = Approved();
        workflow.RequestCancellation("operator", Now);
        workflow.CompleteStep(workflow.State, WorkflowStepResult.Success(Receipt(workflow)), Now, 1);
        workflow.CompleteStep(workflow.State, WorkflowStepResult.Rejected("ownership_missing"), Now, 1);
        Assert.That(workflow.State, Is.EqualTo(WorkflowState.NeedsAttention));
        Assert.That(workflow.PausedStep, Is.EqualTo(WorkflowState.ReleasingResources));
    }

    [Test]
    public void InvalidReceiptCannotAdvanceTheWorkflow()
    {
        var workflow = Approved();
        var version = workflow.Version;
        Assert.Throws<DomainException>(() => workflow.CompleteStep(workflow.State, WorkflowStepResult.Success([Guid.NewGuid()]), Now, 3));
        Assert.That(workflow.State, Is.EqualTo(WorkflowState.ReservingResources));
        Assert.That(workflow.Version, Is.EqualTo(version));
    }

    [Test]
    public void LicenseCompensationOwnsAReservationAndPreservesOtherAllocations()
    {
        var pool = new LicensePool("compiler", 20);
        var project = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        pool.Allocate(project, 3);
        pool.Reserve(first, project, 4);
        pool.Reserve(second, project, 5);
        var before = pool.Version;
        pool.Reserve(first, project, 4);
        Assert.That(pool.Version, Is.EqualTo(before));
        Assert.Throws<DomainException>(() => pool.Release(project, 4));
        pool.ReleaseReservation(first);
        Assert.That(pool.AllocatedSeats, Is.EqualTo(8));
        Assert.That(pool.Allocations.Sum(a => a.Seats), Is.EqualTo(8));
        var events = pool.DomainEvents.Count;
        pool.ReleaseReservation(first);
        Assert.That(pool.DomainEvents.Count, Is.EqualTo(events));
        Assert.That(pool.Allocations.Any(a => a.ReservationId == second), Is.True);
        Assert.Throws<DomainException>(() => pool.Reserve(second, Guid.NewGuid(), 5));
    }

    [Test]
    public void WorkflowAndReleaseEventsRoundTripWithOriginalIdentity()
    {
        var workflow = Approved();
        foreach (var domainEvent in workflow.DomainEvents.Append(new LicenseSeatsReleased("compiler", workflow.ProjectId, 2, workflow.Id)))
        {
            var row = DomainEventSerializer.Serialize(domainEvent, "actor", "correlation");
            Assert.That(DomainEventSerializer.Deserialize(row), Is.EqualTo(domainEvent));
        }
    }

    [Test]
    public void CancellationCannotOverwriteTheBusinessCauseOfAnOngoingCompensation()
    {
        var workflow = Approved();
        workflow.CompleteStep(workflow.State, WorkflowStepResult.Success(Receipt(workflow)), Now, 3);
        workflow.CompleteStep(workflow.State, WorkflowStepResult.Rejected("license_capacity"), Now, 3);
        workflow.RequestCancellation("operator", Now);
        Assert.That(workflow.Outcome, Is.EqualTo(WorkflowOutcome.Rejected));
        Assert.That(workflow.FailureCode, Is.EqualTo("license_capacity"));
    }
}
