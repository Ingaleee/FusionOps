using FluentValidation;
using FusionOps.Application.Abstractions;
using FusionOps.Application.Pipelines;
using FusionOps.Application.UseCases.AllocateResource;
using FusionOps.Domain.Shared;
using Moq;
using Xunit;

namespace FusionOps.Application.Tests;

public sealed class IdempotencyTests
{
    private static AllocateCommand Command(string? key = "request-42") => new(Guid.NewGuid(),
        [Guid.NewGuid(), Guid.NewGuid()], DateTimeOffset.Parse("2026-10-20T08:00:00Z"), DateTimeOffset.Parse("2026-10-20T10:00:00Z"), key);

    [Fact]
    public void FingerprintIgnoresResourceOrderTimeZoneAndTransportKey()
    {
        var command = Command();
        var equivalent = command with
        {
            ResourceIds = command.ResourceIds.Reverse().ToArray(),
            PeriodFrom = command.PeriodFrom.ToOffset(TimeSpan.FromHours(3)),
            PeriodTo = command.PeriodTo.ToOffset(TimeSpan.FromHours(-5)),
            IdempotencyKey = "different-key"
        };
        Assert.Equal(command.Fingerprint, equivalent.Fingerprint);
        Assert.NotEqual(command.Fingerprint, (command with { ProjectId = Guid.NewGuid() }).Fingerprint);
        Assert.NotEqual(command.Fingerprint, (command with { PeriodTo = command.PeriodTo.AddTicks(1) }).Fingerprint);
        Assert.NotEqual(command.Fingerprint, (command with { ResourceIds = [Guid.NewGuid()] }).Fingerprint);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has spaces")]
    [InlineData("different,key")]
    [InlineData("русский")]
    public void InvalidKeysAreRejectedBeforePersistence(string key)
    {
        Assert.False(new AllocateValidator().Validate(Command(key)).IsValid);
    }

    [Fact]
    public void KeyIsOptionalButLengthIsBounded()
    {
        var validator = new AllocateValidator();
        Assert.True(validator.Validate(Command(null)).IsValid);
        Assert.True(validator.Validate(Command(new string('a', 128))).IsValid);
        Assert.False(validator.Validate(Command(new string('a', 129))).IsValid);
    }

    [Fact]
    public async Task StoredResponseIsReturnedWithoutExecutingTheHandlerAgain()
    {
        var command = Command();
        var originalIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var store = new Mock<ICommandReceiptStore>(MockBehavior.Strict);
        store.Setup(s => s.FindForUpdateAsync(command.Operation, command.IdempotencyKey!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoredCommandResult(command.Fingerprint, System.Text.Json.JsonSerializer.Serialize(originalIds)));
        var behavior = new IdempotencyBehavior<AllocateCommand, IReadOnlyCollection<Guid>>(store.Object);
        var result = await behavior.Handle(command, () => throw new Exception("Handler must not run on replay"), default);
        Assert.Equal(originalIds, result);
        store.Verify(s => s.Stage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DifferentDataForACompletedKeyProducesAConflict()
    {
        var command = Command();
        var store = new Mock<ICommandReceiptStore>();
        store.Setup(s => s.FindForUpdateAsync(command.Operation, command.IdempotencyKey!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoredCommandResult("different-fingerprint", "[]"));
        await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
            new IdempotencyBehavior<AllocateCommand, IReadOnlyCollection<Guid>>(store.Object)
                .Handle(command, () => throw new Exception("Handler must not run"), default));
    }

    [Fact]
    public async Task NoKeyBypassesTheReceiptStore()
    {
        var store = new Mock<ICommandReceiptStore>(MockBehavior.Strict);
        var response = new[] { Guid.NewGuid() };
        var result = await new IdempotencyBehavior<AllocateCommand, IReadOnlyCollection<Guid>>(store.Object)
            .Handle(Command(null), () => Task.FromResult<IReadOnlyCollection<Guid>>(response), default);
        Assert.Same(response, result);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FailedHandlerDoesNotStageAnUncommittedResponse()
    {
        var command = Command();
        var store = new Mock<ICommandReceiptStore>();
        var error = new DomainException("Resource conflict");
        var caught = await Assert.ThrowsAsync<DomainException>(() =>
            new IdempotencyBehavior<AllocateCommand, IReadOnlyCollection<Guid>>(store.Object)
                .Handle(command, () => throw error, default));
        Assert.Same(error, caught);
        store.Verify(s => s.Stage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
