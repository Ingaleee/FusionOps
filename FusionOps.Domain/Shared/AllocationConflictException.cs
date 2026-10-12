namespace FusionOps.Domain.Shared;

public sealed class AllocationConflictException(Guid resourceId)
    : DomainException($"Resource {resourceId} is already allocated for the requested period");
