using FusionOps.Application.Workflows;
using FusionOps.Domain.ValueObjects;
using FusionOps.Domain.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FusionOps.Application.Tests;

public sealed class ReservationWorkflowRunnerTests
{
    private static WorkflowClaim Claim()
    {
        var now = DateTimeOffset.UtcNow;
        var workflow = ReservationWorkflow.Propose(Guid.NewGuid(), new(Guid.NewGuid(), [Guid.NewGuid()],
            new TimeRange(now.AddDays(1), now.AddDays(1).AddHours(1)), []), "proposer", now);
        workflow.Approve("approver", true, now);
        return new(workflow, Guid.NewGuid(), 1);
    }
    private static ReservationWorkflowRunner Runner(Mock<IReservationWorkflowStore> store, Mock<IWorkforceWorkflowParticipant> workforce) =>
        new(store.Object, workforce.Object, Mock.Of<ILicenseWorkflowParticipant>(), NullLogger<ReservationWorkflowRunner>.Instance);

    [Fact]
    public async Task LostResponseIsAnUnresolvedRetryRatherThanABusinessRejection()
    {
        var claim = Claim();
        var store = new Mock<IReservationWorkflowStore>();
        var workforce = new Mock<IWorkforceWorkflowParticipant>();
        store.Setup(s => s.ClaimAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(claim);
        workforce.Setup(p => p.ReserveAsync(claim.Workflow, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("Reply lost after commit"));
        store.Setup(s => s.CompleteAsync(claim, It.IsAny<WorkflowStepResult>(), 5, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Assert.True(await Runner(store, workforce).RunOnceAsync(default));
        store.Verify(s => s.CompleteAsync(claim, It.Is<WorkflowStepResult>(r => r.Kind == StepResultKind.Retry && r.Code == nameof(TimeoutException)), 5, It.IsAny<CancellationToken>()));
        workforce.Verify(p => p.ReleaseAsync(It.IsAny<ReservationWorkflow>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmedRolledBackRejectionIsPassedToTheStateMachine()
    {
        var claim = Claim();
        var store = new Mock<IReservationWorkflowStore>();
        var workforce = new Mock<IWorkforceWorkflowParticipant>();
        store.Setup(s => s.ClaimAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(claim);
        workforce.Setup(p => p.ReserveAsync(claim.Workflow, It.IsAny<CancellationToken>())).ThrowsAsync(new WorkflowStepRejectedException("resource_conflict"));
        await Runner(store, workforce).RunOnceAsync(default);
        store.Verify(s => s.CompleteAsync(claim, It.Is<WorkflowStepResult>(r => r.Kind == StepResultKind.Rejected && r.Code == "resource_conflict"), 5, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task GracefulShutdownLeavesTheLeaseForCrashRecovery()
    {
        var claim = Claim();
        var store = new Mock<IReservationWorkflowStore>();
        var workforce = new Mock<IWorkforceWorkflowParticipant>();
        using var stop = new CancellationTokenSource();
        store.Setup(s => s.ClaimAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(claim);
        workforce.Setup(p => p.ReserveAsync(claim.Workflow, It.IsAny<CancellationToken>())).Returns(() =>
        {
            stop.Cancel();
            return Task.FromException<IReadOnlyList<Guid>>(new OperationCanceledException(stop.Token));
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Runner(store, workforce).RunOnceAsync(stop.Token));
        store.Verify(s => s.CompleteAsync(It.IsAny<WorkflowClaim>(), It.IsAny<WorkflowStepResult>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnEmptyQueueDoesNotCallAnyParticipant()
    {
        var store = new Mock<IReservationWorkflowStore>(MockBehavior.Strict);
        var workforce = new Mock<IWorkforceWorkflowParticipant>(MockBehavior.Strict);
        store.Setup(s => s.ClaimAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync((WorkflowClaim?)null);
        Assert.False(await Runner(store, workforce).RunOnceAsync(default));
        workforce.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvalidParticipantReceiptIsUnresolvedAndCannotAdvanceAsSuccess()
    {
        var claim = Claim();
        var store = new Mock<IReservationWorkflowStore>();
        var workforce = new Mock<IWorkforceWorkflowParticipant>();
        store.Setup(s => s.ClaimAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(claim);
        workforce.Setup(p => p.ReserveAsync(claim.Workflow, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Guid>());
        await Runner(store, workforce).RunOnceAsync(default);
        store.Verify(s => s.CompleteAsync(claim, It.Is<WorkflowStepResult>(r => r.Kind == StepResultKind.Retry), 5, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task LeaseLossDoesNotInvokeCompensationWithoutCoordinatorOwnership()
    {
        var claim = Claim();
        var store = new Mock<IReservationWorkflowStore>();
        var workforce = new Mock<IWorkforceWorkflowParticipant>();
        store.Setup(s => s.ClaimAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(claim);
        workforce.Setup(p => p.ReserveAsync(claim.Workflow, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { Guid.NewGuid() });
        store.Setup(s => s.CompleteAsync(claim, It.IsAny<WorkflowStepResult>(), 5, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        Assert.True(await Runner(store, workforce).RunOnceAsync(default));
        workforce.Verify(p => p.ReleaseAsync(It.IsAny<ReservationWorkflow>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
