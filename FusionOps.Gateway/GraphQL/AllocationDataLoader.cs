using GreenDonut;
using FusionOps.Gateway.Models;
using System.Net.Http.Json;

namespace FusionOps.Gateway.GraphQL;

public class AllocationDataLoader : BatchDataLoader<Guid, IEnumerable<Allocation>>
{
    private readonly IHttpClientFactory _factory;
    public AllocationDataLoader(IBatchScheduler b, IHttpClientFactory f)
        : base(b, new DataLoaderOptions())
    {
        _factory = f;
    }

    protected override async Task<IReadOnlyDictionary<Guid, IEnumerable<Allocation>>> LoadBatchAsync(
                IReadOnlyList<Guid> keys, CancellationToken ct)
    {
        var client = _factory.CreateClient("FusionApi");
        var ids = string.Join(",", keys);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(4));
            var resp = await client.GetFromJsonAsync<Dictionary<Guid, IEnumerable<Allocation>>>(
                           $"/api/v1/projects/allocations?ids={ids}", cts.Token);
            return resp ?? keys.ToDictionary(k => k, _ => Enumerable.Empty<Allocation>() as IEnumerable<Allocation>);
        }
        catch
        {
            return keys.ToDictionary(k => k, _ => Enumerable.Empty<Allocation>() as IEnumerable<Allocation>);
        }
    }
}
