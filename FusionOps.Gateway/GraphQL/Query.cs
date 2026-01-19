using FusionOps.Gateway.Models;

namespace FusionOps.Gateway.GraphQL;

public class Query
{
    public async Task<IEnumerable<Allocation>> Allocation(
        Guid projectId,
        [Service] AllocationDataLoader loader,
        CancellationToken ct)
    {
        var result = await loader.LoadAsync(projectId, ct);
        return result ?? Enumerable.Empty<Allocation>();
    }
}
