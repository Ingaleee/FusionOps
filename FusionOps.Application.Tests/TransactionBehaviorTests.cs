using FusionOps.Application.Abstractions;
using FusionOps.Application.Pipelines;
using FusionOps.Domain.Interfaces;
using MediatR;
using Moq;
using Xunit;

namespace FusionOps.Application.Tests;

public sealed class TransactionBehaviorTests
{
    private sealed record Write : ICommand<int> { public PersistenceBoundary Boundary => PersistenceBoundary.Workforce; }
    private sealed record Read : IRequest<int>;

    [Fact]
    public async Task TransactionCoversDecisionAndCommitsExactlyOnce()
    {
        var work = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var begun = false;
        work.Setup(w => w.BeginAsync(PersistenceBoundary.Workforce, It.IsAny<CancellationToken>()))
            .Callback(() => begun = true).Returns(Task.CompletedTask);
        work.Setup(w => w.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var result = await new TransactionBehavior<Write, int>(work.Object).Handle(new Write(), () =>
        {
            Assert.True(begun);
            return Task.FromResult(42);
        }, CancellationToken.None);
        Assert.Equal(42, result);
        work.Verify(w => w.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        work.Verify(w => w.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReadQueriesNeverStartOrCommitAWriteTransaction()
    {
        var work = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var result = await new TransactionBehavior<Read, int>(work.Object).Handle(new Read(), () => Task.FromResult(7), default);
        Assert.Equal(7, result);
        work.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancelledCallerDoesNotCancelRollbackCleanup()
    {
        using var cancellation = new CancellationTokenSource();
        var work = new Mock<IUnitOfWork>();
        var error = new OperationCanceledException(cancellation.Token);
        var caught = await Assert.ThrowsAsync<OperationCanceledException>(() => new TransactionBehavior<Write, int>(work.Object)
            .Handle(new Write(), () => { cancellation.Cancel(); throw error; }, cancellation.Token));
        Assert.Same(error, caught);
        work.Verify(w => w.RollbackAsync(CancellationToken.None), Times.Once);
        work.Verify(w => w.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
