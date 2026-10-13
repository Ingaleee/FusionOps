namespace FusionOps.Domain.Shared;

public sealed class IdempotencyConflictException()
    : DomainException("The idempotency key was already used with different command data");

public sealed class IdempotencyInProgressException()
    : DomainException("A request with this idempotency key is still executing; retry with the same key");
