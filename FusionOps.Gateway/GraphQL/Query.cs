using FusionOps.Gateway.Models;

namespace FusionOps.Gateway.GraphQL;

public class Query
{
    public Task<IEnumerable<Allocation>> Allocation(
        Guid projectId,
        [Service] AllocationDataLoader loader,
        CancellationToken ct) =>
        loader.LoadAsync(projectId, ct).ContinueWith(t => t.Result ?? Enumerable.Empty<Allocation>(), ct);
}
