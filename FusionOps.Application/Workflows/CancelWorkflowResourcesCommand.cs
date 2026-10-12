using System.Security.Cryptography;
using System.Text.Json;
using FusionOps.Application.Abstractions;
using FusionOps.Domain.Interfaces;
using FusionOps.Domain.Shared;
using FusionOps.Domain.ValueObjects;
using MediatR;

namespace FusionOps.Application.Workflows;

public sealed record CancelWorkflowResourcesCommand(Guid ProjectId, IReadOnlyList<Guid> ResourceIds,
    IReadOnlyList<Guid> AllocationIds, TimeRange Period, string IdempotencyKey) : IRequest<bool>, IIdempotentCommand
{
    public PersistenceBoundary Boundary => PersistenceBoundary.Workforce;
    public string Operation => "workforce.workflow-cancel/v1";
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
        ProjectId, Resources = ResourceIds.Order().ToArray(), Allocations = AllocationIds.Order().ToArray(),
        From = Period.Start.UtcTicks, To = Period.End.UtcTicks
    })));
}

public sealed class CancelWorkflowResourcesHandler(IAllocationRepository allocations)
    : IRequestHandler<CancelWorkflowResourcesCommand, bool>
{
    public async Task<bool> Handle(CancelWorkflowResourcesCommand command, CancellationToken ct)
    {
        if (command.AllocationIds.Count != command.ResourceIds.Count || command.AllocationIds.Distinct().Count() != command.AllocationIds.Count)
            throw new DomainException("The original reservation receipt is incomplete");
        var expectedIds = command.AllocationIds.ToHashSet();
        var found = 0;
        foreach (var resource in command.ResourceIds.Order())
        {
            var calendar = await allocations.FindForResourceWithLockAsync(resource, ct);
            foreach (var allocation in calendar.Where(a => expectedIds.Contains(a.Id.Value)))
            {
                if (allocation.ProjectId != command.ProjectId || allocation.Period != command.Period)
                    throw new DomainException("The allocation does not belong to this reservation specification");
                allocation.Cancel();
                found++;
            }
        }
        if (found != expectedIds.Count) throw new DomainException("An original allocation is missing; compensation needs investigation");
        return true;
    }
}
